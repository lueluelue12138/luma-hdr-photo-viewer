"""Real WinUI navigation/zoom/placement timing with a separate local test library."""
import argparse, json, pathlib, statistics, subprocess, time

root = pathlib.Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('photo', type=pathlib.Path)
args = parser.parse_args()
folder = root/'artifacts'/('viewer-interaction-'+time.strftime('%Y%m%d-%H%M%S'))
folder.mkdir(parents=True)
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
