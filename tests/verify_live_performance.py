"""Read-only real MediaPlayer startup/replay timing with isolated settings.

SDR deliberately isolates media/device costs from RTX initialization. These are
submission/dispatcher timings, not physical display latency or HDR proof.
"""
import argparse,json,pathlib,subprocess,time
root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('photos',type=pathlib.Path,nargs='+');p.add_argument('--app',type=pathlib.Path,default=root/'artifacts/Luma/Luma.App.exe');p.add_argument('--label',default='candidate');a=p.parse_args()
folder=root/'artifacts'/('live-performance-'+a.label+'-'+time.strftime('%Y%m%d-%H%M%S'));folder.mkdir()
results=[]
for n,photo in enumerate(a.photos):
    case=folder/str(n);case.mkdir()
    process=subprocess.Popen([str(a.app.resolve()),'--test-library',str(case),'--verify-live-performance',str(photo.resolve())],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    try:code=process.wait(timeout=90)
    except subprocess.TimeoutExpired:process.terminate();process.wait(timeout=15);raise
    report=case/'live-performance.json';data=json.loads(report.read_text(encoding='utf-8-sig')) if report.exists() else {}
    assert code==0 and data.get('complete'),f'Live performance failed: {case}; {data}'
    results.append(data['result'])
    print('PASS:',photo.name,json.dumps(data['result'],ensure_ascii=False),flush=True)
(folder/'summary.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf8')
print('Reports:',folder)
