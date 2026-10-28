"""Reconstruct the exported XMP gain map independently of libultrahdr.

Pillow decodes the two ordinary JPEG streams; NumPy implements the published
Android formula. No Luma renderer or libultrahdr decoder participates here.
"""
from pathlib import Path
import io
import json
import math
import re
import xml.etree.ElementTree as ET

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
QA = ROOT / 'artifacts/hdr-converter-tests'
folder = Path((QA / 'latest.txt').read_text())
NS = {'hdrgm': 'http://ns.adobe.com/hdr-gain-map/1.0/',
      'rdf': 'http://www.w3.org/1999/02/22-rdf-syntax-ns#',
      'Item': 'http://ns.google.com/photos/1.0/container/item/'}


def reconstruct(path):
    data = path.read_bytes()
    packets = [ET.fromstring(x) for x in re.findall(rb'<x:xmpmeta.*?</x:xmpmeta>', data, re.S)]
    primary = packets[0]
    gain_item = next(e for e in primary.iter() if e.get(f'{{{NS["Item"]}}}Semantic') == 'GainMap')
    length = int(gain_item.get(f'{{{NS["Item"]}}}Length'))
    assert 0 < length < len(data) and data[-length:][:2] == b'\xff\xd8'
    base = Image.open(io.BytesIO(data))
    gain = Image.open(io.BytesIO(data[-length:]))
    assert base.size == gain.size and gain.mode == 'RGB', 'Need full-resolution RGB gain map'
    assert all(layer[1:3] == (1, 1) for layer in base.layer), 'Primary JPEG loses color resolution'
    assert base.info.get('icc_profile'), 'Primary JPEG must describe its color space'
    assert b'urn:iso:std:iso:ts:21496:-1' in data, 'ISO gain-map metadata missing'
    meta = packets[1].find('.//rdf:Description', NS)

    def values(name, default):
        key = f'{{{NS["hdrgm"]}}}{name}'
        if key in meta.attrib:
            result = [float(meta.attrib[key])]
        else:
            node = meta.find(f'hdrgm:{name}', NS)
            result = [float(e.text) for e in node.findall('rdf:Seq/rdf:li', NS)] if node is not None else [default]
        assert len(result) in (1, 3) and all(math.isfinite(v) for v in result)
        return np.array(result)

    assert meta.get(f'{{{NS["hdrgm"]}}}BaseRenditionIsHDR') == 'False'
    lo, hi, gamma = values('GainMapMin', 0), values('GainMapMax', 1), values('Gamma', 1)
    offset_sdr, offset_hdr = values('OffsetSDR', 1/64), values('OffsetHDR', 1/64)
    sdr = np.asarray(base.convert('RGB'), dtype=np.float64) / 255
    linear = np.where(sdr <= .04045, sdr/12.92, ((sdr+.055)/1.055)**2.4)
    encoded = np.asarray(gain, dtype=np.float64) / 255
    log_gain = lo + (hi-lo)*encoded**(1/gamma)
    nits = ((linear + offset_sdr) * 2**log_gain - offset_hdr) * 203
    assert np.isfinite(nits).all()
    return nits


gray = reconstruct(folder / '灰阶_UltraHDR.jpg')
gray_packets = re.findall(rb'<x:xmpmeta.*?</x:xmpmeta>', (folder / '灰阶_UltraHDR.jpg').read_bytes(), re.S)
capacity = ET.fromstring(gray_packets[1]).find('.//rdf:Description', NS).get(f'{{{NS["hdrgm"]}}}HDRCapacityMax')
assert abs(2**float(capacity) - 4000/203) < .01, 'Mastering peak must follow the source, not the 10000-nit default'
expected = np.array([0, 4, 40, 203, 406, 800, 1600, 4000])
actual = gray[32, np.arange(8)*64+32, :].mean(axis=1)
assert np.all(np.abs(actual-expected) < np.maximum(1, expected*.08)), (expected, actual)

# libultrahdr's HDR-only API tone maps into a Display P3 / sRGB-transfer base.
# Reference matrices below directly map the scRGB fixture into linear P3;
# they do not reuse the converter's Rec.2020 intermediate matrix.
to_p3 = np.array([[.82259287, .17753395, 0], [.03319951, .9667835, 0],
                  [.01708535, .07239572, .91030148]])
color_results = []
for name, source in [('odd.jpg', [4, 2, .5]), ('wide-gamut.jpg', [-.5, 4, .5])]:
    rgb = reconstruct(folder / name)
    predicted = to_p3 @ np.array(source) * 80
    measured = rgb[rgb.shape[0]//2, rgb.shape[1]//2]
    assert np.all(np.abs(measured-predicted) < np.maximum(3, np.abs(predicted)*.1)), (name, measured, predicted)
    color_results.append({'file': name, 'expected_p3_nits': predicted.tolist(), 'decoded_p3_nits': measured.tolist()})

report = {'method': 'Pillow JPEG decode + independent Android XMP gain-map formula',
          'fixtures': str(folder), 'gray_expected_nits': expected.tolist(),
          'gray_decoded_nits': actual.tolist(), 'colors': color_results,
          'phone_hdr_display_verified': False}
(QA / 'independent-verification.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print('PASS: independent XMP reconstruction, 8 absolute luminance endpoints, color/gamut conversion, RGB gain map, SDR ICC and ISO metadata.')
