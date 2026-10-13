"""Real WinUI exclusive device transfers, short idle expiry and SDK session reuse."""
import argparse,json,pathlib,subprocess,time
root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('photo',type=pathlib.Path);a=p.parse_args()
folder=root/'artifacts'/('device-cache-'+time.strftime('%Y%m%d-%H%M%S'));folder.mkdir()
proc=subprocess.Popen([str(root/'artifacts/Luma/Luma.App.exe'),'--test-library',str(folder),'--verify-device-cache',str(a.photo.resolve())])
try:code=proc.wait(timeout=120)
except subprocess.TimeoutExpired:proc.terminate();proc.wait(timeout=15);raise RuntimeError(f'Device cache test timed out: {folder}')
data=json.loads((folder/'device-cache.json').read_text(encoding='utf-8'));assert code==0 and data['complete'],data
for row in data['results']:print(row,flush=True)
print('PASS: independent windows, real HDR-only, SDR laziness, atomic glass, one idle slot, automatic expiry.',folder)
