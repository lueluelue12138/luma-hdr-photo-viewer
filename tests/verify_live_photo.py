"""Opt-in integration check using a public or user-supplied complete Live asset."""
import argparse
import json
from pathlib import Path
import subprocess
import time

root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('photo',type=Path)
parser.add_argument('--app',type=Path,default=root/'artifacts/Luma-live-ui/Luma.App.exe')
args=parser.parse_args()
assert args.photo.is_file() and args.app.is_file()
folder=root/'artifacts'/('live-photo-'+time.strftime('%Y%m%d-%H%M%S'))
folder.mkdir()
start=time.perf_counter()
process=subprocess.Popen([str(args.app.resolve()),'--test-library',str(folder),'--verify-live-photo',str(args.photo.resolve())],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
try:
    code=process.wait(timeout=100)
except subprocess.TimeoutExpired:
    process.terminate();process.wait(timeout=15)
    raise RuntimeError(f'Live verification timed out; only its own test process stopped. Inspect {folder}')
report=folder/'live-photo.json'
assert code==0 and report.exists(),f'Live process failed ({code}); inspect {folder}'
data=json.loads(report.read_text(encoding='utf-8-sig'))
assert data['complete'],data
print(json.dumps({'seconds':round(time.perf_counter()-start,2),'report':str(report),**data},ensure_ascii=False,indent=2))
