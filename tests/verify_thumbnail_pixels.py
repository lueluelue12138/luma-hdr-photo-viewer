"""Actual WIC-to-WinUI thumbnail pixels, EXIF/ICC and HDR rejection."""
import ctypes as c,json,os,pathlib
from PIL import Image,ImageCms
root=pathlib.Path(__file__).resolve().parents[1]
out=root/'artifacts/thumbnail-pixels';out.mkdir(parents=True,exist_ok=True)
class Info(c.Structure):
    _fields_=[('w',c.c_uint),('h',c.c_uint),('ow',c.c_uint),('oh',c.c_uint),('hdr',c.c_int),('down',c.c_int),('bytes',c.c_uint64)]
d=c.CDLL(os.environ.get('LUMA_NATIVE_TEST_DLL',str(root/'artifacts/Luma/Luma.Native.dll')));d.LumaError.restype=c.c_wchar_p
d.LumaDecodeSized.argtypes=[c.c_wchar_p,c.c_uint,c.POINTER(Info),c.POINTER(c.c_void_p)]
d.LumaFreeFrame.argtypes=[c.c_void_p];d.LumaCopySdrBgra.argtypes=[c.c_void_p,c.POINTER(c.c_uint8),c.c_uint64]
def check(hr):
    if hr<0:raise RuntimeError(d.LumaError())
def decode(path,edge=480):
    info=Info();frame=c.c_void_p();check(d.LumaDecodeSized(str(path),edge,c.byref(info),c.byref(frame)));return info,frame
def copy(info,frame):
    pixels=(c.c_uint8*info.bytes)();check(d.LumaCopySdrBgra(frame,pixels,info.bytes));return bytes(pixels)
image=Image.new('RGBA',(2,2));image.putdata([(200,100,50,128),(9,40,90,0),(255,128,0,255),(24,80,160,64)]);image.save(out/'alpha.png')
info,frame=decode(out/'alpha.png')
try:
    assert copy(info,frame)==bytes([25,50,100,128,0,0,0,0,0,128,255,255,40,20,6,64]),'BGRA/premultiplied alpha mismatch'
    guard=(c.c_uint8*info.bytes)(*([204]*info.bytes));assert d.LumaCopySdrBgra(frame,guard,info.bytes-1)<0 and bytes(guard)==bytes([204]*info.bytes),'Short destination was modified'
finally:d.LumaFreeFrame(frame)
format_checks=[]
for extension,format in [('bmp','BMP'),('gif','GIF'),('tif','TIFF'),('ico','ICO'),('png','PNG')]:
    image=Image.new('RGBA' if extension=='ico' else 'RGB',(32,32),(48,96,160));path=out/('format.'+extension);image.save(path,format=format,**({'sizes':[(32,32)]} if extension=='ico' else {}))
    info,frame=decode(path)
    try:
        assert (info.w,info.h,info.hdr)==(32,32,0),(extension,info.w,info.h)
        pixels=copy(info,frame);assert pixels[:4]==bytes([160,96,48,255]),(extension,pixels[:4])
        format_checks.append(extension)
    finally:d.LumaFreeFrame(frame)
first=Image.new('RGB',(32,32),(255,0,0));first.save(out/'animated.gif',save_all=True,append_images=[Image.new('RGB',(32,32),(0,0,255))],duration=100,loop=0)
info,frame=decode(out/'animated.gif')
try:assert copy(info,frame)[:4]==bytes([0,0,255,255]),'GIF thumbnail did not use the supported first frame'
finally:d.LumaFreeFrame(frame)
profile=ImageCms.ImageCmsProfile(ImageCms.createProfile('sRGB')).tobytes()
Image.new('RGB',(2048,1024),(48,96,160)).save(out/'profile.jpg',quality=100,subsampling=0,icc_profile=profile)
info,frame=decode(out/'profile.jpg')
try:
    assert (info.w,info.h)==(480,240);pixels=copy(info,frame);assert all(abs(pixels[i]-v)<=2 for i,v in enumerate([160,96,48,255])),'ICC/channel order changed thumbnail color'
finally:d.LumaFreeFrame(frame)
image=Image.new('RGB',(2048,1024),(255,0,0));image.paste((0,0,255),(1024,0,2048,1024));exif=Image.Exif();exif[274]=6;image.save(out/'orientation.jpg',quality=100,subsampling=0,exif=exif)
info,frame=decode(out/'orientation.jpg')
try:
    assert (info.w,info.h,info.ow,info.oh)==(240,480,1024,2048);pixels=copy(info,frame)
    top=pixels[(120*info.w+120)*4:][:4];bottom=pixels[(360*info.w+120)*4:][:4]
    assert top[2]>240 and top[0]<5 and bottom[0]>240 and bottom[2]<5,'EXIF rotation changed thumbnail orientation'
finally:d.LumaFreeFrame(frame)
hdr=root/'artifacts/native-tests/linear-hdr.jxr';assert hdr.is_file(),'Generate the existing HDR fixture first'
info,frame=decode(hdr,64)
try:
    assert info.hdr;guard=(c.c_uint8*info.bytes)(*([204]*info.bytes));assert d.LumaCopySdrBgra(frame,guard,info.bytes)<0 and bytes(guard)==bytes([204]*info.bytes),'Native HDR was silently converted to 8-bit SDR'
finally:d.LumaFreeFrame(frame)
out.joinpath('thumbnail-pixels.json').write_text(json.dumps({'complete':True,'checks':['premultiplied-BGRA','short-buffer-guard','scaled-JPEG-ICC','EXIF-rotation','native-HDR-rejection','GIF-first-frame'],'sdrFormats':format_checks},indent=2),encoding='utf-8')
print('PASS: WIC thumbnail BGRA/alpha, short buffer, JPEG ICC/EXIF, native HDR rejection.',out)
