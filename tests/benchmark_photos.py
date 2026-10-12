"""Separate first image / completed processing in real WinUI and NVIDIA paths.
The large input is read-only. SDK navigation uses owned 1280-edge JPEG fixtures
derived from it, with distinct paths per mode; this is not a large-photo AI test.
"""
import argparse, json, pathlib, statistics, subprocess, time
from PIL import Image

root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('photo',type=pathlib.Path);p.add_argument('--output',type=pathlib.Path);a=p.parse_args()
output=(a.output or root/'artifacts'/('photo-performance-'+time.strftime('%Y%m%d-%H%M%S'))).resolve();output.mkdir(parents=True)
with Image.open(a.photo) as source:
    image=source.convert('RGB');image.thumbnail((1280,1280),Image.Resampling.LANCZOS)
    for mode in ['SDR','HDR','VSR','HDR+VSR']:
        folder=output/mode;folder.mkdir()
        for n in range(12):image.save(folder/f'{n:02}.jpg',quality=95)
    fixture_size=image.size
proc=subprocess.Popen([str(root/'artifacts/Luma/Luma.App.exe'),'--test-library',str(output),'--verify-photo-performance',str(a.photo.resolve())])
try:code=proc.wait(timeout=240)
except subprocess.TimeoutExpired:proc.terminate();proc.wait(timeout=15);raise
data=json.loads((output/'photo-performance.json').read_text(encoding='utf-8'));assert code==0 and data['complete'],data
rows=data['results'];assert len(rows)==75,len(rows)
assert all(r['frame']['OriginalWidth']==fixture_size[0] and r['frame']['OriginalHeight']==fixture_size[1] for r in rows if r['mode'] in ['SDR','HDR','VSR','HDR+VSR'])
for mode in ['SDR','HDR','VSR','HDR+VSR']:
    initial=next(r for r in rows if r['mode']==mode and r['kind']=='new-window')
    print(f'{mode}: new window first {initial["firstPhotoMilliseconds"]:.1f} ms, completed {initial["completedMilliseconds"]:.1f} ms')
    for kind in ['random','prefetched-or-cached']:
        samples=[r for r in rows if r['mode']==mode and r['kind']==kind]
        first=[r['firstPhotoMilliseconds'] for r in samples];done=[r['completedMilliseconds'] for r in samples]
        print(f'  {kind}: first median/max {statistics.median(first):.1f}/{max(first):.1f} ms, completed {statistics.median(done):.1f}/{max(done):.1f} ms')
        assert all(r['HdrEffective']==int('HDR' in mode) and r['VsrEffective']==int('VSR' in mode) for r in samples)
print(f'PASS: 75 measurements/checks; actual effects, latest selection, direct-file metadata and unavailable-file error. SDK fixtures {fixture_size}; large input {a.photo.name}. {output}')
