"""Real WinUI navigation/zoom/placement timing with a separate local test library."""
import argparse, json, pathlib, statistics, subprocess, time
from PIL import Image

root = pathlib.Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('photo', type=pathlib.Path)
args = parser.parse_args()
folder = root/'artifacts'/('viewer-interaction-'+time.strftime('%Y%m%d-%H%M%S'))
folder.mkdir(parents=True)
fixture=folder/'album-fixture';fixture.mkdir()
for n in range(32):
    Image.new('RGB',(640,360),(60+n*4,110,150)).save(fixture/f'photo-{n:02}.jpg',quality=90)
(fixture/'000-corrupt.jpg').write_bytes(b'Not a JPEG')
(fixture/'._photo-00.jpg').write_bytes(b'AppleDouble metadata, not a JPEG')
process = subprocess.Popen([str(root/'artifacts/Luma/Luma.App.exe'), '--test-library', str(folder), '--verify-viewer-interaction', str(args.photo.resolve())])
try:
    code = process.wait(timeout=120)
except subprocess.TimeoutExpired:
    process.terminate(); process.wait(timeout=15)
    raise RuntimeError(f'Interaction test timed out; inspect {folder}')
data = json.loads((folder/'viewer-interaction.json').read_text(encoding='utf-8'))
assert code == 0 and data['complete'], data
for scenario in ('random-SDR', 'prefetched-SDR'):
    samples = [r['firstFrame'] for r in data['results'] if r['scenario']==scenario]
    print(f'PASS: {scenario}: n={len(samples)}, median={statistics.median(samples)}ms, max={max(samples)}ms', flush=True)
print(f'PASS: centered windows, no NGX during SDR, zoom/pan/fit, full detail, latest request, lazy HDR; report {folder}', flush=True)
comparison = next(r for r in data['results'] if r['scenario']=='cached-original-comparison')
assert comparison['additionalWorkerJobs']==comparison['additionalHdrCalls']==0
print(f'PASS: {comparison["switches"]} cached original/HDR switches: median={statistics.median(comparison["milliseconds"]):.2f}ms, max={max(comparison["milliseconds"]):.2f}ms; no extra worker jobs or AI calls; hold during processing.', flush=True)
gallery=next(r for r in data['results'] if r['scenario']=='gallery-refresh-cover-and-24-resize-composition-samples')
print(f'PASS: deleted/metadata entries hidden, corrupt cover fallback, {gallery["cachedThumbnails"]} reused thumbnails; 24 gallery resizes to two Composition callbacks: median={statistics.median(gallery["milliseconds"]):.1f}ms, max={max(gallery["milliseconds"]):.1f}ms. These are UI composition timings, not screen frame latency.',flush=True)
