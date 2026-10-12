"""Real WinUI first-frame glass and dispatcher latency during render/NGX work."""
import argparse,json,pathlib,statistics,subprocess,time
root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('photo',type=pathlib.Path);a=p.parse_args()
folder=root/'artifacts'/('viewer-performance-'+time.strftime('%Y%m%d-%H%M%S'));folder.mkdir()
proc=subprocess.Popen([str(root/'artifacts/Luma/Luma.App.exe'),'--test-library',str(folder),'--verify-viewer-performance',str(a.photo.resolve())])
try:code=proc.wait(timeout=150)
except subprocess.TimeoutExpired:proc.terminate();proc.wait(timeout=15);raise RuntimeError(f'Performance validation timed out: {folder}')
data=json.loads((folder/'viewer-performance.json').read_text(encoding='utf-8'));assert code==0 and data['complete'],data
for row in data['results']:
    if 'dispatcherLatency' in row:
        samples=sorted(row['dispatcherLatency']);assert len(samples)>30
        print(f'{row["scenario"]}: UI callback median {statistics.median(samples):.2f} ms, P95 {samples[int(len(samples)*.95)]:.2f} ms, max {max(samples):.2f} ms; {row["renderSubmissions"]} presents',flush=True)
    else:print(row,flush=True)
print(f'PASS: real first-photo glass, saved HDR startup, static blur caching; {folder}')
