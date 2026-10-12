"""Compare actual WIC full/viewport decoding; local photos are never copied or uploaded."""
import argparse, ctypes as c, json, pathlib, statistics, time

parser = argparse.ArgumentParser()
parser.add_argument('photo', type=pathlib.Path)
parser.add_argument('--edge', type=int, default=2048)
parser.add_argument('--runs', type=int, default=5)
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parents[1]

class Info(c.Structure):
    _fields_ = [('width', c.c_uint), ('height', c.c_uint), ('originalWidth', c.c_uint), ('originalHeight', c.c_uint), ('nativeHdr', c.c_int), ('downsampled', c.c_int), ('bytes', c.c_uint64)]

d = c.CDLL(str(root/'src/Luma.Native/bin/Luma.Native.dll'))
d.LumaDecodeSized.argtypes = [c.c_wchar_p, c.c_uint, c.POINTER(Info), c.POINTER(c.c_void_p)]
d.LumaFreeFrame.argtypes = [c.c_void_p]
d.LumaError.restype = c.c_wchar_p
results = []
for edge in (0, args.edge):
    samples = []
    for _ in range(args.runs):
        pointer = c.c_void_p(); info = Info(); started = time.perf_counter()
        result = d.LumaDecodeSized(str(args.photo.resolve()), edge, c.byref(info), c.byref(pointer))
        if result < 0:
            raise RuntimeError(d.LumaError())
        try:
            samples.append(round((time.perf_counter()-started)*1000, 2))
            assert not edge or max(info.width, info.height) <= edge
        finally:
            d.LumaFreeFrame(pointer)
    results.append(dict(edge=edge, width=info.width, height=info.height, original=[info.originalWidth, info.originalHeight], nativeHdr=info.nativeHdr, bytes=info.bytes, medianMs=statistics.median(samples), samplesMs=samples))
print(json.dumps(results, indent=2))
