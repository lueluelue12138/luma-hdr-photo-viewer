"""Fresh-process, lossless thumbnail cache checks on a read-only album.

Does not clear Windows file caches. Input metadata and sampled content hashes
are checked before/after; only isolated databases and derived caches are written.
"""
import argparse,hashlib,json,os,pathlib,shutil,statistics,subprocess,time

root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('folder',type=pathlib.Path);p.add_argument('--app',type=pathlib.Path,default=root/'artifacts/Luma/Luma.App.exe');a=p.parse_args()
source=a.folder.resolve()
supported={'.jpg','.jpeg','.png','.bmp','.gif','.tif','.tiff','.jxr','.wdp','.hdp','.ico'}
def snapshot():
    result={}
    for directory,children,files in os.walk(source,followlinks=False):
        children[:]=[n for n in children if not (pathlib.Path(directory)/n).is_junction() and not (pathlib.Path(directory)/n).is_symlink()]
        for name in files:
            f=pathlib.Path(directory)/name
            if f.suffix.lower() in supported and not name.startswith('._'):
                s=f.stat();result[f.resolve()]=(s.st_size,s.st_mtime_ns)
    return result
before=snapshot();assert len(before)>=64
files=sorted(before);sampled=[files[n*(len(files)-1)//31] for n in range(32)]
def digest(path):
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
hashes={f:digest(f) for f in sampled}
output=root/'artifacts'/('thumbnail-cache-'+time.strftime('%Y%m%d-%H%M%S'));output.mkdir()
def launch(folder,arguments,report):
    folder.mkdir(exist_ok=True)
    process=subprocess.run([str(a.app.resolve()),'--test-library',str(folder.resolve()),*arguments],timeout=90)
    f=folder/report;data=json.loads(f.read_text(encoding='utf-8'));assert process.returncode==0 and data['complete'],data
    return data
check=launch(output/'correctness',['--verify-thumbnail-disk-cache'],'thumbnail-disk-cache.json');assert check['checks']>=11;print('PASS:',check,flush=True)
rows=[]
for kind in ('first','restarted','restarted-again','disabled'):
    library=output/('control' if kind=='disabled' else 'shared')
    args=['--verify-gallery-performance','--gallery-source',str(source)]
    if kind=='disabled':args.append('--no-thumbnail-disk-cache')
    data=launch(library,args,'gallery-performance.json');assert data['count']==len(before) and data['unchangedRefreshLoads']==0
    shutil.copyfile(library/'gallery-performance.json',output/(kind+'.json'))
    delays=sorted(data['dispatcherLatency']);row={'kind':kind,'count':data['count'],'firstVisibleMilliseconds':data['firstVisibleMilliseconds'],'firstVisibleCount':data['firstVisibleCount'],'afterScrollMilliseconds':data['visibleReadyMilliseconds'],'wicLoads':data['totalWicLoads'],'diskHits':data['diskCacheHits'],'diskWrites':data['diskCacheWrites'],'diskBytes':data['diskCacheBytes'],'uiP95ms':delays[int(len(delays)*.95)],'uiMaxMs':max(delays)}
    rows.append(row);print(row,flush=True)
assert rows[0]['diskWrites']>0 and rows[1]['diskHits']>0 and rows[3]['diskHits']==rows[3]['diskWrites']==0
assert all(r['diskBytes']<=256*1024**2 for r in rows)
assert snapshot()==before and all(digest(f)==h for f,h in hashes.items()),'Read-only originals changed'
(output/'summary.json').write_text(json.dumps({'complete':True,'rows':rows,'sourcePhotos':len(before),'sourceBytes':sum(v[0] for v in before.values()),'sourceHashesVerified':len(hashes),'filesystemCacheCleared':False},indent=2),encoding='utf-8')
print('PASS: lossless cache, corruption/source change/eviction/native HDR rejection, fresh-process reuse and read-only originals.',output)
