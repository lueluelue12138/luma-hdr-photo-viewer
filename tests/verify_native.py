"""Real WIC + D3D11 tests; FP16 is checked by GPU readback, not screenshots."""
import ctypes as c, pathlib, sys, struct, os
from PIL import Image,ImageDraw
root=pathlib.Path(__file__).resolve().parents[1]
out=root/'artifacts'/'native-tests';out.mkdir(parents=True,exist_ok=True)
Image.new('RGB',(400,200),(128,64,32)).save(out/'sdr.png')
Image.new('RGBA',(100,100),(255,0,0,128)).save(out/'alpha.png')
exif=Image.Exif();exif[274]=6;Image.new('RGB',(90,40),(255,255,255)).save(out/'orientation.jpg',exif=exif)
(out/'corrupt.jpg').write_bytes(b'not a photo')
jpg=Image.new('RGB',(3840,2160),(80,100,140));ImageDraw.Draw(jpg).rectangle((1600,600,2400,1400),fill=(255,255,255));jpg.save(out/'highlights.jpg',quality=95)
class Info(c.Structure):_fields_=[('width',c.c_uint),('height',c.c_uint),('ow',c.c_uint),('oh',c.c_uint),('hdr',c.c_int),('downsampled',c.c_int),('bytes',c.c_uint64)]
class Status(c.Structure):_fields_=[('gpu',c.c_wchar*128),('display',c.c_wchar*128),('nvidia',c.c_int),('hdrActive',c.c_int),('fp16',c.c_int),('nativeHdr',c.c_int),('maxNits',c.c_float),('sdrWhite',c.c_float),('gpuBytes',c.c_uint64),('hdrEffective',c.c_int),('vsrEffective',c.c_int),('sdkAvailable',c.c_int),('hdrAvailable',c.c_int),('vsrAvailable',c.c_int),('ngxResult',c.c_uint),('hdrEvaluations',c.c_uint),('vsrEvaluations',c.c_uint),('hdrMessage',c.c_wchar*256),('vsrMessage',c.c_wchar*256)]
class GlassRect(c.Structure):_fields_=[(n,c.c_float) for n in ('left','top','width','height','radius','opacity')]
d=c.CDLL(os.environ.get('LUMA_NATIVE_TEST_DLL',str(root/'src'/'Luma.Native'/'bin'/'Luma.Native.dll')))
d.LumaDecode.argtypes=[c.c_wchar_p,c.POINTER(Info),c.POINTER(c.c_void_p)];d.LumaCreate.argtypes=[c.c_void_p,c.c_void_p,c.POINTER(c.c_void_p)]
d.LumaResize.argtypes=[c.c_void_p,c.c_uint,c.c_uint,c.c_float,c.c_float];d.LumaUseFrame.argtypes=[c.c_void_p,c.c_void_p];d.LumaStatus.argtypes=[c.c_void_p,c.POINTER(Status)]
d.LumaView.argtypes=[c.c_void_p,c.c_float,c.c_float,c.c_float,c.c_int];d.LumaReadPixel.argtypes=[c.c_void_p,c.c_uint,c.c_uint,c.POINTER(c.c_uint16)];d.LumaFreeFrame.argtypes=[c.c_void_p];d.LumaDestroy.argtypes=[c.c_void_p];d.LumaError.restype=c.c_wchar_p
d.LumaEffects.argtypes=[c.c_void_p,c.c_int,c.c_int,c.c_int,c.c_uint]
d.LumaPrepareEffects.argtypes=d.LumaEffects.argtypes
d.LumaWaitForGpu.argtypes=[c.c_void_p]
d.LumaSetGlass.argtypes=[c.c_void_p,c.POINTER(GlassRect),c.c_uint,c.c_int];d.LumaRepaint.argtypes=[c.c_void_p]
d.LumaReadGlass.argtypes=[c.c_void_p,GlassRect,c.c_int,c.c_uint,c.c_uint,c.POINTER(c.c_uint8),c.c_uint]
def check(hr):
    if hr<0:raise RuntimeError(f'{hr & 0xffffffff:08x}: {d.LumaError()}')
def decode(name):
    i=Info();p=c.c_void_p();check(d.LumaDecode(str(out/name),c.byref(i),c.byref(p)));return i,p
def pixel(renderer,x,y):
    v=(c.c_uint16*4)();check(d.LumaReadPixel(renderer,x,y,v));return [struct.unpack('<e',struct.pack('<H',n))[0] for n in v]
r=c.c_void_p();check(d.LumaCreate(None,None,c.byref(r)))
try:
    s=Status();check(d.LumaStatus(r,c.byref(s)))
    check(d.LumaResize(r,400,400,1,1));i,p=decode('sdr.png')
    try:
        assert i.width==400 and i.height==200 and not i.hdr
        check(d.LumaUseFrame(r,p));center=pixel(r,200,200);edge=pixel(r,200,20)
        expected=((128/255+0.055)/1.055)**2.4*s.sdrWhite
        assert abs(center[0]-expected)<0.004,(center,expected)
        assert edge==[0,0,0,0],'Fit must reveal desktop backdrop in letterbox, never crop or paint opaque black'
        check(d.LumaView(r,1,0,0,1));assert pixel(r,20,200)[0]<.02,'Rotation must preserve aspect ratio'
        check(d.LumaView(r,2,0,0,0));assert pixel(r,200,20)[0]>.1,'Zoom affects real render'
    finally:d.LumaFreeFrame(p)
    i,p=decode('orientation.jpg');assert(i.width,i.height)==(40,90),'EXIF orientation';d.LumaFreeFrame(p)
    bad=Info();p=c.c_void_p();assert d.LumaDecode(str(out/'corrupt.jpg'),c.byref(bad),c.byref(p))<0 and not p
    s=Status();check(d.LumaStatus(r,c.byref(s)));assert s.fp16 and not s.hdrEffective and not s.vsrEffective
    print(f'PASS: SDR gamma, fit, zoom, rotation, EXIF, corrupt-file handling. GPU: {s.gpu}; HDR: {s.hdrActive}; FP16: {s.fp16}; SDK: {s.sdkAvailable}; TrueHDR: {s.hdrAvailable}; {s.hdrMessage}')
    i,p=decode('sdr.png')
    try:
        check(d.LumaUseFrame(r,p));check(d.LumaView(r,1,0,0,0));before=pixel(r,200,200)
        check(d.LumaPrepareEffects(r,1,0,0,1));check(d.LumaWaitForGpu(r));assert pixel(r,200,200)==before,'Background preparation must not present an obsolete result'
        check(d.LumaView(r,1,0,0,0));check(d.LumaStatus(r,c.byref(s)))
        print(f'TrueHDR evaluation: {s.hdrMessage}; result 0x{s.ngxResult:08X}; HDR calls {s.hdrEvaluations}; VSR calls {s.vsrEvaluations}; pixel {pixel(r,200,200)}')
        assert not s.vsrEffective and s.vsrEvaluations==0,'HDR-only must skip VSR entirely'
        if s.hdrAvailable and s.hdrActive:
            assert s.hdrEffective,'SDK did not evaluate TrueHDR'
            count=s.hdrEvaluations;check(d.LumaEffects(r,1,0,0,1));check(d.LumaStatus(r,c.byref(s)));assert s.hdrEvaluations==count,'Static HDR should be cached'
            check(d.LumaEffects(r,1,0,1,1));check(d.LumaStatus(r,c.byref(s)));assert not s.hdrEffective,'Hold original disables enhancement'
        check(d.LumaEffects(r,0,0,0,1));check(d.LumaStatus(r,c.byref(s)));assert not s.hdrEffective and not s.vsrEffective
    finally:d.LumaFreeFrame(p)
    if (out/'linear-hdr.jxr').exists():
        i,p=decode('linear-hdr.jxr')
        try:
            assert i.hdr,'Float JPEG XR must keep native HDR';check(d.LumaUseFrame(r,p));check(d.LumaView(r,1,0,0,0));check(d.LumaEffects(r,1,0,0,1));check(d.LumaStatus(r,c.byref(s)));assert s.nativeHdr and not s.hdrEffective,'Native HDR must bypass SDR enhancement';assert pixel(r,200,200)[0]>3.8,'FP16 must preserve values above SDR white'
            print('PASS: native HDR JPEG XR stays linear and preserves 4.0 (>1) through FP16 GPU readback')
        finally:d.LumaFreeFrame(p)
    i,p=decode('highlights.jpg')
    try:
        check(d.LumaUseFrame(r,p));check(d.LumaView(r,1,0,0,0));check(d.LumaEffects(r,1,0,0,1));check(d.LumaStatus(r,c.byref(s)))
        if s.hdrAvailable and s.hdrActive:
            assert s.hdrEffective and not s.vsrEffective;assert pixel(r,200,200)[0]>1,'TrueHDR JPEG highlight must survive FP16 output'
            print(f'PASS: 3840x2160 JPG -> official TrueHDR only -> FP16 highlight {pixel(r,200,200)[0]:.3f}, VSR calls {s.vsrEvaluations}')
    finally:d.LumaFreeFrame(p)
    i,p=decode('sdr.png')
    try:
        check(d.LumaUseFrame(r,p));check(d.LumaView(r,1,0,0,0));check(d.LumaStatus(r,c.byref(s)));hdr_count=s.hdrEvaluations;vsr_count=s.vsrEvaluations
        check(d.LumaEffects(r,0,1,0,1));check(d.LumaStatus(r,c.byref(s)))
        if s.vsrAvailable:
            assert s.vsrEffective and not s.hdrEffective and s.hdrEvaluations==hdr_count,(s.vsrMessage,s.ngxResult)
            assert s.vsrEvaluations==vsr_count+1
            check(d.LumaEffects(r,0,1,0,1));check(d.LumaStatus(r,c.byref(s)));assert s.vsrEvaluations==vsr_count+1,'VSR still must be cached'
            check(d.LumaEffects(r,1,1,0,1));check(d.LumaStatus(r,c.byref(s)))
            if s.hdrAvailable and s.hdrActive:assert s.hdrEffective and s.vsrEffective and s.hdrEvaluations==hdr_count+1
            check(d.LumaEffects(r,1,0,0,1));check(d.LumaStatus(r,c.byref(s)))
            if s.hdrAvailable and s.hdrActive:assert s.hdrEffective and not s.vsrEffective and s.hdrEvaluations==hdr_count+2,'Changing HDR input from VSR to original must invalidate HDR cache'
            print('PASS: independent VSR-only, combined mode, cached stills, return to HDR-only')
    finally:d.LumaFreeFrame(p)
    i,p=decode('alpha.png')
    try:
        check(d.LumaUseFrame(r,p));check(d.LumaStatus(r,c.byref(s)));hdr_count=s.hdrEvaluations;vsr_count=s.vsrEvaluations;memory=s.gpuBytes;check(d.LumaEffects(r,1,1,0,1));check(d.LumaStatus(r,c.byref(s)))
        assert not s.hdrEffective and not s.vsrEffective and s.hdrEvaluations==hdr_count and s.vsrEvaluations==vsr_count,'Transparent photos must not lose alpha through enhancement'
        assert s.gpuBytes==memory,'Unavailable enhancement must not allocate SDK input textures'
        value=pixel(r,200,200);assert abs(value[3]-128/255)<.001 and abs(value[0]-s.sdrWhite*128/255)<.01,'Transparent photo must use premultiplied alpha'
    finally:d.LumaFreeFrame(p)
    # Actual GPU blur must inherit nearby photo colors, keep outside pixels intact,
    # retain the linear HDR buffer, and fade in synchrony with XAML controls.
    chart=Image.new('RGB',(400,400),(235,35,25));ImageDraw.Draw(chart).rectangle((200,0,400,400),fill=(20,40,235));chart.save(out/'glass-chart.png')
    i,p=decode('glass-chart.png')
    try:
        check(d.LumaEffects(r,0,0,0,1));check(d.LumaUseFrame(r,p));check(d.LumaView(r,1,0,0,0));outside=pixel(r,15,200);red=pixel(r,90,200)
        rect=(GlassRect*1)(GlassRect(40,40,320,320,18,1));check(d.LumaSetGlass(r,rect,1,0));check(d.LumaRepaint(r))
        left=pixel(r,90,200);right=pixel(r,310,200)
        assert left[0]>right[0]+.3 and right[2]>left[2]+.3,'Glass must sample actual red/blue photo, not a flat tint'
        assert pixel(r,15,200)==outside,'Glass must never blur or alter the photo outside its panel'
        assert left[1]>red[1] and left[0]>1,'FP16 glass tint must retain linear high-range values'
        middle=pixel(r,200,200);assert middle[0]<left[0]-.3 and middle[2]>left[2]+.3,'Gaussian blur must mix the actual red/blue boundary'
        popup=(c.c_uint8*(320*320*4))();check(d.LumaReadGlass(r,rect[0],0,320,320,popup,len(popup)))
        assert popup[(160*320+30)*4+2]>popup[(160*320+280)*4+2]+40,'Popup brush must inherit actual photo colors'
        assert all(popup[n]==255 for n in range(3,len(popup),4)),'Popup background must occlude underlying XAML chrome'
        assert pixel(r,15,200)==outside,'Popup background readback must not change FP16 photo output'
        check(d.LumaSetGlass(r,rect,1,1));check(d.LumaRepaint(r));dark=pixel(r,90,200);assert sum(dark[:3])<sum(left[:3]),'Dark theme glass'
        assert sum(dark[ch]*weight for ch,weight in enumerate((.2126,.7152,.0722)))<.05*s.sdrWhite,'Dark chrome must retain white-text contrast above a bright photo'
        rect[0].opacity=0;check(d.LumaSetGlass(r,rect,1,0));check(d.LumaRepaint(r));assert pixel(r,90,200)==red,'Hidden toolbar must reveal original pixels'
        # A portrait/zoomed-out photograph leaves transparent margins. The glass
        # must transition smoothly across that boundary, including blurred alpha.
        rect[0].opacity=1;check(d.LumaSetGlass(r,rect,1,0));check(d.LumaView(r,.5,0,0,0));
        edge=[pixel(r,x,200) for x in range(65,135)]
        assert max(abs(a[ch]-b[ch]) for a,b in zip(edge,edge[1:]) for ch in range(3))<.08*s.sdrWhite,'Glass has a hard seam at the transparent photo boundary'
        assert max(abs(edge[0][ch]-edge[-1][ch]) for ch in range(3))>.25,'Native Gaussian must retain a gradual photo/letterbox transition'
        check(d.LumaSetGlass(r,None,0,0));check(d.LumaRepaint(r))
        print(f'PASS: real photo-sampled FP16 glass, rounded panels, dark theme, untouched photo and fade; red {left}, blue {right}')
    finally:d.LumaFreeFrame(p)
    # Fine repeating texture should converge to its linear-light average when reduced.
    small=Image.frombytes('RGB',(256,256),bytes(v for y in range(256) for x in range(256) for v in ((255,255,255) if (x+y)%2 else (0,0,0))))
    small.save(out/'checker.png');i,p=decode('checker.png')
    try:
        check(d.LumaEffects(r,0,0,0,1));check(d.LumaResize(r,17,17,1,1));check(d.LumaUseFrame(r,p));check(d.LumaStatus(r,c.byref(s)))
        value=pixel(r,8,8)[0];assert abs(value-.5*s.sdrWhite)<.02*s.sdrWhite,(value,s.sdrWhite)
        assert s.gpuBytes>i.bytes,'GPU budget must include mip levels and canvas'
        print('PASS: linear-light mip resampling and owned GPU allocation reporting')
    finally:d.LumaFreeFrame(p)
finally:d.LumaDestroy(r)
if len(sys.argv)>1:
    import time
    path=pathlib.Path(sys.argv[1]);i=Info();p=c.c_void_p();r=c.c_void_p();start=time.perf_counter()
    check(d.LumaDecode(str(path),c.byref(i),c.byref(p)));check(d.LumaCreate(None,None,c.byref(r)))
    try:
        check(d.LumaResize(r,1280,720,1,1));check(d.LumaUseFrame(r,p));check(d.LumaEffects(r,1,0,0,1));s=Status();check(d.LumaStatus(r,c.byref(s)))
        assert s.hdrEffective and not s.vsrEffective and s.vsrEvaluations==0
        print(f'PASS: large user JPG {i.ow}x{i.oh}; decoded {i.width}x{i.height}; {s.hdrMessage}; HDR/VSR calls {s.hdrEvaluations}/{s.vsrEvaluations}; total {time.perf_counter()-start:.2f}s')
    finally:d.LumaFreeFrame(p);d.LumaDestroy(r)
