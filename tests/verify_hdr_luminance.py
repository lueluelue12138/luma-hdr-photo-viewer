import ctypes as c, pathlib, sys, struct, json
from PIL import Image, ImageDraw
root=pathlib.Path(__file__).resolve().parents[1];out=root/'artifacts/native-tests';out.mkdir(parents=True,exist_ok=True)
class Info(c.Structure):_fields_=[('width',c.c_uint),('height',c.c_uint),('ow',c.c_uint),('oh',c.c_uint),('hdr',c.c_int),('downsampled',c.c_int),('bytes',c.c_uint64)]
class Status(c.Structure):_fields_=[('gpu',c.c_wchar*128),('display',c.c_wchar*128),('nvidia',c.c_int),('hdrActive',c.c_int),('fp16',c.c_int),('nativeHdr',c.c_int),('maxNits',c.c_float),('sdrWhite',c.c_float),('gpuBytes',c.c_uint64),('hdrEffective',c.c_int),('vsrEffective',c.c_int),('sdkAvailable',c.c_int),('hdrAvailable',c.c_int),('vsrAvailable',c.c_int),('ngxResult',c.c_uint),('hdrEvaluations',c.c_uint),('vsrEvaluations',c.c_uint),('hdrMessage',c.c_wchar*256),('vsrMessage',c.c_wchar*256)]
d=c.CDLL(str(root/'src/Luma.Native/bin/Luma.Native.dll'))
d.LumaDecode.argtypes=[c.c_wchar_p,c.POINTER(Info),c.POINTER(c.c_void_p)];d.LumaCreate.argtypes=[c.c_void_p,c.c_void_p,c.POINTER(c.c_void_p)];d.LumaResize.argtypes=[c.c_void_p,c.c_uint,c.c_uint,c.c_float,c.c_float];d.LumaUseFrame.argtypes=[c.c_void_p,c.c_void_p];d.LumaEffects.argtypes=[c.c_void_p,c.c_int,c.c_int,c.c_int,c.c_uint];d.LumaHdrTuning.argtypes=[c.c_void_p,c.c_uint,c.c_uint];d.LumaStatus.argtypes=[c.c_void_p,c.POINTER(Status)];d.LumaReadSurface.argtypes=[c.c_void_p,c.POINTER(c.c_uint16),c.c_uint];d.LumaFreeFrame.argtypes=[c.c_void_p];d.LumaDestroy.argtypes=[c.c_void_p]
def check(hr):
 if hr<0:raise RuntimeError(hex(hr&0xffffffff))
def sample(r,w,h):
 values=(c.c_uint16*(w*h*4))();check(d.LumaReadSurface(r,values,w*h));f=[n[0] for n in struct.iter_unpack('<e',bytes(values))];l=sorted((.2126*f[i]+.7152*f[i+1]+.0722*f[i+2])*80 for i in range(0,len(f),4));return {'mean':sum(l)/len(l),'p50':l[int(len(l)*.5)],'p95':l[int(len(l)*.95)],'p99':l[int(len(l)*.99)],'peak':l[-1]}
def measure(path):
 p=c.c_void_p();r=c.c_void_p();info=Info();check(d.LumaDecode(str(path),c.byref(info),c.byref(p)));check(d.LumaCreate(None,None,c.byref(r)));w=320;h=round(w*info.height/info.width);check(d.LumaResize(r,w,h,1,1));check(d.LumaUseFrame(r,p));s=Status();check(d.LumaStatus(r,c.byref(s)));result={'photo':path.name,'display_peak':s.maxNits,'sdr_white_nits':s.sdrWhite*80,'SDR':sample(r,w,h)}
 try:
  for name,middle in [('legacy_50',50),('auto',0),('maximum_100',100)]:
   check(d.LumaHdrTuning(r,middle,0));check(d.LumaEffects(r,1,0,0,1));check(d.LumaStatus(r,c.byref(s)));assert s.hdrEffective and s.vsrEvaluations==0;result[name]=sample(r,w,h)
 finally:d.LumaFreeFrame(p);d.LumaDestroy(r)
 return result
ramp=Image.new('RGB',(1920,1080));draw=ImageDraw.Draw(ramp)
for n,value in enumerate([0,32,64,96,128,160,192,224,255]):draw.rectangle((n*1920//9,0,(n+1)*1920//9,1080),fill=(value,value,value))
ramp.save(out/'gray-ramp.png')
paths=[out/'gray-ramp.png']+[pathlib.Path(p) for p in sys.argv[1:]]
results=[measure(p) for p in paths];(out/'luminance-report.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
for r in results:print(json.dumps(r,ensure_ascii=False))
