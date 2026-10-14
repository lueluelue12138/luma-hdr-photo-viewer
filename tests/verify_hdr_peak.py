"""Analytic HDR endpoint oracle through the real FP16 GPU target."""
import ctypes as c,json,pathlib,struct,sys
from PIL import Image
from hdr_fixture_utils import jpeg,gain_jpeg
root=pathlib.Path(__file__).resolve().parents[1];out=root/'artifacts/hdr-peak';out.mkdir(parents=True,exist_ok=True)
class Info(c.Structure):_fields_=[('w',c.c_uint),('h',c.c_uint),('ow',c.c_uint),('oh',c.c_uint),('hdr',c.c_int),('down',c.c_int),('bytes',c.c_uint64)]
class Status(c.Structure):_fields_=[('gpu',c.c_wchar*128),('display',c.c_wchar*128),('nvidia',c.c_int),('hdrActive',c.c_int),('fp16',c.c_int),('nativeHdr',c.c_int),('maxNits',c.c_float),('sdrWhite',c.c_float),('gpuBytes',c.c_uint64),('hdrEffective',c.c_int),('vsrEffective',c.c_int),('sdkAvailable',c.c_int),('hdrAvailable',c.c_int),('vsrAvailable',c.c_int),('ngxResult',c.c_uint),('hdrEvaluations',c.c_uint),('vsrEvaluations',c.c_uint),('hdrMessage',c.c_wchar*256),('vsrMessage',c.c_wchar*256)]
d=c.CDLL(str(pathlib.Path(sys.argv[1]).resolve()));d.LumaError.restype=c.c_wchar_p;d.LumaCreateFast.argtypes=[c.c_void_p,c.c_void_p,c.POINTER(c.c_void_p)];d.LumaCopyStatus.argtypes=[c.c_void_p,c.POINTER(Status)];d.LumaDecodeSized.argtypes=[c.c_wchar_p,c.c_uint,c.POINTER(Info),c.POINTER(c.c_void_p)];d.LumaFreeFrame.argtypes=[c.c_void_p];d.LumaDestroy.argtypes=[c.c_void_p];d.LumaResize.argtypes=[c.c_void_p,c.c_uint,c.c_uint,c.c_float,c.c_float];d.LumaUseFrame.argtypes=[c.c_void_p,c.c_void_p];d.LumaReadPixel.argtypes=[c.c_void_p,c.c_uint,c.c_uint,c.POINTER(c.c_uint16)];d.LumaNativeDisplayTest.argtypes=[c.c_void_p,c.c_int,c.c_float,c.c_float]
def check(hr):
 if hr<0:raise RuntimeError(d.LumaError())
r=c.c_void_p();check(d.LumaCreateFast(None,None,c.byref(r)));real=Status();check(d.LumaCopyStatus(r,c.byref(real)));results=[]
try:
 for stops in [2,5]:
  xml=f'<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"><rdf:Description xmlns:hdrgm="http://ns.adobe.com/hdr-gain-map/1.0/" hdrgm:Version="1.0" hdrgm:GainMapMin="0" hdrgm:GainMapMax="{stops}" hdrgm:Gamma="1" hdrgm:OffsetSDR="0" hdrgm:OffsetHDR="0" hdrgm:HDRCapacityMin="0" hdrgm:HDRCapacityMax="{stops}" hdrgm:BaseRenditionIsHDR="False"/></rdf:RDF></x:xmpmeta>'
  path=out/f'white-{stops}.jpg';path.write_bytes(gain_jpeg(jpeg(Image.new('RGB',(64,48),'white')),jpeg(Image.new('L',(64,48),255)),xml));i=Info();p=c.c_void_p();check(d.LumaDecodeSized(str(path),64,c.byref(i),c.byref(p)))
  try:
   check(d.LumaResize(r,64,48,1,1));check(d.LumaUseFrame(r,p));endpoints=[]
   for nits in [203,406,812,1600]+([real.maxNits] if real.maxNits>0 else []):
    check(d.LumaNativeDisplayTest(r,1,nits,real.sdrWhite));v=(c.c_uint16*4)();check(d.LumaReadPixel(r,32,24,v));actual=struct.unpack('<e',struct.pack('<H',v[0]))[0]*80;expected=min(nits,min(nits,real.sdrWhite*80)*2**stops)
    assert abs(actual-expected)<=max(.6,expected*.001),(stops,nits,actual,expected)
    endpoints.append(dict(displayPeakNits=nits,expectedNits=expected,fp16Nits=actual))
   results.append(dict(sourceHeadroom=2**stops,endpoints=endpoints))
  finally:d.LumaFreeFrame(p)
finally:d.LumaDestroy(r)
result=dict(complete=True,windowsPeakNits=real.maxNits,windowsHdr=bool(real.hdrActive),windowsSdrWhiteNits=real.sdrWhite*80,realFp16=True,results=results)
(out/'peak.json').write_text(json.dumps(result,indent=2),encoding='utf8');print(json.dumps(result,indent=2));print('PASS: 203/406/812/1600 nits and actual Windows peak; no stretch beyond authored HDR')
