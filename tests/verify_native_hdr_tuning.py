"""Presentation-only HDR controls, checked against analytic FP16 pixel oracles."""
import argparse, ctypes as c, json, math, pathlib, struct, time
from PIL import Image
from hdr_fixture_utils import jpeg, gain_jpeg

root = pathlib.Path(__file__).resolve().parents[1]
ap = argparse.ArgumentParser()
ap.add_argument('dll', type=pathlib.Path)
ap.add_argument('--samples', type=pathlib.Path, default=root/'artifacts/hdr-samples')
args = ap.parse_args()
out = root/'artifacts/native-hdr-tuning'; out.mkdir(parents=True, exist_ok=True)
class Info(c.Structure):
    _fields_=[('w',c.c_uint),('h',c.c_uint),('ow',c.c_uint),('oh',c.c_uint),('hdr',c.c_int),('down',c.c_int),('bytes',c.c_uint64)]
class Status(c.Structure):
    _fields_=[('gpu',c.c_wchar*128),('display',c.c_wchar*128),('nvidia',c.c_int),('hdrActive',c.c_int),('fp16',c.c_int),('nativeHdr',c.c_int),('maxNits',c.c_float),('sdrWhite',c.c_float),('gpuBytes',c.c_uint64),('hdrEffective',c.c_int),('vsrEffective',c.c_int),('sdkAvailable',c.c_int),('hdrAvailable',c.c_int),('vsrAvailable',c.c_int),('ngxResult',c.c_uint),('hdrEvaluations',c.c_uint),('vsrEvaluations',c.c_uint),('hdrMessage',c.c_wchar*256),('vsrMessage',c.c_wchar*256)]
class Glass(c.Structure):
    _fields_=[('left',c.c_float),('top',c.c_float),('width',c.c_float),('height',c.c_float),('radius',c.c_float),('opacity',c.c_float)]
d=c.CDLL(str(args.dll.resolve())); d.LumaError.restype=c.c_wchar_p
signatures={
    'LumaCreateFast':[c.c_void_p,c.c_void_p,c.POINTER(c.c_void_p)],
    'LumaDecodeSized':[c.c_wchar_p,c.c_uint,c.POINTER(Info),c.POINTER(c.c_void_p)],
    'LumaFreeFrame':[c.c_void_p], 'LumaDestroy':[c.c_void_p],
    'LumaCopyStatus':[c.c_void_p,c.POINTER(Status)],
    'LumaResize':[c.c_void_p,c.c_uint,c.c_uint,c.c_float,c.c_float],
    'LumaUseFrame':[c.c_void_p,c.c_void_p],
    'LumaReadPixel':[c.c_void_p,c.c_uint,c.c_uint,c.POINTER(c.c_uint16)],
    'LumaNativeDisplayTest':[c.c_void_p,c.c_int,c.c_float,c.c_float],
    'LumaNativeHdrTuning':[c.c_void_p,c.c_float,c.c_float],
    'LumaSetOriginal':[c.c_void_p,c.c_int], 'LumaRepaint':[c.c_void_p],
    'LumaSetGlass':[c.c_void_p,c.POINTER(Glass),c.c_uint,c.c_int],
}
for name, signature in signatures.items(): getattr(d,name).argtypes=signature
def check(hr):
    if hr<0: raise RuntimeError(d.LumaError())
def pixel(r,x,y):
    values=(c.c_uint16*4)(); check(d.LumaReadPixel(r,x,y,values))
    return list(struct.unpack('<4e',bytes(values)))
def status(r):
    s=Status(); check(d.LumaCopyStatus(r,c.byref(s))); return s
def near(actual,expected):
    assert abs(actual-expected)<=max(.008,abs(expected)*.003),(actual,expected)

xml='<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"><rdf:Description xmlns:hdrgm="http://ns.adobe.com/hdr-gain-map/1.0/" hdrgm:Version="1.0" hdrgm:GainMapMin="0" hdrgm:GainMapMax="5" hdrgm:Gamma="1" hdrgm:OffsetSDR="0" hdrgm:OffsetHDR="0" hdrgm:HDRCapacityMin="0" hdrgm:HDRCapacityMax="5" hdrgm:BaseRenditionIsHDR="False"/></rdf:RDF></x:xmpmeta>'
gain=out/'white-gain.jpg'; gain.write_bytes(gain_jpeg(jpeg(Image.new('RGB',(64,48),'white')),jpeg(Image.new('L',(64,48),255)),xml))
paths=[gain,root/'artifacts/native-tests/linear-hdr.jxr']
for rel in ['apple-camera.heic','ipad-iso/matrix4.HEIC','apple-official-pq/example-ISO-HDR-images/image_01.heic']:
    path=args.samples/rel
    if path.exists(): paths.append(path)
results=[]; r=c.c_void_p(); check(d.LumaCreateFast(None,None,c.byref(r)))
try:
    for path in paths:
        i=Info(); f=c.c_void_p(); start=time.perf_counter()
        check(d.LumaDecodeSized(str(path.resolve()),128,c.byref(i),c.byref(f)))
        decode_ms=(time.perf_counter()-start)*1000
        try:
            check(d.LumaResize(r,i.w,i.h,1,1)); check(d.LumaUseFrame(r,f))
            check(d.LumaNativeDisplayTest(r,1,1600,203/80))
            check(d.LumaNativeHdrTuning(r,0,0)); check(d.LumaRepaint(r))
            # Exercise the cached Gaussian scene as in the actual viewer. The
            # sampled pixel lies outside the small glass rectangle; it must still
            # refresh when an absolute HDR source bypasses manual tuning on hold.
            panel=Glass(0,0,8,8,2,1);check(d.LumaSetGlass(r,c.byref(panel),1,1));check(d.LumaRepaint(r))
            before=status(r); baseline=pixel(r,i.w//2,i.h//2)
            assert before.fp16 and before.nativeHdr==i.hdr and i.hdr!=0
            cases=[]
            for gray,peak in [(22,400),(88,1200),(44,80),(0,10000),(0,0)]:
                check(d.LumaNativeHdrTuning(r,gray,peak)); check(d.LumaRepaint(r))
                actual=pixel(r,i.w//2,i.h//2); assert all(math.isfinite(v) for v in actual)
                if path==gain:
                    white=min(gray/.21586 if gray else 203,peak or 1600)
                    expected=min(peak or 1600,white*32)/80
                    for channel in range(3): near(actual[channel],expected)
                elif i.hdr==1:
                    exposure=gray/(203*.21586) if gray else 1
                    expected=[v*exposure for v in baseline[:3]]
                    luminance=sum(a*b for a,b in zip(expected,[.2126,.7152,.0722]))
                    if peak and luminance>peak/80*.75:
                        knee=peak/80*.75; span=peak/80-knee
                        mapped=knee+span*(1-math.exp(-(luminance-knee)/span))
                        expected=[v*mapped/luminance for v in expected]
                    for a,b in zip(actual[:3],expected): near(a,b)
                elif gray==peak==0:
                    assert actual==baseline,'Automatic gain-map mode must restore identical FP16 pixels'
                check(d.LumaSetOriginal(r,1)); check(d.LumaRepaint(r)); original=pixel(r,i.w//2,i.h//2)
                if i.hdr==1: assert original==baseline,'Held HDR original must bypass display tuning'
                elif path==gain:
                    for channel in range(3): near(original[channel],203/80)
                check(d.LumaSetOriginal(r,0)); check(d.LumaRepaint(r))
                assert pixel(r,i.w//2,i.h//2)==actual,'Release must restore the cached tuned image'
                after=status(r)
                assert after.hdrEvaluations==before.hdrEvaluations==0 and after.vsrEvaluations==before.vsrEvaluations==0
                assert after.gpuBytes==before.gpuBytes,'Tuning must not allocate new photo resources'
                cases.append(dict(grayNits=gray,peakNits=peak,fp16=actual,original=original))
            for bad in [float('nan'),float('inf'),-1]: assert d.LumaNativeHdrTuning(r,bad,1000)<0
            results.append(dict(file=path.name,nativeHdr=i.hdr,decodeMs=decode_ms,cases=cases,noSdkEvaluations=True))
        finally: d.LumaFreeFrame(f)
finally: d.LumaDestroy(r)
report=dict(complete=True,realFp16=True,results=results)
(out/'tuning.json').write_text(json.dumps(report,indent=2),encoding='utf8')
print(f'PASS: {len(results)} native HDR sources; analytic gain/exposure/peak, cached original, automatic restoration, unchanged GPU memory, no SDK evaluations')
