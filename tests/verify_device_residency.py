"""Bound actual DXGI process residency across changing SDK resources and windows.

This is a headless local probe, excludes physical screen timing and external UI
overlays. Owned opaque/alpha fixtures; real SDR/HDR/VSR pixels are read back.
"""
import ctypes as c,json,pathlib,time,argparse
from PIL import Image,ImageDraw
root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--output',type=pathlib.Path,required=True);p.add_argument('--expect-stable',action='store_true');a=p.parse_args();a.output.mkdir(parents=True,exist_ok=True)
class Info(c.Structure):_fields_=[('w',c.c_uint),('h',c.c_uint),('ow',c.c_uint),('oh',c.c_uint),('hdr',c.c_int),('down',c.c_int),('bytes',c.c_uint64)]
class Status(c.Structure):_fields_=[('gpu',c.c_wchar*128),('display',c.c_wchar*128),('nvidia',c.c_int),('hdrActive',c.c_int),('fp16',c.c_int),('nativeHdr',c.c_int),('maxNits',c.c_float),('sdrWhite',c.c_float),('gpuBytes',c.c_uint64),('hdrEffective',c.c_int),('vsrEffective',c.c_int),('sdkAvailable',c.c_int),('hdrAvailable',c.c_int),('vsrAvailable',c.c_int),('ngxResult',c.c_uint),('hdrEvaluations',c.c_uint),('vsrEvaluations',c.c_uint),('hdrMessage',c.c_wchar*256),('vsrMessage',c.c_wchar*256)]
d=c.CDLL(str(root/'src/Luma.Native/bin/Luma.Native.dll'));d.LumaError.restype=c.c_wchar_p
for name,args in {'LumaDecode':[c.c_wchar_p,c.POINTER(Info),c.POINTER(c.c_void_p)],'LumaFreeFrame':[c.c_void_p],'LumaCreateFast':[c.c_void_p,c.c_void_p,c.POINTER(c.c_void_p)],'LumaInitEffects':[c.c_void_p],'LumaDestroy':[c.c_void_p],'LumaPrepareResize':[c.c_void_p,c.c_uint,c.c_uint,c.c_float,c.c_float],'LumaPrepareFrame':[c.c_void_p,c.c_void_p],'LumaPrepareEffects':[c.c_void_p,c.c_int,c.c_int,c.c_int,c.c_uint],'LumaRepaint':[c.c_void_p],'LumaCopyStatus':[c.c_void_p,c.POINTER(Status)],'LumaReadPixel':[c.c_void_p,c.c_uint,c.c_uint,c.POINTER(c.c_uint16)],'LumaVideoMemory':[c.c_void_p,c.POINTER(c.c_uint64),c.POINTER(c.c_uint64)]}.items():getattr(d,name).argtypes=args
def check(hr):
    if hr<0:raise RuntimeError(d.LumaError())
def fingerprint(r):
    result=[]
    for x,y in [(60,60),(128,170),(420,250)]:
        pixel=(c.c_uint16*4)();check(d.LumaReadPixel(r,x,y,pixel));result.extend(pixel)
    return result
frames=[];report={'complete':False,'results':[]};reference_sets={}
kernel=c.WinDLL('kernel32');kernel.GetModuleHandleW.argtypes=[c.c_wchar_p];kernel.GetModuleHandleW.restype=c.c_void_p;report['gppLoaded']=bool(kernel.GetModuleHandleW('GPP64.dll'))
try:
    for n,(size,color) in enumerate([((640,426),(40,80,160)),((960,640),(180,60,30)),((640,960),(40,160,60))]):
        image=Image.new('RGB',size,color);ImageDraw.Draw(image).rectangle((size[0]*.35,size[1]*.3,size[0]*.6,size[1]*.65),fill=(255,255,255));path=a.output/f'photo-{n}.png';image.save(path);frame=c.c_void_p();info=Info();check(d.LumaDecode(str(path),c.byref(info),c.byref(frame)));frames.append(frame)
    for mode,hdr,vsr in [('HDR',1,0),('VSR',0,1),('HDR+VSR',1,1)]:
        r=c.c_void_p();check(d.LumaCreateFast(None,None,c.byref(r)));check(d.LumaInitEffects(r));references={};samples=[];last=Status()
        try:
            check(d.LumaPrepareResize(r,512,342,1,1))
            for n in range(36):
                index=n%len(frames);start=time.perf_counter();check(d.LumaPrepareFrame(r,frames[index]));check(d.LumaPrepareEffects(r,hdr,vsr,0,1));check(d.LumaRepaint(r));actual=fingerprint(r);status=Status();check(d.LumaCopyStatus(r,c.byref(status)))
                assert (status.hdrEffective,status.vsrEffective)==(hdr,vsr),(mode,status.hdrMessage,status.vsrMessage)
                assert (status.hdrEvaluations-last.hdrEvaluations,status.vsrEvaluations-last.vsrEvaluations)==(hdr,vsr)
                if index in references:assert references[index]==actual,'New resource dimensions displayed stale or different enhanced pixels'
                else:references[index]=actual
                budget=c.c_uint64();usage=c.c_uint64();check(d.LumaVideoMemory(r,c.byref(budget),c.byref(usage)));samples.append({'photo':n+1,'index':index,'processLocalGpuBytes':usage.value,'ownTextureBytes':status.gpuBytes,'completeReadbackMilliseconds':(time.perf_counter()-start)*1000});last=status
            values=[s['processLocalGpuBytes'] for s in samples[-12:]];spread=max(values)-min(values)
            report['results'].append({'mode':mode,'samples':samples,'steadyResidencySpreadBytes':spread});a.output.joinpath('device-residency.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
            print(mode,'first/last actual GPU MiB',round(samples[0]['processLocalGpuBytes']/1048576,1),round(samples[-1]['processLocalGpuBytes']/1048576,1),'last 12 spread',round(spread/1048576,1),flush=True)
            if a.expect_stable:assert spread<64*1048576,'SDK residency still grows across changing resource sizes'
            reference_sets[mode]=references
        finally:d.LumaDestroy(r)
    active=[];session_samples=[]
    def create():
        r=c.c_void_p();check(d.LumaCreateFast(None,None,c.byref(r)));active.append(r);check(d.LumaPrepareResize(r,512,342,1,1));check(d.LumaInitEffects(r));return r
    def enhance(r,index,hdr,vsr):
        check(d.LumaPrepareFrame(r,frames[index]));check(d.LumaPrepareEffects(r,hdr,vsr,0,1));check(d.LumaRepaint(r));actual=fingerprint(r);state=Status();check(d.LumaCopyStatus(r,c.byref(state)))
        assert (state.hdrEffective,state.vsrEffective)==(hdr,vsr),(hex(state.ngxResult),state.hdrMessage,state.vsrMessage)
        mode='HDR+VSR' if hdr and vsr else 'HDR' if hdr else 'VSR'
        assert actual==reference_sets[mode][index],'Another context changed this context\'s real SDK output'
    def destroy(r):d.LumaDestroy(r);active.remove(r)
    try:
        first=create();second=create();enhance(first,0,1,0);enhance(second,1,1,1);destroy(first)
        # This formerly returned NGX FAIL_NotInitialized after another device
        # shut down. New dimensions force real feature recreation on the survivor.
        for n in range(16):
            enhance(second,n%3,1,0)
            transient=create();enhance(transient,(n+1)%3,1,1);destroy(transient)
            enhance(second,(n+2)%3,1,0)
            budget=c.c_uint64();usage=c.c_uint64();check(d.LumaVideoMemory(second,c.byref(budget),c.byref(usage)));session_samples.append(usage.value)
        if a.expect_stable:assert max(session_samples[-8:])-min(session_samples[-8:])<128*1048576,'Retired registered devices accumulate unbounded residency'
        destroy(second)
        restarted=create();enhance(restarted,0,1,0);destroy(restarted)
        report['results'].append({'mode':'surviving-context-and-global-restart','actualProcessLocalGpuBytes':session_samples,'transientWindows':16})
        print('PASS: surviving context after peer teardown, 16 transient devices, final global SDK restart.',flush=True)
    finally:
        for r in active:d.LumaDestroy(r)
    report['complete']=True
finally:
    for frame in frames:d.LumaFreeFrame(frame)
    a.output.joinpath('device-residency.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
assert report['complete'];print('PASS: real changing-size SDK pixels; '+('stable actual process GPU residency.' if a.expect_stable else 'residency measured without a stability assertion.'),a.output.resolve())
