"""Check buffer pruning, format/size transitions and same-photo cache safety."""
import ctypes as c, json, os, pathlib, time
from PIL import Image

root=pathlib.Path(__file__).resolve().parents[1];out=root/'artifacts/texture-transitions';out.mkdir(parents=True,exist_ok=True)
for name,size,color in [('opaque',(640,426),(70,120,190,255)),('alpha',(640,426),(210,40,90,128)),('portrait',(426,640),(160,90,40,255))]:Image.new('RGBA',size,color).save(out/f'{name}.png')
class Info(c.Structure):_fields_=[('width',c.c_uint),('height',c.c_uint),('ow',c.c_uint),('oh',c.c_uint),('hdr',c.c_int),('downsampled',c.c_int),('bytes',c.c_uint64)]
class Status(c.Structure):_fields_=[('gpu',c.c_wchar*128),('display',c.c_wchar*128),('nvidia',c.c_int),('hdrActive',c.c_int),('fp16',c.c_int),('nativeHdr',c.c_int),('maxNits',c.c_float),('sdrWhite',c.c_float),('gpuBytes',c.c_uint64),('hdrEffective',c.c_int),('vsrEffective',c.c_int),('sdkAvailable',c.c_int),('hdrAvailable',c.c_int),('vsrAvailable',c.c_int),('ngxResult',c.c_uint),('hdrEvaluations',c.c_uint),('vsrEvaluations',c.c_uint),('hdrMessage',c.c_wchar*256),('vsrMessage',c.c_wchar*256)]
class Metrics(c.Structure):_fields_=[(n,c.c_uint64) for n in ('originalAllocations','inputAllocations','hdrAllocations','vsrAllocations','alphaScans')]+[(n,c.c_double) for n in ('lastUseMilliseconds','lastAlphaMilliseconds')]
d=c.CDLL(os.environ.get('LUMA_NATIVE_TEST_DLL',str(root/'src/Luma.Native/bin/Luma.Native.dll')));d.LumaError.restype=c.c_wchar_p
for name,args in {'LumaDecode':[c.c_wchar_p,c.POINTER(Info),c.POINTER(c.c_void_p)],'LumaFreeFrame':[c.c_void_p],'LumaCreateFast':[c.c_void_p,c.c_void_p,c.POINTER(c.c_void_p)],'LumaInitEffects':[c.c_void_p],'LumaDestroy':[c.c_void_p],'LumaPrepareResize':[c.c_void_p,c.c_uint,c.c_uint,c.c_float,c.c_float],'LumaPrepareFrame':[c.c_void_p,c.c_void_p],'LumaPrepareEffects':[c.c_void_p,c.c_int,c.c_int,c.c_int,c.c_uint],'LumaRepaint':[c.c_void_p],'LumaSetOriginal':[c.c_void_p,c.c_int],'LumaStatus':[c.c_void_p,c.POINTER(Status)],'LumaGetTextureMetrics':[c.c_void_p,c.POINTER(Metrics)],'LumaReadPixel':[c.c_void_p,c.c_uint,c.c_uint,c.POINTER(c.c_uint16)]}.items():getattr(d,name).argtypes=args
def check(hr):
    if hr<0:raise RuntimeError(f'{hr & 0xffffffff:08x}: {d.LumaError()}')
def status(r):
    s=Status();check(d.LumaStatus(r,c.byref(s)));return s
def metrics(r):
    m=Metrics();check(d.LumaGetTextureMetrics(r,c.byref(m)));return m
def pixel(r):
    values=(c.c_uint16*4)();check(d.LumaReadPixel(r,128,128,values));return list(values)
def original_bytes(info):
    w,h=info.width,info.height;total=0
    while True:
        total+=w*h*(16 if info.hdr else 4)
        if w==h==1:return total
        w=max(1,w//2);h=max(1,h//2)

frames={};r=c.c_void_p();rows=[]
try:
    for name in ['opaque','alpha','portrait','native-HDR']:
        path=root/'artifacts/native-tests/linear-hdr.jxr' if name=='native-HDR' else out/f'{name}.png'
        info=Info();frame=c.c_void_p();check(d.LumaDecode(str(path),c.byref(info),c.byref(frame)));frames[name]=(frame,info)
    check(d.LumaCreateFast(None,None,c.byref(r)));check(d.LumaPrepareResize(r,256,256,1,1));check(d.LumaInitEffects(r))
    for name in ['opaque','opaque','portrait','opaque','alpha','opaque','native-HDR','opaque']:
        frame,info=frames[name];before=status(r);check(d.LumaPrepareFrame(r,frame));check(d.LumaPrepareEffects(r,1,0,0,1));check(d.LumaRepaint(r));start=time.perf_counter();enhanced=pixel(r);readback=(time.perf_counter()-start)*1000;after=status(r)
        allowed=name not in ['alpha','native-HDR'];assert after.hdrEffective==int(allowed) and not after.vsrEffective,(name,after.hdrMessage)
        assert after.hdrEvaluations-before.hdrEvaluations==int(allowed) and after.vsrEvaluations==before.vsrEvaluations
        if not allowed:assert after.gpuBytes==original_bytes(info)+256*256*8,'Obsolete SDK buffers must be released for alpha/native HDR'
        if name=='alpha':assert not after.hdrAvailable and not after.vsrAvailable and enhanced[3]==14340,'Alpha policy and half-float opacity must survive reuse'
        check(d.LumaSetOriginal(r,1));check(d.LumaRepaint(r));original=pixel(r);check(d.LumaSetOriginal(r,0));check(d.LumaRepaint(r));assert pixel(r)==enhanced
        if allowed:
            allocations=metrics(r);calls=after.hdrEvaluations
            for preset in [0,2,1]:
                check(d.LumaPrepareEffects(r,1,0,0,preset));check(d.LumaRepaint(r));pixel(r)
            assert metrics(r).hdrAllocations==allocations.hdrAllocations,'Preset changes should reuse the same FP16 allocation'
            assert status(r).hdrEvaluations==calls+3,'Presets must still reevaluate TrueHDR'
            current=pixel(r);check(d.LumaPrepareEffects(r,0,0,0,1));check(d.LumaRepaint(r));assert pixel(r)==original
            check(d.LumaPrepareEffects(r,1,0,0,1));check(d.LumaRepaint(r));assert pixel(r)==current and status(r).hdrEvaluations==calls+3,'Toggle must keep the current cached result'
        rows.append({'name':name,'width':info.width,'height':info.height,'gpuTextureBytes':after.gpuBytes,'readbackMilliseconds':readback})
    # A new SDR photo invalidates old results and drops its now-unrequested AI buffers.
    check(d.LumaPrepareEffects(r,0,0,0,1));frame,info=frames['opaque'];check(d.LumaPrepareFrame(r,frame));check(d.LumaRepaint(r));s=status(r)
    assert s.gpuBytes==original_bytes(info)+256*256*8 and not s.hdrEffective and not s.vsrEffective
    out.joinpath('texture-transitions.json').write_text(json.dumps({'complete':True,'results':rows},indent=2),encoding='utf-8')
    print('PASS: same size, portrait, alpha/native-HDR pruning, capability restoration, presets, cached toggles and SDR release.',out)
    print('GPU readback times:',[round(row['readbackMilliseconds'],2) for row in rows])
finally:
    if r.value:d.LumaDestroy(r)
    for frame,_ in frames.values():d.LumaFreeFrame(frame)
