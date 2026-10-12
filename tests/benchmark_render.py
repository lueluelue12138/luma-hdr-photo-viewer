"""Measure real D3D/Direct2D submissions and GPU completion, without NGX or UI automation."""
import argparse, ctypes as c, json, pathlib, statistics, time
from PIL import Image

root = pathlib.Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser()
p.add_argument('--dll', type=pathlib.Path, default=root/'artifacts/Luma/Luma.Native.dll')
p.add_argument('--output', type=pathlib.Path, required=True)
a = p.parse_args()
a.output.parent.mkdir(parents=True, exist_ok=True)
photo = a.output.parent/'render-fixture.png'
Image.new('RGB', (2048,1365),(80,130,190)).save(photo)
print('Creating renderer',flush=True)
d = c.CDLL(str(a.dll.resolve()))
class Info(c.Structure):
    _fields_ = [('width',c.c_uint),('height',c.c_uint),('ow',c.c_uint),('oh',c.c_uint),('hdr',c.c_int),('downsampled',c.c_int),('bytes',c.c_uint64)]
class Rect(c.Structure):
    _fields_ = [(name,c.c_float) for name in ('left','top','width','height','radius','opacity')]
d.LumaError.restype=c.c_wchar_p
d.LumaCreateFast.argtypes=[c.c_void_p,c.c_void_p,c.POINTER(c.c_void_p)]
d.LumaDecode.argtypes=[c.c_wchar_p,c.POINTER(Info),c.POINTER(c.c_void_p)]
d.LumaUseFrame.argtypes=[c.c_void_p,c.c_void_p]
d.LumaResize.argtypes=[c.c_void_p,c.c_uint,c.c_uint,c.c_float,c.c_float]
d.LumaView.argtypes=[c.c_void_p,c.c_float,c.c_float,c.c_float,c.c_int]
d.LumaSetGlass.argtypes=[c.c_void_p,c.POINTER(Rect),c.c_uint,c.c_int]
for name in ('LumaRepaint','LumaWaitForGpu','LumaDestroy','LumaFreeFrame'):
    getattr(d,name).argtypes=[c.c_void_p]
def check(hr):
    if hr<0: raise RuntimeError(d.LumaError())
r=c.c_void_p(); frame=c.c_void_p(); info=Info(); results=[]
t=time.perf_counter();check(d.LumaCreateFast(None,None,c.byref(r)));creation=(time.perf_counter()-t)*1000
print(f'Renderer created in {creation:.1f} ms',flush=True)
try:
    check(d.LumaDecode(str(photo),c.byref(info),c.byref(frame)))
    for width,height in ((1920,1080),(3840,2160)):
        check(d.LumaResize(r,width,height,1,1));check(d.LumaUseFrame(r,frame))
        rects=(Rect*2)(Rect(width/2-260,20,520,56,12,1),Rect(width/2-460,height-132,920,104,13,1))
        check(d.LumaSetGlass(r,rects,2,1));check(d.LumaRepaint(r));check(d.LumaWaitForGpu(r))
        for scenario in ('view','chrome'):
            samples=[];batch_start=time.perf_counter()
            for n in range(30):
                start=time.perf_counter()
                if scenario=='view':check(d.LumaView(r,1+n*.002,0,0,0))
                else:
                    rects[0].opacity=rects[1].opacity=.3+n*.02
                    check(d.LumaSetGlass(r,rects,2,1));check(d.LumaRepaint(r))
                # Query completion on every submission can obscure the actual
                # CPU cost; measure submissions, then drain once per batch.
                samples.append((time.perf_counter()-start)*1000)
            drain_start=time.perf_counter();check(d.LumaWaitForGpu(r))
            results.append(dict(scenario=scenario,width=width,height=height,cpuSubmissionMilliseconds=samples,median=statistics.median(samples),p95=sorted(samples)[28],maximum=max(samples),gpuDrainMilliseconds=(time.perf_counter()-drain_start)*1000,batchCompletionMilliseconds=(time.perf_counter()-batch_start)*1000))
            print(f'{width}x{height} {scenario}: {statistics.median(samples):.2f} ms',flush=True)
finally:
    if frame:d.LumaFreeFrame(frame)
    if r:d.LumaDestroy(r)
data=dict(creationMilliseconds=creation,results=results)
a.output.write_text(json.dumps(data,indent=2),encoding='utf-8')
for row in results:print(f'{row["width"]}x{row["height"]} {row["scenario"]}: median {row["median"]:.2f} ms, P95 {row["p95"]:.2f} ms')
