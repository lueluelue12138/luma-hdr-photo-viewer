"""Compare absolute HDR pixels and actual automatic FP16 presentation separately.

User images stay local; only compact numeric evidence is written to the report.
"""
import argparse
import ctypes as c
import json
from pathlib import Path
import numpy as np

parser = argparse.ArgumentParser()
parser.add_argument('--original', required=True)
parser.add_argument('--converted', required=True)
parser.add_argument('--dll', required=True)
parser.add_argument('--report', required=True)
parser.add_argument('--require-close', action='store_true')
args = parser.parse_args()

class Info(c.Structure):
    _fields_ = [('w', c.c_uint), ('h', c.c_uint), ('ow', c.c_uint), ('oh', c.c_uint),
                ('hdr', c.c_int), ('down', c.c_int), ('bytes', c.c_uint64)]
class Status(c.Structure):
    _fields_ = [('gpu', c.c_wchar*128), ('display', c.c_wchar*128),
                ('nvidia', c.c_int), ('hdrActive', c.c_int), ('fp16', c.c_int),
                ('nativeHdr', c.c_int), ('maxNits', c.c_float), ('sdrWhite', c.c_float),
                ('gpuBytes', c.c_uint64), ('hdrEffective', c.c_int), ('vsrEffective', c.c_int),
                ('sdkAvailable', c.c_int), ('hdrAvailable', c.c_int), ('vsrAvailable', c.c_int),
                ('ngxResult', c.c_uint), ('hdrEvaluations', c.c_uint), ('vsrEvaluations', c.c_uint),
                ('hdrMessage', c.c_wchar*256), ('vsrMessage', c.c_wchar*256)]
class Gain(c.Structure):
    _fields_ = [('minimum', c.c_float), ('maximum', c.c_float), ('offsetSdr', c.c_float*3),
                ('offsetHdr', c.c_float*3), ('gamut', c.c_int), ('model', c.c_int)]
d = c.CDLL(str(Path(args.dll).resolve()))
d.LumaError.restype = c.c_wchar_p
signatures = {'LumaDecodeSized': [c.c_wchar_p, c.c_uint, c.POINTER(Info), c.POINTER(c.c_void_p)],
              'LumaFramePixel': [c.c_void_p, c.c_uint, c.c_uint, c.POINTER(c.c_float)],
              'LumaFrameGainInfo': [c.c_void_p, c.POINTER(Gain)],
              'LumaFreeFrame': [c.c_void_p], 'LumaCreateFast': [c.c_void_p, c.c_void_p, c.POINTER(c.c_void_p)],
              'LumaCopyStatus': [c.c_void_p, c.POINTER(Status)], 'LumaDestroy': [c.c_void_p],
              'LumaResize': [c.c_void_p, c.c_uint, c.c_uint, c.c_float, c.c_float],
              'LumaUseFrame': [c.c_void_p, c.c_void_p],
              'LumaNativeDisplayTest': [c.c_void_p, c.c_int, c.c_float, c.c_float],
              'LumaReadSurface': [c.c_void_p, c.POINTER(c.c_uint16), c.c_uint]}
for name, signature in signatures.items():
    getattr(d, name).argtypes = signature

def check(hr):
    if hr < 0:
        raise RuntimeError(d.LumaError())

def metrics(a, b):
    a, b = np.array(a, dtype=np.float64), np.array(b, dtype=np.float64)
    ya, yb = a @ [.2126, .7152, .0722], b @ [.2126, .7152, .0722]
    mask = ya > .125  # Above 10 nits in scRGB.
    ratios = yb[mask] / ya[mask]
    errors = np.abs(b-a) * 80
    return {'source_luminance_nits_p50_p95_p99': np.percentile(ya*80, [50, 95, 99]).tolist(),
            'output_luminance_nits_p50_p95_p99': np.percentile(yb*80, [50, 95, 99]).tolist(),
            'luminance_ratio_p05_p50_p95': np.percentile(ratios, [5, 50, 95]).tolist(),
            'rgb_absolute_error_nits_mean_p95_p99': [float(errors.mean()), *np.percentile(errors, [95, 99]).tolist()],
            'relative_rgb_error_p50_p95': np.percentile(np.max(np.abs(b-a), axis=-1)[mask] / np.maximum(np.max(np.abs(a), axis=-1)[mask], .125), [50, 95]).tolist()}

renderer = c.c_void_p()
check(d.LumaCreateFast(None, None, c.byref(renderer)))
status = Status()
check(d.LumaCopyStatus(renderer, c.byref(status)))
frames, infos, decoded, automatic, absolute, gains = [], [], [], [], [], []
try:
    for path in [args.original, args.converted]:
        info, frame = Info(), c.c_void_p()
        check(d.LumaDecodeSized(str(Path(path).resolve()), 0, c.byref(info), c.byref(frame)))
        frames.append(frame); infos.append(info)
        gain = Gain(); check(d.LumaFrameGainInfo(frame, c.byref(gain)))
        gains.append({'model': gain.model, 'minimum': gain.minimum, 'maximum': gain.maximum, 'gamut': gain.gamut})
        pixels = []
        for y in range(4, info.h, max(1, info.h//100)):
            for x in range(4, info.w, max(1, info.w//160)):
                pixel = (c.c_float*4)()
                check(d.LumaFramePixel(frame, x, y, pixel))
                pixels.append(list(pixel)[:3])
        decoded.append(np.array(pixels))
        w, h = 960, round(960*info.h/info.w)
        check(d.LumaResize(renderer, w, h, 1, 1))
        check(d.LumaUseFrame(renderer, frame))
        for target, peak, white in [(automatic, status.maxNits, status.sdrWhite), (absolute, 10000, 203/80)]:
            check(d.LumaNativeDisplayTest(renderer, 1, peak, white))
            raw = np.empty((h, w, 4), dtype=np.uint16)
            check(d.LumaReadSurface(renderer, raw.ctypes.data_as(c.POINTER(c.c_uint16)), w*h))
            target.append(raw.view(np.float16).astype(np.float64)[..., :3].reshape(-1, 3))
    assert [(i.w, i.h) for i in infos][0] == [(i.w, i.h) for i in infos][1], 'Input sizes differ'
    report = {'display': {'peak_nits': status.maxNits, 'sdr_white_nits': status.sdrWhite*80, 'hdr_active': bool(status.hdrActive)},
              'dimensions': [infos[0].w, infos[0].h], 'gain_metadata': gains,
              'decoded_absolute_hdr': metrics(*decoded), 'automatic_fp16_display': metrics(*automatic),
              'full_gain_at_203_nits_white_fp16': metrics(*absolute)}
    Path(args.report).parent.mkdir(parents=True, exist_ok=True)
    Path(args.report).write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))
    if args.require_close:
        measured = report['automatic_fp16_display']
        lo, median, hi = measured['luminance_ratio_p05_p50_p95']
        assert .97 < lo <= median <= hi < 1.03, 'Visible exposure/contrast shift remains'
        assert measured['relative_rgb_error_p50_p95'][1] < .04, 'Color error remains above the preview tolerance'
        assert measured['rgb_absolute_error_nits_mean_p95_p99'][0] < 2, 'Mean presentation error exceeds 2 nits'
        print('PASS: actual FP16 automatic display, luminance p05-p95 within 3%, RGB relative p95 below 4%, mean channel error below 2 nits.')
finally:
    for frame in frames:
        d.LumaFreeFrame(frame)
    d.LumaDestroy(renderer)
