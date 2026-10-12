"""Native upload/allocation benchmark, excluding disk decode and WinUI startup.

Uses the read-only full-resolution JPEG plus owned alternating color fixtures.
Reports CPU submission and completed GPU work separately. This is not a screen
latency test. An independent fingerprint checks that reused buffers get new data.
"""
import argparse, ctypes as c, json, pathlib, statistics, time
from PIL import Image, ImageDraw

root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('photo',type=pathlib.Path);p.add_argument('--dll',type=pathlib.Path,default=root/'src/Luma.Native/bin/Luma.Native.dll');p.add_argument('--output',type=pathlib.Path,required=True);p.add_argument('--expect-reuse',action='store_true');p.add_argument('--event-query',action='store_true');a=p.parse_args()
a.output.mkdir(parents=True,exist_ok=True)
for n,color in enumerate([(40,80,160),(180,60,30)]):
    image=Image.new('RGB',(1280,853),color);ImageDraw.Draw(image).rectangle((380,200,900,550),fill=(255,255,255));image.save(a.output/f'color-{n}.png')

class Info(c.Structure):_fields_=[('width',c.c_uint),('height',c.c_uint),('ow',c.c_uint),('oh',c.c_uint),('hdr',c.c_int),('downsampled',c.c_int),('bytes',c.c_uint64)]
class Status(c.Structure):_fields_=[('gpu',c.c_wchar*128),('display',c.c_wchar*128),('nvidia',c.c_int),('hdrActive',c.c_int),('fp16',c.c_int),('nativeHdr',c.c_int),('maxNits',c.c_float),('sdrWhite',c.c_float),('gpuBytes',c.c_uint64),('hdrEffective',c.c_int),('vsrEffective',c.c_int),('sdkAvailable',c.c_int),('hdrAvailable',c.c_int),('vsrAvailable',c.c_int),('ngxResult',c.c_uint),('hdrEvaluations',c.c_uint),('vsrEvaluations',c.c_uint),('hdrMessage',c.c_wchar*256),('vsrMessage',c.c_wchar*256)]
class Metrics(c.Structure):_fields_=[(n,c.c_uint64) for n in ('originalAllocations','inputAllocations','hdrAllocations','vsrAllocations','alphaScans')]+[(n,c.c_double) for n in ('lastUseMilliseconds','lastAlphaMilliseconds')]
d=c.CDLL(str(a.dll.resolve()));d.LumaError.restype=c.c_wchar_p
for name,args in {
    'LumaDecode':[c.c_wchar_p,c.POINTER(Info),c.POINTER(c.c_void_p)],'LumaFreeFrame':[c.c_void_p],
    'LumaCreateFast':[c.c_void_p,c.c_void_p,c.POINTER(c.c_void_p)],'LumaInitEffects':[c.c_void_p],'LumaDestroy':[c.c_void_p],
    'LumaPrepareResize':[c.c_void_p,c.c_uint,c.c_uint,c.c_float,c.c_float],'LumaPrepareFrame':[c.c_void_p,c.c_void_p],
    'LumaPrepareEffects':[c.c_void_p,c.c_int,c.c_int,c.c_int,c.c_uint],'LumaRepaint':[c.c_void_p],'LumaWaitForGpu':[c.c_void_p],
    'LumaSetOriginal':[c.c_void_p,c.c_int],'LumaStatus':[c.c_void_p,c.POINTER(Status)],
    'LumaGetTextureMetrics':[c.c_void_p,c.POINTER(Metrics)],'LumaReadPixel':[c.c_void_p,c.c_uint,c.c_uint,c.POINTER(c.c_uint16)],
}.items():getattr(d,name).argtypes=args

def check(hr):
    if hr<0:raise RuntimeError(f'{hr & 0xffffffff:08x}: {d.LumaError()}')
def status(r):
    value=Status();check(d.LumaStatus(r,c.byref(value)));return value
def metrics(r):
    value=Metrics();check(d.LumaGetTextureMetrics(r,c.byref(value)));return {n:getattr(value,n) for n,_ in Metrics._fields_}
def fingerprint(r):
    result=[]
    for x,y in [(60,60),(256,170),(420,250)]:
        value=(c.c_uint16*4)();check(d.LumaReadPixel(r,x,y,value));result.extend(value)
    return result

frames=[];report={'complete':False,'completionMethod':'event-query+readback' if a.event_query else 'FP16-readback','results':[]}
kernel=c.WinDLL('kernel32');kernel.GetModuleHandleW.argtypes=[c.c_wchar_p];kernel.GetModuleHandleW.restype=c.c_void_p
try:
    for path in [a.photo.resolve(),a.output/'color-0.png',a.output/'color-1.png']:
        info=Info();frame=c.c_void_p();check(d.LumaDecode(str(path),c.byref(info),c.byref(frame)));frames.append((frame,info))
    assert frames[0][1].ow==frames[0][1].width and frames[0][1].oh==frames[0][1].height,'Large frame must retain full resolution'
    for mode,choices,hdr,vsr in [('large-SDR',[0],0,0),('large-HDR',[0],1,0),('small-VSR',[1,2],0,1),('small-HDR',[1,2],1,0),('small-HDR+VSR',[1,2],1,1)]:
        r=c.c_void_p();check(d.LumaCreateFast(None,None,c.byref(r)))
        try:
            check(d.LumaPrepareResize(r,512,342,1,1))
            if hdr or vsr:check(d.LumaInitEffects(r))
            references={};original_references={};samples=[]
            # Warm each distinct frame once, saving actual original/enhanced pixels.
            for index in choices:
                check(d.LumaPrepareFrame(r,frames[index][0]));check(d.LumaPrepareEffects(r,hdr,vsr,0,1));check(d.LumaRepaint(r))
                if a.event_query:check(d.LumaWaitForGpu(r))
                references[index]=fingerprint(r)
                check(d.LumaSetOriginal(r,1));check(d.LumaRepaint(r));original_references[index]=fingerprint(r);check(d.LumaSetOriginal(r,0))
            if len(choices)>1:assert references[choices[0]]!=references[choices[1]],'Fixture output fingerprints must distinguish different photos'
            before=metrics(r);last=status(r)
            for n in range(16):
                index=choices[n%len(choices)];start=time.perf_counter();check(d.LumaPrepareFrame(r,frames[index][0]));upload=(time.perf_counter()-start)*1000
                check(d.LumaPrepareEffects(r,hdr,vsr,0,1));check(d.LumaRepaint(r));submit=(time.perf_counter()-start)*1000
                if a.event_query:check(d.LumaWaitForGpu(r))
                output_pixels=fingerprint(r);completed=(time.perf_counter()-start)*1000
                state=status(r);assert (state.hdrEffective,state.vsrEffective)==(hdr,vsr),(mode,state.hdrMessage,state.vsrMessage)
                assert state.hdrEvaluations-last.hdrEvaluations==hdr and state.vsrEvaluations-last.vsrEvaluations==vsr,'Every new frame must really call only the requested SDK feature'
                assert output_pixels==references[index],'Enhancement must use this frame, not stale input/output'
                check(d.LumaSetOriginal(r,1));check(d.LumaRepaint(r));assert fingerprint(r)==original_references[index],'Original compare must retain the correct full-detail source'
                check(d.LumaSetOriginal(r,0));check(d.LumaRepaint(r));assert fingerprint(r)==references[index]
                after_compare=status(r);assert (after_compare.hdrEvaluations,after_compare.vsrEvaluations)==(state.hdrEvaluations,state.vsrEvaluations),'Comparison must not reevaluate AI'
                last=state;sample_metrics=metrics(r);samples.append({'index':index,'uploadCpuMilliseconds':upload,'submissionMilliseconds':submit,'completedReadbackMilliseconds':completed,'metrics':sample_metrics,'gpuTextureBytes':state.gpuBytes})
            after=metrics(r);allocations={n:after[n]-before[n] for n in ('originalAllocations','inputAllocations','hdrAllocations','vsrAllocations','alphaScans')}
            if a.expect_reuse:assert all(v==0 for v in allocations.values()),(mode,allocations)
            row={'mode':mode,'frameSizes':[[frames[i][1].width,frames[i][1].height] for i in choices],'warmAllocations':before,'steadyAllocations':allocations,'samples':samples,'gpuTextureBytes':last.gpuBytes,'gppLoaded':bool(kernel.GetModuleHandleW('GPP64.dll'))};report['results'].append(row)
            a.output.joinpath('texture-performance.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
            print(mode,allocations,'upload/readback median',round(statistics.median(s['uploadCpuMilliseconds'] for s in samples),2),round(statistics.median(s['completedReadbackMilliseconds'] for s in samples),2),'ms',flush=True)
        finally:check(d.LumaDestroy(r))
    report['complete']=True
finally:
    for frame,_ in frames:d.LumaFreeFrame(frame)
    a.output.joinpath('texture-performance.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
assert report['complete']
print('PASS: original and real enhanced fingerprints; SDK call separation; full-detail source; cached comparison.',a.output.resolve())
