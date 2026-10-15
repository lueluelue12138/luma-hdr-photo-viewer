"""Compare the settings-only display query with the actual renderer monitor path."""
import ctypes as c,json,os,pathlib,statistics,time
from ctypes import wintypes as w
root=pathlib.Path(__file__).resolve().parents[1]
d=c.CDLL(os.environ.get('LUMA_NATIVE_TEST_DLL',str(root/'src/Luma.Native/bin/Luma.Native.dll')))
class Display(c.Structure):
    _fields_=[('device',c.c_wchar*128),('name',c.c_wchar*128),('hdr',c.c_int),('whiteKnown',c.c_int)]+[(n,c.c_float) for n in ('peak','fullFrame','minimum','white')]
class Status(c.Structure):
    _fields_=[('gpu',c.c_wchar*128),('display',c.c_wchar*128)]+[(n,c.c_int) for n in ('nvidia','hdr','fp16','nativeHdr')]+[('peak',c.c_float),('white',c.c_float),('gpuBytes',c.c_uint64)]+[(n,c.c_int) for n in ('hdrEffective','vsrEffective','sdk','hdrAvailable','vsrAvailable')]+[(n,c.c_uint) for n in ('ngx','hdrCalls','vsrCalls')]+[('hdrMessage',c.c_wchar*256),('vsrMessage',c.c_wchar*256)]
d.LumaGetDisplayInfo.argtypes=[w.HWND,c.POINTER(Display)];d.LumaGetDisplayInfo.restype=c.c_long
d.LumaCreateFast.argtypes=[c.c_void_p,w.HWND,c.POINTER(c.c_void_p)];d.LumaProbeDisplay.argtypes=[c.c_void_p,c.POINTER(Status)];d.LumaDestroy.argtypes=[c.c_void_p];d.LumaError.restype=c.c_wchar_p
def check(hr):
    assert hr>=0,f'{hr&0xffffffff:08x}: {d.LumaError()}'
bad=Display();bad.peak=1234
assert d.LumaGetDisplayInfo(None,c.byref(bad))<0 and bad.peak==0,'Invalid HWND must not preserve stale data'
assert d.LumaGetDisplayInfo(None,None)<0,'Null output must fail safely'
u=c.WinDLL('user32',use_last_error=True)
u.CreateWindowExW.argtypes=[w.DWORD,w.LPCWSTR,w.LPCWSTR,w.DWORD,c.c_int,c.c_int,c.c_int,c.c_int,w.HWND,w.HMENU,w.HINSTANCE,c.c_void_p];u.CreateWindowExW.restype=w.HWND
u.DestroyWindow.argtypes=[w.HWND]
callback=c.WINFUNCTYPE(w.BOOL,w.HMONITOR,w.HDC,c.POINTER(w.RECT),w.LPARAM);rects=[]
def collect(monitor,dc,rect,data):
    a=rect.contents;rects.append((a.left,a.top,a.right,a.bottom));return True
u.EnumDisplayMonitors(None,None,callback(collect),0)
rows=[]
for left,top,right,bottom in rects:
    hwnd=u.CreateWindowExW(0,'STATIC','Luma read-only display query verification',0x80000000,(left+right)//2,(top+bottom)//2,16,16,None,None,None,None)
    assert hwnd,'Test window creation failed'
    renderer=c.c_void_p()
    try:
        info=Display();timing=[]
        for n in range(8):
            start=time.perf_counter();check(d.LumaGetDisplayInfo(hwnd,c.byref(info)));timing.append((time.perf_counter()-start)*1000)
        check(d.LumaCreateFast(None,hwnd,c.byref(renderer)));status=Status();check(d.LumaProbeDisplay(renderer,c.byref(status)))
        assert status.display==info.device and status.hdr==info.hdr and abs(status.peak-info.peak)<.01,'Settings must query the same window monitor and peak as the renderer'
        if info.whiteKnown:assert abs(status.white*80-info.white)<.01,'SDR white ratio must be converted to nits exactly once'
        assert status.hdrCalls==status.vsrCalls==0,'A display query must not evaluate HDR or VSR'
        rows.append(dict(name=info.name,device=info.device,hdr=info.hdr,peak=info.peak,fullFrame=info.fullFrame,sdrWhite=info.white if info.whiteKnown else None,firstMs=timing[0],medianMs=statistics.median(timing),bounds=[left,top,right,bottom]))
    finally:
        if renderer:d.LumaDestroy(renderer)
        u.DestroyWindow(hwnd)
out=root/'artifacts'/('display-info-'+pathlib.Path(d._name).parent.name+'.json');out.write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(rows,ensure_ascii=False,indent=2));print(f'PASS: null/invalid handles, {len(rows)} actual monitor(s), renderer agreement, no AI evaluation. {out}')
