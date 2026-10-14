"""RGB XMP gain maps: independent math, FP16 output and safe JPEG fallback.

All public fixtures are generated here. Optional user photos remain local and
are never copied into the repository or test reports.
"""
import argparse, ctypes as c, hashlib, io, json, math, pathlib, statistics, struct, subprocess, time
from PIL import Image, ImageCms
from hdr_fixture_utils import jpeg, gain_jpeg

root=pathlib.Path(__file__).resolve().parents[1]
ap=argparse.ArgumentParser();ap.add_argument('dll',type=pathlib.Path);ap.add_argument('--user-photo',type=pathlib.Path);ap.add_argument('--reference-tool',type=pathlib.Path)
args=ap.parse_args();out=root/'artifacts/xmp-gainmaps';out.mkdir(parents=True,exist_ok=True)
class Info(c.Structure):
    _fields_=[('w',c.c_uint),('h',c.c_uint),('ow',c.c_uint),('oh',c.c_uint),('hdr',c.c_int),('down',c.c_int),('bytes',c.c_uint64)]
class Status(c.Structure):
    _fields_=[('gpu',c.c_wchar*128),('display',c.c_wchar*128),('nvidia',c.c_int),('hdrActive',c.c_int),('fp16',c.c_int),('nativeHdr',c.c_int),('maxNits',c.c_float),('sdrWhite',c.c_float),('gpuBytes',c.c_uint64),('hdrEffective',c.c_int),('vsrEffective',c.c_int),('sdkAvailable',c.c_int),('hdrAvailable',c.c_int),('vsrAvailable',c.c_int),('ngxResult',c.c_uint),('hdrEvaluations',c.c_uint),('vsrEvaluations',c.c_uint),('hdrMessage',c.c_wchar*256),('vsrMessage',c.c_wchar*256)]
d=c.CDLL(str(args.dll.resolve()));d.LumaError.restype=c.c_wchar_p
for name,types in {
    'LumaDecodeSized':[c.c_wchar_p,c.c_uint,c.POINTER(Info),c.POINTER(c.c_void_p)],
    'LumaFreeFrame':[c.c_void_p],'LumaDestroy':[c.c_void_p],
    'LumaFrameWarning':[c.c_void_p,c.c_wchar_p,c.c_uint],
    'LumaFramePixel':[c.c_void_p,c.c_uint,c.c_uint,c.POINTER(c.c_float)],
    'LumaCopySdrBgra':[c.c_void_p,c.POINTER(c.c_uint8),c.c_uint64],
    'LumaCreateFast':[c.c_void_p,c.c_void_p,c.POINTER(c.c_void_p)],
    'LumaUseFrame':[c.c_void_p,c.c_void_p],
    'LumaResize':[c.c_void_p,c.c_uint,c.c_uint,c.c_float,c.c_float],
    'LumaReadPixel':[c.c_void_p,c.c_uint,c.c_uint,c.POINTER(c.c_uint16)],
    'LumaCopyStatus':[c.c_void_p,c.POINTER(Status)],
    'LumaNativeDisplayTest':[c.c_void_p,c.c_int,c.c_float,c.c_float],
    'LumaEffects':[c.c_void_p,c.c_int,c.c_int,c.c_int,c.c_uint],
    'LumaSetOriginal':[c.c_void_p,c.c_int],'LumaRepaint':[c.c_void_p],
}.items():getattr(d,name).argtypes=types
def check(hr):
    if hr<0:raise RuntimeError(d.LumaError())
def decode(path,edge=0):
    info=Info();frame=c.c_void_p();start=time.perf_counter();check(d.LumaDecodeSized(str(path.resolve()),edge,c.byref(info),c.byref(frame)))
    text=c.create_unicode_buffer(512);check(d.LumaFrameWarning(frame,text,len(text)))
    return info,frame,text.value,(time.perf_counter()-start)*1000
def source(frame,x,y):
    rgb=(c.c_float*4)();check(d.LumaFramePixel(frame,x,y,rgb));return list(rgb)
def pixel(renderer,x,y):
    values=(c.c_uint16*4)();check(d.LumaReadPixel(renderer,x,y,values));return list(struct.unpack('<4e',bytes(values)))
def status(renderer):
    s=Status();check(d.LumaCopyStatus(renderer,c.byref(s)));return s
def linear(v):return v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4
def oracle(base,gain,low,high,gamma,offset_sdr,offset_hdr,p3=False):
    rgb=[max(0,(linear(b/255)+s)*2**(lo+(hi-lo)*(g/255)**(1/y))-h) for b,g,lo,hi,y,s,h in zip(base,gain,low,high,gamma,offset_sdr,offset_hdr)]
    if p3:
        r,g,b=rgb;rgb=[1.22494*r-.22494*g,-.042057*r+1.042057*g,-.019638*r-.078636*g+1.098274*b]
    return [v*203/80 for v in rgb]
def xml(attrs,children='',prefix='hdrgm',declaration=''):
    return declaration+f'<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"><rdf:Description xmlns:{prefix}="http://ns.adobe.com/hdr-gain-map/1.0/" {attrs}>{children}</rdf:Description></rdf:RDF></x:xmpmeta>'
def seq(name,values,prefix='hdrgm'):
    return f'<{prefix}:{name}><rdf:Seq>'+''.join(f'<rdf:li>{v}</rdf:li>' for v in values)+f'</rdf:Seq></{prefix}:{name}>'

base=jpeg(Image.new('RGB',(64,48),(128,160,96)));gain=jpeg(Image.new('RGB',(64,48),(64,128,192)))
base_rgb=Image.open(io.BytesIO(base)).convert('RGB').getpixel((20,20));gain_rgb=Image.open(io.BytesIO(gain)).convert('RGB').getpixel((20,20))
low=[-.5,-.25,0];high=[2,3,4];gamma=[.4,.6,.8];zeros=[0]*3
attrs='hdrgm:Version="1.0" hdrgm:HDRCapacityMax="4" hdrgm:OffsetSDR="0" hdrgm:OffsetHDR="0"'
arrays=seq('GainMapMin',low)+seq('GainMapMax',high)+seq('Gamma',gamma)
valid=[
    ('xmp-rgb',xml(attrs,arrays),oracle(base_rgb,gain_rgb,low,high,gamma,zeros,zeros)),
    # Scalar maximum is accepted upstream; the array gamma must still override
    # its silently assumed gamma=1. This catches a second compatibility bug.
    ('array-gamma',xml(attrs+' hdrgm:GainMapMax="3"',seq('Gamma',gamma)),oracle(base_rgb,gain_rgb,zeros,[3]*3,gamma,zeros,zeros)),
    ('namespace-alias',xml(attrs.replace('hdrgm:','g:'),arrays.replace('hdrgm:','g:'),'g'),oracle(base_rgb,gain_rgb,low,high,gamma,zeros,zeros)),
    ('scalar-elements',xml('', '<hdrgm:Version>1.0</hdrgm:Version><hdrgm:HDRCapacityMax>4e0</hdrgm:HDRCapacityMax><hdrgm:OffsetSDR>0</hdrgm:OffsetSDR><hdrgm:OffsetHDR>0</hdrgm:OffsetHDR><hdrgm:GainMapMax><![CDATA[ 3.0 ]]></hdrgm:GainMapMax>'),oracle(base_rgb,gain_rgb,zeros,[3]*3,[1]*3,zeros,zeros)),
    ('single-array',xml(attrs,seq('GainMapMax',[3])+seq('Gamma',[.5])),oracle(base_rgb,gain_rgb,zeros,[3]*3,[.5]*3,zeros,zeros)),
    ('rgb-offsets',xml('hdrgm:Version="1.0" hdrgm:HDRCapacityMax="4"',arrays+seq('OffsetSDR',[.01,.02,.03])+seq('OffsetHDR',[.03,.02,.01])),oracle(base_rgb,gain_rgb,low,high,gamma,[.01,.02,.03],[.03,.02,.01])),
    ('unknown-vendor',xml(attrs+' hdrgm:OplusScale="-1"',arrays),oracle(base_rgb,gain_rgb,low,high,gamma,zeros,zeros)),
]
invalid=[
    ('two-values',xml(attrs,seq('GainMapMax',[1,2]))),
    ('four-values',xml(attrs,seq('GainMapMax',[1,2,3,4]))),
    ('missing-maximum',xml(attrs)),
    ('nan',xml(attrs+' hdrgm:GainMapMax="NaN"')),
    ('infinite',xml(attrs+' hdrgm:GainMapMax="1e9999"')),
    ('zero-gamma',xml(attrs+' hdrgm:GainMapMax="3" hdrgm:Gamma="0"')),
    ('negative-offset',xml(attrs.replace('hdrgm:OffsetHDR="0"','hdrgm:OffsetHDR="-1"')+' hdrgm:GainMapMax="3"')),
    ('duplicate-field',xml(attrs+' hdrgm:GainMapMax="3"',seq('GainMapMax',[2]))),
    ('reversed-capacity',xml(attrs+' hdrgm:GainMapMax="3" hdrgm:HDRCapacityMin="5"')),
    ('reverse-base',xml(attrs+' hdrgm:GainMapMax="3" hdrgm:BaseRenditionIsHDR="True"')),
    ('doctype',xml(attrs+' hdrgm:GainMapMax="3"',declaration='<!DOCTYPE x:xmpmeta [<!ENTITY value "3">]>')),
    ('bad-xml',xml(attrs,arrays)[:-20]),
]
results=[];r=c.c_void_p();check(d.LumaCreateFast(None,None,c.byref(r)))
try:
    for name,metadata,expected in valid:
        path=out/(name+'.jpg');path.write_bytes(gain_jpeg(base,gain,metadata));i,f,warning,ms=decode(path)
        try:
            assert i.hdr==2 and not warning,(name,i.hdr,warning)
            actual=source(f,20,20)[:3];error=max(abs(a-b) for a,b in zip(actual,expected));assert error<.02,(name,actual,expected,error)
            check(d.LumaResize(r,i.w,i.h,1,1));check(d.LumaUseFrame(r,f));check(d.LumaNativeDisplayTest(r,1,203*256,203/80));check(d.LumaRepaint(r))
            presented=pixel(r,20,20);assert max(abs(a-b) for a,b in zip(presented[:3],expected))<.02
            before=status(r);assert before.fp16 and before.nativeHdr==2
            for _ in range(10):
                check(d.LumaSetOriginal(r,1));check(d.LumaRepaint(r));sdr=pixel(r,20,20)
                assert max(abs(a-linear(b/255)*203/80) for a,b in zip(sdr[:3],base_rgb))<.02
                check(d.LumaSetOriginal(r,0));check(d.LumaRepaint(r));assert pixel(r,20,20)==presented
            check(d.LumaEffects(r,1,1,0,1));after=status(r)
            assert not after.hdrEvaluations and not after.vsrEvaluations and not after.hdrEffective and not after.vsrEffective
            results.append(dict(case=name,hdr=2,decodeMs=ms,maxAbsoluteError=error,realFp16=True,comparisonSwitches=20,noAiEvaluations=True))
        finally:d.LumaFreeFrame(f)
    for name,metadata in invalid:
        path=out/(name+'.jpg');path.write_bytes(gain_jpeg(base,gain,metadata));i,f,warning,ms=decode(path)
        try:
            assert i.hdr==0 and warning,(name,i.hdr,warning)
            pixels=(c.c_uint8*i.bytes)();check(d.LumaCopySdrBgra(f,pixels,i.bytes));at=(20*i.w+20)*4
            assert max(abs(pixels[at+2-channel]-base_rgb[channel]) for channel in range(3))<=1
            results.append(dict(case=name,hdr=0,compatibilityWarning=True,readableBase=True,decodeMs=ms))
        finally:d.LumaFreeFrame(f)
    # The primary JPEG really must be readable; this is not a blanket success
    # fallback that conceals missing files or corrupt primary pixels.
    bad=out/'broken-primary.jpg';bad.write_bytes(b'not a jpeg')
    i=Info();f=c.c_void_p();assert d.LumaDecodeSized(str(bad),128,c.byref(i),c.byref(f))<0 and not f.value
    i,f,warning,_=decode(out/'xmp-rgb.jpg',32)
    try:assert max(i.w,i.h)<=32 and i.hdr==2 and not warning
    finally:d.LumaFreeFrame(f)
    # ISO metadata is authoritative when both representations are present.
    # A malformed Adobe packet must not override a valid ISO gain map.
    fixture=args.reference_tool or args.dll.parent/'GainMapFixture.exe'
    if not fixture.exists():fixture=root/'src/Luma.Native/bin/GainMapFixture.exe'
    iso=out/'iso-original.jpg';subprocess.run([str(fixture.resolve()),'encode',str(iso)],check=True)
    raw=iso.read_bytes();offset=raw.find(b'\xff\xd8',raw.find(b'\xff\xd9')+2);assert offset>0
    def strip_container(data):
        result=data[:2];p=2
        while p+4<len(data):
            marker=data[p+1]
            if marker in (0xda,0xd9):return result+data[p:]
            length=int.from_bytes(data[p+2:p+4],'big');segment=data[p:p+2+length];payload=segment[4:]
            if not ((marker==0xe1 and payload.startswith(b'http://ns.adobe.com/xap/1.0/\x00')) or (marker==0xe2 and payload.startswith(b'MPF\x00'))):result+=segment
            p+=2+length
        raise AssertionError('Invalid generated JPEG')
    dual=out/'iso-with-invalid-xmp.jpg';dual.write_bytes(gain_jpeg(strip_container(raw[:offset]),strip_container(raw[offset:]),xml(attrs)))
    i,f,warning,_=decode(dual)
    try:assert i.hdr==2 and not warning,'ISO metadata must retain precedence over invalid XMP'
    finally:d.LumaFreeFrame(f)
    if args.user_photo:
        path=args.user_photo.resolve();raw=path.read_bytes();before=(hashlib.sha256(raw).hexdigest(),path.stat().st_size,path.stat().st_mtime_ns)
        image=Image.open(io.BytesIO(raw));offset=raw.find(b'\xff\xd8',raw.find(b'\xff\xd9')+2);assert offset>0
        base_image=image.copy().convert('RGB');gain_image=Image.open(io.BytesIO(raw[offset:])).convert('RGB');assert gain_image.size==base_image.size
        import xml.etree.ElementTree as ET
        marker=b'http://ns.adobe.com/xap/1.0/\x00';packet=raw.index(marker,offset)+len(marker);length=int.from_bytes(raw[packet-len(marker)-2:packet-len(marker)],'big')-2-len(marker)
        tree=ET.fromstring(raw[packet:packet+length]);ns='{http://ns.adobe.com/hdr-gain-map/1.0/}';rdf='{http://www.w3.org/1999/02/22-rdf-syntax-ns#}'
        description=tree.find('.//'+rdf+'Description')
        def values(name,default):
            item=description.find(ns+name)
            v=[float(e.text) for e in item.findall('.//'+rdf+'li')] if item is not None else [float(description.attrib.get(ns+name,default))]
            return v*3 if len(v)==1 else v
        pars=[values('GainMapMin',0),values('GainMapMax',0),values('Gamma',1),values('OffsetSDR',1/64),values('OffsetHDR',1/64)]
        profile=image.info.get('icc_profile');p3=bool(profile and 'P3' in ImageCms.getProfileName(ImageCms.ImageCmsProfile(io.BytesIO(profile))))
        i,f,warning,ms=decode(path)
        try:
            assert i.hdr==2 and not warning and (i.w,i.h)==base_image.size
            errors=[];peak=0
            for y in range(20,i.h,73):
                for x in range(20,i.w,73):
                    actual=source(f,x,y)[:3];expected=oracle(base_image.getpixel((x,y)),gain_image.getpixel((x,y)),*pars,p3)
                    errors.extend(abs(a-b) for a,b in zip(actual,expected));peak=max(peak,*actual)
            assert statistics.mean(errors)<.06 and max(errors)<1.25,(statistics.mean(errors),max(errors))
            check(d.LumaResize(r,i.w,i.h,1,1));check(d.LumaUseFrame(r,f));check(d.LumaNativeDisplayTest(r,1,203*256,203/80));check(d.LumaRepaint(r));s=status(r)
            # Check pixels on the actual output texture, not an RGBA8 screenshot.
            fp16_error=max(abs(a-b) for a,b in zip(pixel(r,i.w//2,i.h//2),source(f,i.w//2,i.h//2)))
            assert fp16_error<.025 and s.fp16 and s.nativeHdr==2 and not s.hdrEvaluations and not s.vsrEvaluations
            results.append(dict(case='local-user-photo',sha256=before[0],width=i.w,height=i.h,hdr=2,decodeMs=ms,referenceMeanAbsoluteError=statistics.mean(errors),referenceMaxAbsoluteError=max(errors),sourcePeakScRgb=peak,fp16AbsoluteError=fp16_error,realFp16=True,noAiEvaluations=True))
        finally:d.LumaFreeFrame(f)
        assert before==(hashlib.sha256(path.read_bytes()).hexdigest(),path.stat().st_size,path.stat().st_mtime_ns),'Source photo changed'
finally:d.LumaDestroy(r)
report=dict(complete=True,validCases=len(valid),compatibilityCases=len(invalid),brokenPrimaryRejected=True,previewSizing=True,isoPrecedence=True,results=results)
(out/('xmp-'+args.dll.parent.name+'.json')).write_text(json.dumps(report,indent=2),encoding='utf8')
print(f'PASS: {len(valid)} scalar/RGB/namespace XMP cases; {len(invalid)} readable SDR fallbacks; independent pixels, FP16 and cached comparison; broken primary rejected')
for result in results:
    if result['case']=='local-user-photo':print(json.dumps(result))
