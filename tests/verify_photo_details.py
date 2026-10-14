"""Real WinUI/system EXIF metadata checks; only generated fixtures are modified."""
import argparse,hashlib,json,pathlib,shutil,subprocess,time
from PIL import Image,TiffImagePlugin
root=pathlib.Path(__file__).resolve().parents[1]
ap=argparse.ArgumentParser();ap.add_argument('--app',type=pathlib.Path,default=root/'artifacts/Luma-photo-details/Luma.App.exe');ap.add_argument('--photo',type=pathlib.Path);args=ap.parse_args()
out=root/'artifacts'/('photo-details-'+time.strftime('%Y%m%d-%H%M%S'));fixtures=out/'fixtures';fixtures.mkdir(parents=True)
r=TiffImagePlugin.IFDRational;exif=Image.Exif();exif[271]='Luma Fixture';exif[272]='Luma Fixture Camera';exif[274]=6;exif[282]=r(350);exif[283]=r(350);exif[296]=2;exif[305]='Luma fixture writer'
exif[34665]={36867:'2026:10:01 14:10:00',36868:'2026:10:01 14:10:00',36864:b'0232',33434:r(1,4000),33437:r(7,2),34855:160,37380:r(0),37386:r(48),41989:48,42036:'Luma Fixture 28-200mm',37385:16,37383:5,41987:0,40961:1,34850:3,41992:0,41993:0,41994:1}
exif[34853]={0:b'\x02\x03\x00\x00',1:'N',2:(r(25),r(0),r(0)),3:'E',4:(r(100),r(0),r(0))}
Image.new('RGB',(640,480),(80,100,130)).save(fixtures/'camera.jpg','JPEG',quality=95,exif=exif)
Image.new('RGB',(320,240),(40,65,80)).save(fixtures/'plain.png');Image.new('RGB',(320,240),(140,155,160)).save(fixtures/'ordinary.jpg')
for source,name in [(root/'artifacts/heic-windows/fixtures/apple-heic.heic','hdr.heic'),(root/'artifacts/xmp-gainmaps/xmp-rgb.jpg','gain.jpg')]:
    if source.exists():shutil.copy2(source,fixtures/name)
before=None
if args.photo:before=(hashlib.sha256(args.photo.read_bytes()).hexdigest(),args.photo.stat().st_size,args.photo.stat().st_mtime_ns)
command=[str(args.app.resolve()),'--test-library',str(out),'--verify-photo-details']
if args.photo:command.append(str(args.photo.resolve()))
p=subprocess.Popen(command,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
try:code=p.wait(timeout=150)
except subprocess.TimeoutExpired:p.terminate();p.wait(timeout=15);raise
report=out/'photo-details.json';data=json.loads(report.read_text(encoding='utf8')) if report.exists() else {}
assert code==0 and data.get('complete'),f'Failed process {code}; inspect {out}'
if args.photo:assert before==(hashlib.sha256(args.photo.read_bytes()).hexdigest(),args.photo.stat().st_size,args.photo.stat().st_mtime_ns),'Original photo changed'
print(f'PASS: system EXIF, shutter/aperture/ISO/lens/date/GPS, no-EXIF/HDR, lazy cache, cancelled navigation, themes and no renderer jobs. Report: {report}')
for item in data['results']:print(json.dumps(item,ensure_ascii=False))
