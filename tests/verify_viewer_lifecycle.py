"""Local real-window integration test. Never opens the production library."""
import argparse,json,pathlib,subprocess,time
from PIL import Image,ImageDraw

root=pathlib.Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('photo',type=pathlib.Path)
parser.add_argument('--runs',type=int,default=3)
args=parser.parse_args()
assert args.photo.is_file()
exe=root/'artifacts/Luma/Luma.App.exe'
assert exe.is_file(),'Publish Luma before verification'
suite=root/'artifacts'/('viewer-lifecycle-'+time.strftime('%Y%m%d-%H%M%S'))
suite.mkdir(parents=True)
for n in range(args.runs):
    folder=suite/str(n+1);folder.mkdir()
    photo=Image.new('RGB',(1024,512),(70,100,130));ImageDraw.Draw(photo).ellipse((350,100,650,400),fill=(250,240,210));photo.save(folder/'small.jpg',quality=95)
    start=time.perf_counter()
    process=subprocess.Popen([str(exe),'--test-library',str(folder),'--verify-viewer-lifecycle',str(args.photo.resolve())],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    try:code=process.wait(timeout=240)
    except subprocess.TimeoutExpired:
        process.terminate();process.wait(timeout=15);raise RuntimeError('Viewer verification timed out; only its own test process was stopped')
    report=folder/'viewer-lifecycle.json'
    assert code==0 and report.exists(),f'Process failed ({code}); inspect {folder}'
    data=json.loads(report.read_text(encoding='utf-8'))
    assert data['complete'] and data['windows']==44,data
    assert sum(r.get('scenario')=='expanded-popup-photo-glass' for r in data['results'])==5
    print(f'PASS: fresh process {n+1}, {data["windows"]} windows, expanded popup glass, HDR/VSR teardown, {time.perf_counter()-start:.1f}s',flush=True)
print(f'PASS: {args.runs*44} real viewer windows across {args.runs} fresh launches. Reports: {suite}',flush=True)
