"""Real WinUI fullscreen geometry and frame restoration, isolated from user data."""
import argparse
import json
import pathlib
import subprocess
import time
from PIL import Image, ImageDraw

root = pathlib.Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--app', type=pathlib.Path, default=root/'artifacts/Luma/Luma.App.exe')
parser.add_argument('--output', type=pathlib.Path)
args = parser.parse_args()
folder = (args.output or root/'artifacts'/('fullscreen-'+time.strftime('%Y%m%d-%H%M%S'))).resolve()
folder.mkdir(parents=True, exist_ok=False)
photo = Image.new('RGB', (2048, 1152), (12, 22, 32))
draw = ImageDraw.Draw(photo)
draw.rectangle((0, 0, 90, 90), fill=(120, 40, 40))
draw.rectangle((1957, 0, 2047, 90), fill=(40, 120, 40))
draw.rectangle((0, 1061, 90, 1151), fill=(40, 40, 120))
draw.rectangle((1957, 1061, 2047, 1151), fill=(120, 100, 40))
path = folder/'edge-fixture.png'
photo.save(path)
process = subprocess.Popen([str(args.app.resolve()), '--test-library', str(folder), '--verify-fullscreen', str(path)])
try:
    code = process.wait(timeout=100)
except subprocess.TimeoutExpired:
    process.terminate()
    process.wait(timeout=15)
    raise RuntimeError(f'Fullscreen test timed out; inspect {folder}')
report = folder/'fullscreen.json'
assert report.is_file(), f'Missing report; inspect {folder}'
data = json.loads(report.read_text(encoding='utf-8'))
assert code == 0 and data['complete'], data
full = [r for r in data['results'] if r['scenario'] == 'fullscreen']
restored = [r for r in data['results'] if r['scenario'] == 'restored']
assert len(full) == len(restored) == 12, data
assert {(r['theme'], r['maximized']) for r in full} == {
    (theme, maximized) for theme in ('Light', 'Dark') for maximized in (False, True)}
assert all(r['client'] == r['screen'] and abs(r['root'][1]*r['scale']-r['screen'][3]) < .25 for r in full)
print(f'PASS: 12 fullscreen / restore cycles, light/dark, normal/maximized, activation and details; full-monitor XAML, no one-pixel caption inset. {report}')
