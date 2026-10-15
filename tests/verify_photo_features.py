"""Visible HDR/Live badges using local real samples; no production DB writes."""
import argparse,json,pathlib,shutil,subprocess,time
root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--app',type=pathlib.Path,default=root/'artifacts/Luma/Luma.App.exe');a=p.parse_args()
folder=root/'artifacts'/('photo-features-'+time.strftime('%Y%m%d-%H%M%S'));source=folder/'source';source.mkdir(parents=True)
expected={}
for path in (root/'artifacts/HDR测试相册').iterdir():
    if path.suffix.lower() not in ('.jpg','.heic','.jxr'):continue
    shutil.copy2(path,source/path.name);expected[path.name]=[not path.name.startswith('99_'),path.name=='1000190475.jpg']
samples=root/'artifacts/live-photos/samples'
for name,hdr in [('live_photo_sample.HEIC',True),('google-motion_photo.MP.jpg',False),('google-motion_photo_single_video_track.MP.heic',False),('pixel-6-pro.jpg',True),('pixel-8a.jpg',True),('samsung-one-ui-5.jpg',False),('samsung-one-ui-6.jpg',False),('samsung-one-ui-6.heic',False)]:
    shutil.copy2(samples/name,source/name);expected[name]=[hdr,True]
shutil.copy2(samples/'live_photo_sample.MOV',source/'live_photo_sample.MOV')
(folder/'expected.json').write_text(json.dumps(expected))
process=subprocess.Popen([str(a.app.resolve()),'--test-library',str(folder),'--verify-photo-features','--gallery-source',str(source)],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
try:code=process.wait(timeout=120)
except subprocess.TimeoutExpired:process.terminate();process.wait(timeout=15);raise
report=folder/'photo-features.json';data=json.loads(report.read_text(encoding='utf8')) if report.exists() else {}
assert code==0 and data.get('complete'),f'Failed: {folder}; {data}'
print(f'PASS: {len(expected)} real HDR/Live/SDR gallery cards, light/dark themes and recycled views. Report: {report}')
