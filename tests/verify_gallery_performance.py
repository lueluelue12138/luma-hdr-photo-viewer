"""600 small owned BMP fixtures; real WinUI virtualization and rapid scrolling."""
import json,pathlib,statistics,struct,subprocess,time
root=pathlib.Path(__file__).resolve().parents[1]
folder=root/'artifacts'/('gallery-performance-'+time.strftime('%Y%m%d-%H%M%S'));source=folder/'source';source.mkdir(parents=True)
w,h=128,96;size=w*h*3
header=struct.pack('<2sIHHI',b'BM',54+size,0,0,54)+struct.pack('<IiiHHIIiiII',40,w,h,1,24,0,size,2835,2835,0,0)
for i in range(600):
    pixels=bytes(c for y in range(h) for x in range(w) for c in ((x*2+i)%256,(y*2+i*5)%256,(x+y+i*13)%256))
    source.joinpath(f'photo-{i:04d}.bmp').write_bytes(header+pixels)
proc=subprocess.Popen([str(root/'artifacts/Luma/Luma.App.exe'),'--test-library',str(folder.resolve()),'--verify-gallery-performance'])
try:code=proc.wait(timeout=90)
except subprocess.TimeoutExpired:proc.terminate();proc.wait(timeout=15);raise
data=json.loads(folder.joinpath('gallery-performance.json').read_text(encoding='utf-8'));assert code==0 and data['complete'],data
samples=sorted(data['dispatcherLatency']);print({k:v for k,v in data.items() if k!='dispatcherLatency'})
print(f'UI callback median {statistics.median(samples):.2f} ms, P95 {samples[int(len(samples)*.95)]:.2f} ms, max {max(samples):.2f} ms; {folder}')
