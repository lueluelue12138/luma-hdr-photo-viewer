"""Gain-map/reference parity, real GPU FP16, preview budgets and SDR fallback."""
import argparse, ctypes as c, io, json, os, pathlib, statistics, struct, subprocess, time
from PIL import Image

root=pathlib.Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser();parser.add_argument('--dll',default=os.environ.get('LUMA_NATIVE_TEST_DLL',str(root/'src/Luma.Native/bin/Luma.Native.dll')));parser.add_argument('--reference-tool');parser.add_argument('--jxr');parser.add_argument('--jxr-directory');args=parser.parse_args()
native=pathlib.Path(args.dll).resolve();fixture=native.parent/'GainMapFixture.exe'
if args.reference_tool:fixture=pathlib.Path(args.reference_tool).resolve()
elif not fixture.exists():fixture=root/'src/Luma.Native/bin/GainMapFixture.exe'
out=root/'artifacts/hdr-formats';out.mkdir(parents=True,exist_ok=True)
upstream=root/'.tools/hdr-codecs/libultrahdr-e5f5a022fe96fc4dc2ee35c19f733a50df807abe/tests/data'
class Info(c.Structure):_fields_=[('w',c.c_uint),('h',c.c_uint),('ow',c.c_uint),('oh',c.c_uint),('hdr',c.c_int),('down',c.c_int),('bytes',c.c_uint64)]
class Status(c.Structure):_fields_=[('gpu',c.c_wchar*128),('display',c.c_wchar*128),('nvidia',c.c_int),('hdrActive',c.c_int),('fp16',c.c_int),('nativeHdr',c.c_int),('maxNits',c.c_float),('sdrWhite',c.c_float),('gpuBytes',c.c_uint64),('hdrEffective',c.c_int),('vsrEffective',c.c_int),('sdkAvailable',c.c_int),('hdrAvailable',c.c_int),('vsrAvailable',c.c_int),('ngxResult',c.c_uint),('hdrEvaluations',c.c_uint),('vsrEvaluations',c.c_uint),('hdrMessage',c.c_wchar*256),('vsrMessage',c.c_wchar*256)]
d=c.CDLL(str(native));d.LumaError.restype=c.c_wchar_p
for name in ['LumaDecodeSized','LumaDecodeThumbnail']:getattr(d,name).argtypes=[c.c_wchar_p,c.c_uint,c.POINTER(Info),c.POINTER(c.c_void_p)]
d.LumaFreeFrame.argtypes=[c.c_void_p];d.LumaFramePixel.argtypes=[c.c_void_p,c.c_uint,c.c_uint,c.POINTER(c.c_float)];d.LumaCopySdrBgra.argtypes=[c.c_void_p,c.POINTER(c.c_uint8),c.c_uint64]
d.LumaCreateFast.argtypes=[c.c_void_p,c.c_void_p,c.POINTER(c.c_void_p)];d.LumaDestroy.argtypes=[c.c_void_p];d.LumaUseFrame.argtypes=[c.c_void_p,c.c_void_p];d.LumaCopyStatus.argtypes=[c.c_void_p,c.POINTER(Status)];d.LumaResize.argtypes=[c.c_void_p,c.c_uint,c.c_uint,c.c_float,c.c_float];d.LumaReadPixel.argtypes=[c.c_void_p,c.c_uint,c.c_uint,c.POINTER(c.c_uint16)];d.LumaEffects.argtypes=[c.c_void_p,c.c_int,c.c_int,c.c_int,c.c_uint];d.LumaSetOriginal.argtypes=[c.c_void_p,c.c_int];d.LumaRepaint.argtypes=[c.c_void_p]
def check(hr):
    if hr<0:raise RuntimeError(f'{hr&0xffffffff:08X}: {d.LumaError()}')
def decode(path,edge=0,thumbnail=False):
    i=Info();p=c.c_void_p();start=time.perf_counter();check(getattr(d,'LumaDecodeThumbnail' if thumbnail else 'LumaDecodeSized')(str(path),edge,c.byref(i),c.byref(p)));return i,p,(time.perf_counter()-start)*1000
def frame_pixel(p,x,y):
    v=(c.c_float*4)();check(d.LumaFramePixel(p,x,y,v));return list(v)
def gpu_pixel(r,x,y):
    v=(c.c_uint16*4)();check(d.LumaReadPixel(r,x,y,v));return [struct.unpack('<e',struct.pack('<H',x))[0] for x in v]
def status(r):
    s=Status();check(d.LumaCopyStatus(r,c.byref(s)));return s
def jpeg(im):
    buffer=io.BytesIO();im.save(buffer,'JPEG',quality=100,subsampling=0);return buffer.getvalue()
def app1(data):return b'\xff\xe1'+struct.pack('>H',len(data)+2)+data
def xmp(text):return app1(b'http://ns.adobe.com/xap/1.0/\x00'+text.encode())
def gain_jpeg(base,gain,description):
    secondary=gain[:2]+xmp(description)+gain[2:]
    primary_xml=f'<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"><rdf:Description xmlns:hdrgm="http://ns.adobe.com/hdr-gain-map/1.0/" hdrgm:Version="1.0" xmlns:Container="http://ns.google.com/photos/1.0/container/" xmlns:Item="http://ns.google.com/photos/1.0/container/item/"><Container:Directory><rdf:Seq><rdf:li rdf:parseType="Resource"><Container:Item Item:Semantic="Primary" Item:Mime="image/jpeg"/></rdf:li><rdf:li rdf:parseType="Resource"><Container:Item Item:Semantic="GainMap" Item:Mime="image/jpeg" Item:Length="{len(secondary)}"/></rdf:li></rdf:Seq></Container:Directory></rdf:Description></rdf:RDF></x:xmpmeta>'
    primary=base[:2]+xmp(primary_xml)+base[2:]
    # MPF offsets are relative to the TIFF header inside the APP2 payload.
    ifd=struct.pack('<H',3)+struct.pack('<HHI4s',0xb000,7,4,b'0100')+struct.pack('<HHII',0xb001,4,1,2)+struct.pack('<HHII',0xb002,7,32,50)+bytes(4)
    primary_size=len(primary)+90
    entries=struct.pack('<IIIHH',0x20030000,primary_size,0,0,0)+struct.pack('<IIIHH',0,len(secondary),primary_size-10,0,0)
    mpf=b'MPF\x00II\x2a\x00'+struct.pack('<I',8)+ifd+entries
    segment=b'\xff\xe2'+struct.pack('>H',len(mpf)+2)+mpf
    assert len(segment)==90
    return primary[:2]+segment+primary[2:]+secondary
# Android/Adobe XMP-only gain map, without an ISO packet. Known constant 4x
# gain gives an analytic luminance oracle separate from Luma/library decode.
base=jpeg(Image.new('RGB',(320,240),(180,180,180)));gain=jpeg(Image.new('L',(80,60),255))
description='<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"><rdf:Description xmlns:hdrgm="http://ns.adobe.com/hdr-gain-map/1.0/" hdrgm:Version="1.0" hdrgm:GainMapMin="0" hdrgm:GainMapMax="2" hdrgm:Gamma="1" hdrgm:OffsetSDR="0" hdrgm:OffsetHDR="0" hdrgm:HDRCapacityMin="0" hdrgm:HDRCapacityMax="2" hdrgm:BaseRenditionIsHDR="False"/></rdf:RDF></x:xmpmeta>'
xmp_photo=out/'android-xmp.jpg';xmp_photo.write_bytes(gain_jpeg(base,gain,description))
iso_photo=out/'iso-21496.jpg';subprocess.run([str(fixture),'encode',str(iso_photo)],check=True)
results=[];renderer=c.c_void_p();check(d.LumaCreateFast(None,None,c.byref(renderer)))
try:
    for path in [xmp_photo,iso_photo,upstream/'apple_gainmap_old.jpg',upstream/'apple_gainmap_new.jpg']:
        reference=out/(path.stem+'.reference');subprocess.run([str(fixture),'reference',str(path),str(reference)],check=True)
        ref=reference.read_bytes();rw,rh=struct.unpack_from('<II',ref);i,p,elapsed=decode(path)
        try:
            assert i.hdr==2 and i.bytes==i.w*i.h*20
            assert (i.w,i.h)==(rw,rh)
            errors=[];peak=0
            for y in range(4,i.h,13):
                for x in range(4,i.w,13):
                    actual=frame_pixel(p,x,y);expected=struct.unpack_from('<4f',ref,8+(y*rw+x)*16)
                    peak=max(peak,*actual[:3]);errors.extend(abs(a-b) for a,b in zip(actual[:3],expected[:3]))
            # Independent JPEG decoders round YCbCr differently. Keep the
            # absolute and relative error visible instead of claiming equality.
            maximum=max(errors);mean=statistics.mean(errors)
            # Apple JPEGs include strongly coloured edges: WIC and the
            # reference's YCbCr decoder use different chroma interpolation.
            # Bound both the average error and isolated edge differences.
            assert maximum<.75 and mean<.02,(path.name,maximum,mean)
            assert peak>1,(path.name,peak)
            if path==xmp_photo:
                expected=((180/255+.055)/1.055)**2.4*4*203/80
                assert abs(frame_pixel(p,100,100)[0]-expected)<.015
            check(d.LumaResize(renderer,i.w,i.h,1,1));check(d.LumaUseFrame(renderer,p));s=status(renderer)
            assert s.fp16 and s.nativeHdr==2 and not s.sdkAvailable
            hdr=gpu_pixel(renderer,i.w*3//4,i.h//4);source=frame_pixel(p,i.w*3//4,i.h//4)
            if s.hdrActive:assert max(abs(a-b) for a,b in zip(hdr,source))<.025,(hdr,source)
            before=s.hdrEvaluations,s.vsrEvaluations;check(d.LumaEffects(renderer,1,1,0,1));s=status(renderer);assert not s.hdrEffective and not s.vsrEffective and (s.hdrEvaluations,s.vsrEvaluations)==before
            for repeat in range(20):
                check(d.LumaSetOriginal(renderer,1));check(d.LumaRepaint(renderer));sdr=gpu_pixel(renderer,i.w*3//4,i.h//4)
                check(d.LumaSetOriginal(renderer,0));check(d.LumaRepaint(renderer));assert gpu_pixel(renderer,i.w*3//4,i.h//4)==hdr
            assert sdr[0]<=s.sdrWhite+.02
            results.append(dict(file=path.name,width=i.w,height=i.h,decodeMs=elapsed,referenceMaxAbsoluteError=maximum,referenceMeanAbsoluteError=mean,peakScRgb=peak,windowsHdr=bool(s.hdrActive),fp16=True,comparisonSwitches=40,hdrCalls=s.hdrEvaluations,vsrCalls=s.vsrEvaluations))
        finally:d.LumaFreeFrame(p)
        small,frame,preview=decode(path,128)
        try:assert max(small.w,small.h)<=128 and small.hdr==2 and small.down==2
        finally:d.LumaFreeFrame(frame)
        thumb,frame,thumbnail=decode(path,128,True)
        try:
            assert not thumb.hdr and thumb.bytes==thumb.w*thumb.h*4
            pixels=(c.c_uint8*thumb.bytes)();check(d.LumaCopySdrBgra(frame,pixels,thumb.bytes))
            results[-1].update(previewMs=preview,thumbnailMs=thumbnail)
        finally:d.LumaFreeFrame(frame)
    # Same base/gain transform, no modification of the original two JPEG streams.
    exif=Image.Exif();exif[274]=6;oriented_base=io.BytesIO();Image.new('RGB',(320,240),(180,180,180)).save(oriented_base,'JPEG',quality=100,subsampling=0,exif=exif)
    oriented=out/'orientation.jpg';original=oriented_base.getvalue();oriented.write_bytes(gain_jpeg(original,gain,description))
    i,p,_=decode(oriented,128)
    try:assert(i.w,i.h,i.ow,i.oh,i.hdr)==(96,128,240,320,2)
    finally:d.LumaFreeFrame(p)
    truncated=out/'truncated.jpg';truncated.write_bytes(xmp_photo.read_bytes()[:-len(gain)-len(xmp(description))])
    i=Info();p=c.c_void_p();hr=d.LumaDecodeSized(str(truncated),128,c.byref(i),c.byref(p));assert hr<0 and not p.value,'Invalid gain map was silently advertised as HDR';invalid_error=d.LumaError()
    plain=out/'ordinary.jpg';plain.write_bytes(base);i,p,_=decode(plain,128)
    try:assert not i.hdr
    finally:d.LumaFreeFrame(p)
    large=out/'large-48mp.jpg';large.write_bytes(gain_jpeg(jpeg(Image.new('RGB',(8000,6000),(180,180,180))),jpeg(Image.new('L',(2000,1500),255)),description))
    i,p,elapsed=decode(large,2048)
    try:
        assert(i.w,i.h,i.ow,i.oh,i.hdr)==(2048,1536,8000,6000,2) and i.bytes==2048*1536*20
        assert abs(frame_pixel(p,1000,800)[0]-((180/255+.055)/1.055)**2.4*4*203/80)<.015
        results.append(dict(file=large.name,width=i.w,height=i.h,originalWidth=i.ow,originalHeight=i.oh,decodeMs=elapsed,frameBytes=i.bytes,synthetic=True))
    finally:d.LumaFreeFrame(p)
    jxr_paths=[pathlib.Path(args.jxr)] if args.jxr else []
    if args.jxr_directory:jxr_paths+=sorted(pathlib.Path(args.jxr_directory).glob('*.jxr'))[:6]
    for path in dict.fromkeys(jxr_paths):
        before=(path.stat().st_size,path.stat().st_mtime_ns)
        i,p,elapsed=decode(path,2048)
        try:
            assert i.hdr==1
            peak_point=max(((max(frame_pixel(p,x,y)[:3]),x,y) for y in range(5,i.h,17) for x in range(5,i.w,17)))
            peak,x,y=peak_point;assert peak>1
            check(d.LumaResize(renderer,i.w,i.h,1,1));check(d.LumaUseFrame(renderer,p));s=status(renderer);assert s.nativeHdr==1 and s.fp16 and not s.hdrEvaluations and not s.vsrEvaluations
            presented=gpu_pixel(renderer,x,y);assert max(presented[:3])>1 and max(abs(a-b) for a,b in zip(presented,frame_pixel(p,x,y)))<.03
            results.append(dict(file=path.name,width=i.w,height=i.h,originalWidth=i.ow,originalHeight=i.oh,decodeMs=elapsed,peakScRgb=peak,presentedPeakScRgb=max(presented[:3]),fp16=True))
        finally:d.LumaFreeFrame(p)
        assert before==(path.stat().st_size,path.stat().st_mtime_ns)
finally:d.LumaDestroy(renderer)
out.joinpath('hdr-formats.json').write_text(json.dumps(dict(complete=True,results=results,invalidGainMapRejected=invalid_error,orientation=True,ordinaryJpeg=True),ensure_ascii=False,indent=2),encoding='utf-8')
print('PASS: XMP/ISO and two Apple gain maps, linear scRGB/reference pixels, FP16, SDR comparison, sized previews, fast SDR thumbnails, orientation and malformed input.',flush=True)
for result in results:print(json.dumps(result,ensure_ascii=False),flush=True)
