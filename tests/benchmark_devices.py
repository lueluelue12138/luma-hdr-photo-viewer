"""Measure device/SDK setup and real output across separate viewer lifetimes.

Predecoded read-only source plus owned fixtures; native timing is not window or
screen latency. --reuse exercises the same bounded-cache reset used by WinUI.
"""
import argparse, ctypes as c, json, pathlib, statistics, time
from PIL import Image, ImageDraw

root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('photo',type=pathlib.Path);p.add_argument('--output',type=pathlib.Path,required=True);p.add_argument('--reuse',action='store_true');p.add_argument('--cycles',type=int,default=6);p.add_argument('--dll',type=pathlib.Path,default=root/'src/Luma.Native/bin/Luma.Native.dll');a=p.parse_args()
a.output.mkdir(parents=True,exist_ok=True)
if a.cycles<3:p.error('--cycles must include at least two warmup samples and one measured sample')
class Info(c.Structure):_fields_=[('width',c.c_uint),('height',c.c_uint),('ow',c.c_uint),('oh',c.c_uint),('hdr',c.c_int),('downsampled',c.c_int),('bytes',c.c_uint64)]
class Status(c.Structure):_fields_=[('gpu',c.c_wchar*128),('display',c.c_wchar*128),('nvidia',c.c_int),('hdrActive',c.c_int),('fp16',c.c_int),('nativeHdr',c.c_int),('maxNits',c.c_float),('sdrWhite',c.c_float),('gpuBytes',c.c_uint64),('hdrEffective',c.c_int),('vsrEffective',c.c_int),('sdkAvailable',c.c_int),('hdrAvailable',c.c_int),('vsrAvailable',c.c_int),('ngxResult',c.c_uint),('hdrEvaluations',c.c_uint),('vsrEvaluations',c.c_uint),('hdrMessage',c.c_wchar*256),('vsrMessage',c.c_wchar*256)]
d=c.CDLL(str(a.dll.resolve()));d.LumaError.restype=c.c_wchar_p
signatures={'LumaDecode':[c.c_wchar_p,c.POINTER(Info),c.POINTER(c.c_void_p)],'LumaFreeFrame':[c.c_void_p],'LumaCreateFast':[c.c_void_p,c.c_void_p,c.POINTER(c.c_void_p)],'LumaInitEffects':[c.c_void_p],'LumaDestroy':[c.c_void_p],'LumaPrepareResize':[c.c_void_p,c.c_uint,c.c_uint,c.c_float,c.c_float],'LumaPrepareFrame':[c.c_void_p,c.c_void_p],'LumaPrepareEffects':[c.c_void_p,c.c_int,c.c_int,c.c_int,c.c_uint],'LumaRepaint':[c.c_void_p],'LumaCopyStatus':[c.c_void_p,c.POINTER(Status)],'LumaReadPixel':[c.c_void_p,c.c_uint,c.c_uint,c.POINTER(c.c_uint16)],'LumaSetOriginal':[c.c_void_p,c.c_int]}
if a.reuse:signatures.update({'LumaResetForReuse':[c.c_void_p],'LumaBindWindow':[c.c_void_p,c.c_void_p],'LumaVideoMemory':[c.c_void_p,c.POINTER(c.c_uint64),c.POINTER(c.c_uint64)]})
for name,args in signatures.items():getattr(d,name).argtypes=args
def check(hr):
    if hr<0:raise RuntimeError(f'{hr & 0xffffffff:08x}: {d.LumaError()}')
def status(r):
    value=Status();check(d.LumaCopyStatus(r,c.byref(value)));return value
def pixels(r):
    result=[]
    for x,y in [(60,60),(256,170),(420,250)]:
        value=(c.c_uint16*4)();check(d.LumaReadPixel(r,x,y,value));result.extend(value)
    return result
frames=[];report={'complete':False,'reuse':a.reuse,'results':[]}
kernel=c.WinDLL('kernel32');kernel.GetModuleHandleW.argtypes=[c.c_wchar_p];kernel.GetModuleHandleW.restype=c.c_void_p;report['gppLoaded']=bool(kernel.GetModuleHandleW('GPP64.dll'))
try:
    for n,color in enumerate([(40,80,160),(180,60,30)]):
        image=Image.new('RGB',(1280,853),color);ImageDraw.Draw(image).rectangle((380,200,900,550),fill=(255,255,255));image.save(a.output/f'color-{n}.png')
    for path in [a.output/'color-0.png',a.output/'color-1.png',a.photo.resolve()]:
        frame=c.c_void_p();info=Info();check(d.LumaDecode(str(path),c.byref(info),c.byref(frame)));frames.append((frame,info))
    for mode,choices,hdr,vsr in [('SDR',[0,1],0,0),('HDR',[0,1],1,0),('VSR',[0,1],0,1),('HDR+VSR',[0,1],1,1),('large-HDR',[2],1,0)]:
        r=c.c_void_p();references={};originals={};samples=[]
        try:
            for n in range(a.cycles):
                start=time.perf_counter()
                if not r.value:check(d.LumaCreateFast(None,None,c.byref(r)))
                else:assert d.LumaBindWindow(r,None)==0,'Budget-limited device must not be rebound'
                created=(time.perf_counter()-start)*1000
                check(d.LumaPrepareResize(r,512,342,1,1));sdk_start=time.perf_counter()
                if hdr or vsr:check(d.LumaInitEffects(r))
                init=(time.perf_counter()-sdk_start)*1000
                before=status(r);index=choices[n%len(choices)]
                check(d.LumaPrepareFrame(r,frames[index][0]));effect_start=time.perf_counter();check(d.LumaPrepareEffects(r,hdr,vsr,0,1));effect=(time.perf_counter()-effect_start)*1000
                check(d.LumaRepaint(r));output=pixels(r);complete=(time.perf_counter()-start)*1000;after=status(r)
                assert (after.hdrEffective,after.vsrEffective)==(hdr,vsr),(mode,after.hdrMessage,after.vsrMessage)
                assert (after.hdrEvaluations-before.hdrEvaluations,after.vsrEvaluations-before.vsrEvaluations)==(hdr,vsr),'New lifetime must evaluate new photo; HDR-only must skip VSR'
                if index in references:assert output==references[index],'Reuse changed actual enhanced output'
                else:references[index]=output
                check(d.LumaSetOriginal(r,1));check(d.LumaRepaint(r));original=pixels(r)
                if index in originals:assert original==originals[index],'Reuse lost original pixels'
                else:originals[index]=original
                check(d.LumaSetOriginal(r,0));check(d.LumaRepaint(r));assert pixels(r)==output
                compare=status(r);assert (compare.hdrEvaluations,compare.vsrEvaluations)==(after.hdrEvaluations,after.vsrEvaluations)
                release_start=time.perf_counter()
                idle_usage=None
                if a.reuse:
                    assert d.LumaResetForReuse(r)==0,'A budget-limited device must not be retained'
                    idle=status(r);assert idle.gpuBytes==0 and idle.hdrEffective==idle.vsrEffective==idle.nativeHdr==idle.hdrEvaluations==idle.vsrEvaluations==0,'Idle device retained photo/window state'
                    budget=c.c_uint64();usage=c.c_uint64();check(d.LumaVideoMemory(r,c.byref(budget),c.byref(usage)));idle_usage=usage.value
                else:d.LumaDestroy(r);r=c.c_void_p()
                samples.append({'window':n+1,'createMilliseconds':created,'sdkInitMilliseconds':init,'effectsCpuMilliseconds':effect,'completeReadbackMilliseconds':complete,'releaseMilliseconds':(time.perf_counter()-release_start)*1000,'photoGpuBytes':after.gpuBytes,'idleProcessLocalGpuBytes':idle_usage,'source':[frames[index][1].width,frames[index][1].height]})
            if len(choices)>1:assert references[0]!=references[1],'Different photos must have different actual output'
            if a.reuse and a.cycles>=12:
                values=[s['idleProcessLocalGpuBytes'] for s in samples[-12:]]
                # Large-image SDK allocator residency can oscillate between
                # cached blocks; continuing per-window growth is not allowed.
                assert max(values)-min(values)<(256 if mode=='large-HDR' else 64)*1048576,'Retired device still accumulates per-window GPU residency'
            report['results'].append({'mode':mode,'samples':samples});a.output.joinpath('device-performance.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
            steady=samples[2:];print(mode,'create/init/effects/complete medians',*[round(statistics.median(s[k] for s in steady),2) for k in ['createMilliseconds','sdkInitMilliseconds','effectsCpuMilliseconds','completeReadbackMilliseconds']],flush=True)
        finally:
            if r.value:d.LumaDestroy(r)
    report['complete']=True
finally:
    for frame,_ in frames:d.LumaFreeFrame(frame)
    a.output.joinpath('device-performance.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
assert report['complete'];print('PASS: independent real SDK outputs, original comparison, no retained photo state.',a.output.resolve())
