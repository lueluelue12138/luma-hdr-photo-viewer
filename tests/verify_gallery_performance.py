"""Real WinUI scrolling/viewer stress with isolated DBs and read-only originals.

Modes cover synthetic formats, JPEG copies, or a complete read-only album.
Filesystem caches are not cleared; phase/GC diagnostics are not screen timings.
"""
import argparse,hashlib,json,os,pathlib,shutil,statistics,struct,subprocess,time
from PIL import Image
root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--large-photo',type=pathlib.Path);p.add_argument('--large-folder',type=pathlib.Path);p.add_argument('--read-only-folder',type=pathlib.Path);p.add_argument('--viewer-stress',action='store_true');p.add_argument('--mixed-formats',action='store_true');p.add_argument('--include-native-hdr',action='store_true');p.add_argument('--count',type=int);p.add_argument('--keep-fixtures',action='store_true');p.add_argument('--shell-control',action='store_true');p.add_argument('--trace',action='store_true');p.add_argument('--cpu-trace',action='store_true');p.add_argument('--app',type=pathlib.Path,default=root/'artifacts/Luma/Luma.App.exe');a=p.parse_args()
if a.include_native_hdr and not a.mixed_formats:p.error('--include-native-hdr requires --mixed-formats')
if a.viewer_stress and not a.read_only_folder:p.error('--viewer-stress requires --read-only-folder')
if sum(bool(v) for v in (a.large_photo,a.large_folder,a.read_only_folder,a.mixed_formats))>1:p.error('Choose one gallery fixture mode')
def source_metadata(folder):
    supported={'.jpg','.jpeg','.png','.bmp','.gif','.tif','.tiff','.jxr','.wdp','.hdp','.ico'}
    photos={}
    for directory,children,files in os.walk(folder,followlinks=False):
        children[:]=[name for name in children if not (pathlib.Path(directory)/name).is_junction() and not (pathlib.Path(directory)/name).is_symlink()]
        for name in files:
            f=pathlib.Path(directory)/name
            if f.suffix.lower() in supported and not name.startswith('._'):
                stat=f.stat();photos[f.resolve()]=(stat.st_size,stat.st_mtime_ns)
    return photos
source_before=source_metadata(a.read_only_folder.resolve()) if a.read_only_folder else {}
if a.read_only_folder and a.count:p.error('--read-only-folder indexes the complete album; omit --count')
originals=sorted(f.resolve() for f in a.large_folder.iterdir() if f.is_file() and f.suffix.lower() in ('.jpg','.jpeg') and not f.name.startswith('._')) if a.large_folder else []
count=a.count or (len(source_before) if a.read_only_folder else min(96,len(originals)) if a.large_folder else 96 if a.large_photo else 120 if a.mixed_formats else 600)
if count<64:p.error('At least 64 photographs are required to exercise virtualization')
folder=root/'artifacts'/('gallery-performance-'+time.strftime('%Y%m%d-%H%M%S'));source=folder/'source';source.mkdir(parents=True)
fixtures=[]
original_hashes={}
if a.read_only_folder:
    inputs=sorted(source_before)
    sampled=[inputs[i*(len(inputs)-1)//31] for i in range(32)]
    for original in dict.fromkeys(sampled):original_hashes[original]=hashlib.sha256(original.read_bytes()).hexdigest()
    dataset={'kind':'complete-read-only-album','count':count,'sourceBytes':sum(s[0] for s in source_before.values()),'sampledOriginalHashes':len(original_hashes),'sourceHashes':list(original_hashes.values()),'fixtureBytes':0,'filesystemCacheCleared':False}
    if a.viewer_stress:
        fixture=source/'thumbnail-dedupe.png';Image.new('RGB',(2048,1024),(48,96,160)).save(fixture);fixtures.append(fixture);dataset['fixtureBytes']=fixture.stat().st_size
elif a.large_photo or a.large_folder:
    if a.large_folder and len(originals)<count:p.error('The folder has fewer JPEG photographs than requested')
    inputs=originals[:count] if a.large_folder else [a.large_photo.resolve()]*count
    dimensions=[]
    for original in dict.fromkeys(inputs):
        original_hashes[original]=hashlib.sha256(original.read_bytes()).hexdigest()
        with Image.open(original) as image:
            if image.format!='JPEG':p.error('Large-photo fixtures require actual JPEG files')
            dimensions.append(list(image.size))
    total_bytes=sum(original.stat().st_size for original in inputs)
    if total_bytes>4*1024**3 or shutil.disk_usage(source).free<total_bytes+2*1024**3:p.error('Owned JPEG fixtures exceed the disk budget')
    for i,original in enumerate(inputs):
        target=source/f'photo-{i:04d}.jpg';shutil.copyfile(original,target);fixtures.append(target)
    dataset={'kind':'diverse-JPEG-copies' if a.large_folder else 'large-JPEG-copies','count':count,'dimensions':dimensions,'uniqueSourceImages':len(set(original_hashes.values())),'sourceHashes':list(original_hashes.values()),'fixtureBytes':total_bytes,'filesystemCacheCleared':False}
elif a.mixed_formats:
    formats=[('jpg','JPEG'),('png','PNG'),('bmp','BMP'),('gif','GIF'),('tif','TIFF'),('ico','ICO')]
    for i in range(count):
        ext,fmt=formats[i%len(formats)];target=source/f'photo-{i:04d}.{ext}'
        image=Image.new('RGBA' if ext=='ico' else 'RGB',(32,32) if ext=='ico' else (128,96),(i*13%256,i*5%256,i*2%256));image.save(target,format=fmt);fixtures.append(target)
    dataset={'kind':'mixed-SDR-fixtures','count':count,'formats':[f[0] for f in formats],'fixtureBytes':sum(f.stat().st_size for f in fixtures),'filesystemCacheCleared':False}
    if a.include_native_hdr:
        hdr=root/'artifacts/native-tests/linear-hdr.jxr';assert hdr.is_file(),'Generate the existing native HDR fixture first'
        fixtures[0].unlink();target=source/'photo-0000.jxr';shutil.copyfile(hdr,target);fixtures[0]=target
        dataset['kind']='mixed-SDR-and-native-HDR-fixtures';dataset['formats'].append('jxr');dataset['fixtureBytes']=sum(f.stat().st_size for f in fixtures)
else:
    w,h=128,96;size=w*h*3
    header=struct.pack('<2sIHHI',b'BM',54+size,0,0,54)+struct.pack('<IiiHHIIiiII',40,w,h,1,24,0,size,2835,2835,0,0)
    for i in range(count):
        pixels=bytes(c for y in range(h) for x in range(w) for c in ((x*2+i)%256,(y*2+i*5)%256,(x+y+i*13)%256))
        target=source/f'photo-{i:04d}.bmp';target.write_bytes(header+pixels);fixtures.append(target)
    dataset={'kind':'small-BMP-fixtures','count':count,'dimensions':[w,h],'fixtureBytes':sum(f.stat().st_size for f in fixtures),'filesystemCacheCleared':False}
dataset['shellControl']=a.shell_control
dataset['traceProfile']='cpu-sampling' if a.cpu_trace else 'gc-verbose' if a.trace else None
folder.joinpath('dataset.json').write_text(json.dumps(dataset,indent=2),encoding='utf-8')
print('Prepared isolated gallery fixture:',{k:v for k,v in dataset.items() if k not in ('dimensions','sourceHashes')},flush=True)
command=[str(a.app.resolve()),'--test-library',str(folder.resolve()),'--verify-album-viewer' if a.viewer_stress else '--verify-gallery-performance']
if a.read_only_folder:command+=['--gallery-source',str(a.read_only_folder.resolve())]
if a.shell_control:command.append('--shell-control')
if a.trace or a.cpu_trace:
    profile=['--profile','cpu-sampling','--providers','Microsoft-Windows-DotNETRuntime:0x8003:5','--format','Speedscope'] if a.cpu_trace else ['--profile','gc-verbose']
    command=[str(root/'.tools/performance/dotnet-trace.exe'),'collect',*profile,'--buffersize','64','--output',str(folder/'gallery.nettrace'),'--']+command
proc=subprocess.Popen(command)
try:code=proc.wait(timeout=90)
except subprocess.TimeoutExpired:proc.terminate();proc.wait(timeout=15);raise
data=json.loads(folder.joinpath('album-viewer-performance.json' if a.viewer_stress else 'gallery-performance.json').read_text(encoding='utf-8'));assert code==0 and data['complete'],data
assert data['count']==count,data
if not a.viewer_stress:assert data['refreshCoalescingVerified'] and data['visibleThumbnails']>0 and data['cachedThumbnailBytes']<=32*1024**2,data
if a.include_native_hdr:assert data['totalShellLoads']>0,'Native HDR thumbnail did not preserve the Windows provider'
for original,original_hash in original_hashes.items():assert hashlib.sha256(original.read_bytes()).hexdigest()==original_hash,'Read-only original was changed'
if a.read_only_folder:assert source_metadata(a.read_only_folder.resolve())==source_before,'Read-only album membership or file metadata changed'
print({k:v for k,v in data.items() if k not in ('dispatcherLatency','dispatcherSamples','operations','resources','results')})
if data.get('resources'):
    print('Peak process memory (includes WinUI/native/transient buffers):',{key:max(sample[key] for sample in data['resources']) for key in ('workingSetBytes','privateBytes')})
if a.viewer_stress:
    if 'thumbnailRequestSharingVerified' in data:assert data['thumbnailRequestSharingVerified']
    if any('largerThumbnailCacheHits' in r for r in data['resources']):
        assert max(r.get('largerThumbnailCacheHits',0) for r in data['resources'])>0,'No completed gallery thumbnail was reused by the compact filmstrip'
    for scenario in ('random-SDR','prefetched-SDR'):
        samples=sorted(r['firstFrame'] for r in data['results'] if r['scenario']==scenario)
        print(f'{scenario}: n={len(samples)}, median={statistics.median(samples):.2f} ms, P95={samples[int(len(samples)*.95)]:.2f} ms, max={max(samples):.2f} ms; {folder}')
else:
    samples=sorted(data['dispatcherLatency']);print(f'UI callback median {statistics.median(samples):.2f} ms, P95 {samples[int(len(samples)*.95)]:.2f} ms, max {max(samples):.2f} ms; {folder}')
if not a.keep_fixtures:
    owned=source.resolve();workspace_artifacts=(root/'artifacts').resolve()
    assert owned.is_relative_to(workspace_artifacts) and owned.parent==folder.resolve()
    for fixture in fixtures:
        assert fixture.resolve().parent==owned
        fixture.unlink()
    print('Removed only the generated photo copies; reports retained.',flush=True)
