"""Real iPhone HEIC, ISO HEIC, official PQ; independent math + GPU FP16.

Optional oracle dependencies are QA-only: pip install pillow-heif numpy.
Run Get-HdrSamples.ps1, then this file with --samples and --dll.
"""
import argparse,ctypes as c,io,json,math,pathlib,re,statistics,struct,sys,time,hashlib
from PIL import Image,ImageCms
root=pathlib.Path(__file__).resolve().parents[1]
ap=argparse.ArgumentParser();ap.add_argument('--dll',required=True);ap.add_argument('--samples',default='artifacts/hdr-samples');ap.add_argument('--user-heic');ap.add_argument('--oracle-path');args=ap.parse_args()
if args.oracle_path:sys.path.insert(0,str(pathlib.Path(args.oracle_path).resolve()))
import pillow_heif,numpy as np
samples=pathlib.Path(args.samples).resolve();out=root/'artifacts/heic-verification';out.mkdir(parents=True,exist_ok=True)
class Info(c.Structure):_fields_=[('w',c.c_uint),('h',c.c_uint),('ow',c.c_uint),('oh',c.c_uint),('hdr',c.c_int),('down',c.c_int),('bytes',c.c_uint64)]
class Gain(c.Structure):_fields_=[('minimum',c.c_float),('maximum',c.c_float),('os',c.c_float*3),('oh',c.c_float*3),('gamut',c.c_int),('model',c.c_int)]
class Status(c.Structure):_fields_=[('gpu',c.c_wchar*128),('display',c.c_wchar*128),('nvidia',c.c_int),('hdrActive',c.c_int),('fp16',c.c_int),('nativeHdr',c.c_int),('maxNits',c.c_float),('sdrWhite',c.c_float),('gpuBytes',c.c_uint64),('hdrEffective',c.c_int),('vsrEffective',c.c_int),('sdkAvailable',c.c_int),('hdrAvailable',c.c_int),('vsrAvailable',c.c_int),('ngxResult',c.c_uint),('hdrEvaluations',c.c_uint),('vsrEvaluations',c.c_uint),('hdrMessage',c.c_wchar*256),('vsrMessage',c.c_wchar*256)]
d=c.CDLL(str(pathlib.Path(args.dll).resolve()));d.LumaError.restype=c.c_wchar_p
for n in ['LumaDecodeSized','LumaDecodeThumbnail']:getattr(d,n).argtypes=[c.c_wchar_p,c.c_uint,c.POINTER(Info),c.POINTER(c.c_void_p)]
for n in ['LumaFreeFrame','LumaDestroy']:getattr(d,n).argtypes=[c.c_void_p]
d.LumaFramePixel.argtypes=[c.c_void_p,c.c_uint,c.c_uint,c.POINTER(c.c_float)];d.LumaFrameGainInfo.argtypes=[c.c_void_p,c.POINTER(Gain)];d.LumaCopySdrBgra.argtypes=[c.c_void_p,c.POINTER(c.c_uint8),c.c_uint64]
d.LumaCreateFast.argtypes=[c.c_void_p,c.c_void_p,c.POINTER(c.c_void_p)];d.LumaResize.argtypes=[c.c_void_p,c.c_uint,c.c_uint,c.c_float,c.c_float];d.LumaUseFrame.argtypes=[c.c_void_p,c.c_void_p];d.LumaReadPixel.argtypes=[c.c_void_p,c.c_uint,c.c_uint,c.POINTER(c.c_uint16)]
d.LumaCopyStatus.argtypes=[c.c_void_p,c.POINTER(Status)];d.LumaEffects.argtypes=[c.c_void_p,c.c_int,c.c_int,c.c_int,c.c_uint];d.LumaSetOriginal.argtypes=[c.c_void_p,c.c_int];d.LumaRepaint.argtypes=[c.c_void_p];d.LumaNativeDisplayTest.argtypes=[c.c_void_p,c.c_int,c.c_float,c.c_float]
def check(hr):
 if hr<0:raise RuntimeError(f'{hr&0xffffffff:08X}: {d.LumaError()}')
def decode(path,edge=512,thumb=False):
 i=Info();p=c.c_void_p();start=time.perf_counter();check(getattr(d,'LumaDecodeThumbnail' if thumb else 'LumaDecodeSized')(str(path),edge,c.byref(i),c.byref(p)));return i,p,(time.perf_counter()-start)*1000
def pixel(p,x,y):
 v=(c.c_float*4)();check(d.LumaFramePixel(p,x,y,v));return list(v)
def gpu(r,x,y):
 v=(c.c_uint16*4)();check(d.LumaReadPixel(r,x,y,v));return [struct.unpack('<e',struct.pack('<H',x))[0] for x in v]
def status(r):
 s=Status();check(d.LumaCopyStatus(r,c.byref(s)));return s
P3=np.array([[1.22494,-.22494,0],[-.042057,1.042057,0],[-.019638,-.078636,1.098274]],dtype='float32')
BT2020=np.array([[1.660491,-.587641,-.07285],[-.124551,1.1329,-.008349],[-.018151,-.100579,1.11873]],dtype='float32')
def inverse_srgb(v):return np.where(v<=.04045,v/12.92,((v+.055)/1.055)**2.4)
def resize_float(v,size):
 if v.ndim==2:return np.array(Image.fromarray(v.astype('float32')).resize(size,Image.Resampling.BOX))
 return np.stack([resize_float(v[:,:,j],size) for j in range(3)],axis=2)
def apple_headroom(h,path):
 raw=path.read_bytes();found=re.search(rb'HDRGainMapHeadroom[^>]*>([0-9.]+)',raw)
 if found:return float(found[1])
 ex=Image.Exif();ex.load(h.info['exif']);note=ex.get_ifd(34665)[37500];n=int.from_bytes(note[14:16],'big');v={}
 for j in range(n):
  tag,typ,count,offset=struct.unpack_from('>HHII',note,16+j*12)
  if tag in [33,48]:num,den=struct.unpack_from('>iI',note,offset);v[tag]=num/den
 m33,m48=v[33],v[48];stops=(-20*m48+1.8 if m48<=.01 else -.101*m48+1.601) if m33<1 else (-70*m48+3 if m48<=.01 else -.303*m48+2.303);return 2**max(stops,0)
def apple_oracle(path,size):
 h=pillow_heif.open_heif(path);head=apple_headroom(h,path);base=np.asarray(h).astype('float32')/255;aid=h.info['aux']['urn:com:apple:photo:2020:aux:hdrgainmap'][0];gain=np.asarray(h.get_aux_image(aid)).astype('float32')/255
 base=resize_float(base,size);gain=resize_float(gain,size);gain=np.where(gain<.081,gain/4.5,((gain+.099)/1.099)**(1/.45));rgb=inverse_srgb(base)*(1+(head-1)*gain[:,:,None]);rgb=rgb@P3.T
 return rgb*203/80,head
def pq_oracle(path,size):
 h=pillow_heif.open_heif(path,convert_hdr_to_8bit=False);assert h.info['nclx_profile']['transfer_characteristics']==16
 raw=np.asarray(h).astype('float32')/65535
 # ST2084 is absolute, with 10000 nits at encoded 1.0.
 v=raw**(1/(2523/32));linear=(np.maximum(v-3424/4096,0)/(2413/128-2392/128*v))**(1/(2610/16384))*10000/80
 linear=resize_float(linear,size);return linear@BT2020.T
def iso_oracle(path,size):
 # Independently read the item's byte ranges and rational metadata. Pixel
 # decoding uses libheif (through pillow-heif), independently of Windows WIC.
 raw=path.read_bytes();items={};item_flags={};locations={};links={};primary_offset=None
 def boxes(start,end):
  while start+8<=end:
   n,typ=struct.unpack_from('>I4s',raw,start);hd=8
   if n==1:n=struct.unpack_from('>Q',raw,start+8)[0];hd=16
   if n==0:n=end-start
   assert n>=hd and start+n<=end
   yield typ,start+hd,start+n
   start+=n
 meta=next((a,b) for t,a,b in boxes(0,len(raw)) if t==b'meta');children=list(boxes(meta[0]+4,meta[1]));idat=next((a for t,a,b in children if t==b'idat'),0)
 for typ,a,b in children:
  if typ==b'pitm':primary_offset=a+4;primary_size=4 if raw[a] else 2
  if typ==b'iinf':
   for t,q,e in boxes(a+(8 if raw[a] else 6),b):
    ids=4 if raw[q]==3 else 2;ident=int.from_bytes(raw[q+4:q+4+ids],'big');items[ident]=raw[q+6+ids:q+10+ids];item_flags[ident]=q+3
  if typ==b'iref':
   ids=4 if raw[a] else 2
   for t,q,e in boxes(a+4,b):
    ident=int.from_bytes(raw[q:q+ids],'big');count=int.from_bytes(raw[q+ids:q+ids+2],'big');links[(t,ident)]=[int.from_bytes(raw[q+ids+2+j*ids:q+ids+2+(j+1)*ids],'big') for j in range(count)]
  if typ==b'iloc':
   version=raw[a];p=a+4;os,ls,bs,ix=raw[p]>>4,raw[p]&15,raw[p+1]>>4,(raw[p+1]&15 if version else 0);p+=2
   def read(n):
    nonlocal p
    v=int.from_bytes(raw[p:p+n],'big');p+=n;return v
   for _ in range(read(2 if version<2 else 4)):
    ident=read(2 if version<2 else 4);method=(read(2)&15) if version else 0;assert read(2)==0;base=read(bs);chunks=[]
    for j in range(read(2)):read(ix);offset=read(os);length=read(ls);chunks.append(raw[(idat if method else 0)+base+offset:(idat if method else 0)+base+offset+length])
    locations[ident]=b''.join(chunks)
 tmap=next(k for k,v in items.items() if v==b'tmap');base_id,gain_id=links[(b'dimg',tmap)];packet=locations[tmap];assert packet[0]==0;packet=packet[1:];p=4;flags=packet[p];p+=1;channels=3 if flags&128 else 1;common=bool(flags&8)
 def u32(signed=False):
  nonlocal p
  v=int.from_bytes(packet[p:p+4],'big',signed=signed);p+=4;return v
 den=u32() if common else None
 def fraction(signed=False):
  n=u32(signed);d=den if common else u32();return n/d
 headmin,headmax=2**fraction(),2**fraction();fields=[]
 for j in range(channels):fields.append([fraction(True),fraction(True),fraction(),fraction(True),fraction(True)])
 if channels==1:fields*=3
 def read_item(ident):
  copy=bytearray(raw);copy[primary_offset:primary_offset+primary_size]=ident.to_bytes(primary_size,'big');copy[item_flags[ident]]&=254;f=out/f'iso-item-{ident}.heic';f.write_bytes(copy);h=pillow_heif.open_heif(f,convert_hdr_to_8bit=False);v=np.asarray(h).astype('float32');v/=65535 if v.max()>255 else 255;return resize_float(v,size)
 base=inverse_srgb(read_item(base_id));gain=read_item(gain_id);hdr=np.zeros_like(base)
 for j,(lo,hi,gamma,os,oh) in enumerate(fields):hdr[:,:,j]=(base[:,:,j]+os)*2**(lo+(hi-lo)*gain[:,:,j]**(1/gamma))-oh
 return hdr@P3.T*203/80,headmax
report=[];renderer=c.c_void_p();check(d.LumaCreateFast(None,None,c.byref(renderer)));real=status(renderer)
paths=[(samples/'apple-camera.heic',3),(samples/'ipad-iso/matrix4.HEIC',4),(samples/'apple-official-pq/example-ISO-HDR-images/image_01.heic',1)]
if args.user_heic:paths.insert(0,(pathlib.Path(args.user_heic).resolve(),3))
try:
 for path,kind in paths:
  before=(path.stat().st_mtime_ns,hashlib.sha256(path.read_bytes()).hexdigest());i,p,elapsed=decode(path);assert i.hdr==kind and max(i.w,i.h)<=512
  try:
   gain=Gain();check(d.LumaFrameGainInfo(p,c.byref(gain)));oracle=None;head=None
   if kind==3:oracle,head=apple_oracle(path,(i.w,i.h));assert abs(head-gain.maximum)<.0001
   elif kind==4:oracle,head=iso_oracle(path,(i.w,i.h));assert abs(head-gain.maximum)<.0001
   elif kind==1:oracle=pq_oracle(path,(i.w,i.h))
   values=[];errors=[]
   for y in range(8,i.h-8,11):
    for x in range(8,i.w-8,11):
     actual=pixel(p,x,y);values.append((max(actual[:3]),x,y))
     if oracle is not None:errors.extend(abs(actual[j]-float(oracle[y,x,j])) for j in range(3))
   peak,x,y=max(values);assert peak>1
   if errors:
    # libheif and WIC use different chroma/resampling filters. Test a tight
    # image-wide mean separately from the isolated edge maximum.
    assert statistics.mean(errors)<(.2 if kind==4 else .12),(path.name,statistics.mean(errors),max(errors))
   check(d.LumaResize(renderer,i.w,i.h,1,1));check(d.LumaUseFrame(renderer,p));check(d.LumaNativeDisplayTest(renderer,1,203*256,203/80));shown=gpu(renderer,x,y);raw=pixel(p,x,y)
   assert max(abs(a-b) for a,b in zip(shown,raw))<.04,(path.name,shown,raw)
   check(d.LumaEffects(renderer,1,1,0,1));s=status(renderer);assert s.fp16 and not s.hdrEffective and not s.vsrEffective and not s.hdrEvaluations and not s.vsrEvaluations
   toggles=[]
   for repeat in range(20):
    t=time.perf_counter();check(d.LumaSetOriginal(renderer,1));check(d.LumaSetOriginal(renderer,0));toggles.append((time.perf_counter()-t)*1000)
   assert status(renderer).hdrEvaluations==0
   outputs=[]
   if kind in [3,4]:
    for nits in [203,406,812,real.maxNits]:
     check(d.LumaNativeDisplayTest(renderer,1,nits,real.sdrWhite));outputs.append(dict(peakNits=nits,pixel=gpu(renderer,x,y)))
    assert max(outputs[0]['pixel'][:3])<=203/80*1.3,(path.name,outputs,gain.minimum,gain.maximum,list(gain.os),list(gain.oh),gain.gamut)
    assert all(max(a['pixel'][:3])<=max(b['pixel'][:3])+.02 for a,b in zip(outputs[:2],outputs[1:3]))
    check(d.LumaNativeDisplayTest(renderer,0,real.maxNits,1));assert max(gpu(renderer,x,y)[:3])<=1.01
   report.append(dict(file=path.name,kind=kind,width=i.w,height=i.h,original=[i.ow,i.oh],frameBytes=i.bytes,decodeMs=elapsed,headroom=head,referenceMeanError=statistics.mean(errors) if errors else None,referenceMaxError=max(errors) if errors else None,sourcePeakScRgb=peak,gpuPeakScRgb=max(shown[:3]),fp16=True,hdrCalls=0,vsrCalls=0,selectorMedianMs=statistics.median(toggles),adaptiveOutputs=outputs))
  finally:d.LumaFreeFrame(p)
  if kind in [3,4]:
   ti,tp,tm=decode(path,128,True)
   try:assert ti.hdr==0 and ti.bytes==ti.w*ti.h*4;report[-1]['thumbnailMs']=tm
   finally:d.LumaFreeFrame(tp)
  assert before==(path.stat().st_mtime_ns,hashlib.sha256(path.read_bytes()).hexdigest())
 # Own SDR HEIC ensures HEIC is not automatically classified as HDR.
 sdr=out/'sdr-heic.heic';pillow_heif.from_pillow(Image.new('RGB',(320,240),(120,130,140))).save(sdr,quality=90);i,p,_=decode(sdr)
 try:assert i.hdr==0
 finally:d.LumaFreeFrame(p)
 # A transfer/rename may choose .heif. It must retain the same HDR path.
 alias=out/'apple-camera.heif';alias.write_bytes(paths[-3][0].read_bytes() if args.user_heic else paths[0][0].read_bytes());i,p,_=decode(alias)
 try:assert i.hdr==3
 finally:d.LumaFreeFrame(p)
 for n,blob in enumerate([b'\0'*32,paths[0][0].read_bytes()[:100],b'\0\0\0\xffmeta'+b'\0'*32]):
  broken=out/f'broken-{n}.heic';broken.write_bytes(blob);i=Info();p=c.c_void_p();hr=d.LumaDecodeSized(str(broken),128,c.byref(i),c.byref(p));assert hr<0 and not p.value
finally:d.LumaDestroy(renderer)
result=dict(complete=True,windowsPeakNits=real.maxNits,windowsSdrWhiteNits=real.sdrWhite*80,windowsHdr=bool(real.hdrActive),results=report,sdrHeic=True,heifAlias=True,malformedRejected=3)
(out/'heic.json').write_text(json.dumps(result,indent=2,ensure_ascii=False),encoding='utf8')
print(json.dumps(result,indent=2,ensure_ascii=False));print('PASS: real HEIC HDR, reference math, FP16, display adaptation, cached comparison and SDR fallback')
