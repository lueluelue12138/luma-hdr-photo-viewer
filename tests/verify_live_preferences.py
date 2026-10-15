"""Two processes, isolated settings, real MediaPlayer + details layout."""
import argparse,json,pathlib,subprocess,time
root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('photo',type=pathlib.Path);p.add_argument('--app',type=pathlib.Path,default=root/'artifacts/Luma/Luma.App.exe');a=p.parse_args()
folder=root/'artifacts'/('live-preferences-'+time.strftime('%Y%m%d-%H%M%S'));folder.mkdir()
for step in ('write','read'):
    process=subprocess.Popen([str(a.app.resolve()),'--test-library',str(folder),'--verify-live-audio-'+step,str(a.photo.resolve())],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    try:code=process.wait(timeout=100)
    except subprocess.TimeoutExpired:process.terminate();process.wait(timeout=15);raise
    report=folder/('live-audio-'+step+'.json');data=json.loads(report.read_text(encoding='utf-8-sig')) if report.exists() else {}
    assert code==0 and data.get('complete'),f'{step} failed: {folder}; {data}'
print('PASS: Live sound survives replay, another photo, HDR settings save and process restart; Live details and responsive filmstrip. Reports:',folder)
