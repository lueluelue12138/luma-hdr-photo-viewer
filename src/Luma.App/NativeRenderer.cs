using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;

namespace Luma.App;
[StructLayout(LayoutKind.Sequential)]
internal struct FrameInfo {public uint Width,Height,OriginalWidth,OriginalHeight;public int NativeHdr,Downsampled;public ulong Bytes;}
[StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
internal struct RenderStatus
{
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Gpu;
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Display;
    public int Nvidia,HdrActive,Fp16,NativeHdr;public float MaxNits,SdrWhite;public ulong GpuBytes;public int HdrEffective,VsrEffective,SdkAvailable,HdrAvailable,VsrAvailable;public uint NgxResult,HdrEvaluations,VsrEvaluations;
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string HdrMessage;
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string VsrMessage;
    public string HdrReason => NativeHdr!=0 ? "原生 HDR 图片，跳过 SDR 增强" : HdrEffective!=0 ? HdrMessage : HdrActive==0 ? "当前显示器未启用 Windows HDR" : HdrMessage;
}
internal static class Native
{
    private const string Dll="Luma.Native.dll";
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern IntPtr LumaError();
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)]internal static extern int LumaDecode(string path,out FrameInfo info,out IntPtr frame);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern void LumaFreeFrame(IntPtr frame);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaCreate(IntPtr panel,IntPtr hwnd,out IntPtr renderer);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern void LumaDestroy(IntPtr renderer);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaResize(IntPtr renderer,uint w,uint h,float scaleX,float scaleY);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaUseFrame(IntPtr renderer,IntPtr frame);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaPrepareFrame(IntPtr renderer,IntPtr frame);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaView(IntPtr renderer,float zoom,float x,float y,int rotation);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaStatus(IntPtr renderer,out RenderStatus status);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaPeekStatus(IntPtr renderer,out RenderStatus status);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaCopyStatus(IntPtr renderer,out RenderStatus status);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaWaitForGpu(IntPtr renderer);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaEffects(IntPtr renderer,int hdr,int vsr,int original,uint preset);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaPrepareEffects(IntPtr renderer,int hdr,int vsr,int original,uint preset);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaHdrTuning(IntPtr renderer,uint middleGray,uint peak);
    internal static void Check(int result){if(result<0)throw new COMException(Marshal.PtrToStringUni(LumaError())??"原生渲染失败",result);}
}
internal sealed class DecodedFrame : SafeHandle
{
    public FrameInfo Info {get;}
    public DecodedFrame(IntPtr pointer,FrameInfo info):base(IntPtr.Zero,true){SetHandle(pointer);Info=info;}
    public override bool IsInvalid=>handle==IntPtr.Zero;
    protected override bool ReleaseHandle(){Native.LumaFreeFrame(handle);return true;}
}
internal sealed class FrameLease : IDisposable
{
    public DecodedFrame Frame {get;}
    private bool held;
    public FrameLease(DecodedFrame frame){Frame=frame;frame.DangerousAddRef(ref held);}
    public void Dispose(){if(held){held=false;Frame.DangerousRelease();}}
}
internal sealed class FrameCache : IDisposable
{
    private readonly SemaphoreSlim workers=new(1);
    private readonly Dictionary<string,(DecodedFrame Frame,long Modified,long Used)> frames=new(StringComparer.OrdinalIgnoreCase);
    private long tick; private bool disposed;
    private readonly object sync=new();
    private const ulong Budget=256*1024*1024;
    public async Task<FrameLease> GetAsync(string path,CancellationToken token)
    {
        long modified=File.GetLastWriteTimeUtc(path).Ticks;
        lock(sync){if(disposed)throw new ObjectDisposedException(nameof(FrameCache));if(frames.TryGetValue(path,out var hit)&&hit.Modified==modified){frames[path]=(hit.Frame,modified,++tick);return new(hit.Frame);}}
        await workers.WaitAsync(token);
        DecodedFrame? decoded=null;
        try
        {
            token.ThrowIfCancellationRequested();
            lock(sync){if(frames.TryGetValue(path,out var hit)&&hit.Modified==modified)return new(hit.Frame);}
            decoded=await Task.Run(()=>{Native.Check(Native.LumaDecode(path,out var info,out var p));return new DecodedFrame(p,info);});
            token.ThrowIfCancellationRequested();
            lock(sync)
            {
                if(disposed)throw new ObjectDisposedException(nameof(FrameCache));
                if(frames.TryGetValue(path,out var hit)&&hit.Modified==modified){decoded.Dispose();decoded=null;return new(hit.Frame);}
                if(frames.Remove(path,out var old))old.Frame.Dispose();
                frames[path]=(decoded,modified,++tick);var result=new FrameLease(decoded);decoded=null;return result;
            }
        }
        finally{decoded?.Dispose();workers.Release();}
    }
    // Called on the UI thread after use. Keep current image pinned while evicting prefetch.
    public void Trim(string current)
    {
        lock(sync)
        {
            ulong total=0;foreach(var e in frames.Values)total+=e.Frame.Info.Bytes;
            foreach(var e in frames.OrderBy(x=>x.Value.Used).ToList())
            {if(total<=Budget&&frames.Count<=5)break;if(string.Equals(e.Key,current,StringComparison.OrdinalIgnoreCase))continue;frames.Remove(e.Key);total-=e.Value.Frame.Info.Bytes;e.Value.Frame.Dispose();}
        }
    }
    public void Dispose(){lock(sync){disposed=true;foreach(var f in frames.Values)f.Frame.Dispose();frames.Clear();}}
}
internal sealed class NativeRenderer : IDisposable
{
    private IntPtr handle;
    private readonly SemaphoreSlim access=new(1);
    private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcher;
    private bool disposed;
    private RenderStatus snapshot;
    private (double W,double H,double Dpi)? pendingSize;
    private (double Zoom,double X,double Y,int Rotation)? pendingView;
    public bool Busy=>access.CurrentCount==0;
    public NativeRenderer(SwapChainPanel panel,IntPtr hwnd)
    {
        dispatcher=panel.DispatcherQueue;
        var pointer=WinRT.MarshalInspectable<SwapChainPanel>.FromManaged(panel);
        try{Native.Check(Native.LumaCreate(pointer,hwnd,out handle));Native.Check(Native.LumaPeekStatus(handle,out snapshot));}
        finally{WinRT.MarshalInspectable<SwapChainPanel>.DisposeAbi(pointer);}
    }
    // Serialize the immediate D3D context. Wait for GPU completion on the worker;
    // DXGI presentation and SwapChainPanel lifetime stay on the UI thread.
    private async Task RunAsync(Action action,CancellationToken token)
    {
        await access.WaitAsync(token);
        try{token.ThrowIfCancellationRequested();ObjectDisposedException.ThrowIf(disposed,this);RenderStatus next=default;await Task.Run(()=>{action();Native.Check(Native.LumaWaitForGpu(handle));Native.Check(Native.LumaCopyStatus(handle,out next));});snapshot=next;}
        finally{access.Release();}
    }
    private void Flush()
    {
        if(pendingSize is {} size){pendingSize=null;Native.Check(Native.LumaResize(handle,(uint)Math.Max(1,size.W*size.Dpi),(uint)Math.Max(1,size.H*size.Dpi),(float)size.Dpi,(float)size.Dpi));}
        if(pendingView is {} view){pendingView=null;Native.Check(Native.LumaView(handle,(float)view.Zoom,(float)view.X,(float)view.Y,view.Rotation));}
    }
    private void TryFlush(){if(disposed||!access.Wait(0))return;try{Flush();}finally{access.Release();}}
    public void Resize(double w,double h,double dpi){pendingSize=(w,h,dpi);TryFlush();}
    public void View(double zoom,double x,double y,int rotation){pendingView=(zoom,x,y,rotation);TryFlush();}
    public async Task ShowAsync(DecodedFrame frame,CancellationToken token)
    {
        using var lease=new FrameLease(frame);
        await RunAsync(()=>Native.Check(Native.LumaPrepareFrame(handle,frame.DangerousGetHandle())),token);
        token.ThrowIfCancellationRequested();pendingView=null;TryFlush();
    }
    public RenderStatus Status(){if(disposed||!access.Wait(0))return snapshot;try{Native.Check(Native.LumaPeekStatus(handle,out snapshot));return snapshot;}finally{access.Release();}}
    public async Task EffectsAsync(bool hdr,bool vsr,bool original,int preset,uint middleGray,uint peak,CancellationToken token)
    {
        await RunAsync(()=>{Native.Check(Native.LumaHdrTuning(handle,middleGray,peak));Native.Check(Native.LumaPrepareEffects(handle,hdr?1:0,vsr?1:0,original?1:0,(uint)preset));},token);
        token.ThrowIfCancellationRequested();
        // The caller checks its photo generation before presenting the prepared texture.
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(access.Wait(0)){try{Destroy();}finally{access.Release();}}
        else _=DestroyWhenIdleAsync();
    }
    private void Destroy(){if(handle!=IntPtr.Zero){Native.LumaDestroy(handle);handle=IntPtr.Zero;}}
    private async Task DestroyWhenIdleAsync(){await access.WaitAsync();dispatcher.TryEnqueue(()=>{try{Destroy();}finally{access.Release();}});}
}
