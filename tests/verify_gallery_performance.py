"""Real WinUI virtualization/scrolling with small BMPs or large JPEG copies.

Large mode exercises many file paths and expensive thumbnails using copies of
one read-only source, not a diverse photo corpus or a cold filesystem cache.
"""
import argparse,hashlib,json,pathlib,shutil,statistics,struct,subprocess,time
from PIL import Image
root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--large-photo',type=pathlib.Path);p.add_argument('--count',type=int);p.add_argument('--keep-fixtures',action='store_true');p.add_argument('--shell-control',action='store_true');a=p.parse_args()
count=a.count or (96 if a.large_photo else 600)
if count<64:p.error('At least 64 photographs are required to exercise virtualization')
folder=root/'artifacts'/('gallery-performance-'+time.strftime('%Y%m%d-%H%M%S'));source=folder/'source';source.mkdir(parents=True)
fixtures=[]
if a.large_photo:
    original=a.large_photo.resolve();original_hash=hashlib.sha256(original.read_bytes()).hexdigest()
    with Image.open(original) as image:
        if image.format!='JPEG':p.error('--large-photo requires an actual JPEG')
        dimensions=list(image.size)
    total_bytes=original.stat().st_size*count
    if total_bytes>4*1024**3 or shutil.disk_usage(source).free<total_bytes+2*1024**3:p.error('Owned JPEG fixtures exceed the disk budget')
    for i in range(count):
        target=source/f'photo-{i:04d}.jpg';shutil.copyfile(original,target);fixtures.append(target)
    dataset={'kind':'large-JPEG-copies','count':count,'dimensions':dimensions,'uniqueSourceImages':1,'sourceHash':original_hash,'fixtureBytes':total_bytes,'filesystemCacheCleared':False}
else:
    w,h=128,96;size=w*h*3
    header=struct.pack('<2sIHHI',b'BM',54+size,0,0,54)+struct.pack('<IiiHHIIiiII',40,w,h,1,24,0,size,2835,2835,0,0)
    for i in range(count):
        pixels=bytes(c for y in range(h) for x in range(w) for c in ((x*2+i)%256,(y*2+i*5)%256,(x+y+i*13)%256))
        target=source/f'photo-{i:04d}.bmp';target.write_bytes(header+pixels);fixtures.append(target)
    dataset={'kind':'small-BMP-fixtures','count':count,'dimensions':[w,h],'fixtureBytes':sum(f.stat().st_size for f in fixtures),'filesystemCacheCleared':False}
dataset['shellControl']=a.shell_control
folder.joinpath('dataset.json').write_text(json.dumps(dataset,indent=2),encoding='utf-8')
print('Prepared isolated gallery fixture:',dataset,flush=True)
command=[str(root/'artifacts/Luma/Luma.App.exe'),'--test-library',str(folder.resolve()),'--verify-gallery-performance']
if a.shell_control:command.append('--shell-control')
proc=subprocess.Popen(command)
try:code=proc.wait(timeout=90)
except subprocess.TimeoutExpired:proc.terminate();proc.wait(timeout=15);raise
data=json.loads(folder.joinpath('gallery-performance.json').read_text(encoding='utf-8'));assert code==0 and data['complete'],data
assert data['count']==count and data['refreshCoalescingVerified'] and data['visibleThumbnails']>0 and data['cachedThumbnailBytes']<=32*1024**2,data
if a.large_photo:assert hashlib.sha256(original.read_bytes()).hexdigest()==original_hash,'Read-only original was changed'
samples=sorted(data['dispatcherLatency']);print({k:v for k,v in data.items() if k!='dispatcherLatency'})
print(f'UI callback median {statistics.median(samples):.2f} ms, P95 {samples[int(len(samples)*.95)]:.2f} ms, max {max(samples):.2f} ms; {folder}')
if not a.keep_fixtures:
    owned=source.resolve();workspace_artifacts=(root/'artifacts').resolve()
    assert owned.is_relative_to(workspace_artifacts) and owned.parent==folder.resolve()
    for fixture in fixtures:
        assert fixture.resolve().parent==owned
        fixture.unlink()
    print('Removed only the generated photo copies; reports retained.',flush=True)
