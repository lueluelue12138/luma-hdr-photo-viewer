"""Isolated real WinUI settings, live display query, states and launch fallback checks."""
import argparse,json,pathlib,subprocess,time
root=pathlib.Path(__file__).resolve().parents[1]
ap=argparse.ArgumentParser();ap.add_argument('--app',type=pathlib.Path,default=root/'artifacts/Luma-hdr-setup/Luma.App.exe');args=ap.parse_args()
out=root/'artifacts'/('settings-'+time.strftime('%Y%m%d-%H%M%S'));out.mkdir(parents=True)
p=subprocess.Popen([str(args.app.resolve()),'--test-library',str(out),'--verify-settings'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
try:code=p.wait(timeout=90)
except subprocess.TimeoutExpired:p.terminate();p.wait(timeout=15);raise
report=out/'settings.json';data=json.loads(report.read_text(encoding='utf8')) if report.exists() else {}
assert code==0 and data.get('complete'),f'Failed process {code}; inspect {out}'
print(json.dumps(data,ensure_ascii=False,indent=2));print(f'PASS: {report}')
