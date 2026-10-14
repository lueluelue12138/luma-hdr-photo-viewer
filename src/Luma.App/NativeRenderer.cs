using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;

namespace Luma.App;
[StructLayout(LayoutKind.Sequential)]
internal struct FrameInfo {public uint Width,Height,OriginalWidth,OriginalHeight;public int NativeHdr,Downsampled;public ulong Bytes;}
[StructLayout(LayoutKind.Sequential)]
internal record struct GlassRect(float Left,float Top,float Width,float Height,float Radius,float Opacity);
[StructLayout(LayoutKind.Sequential)]
internal struct RenderMetrics {public ulong Presents,PhotoPresents,UnfrostedPhotoPresents,BlurBuilds;public double LastDrawMilliseconds;}
[StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
internal struct RenderStatus
{
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Gpu;
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Display;
    public int Nvidia,HdrActive,Fp16,NativeHdr;public float MaxNits,SdrWhite;public ulong GpuBytes;public int HdrEffective,VsrEffective,SdkAvailable,HdrAvailable,VsrAvailable;public uint NgxResult,HdrEvaluations,VsrEvaluations;
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string HdrMessage;
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string VsrMessage;
    public bool HasGainMap => NativeHdr is 2 or 3 or 4;
    public string GainMapSource => NativeHdr==3?"Apple HEIC":NativeHdr==4?"ISO HEIC":"增益图 JPEG";
    public string HdrReason => HasGainMap ? "照片自带 HDR 增益图，直接还原光影，无需 RTX HDR" : NativeHdr!=0 ? "原生 HDR 图片，跳过 SDR 增强" : HdrEffective!=0 ? HdrMessage : HdrActive==0 ? "当前显示器未启用 Windows HDR" : HdrMessage;
}
internal static class Native
{
    private const string Dll="Luma.Native.dll";
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern IntPtr LumaError();
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)]internal static extern int LumaDecode(string path,out FrameInfo info,out IntPtr frame);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)]internal static extern int LumaDecodeSized(string path,uint maxEdge,out FrameInfo info,out IntPtr frame);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)]internal static extern int LumaDecodeThumbnail(string path,uint maxEdge,out FrameInfo info,out IntPtr frame);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern void LumaFreeFrame(IntPtr frame);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaCopySdrBgra(IntPtr frame,[Out]byte[] pixels,ulong bytes);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaCreate(IntPtr panel,IntPtr hwnd,out IntPtr renderer);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaCreateFast(IntPtr panel,IntPtr hwnd,out IntPtr renderer);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaInitEffects(IntPtr renderer);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern void LumaDestroy(IntPtr renderer);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaResetForReuse(IntPtr renderer);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaBindWindow(IntPtr renderer,IntPtr hwnd);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaVideoMemory(IntPtr renderer,out ulong budget,out ulong usage);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaAttach(IntPtr renderer,IntPtr panel);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaDetach(IntPtr renderer);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaSetGlass(IntPtr renderer,[In]GlassRect[] rects,uint count,int dark);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaRepaint(IntPtr renderer);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaGetRenderMetrics(IntPtr renderer,out RenderMetrics metrics);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaReadGlass(IntPtr renderer,GlassRect rect,int dark,uint width,uint height,[Out]byte[] pixels,uint bytes);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaResize(IntPtr renderer,uint w,uint h,float scaleX,float scaleY);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaPrepareResize(IntPtr renderer,uint w,uint h,float scaleX,float scaleY);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaPrepareView(IntPtr renderer,float zoom,float x,float y,int rotation);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaUseFrame(IntPtr renderer,IntPtr frame);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaPrepareFrame(IntPtr renderer,IntPtr frame);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaView(IntPtr renderer,float zoom,float x,float y,int rotation);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaStatus(IntPtr renderer,out RenderStatus status);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaPeekStatus(IntPtr renderer,out RenderStatus status);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaCopyStatus(IntPtr renderer,out RenderStatus status);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaProbeDisplay(IntPtr renderer,out RenderStatus status);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaWaitForGpu(IntPtr renderer);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaEffects(IntPtr renderer,int hdr,int vsr,int original,uint preset);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaPrepareEffects(IntPtr renderer,int hdr,int vsr,int original,uint preset);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int LumaSetOriginal(IntPtr renderer,int original);
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
    internal long SourceLength {get;}
    internal long SourceModified {get;}
    private bool held;
    public FrameLease(DecodedFrame frame,long sourceLength=0,long sourceModified=0){Frame=frame;SourceLength=sourceLength;SourceModified=sourceModified;frame.DangerousAddRef(ref held);}
    public void Dispose(){if(held){held=false;Frame.DangerousRelease();}}
}
internal sealed class NativeRenderer : IDisposable
{
    private IntPtr handle;
    private readonly SemaphoreSlim access=new(1);
    private bool disposed;
    private bool healthy=true;
    internal bool DeviceReused {get;private set;}
    private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcher;
    private bool flushScheduled;
    private Task painting=Task.CompletedTask;
    private int processing;
    internal event Action? Presented;
    internal RenderMetrics Metrics {get;private set;}
    internal event Action<COMException>? Faulted;
    private RenderStatus snapshot;
    private bool holdOriginal;private bool? pendingOriginal;
    internal int WorkerJobs {get;private set;}
    public RenderStatus CachedStatus=>snapshot;
    private (double W,double H,double Dpi)? pendingSize;
    private (double Zoom,double X,double Y,int Rotation)? pendingView;
    private (GlassRect[] Rects,bool Dark)? pendingGlass;
    private (GlassRect[] Rects,bool Dark)? lastGlass;
    public bool Busy=>processing>0;
    public Task DisposedAsync {get;private set;}=Task.CompletedTask;
    private NativeRenderer(SwapChainPanel panel)
    {
        dispatcher=panel.DispatcherQueue;
    }
    public static async Task<NativeRenderer> CreateAsync(SwapChainPanel panel,IntPtr hwnd,CancellationToken token,bool enhancements=false)
    {
        var value=new NativeRenderer(panel);
        (value.handle,value.DeviceReused)=await RenderDeviceCache.AcquireAsync(hwnd,enhancements);
        if(token.IsCancellationRequested){await Task.Run(value.Destroy);token.ThrowIfCancellationRequested();}
        var pointer=WinRT.MarshalInspectable<SwapChainPanel>.FromManaged(panel);
        try{Native.Check(Native.LumaAttach(value.handle,pointer));Native.Check(Native.LumaCopyStatus(value.handle,out value.snapshot));return value;}
        catch{value.Dispose(false);throw;}
        finally{WinRT.MarshalInspectable<SwapChainPanel>.DisposeAbi(pointer);}
    }
    // All D3D work and Present run off XAML's thread. SetSwapChain/Detach stay
    // on the XAML owner. One context preserves GPU ordering; no CPU fence is
    // needed between upload, mip generation, SDK evaluation and presentation.
    private async Task RunAsync(Action action,CancellationToken token)
    {
        processing++;
        bool entered=false;
        try{
        await access.WaitAsync(token);
        entered=true;token.ThrowIfCancellationRequested();ObjectDisposedException.ThrowIf(disposed,this);WorkerJobs++;RenderStatus next=default;await Task.Run(()=>{action();Native.Check(Native.LumaCopyStatus(handle,out next));});snapshot=next;
        }catch(COMException){healthy=false;throw;}finally{processing--;if(entered)access.Release();ScheduleFlush();}
    }
    private sealed record PaintBatch(bool? Original,(GlassRect[] Rects,bool Dark)? Glass,(double W,double H,double Dpi)? Size,(double Zoom,double X,double Y,int Rotation)? View);
    private bool HasPending=>pendingOriginal.HasValue||pendingGlass.HasValue||pendingSize.HasValue||pendingView.HasValue;
    private PaintBatch TakePending()
    {
        var batch=new PaintBatch(pendingOriginal,pendingGlass,pendingSize,pendingView);pendingOriginal=null;pendingGlass=null;pendingSize=null;pendingView=null;return batch;
    }
    private void Prepare(PaintBatch batch)
    {
        if(batch.Original is {} original)Native.Check(Native.LumaSetOriginal(handle,original?1:0));
        if(batch.Glass is {} glass)Native.Check(Native.LumaSetGlass(handle,glass.Rects,(uint)glass.Rects.Length,glass.Dark?1:0));
        if(batch.Size is {} size)Native.Check(Native.LumaPrepareResize(handle,(uint)Math.Max(1,size.W*size.Dpi),(uint)Math.Max(1,size.H*size.Dpi),(float)size.Dpi,(float)size.Dpi));
        if(batch.View is {} view)Native.Check(Native.LumaPrepareView(handle,(float)view.Zoom,(float)view.X,(float)view.Y,view.Rotation));
    }
    private async Task PaintAsync()
    {
        await access.WaitAsync();
        try
        {
            if(disposed||!HasPending)return;
            var batch=TakePending();RenderStatus next=default;RenderMetrics metrics=default;
            await Task.Run(()=>{Prepare(batch);Native.Check(Native.LumaRepaint(handle));Native.Check(Native.LumaCopyStatus(handle,out next));Native.Check(Native.LumaGetRenderMetrics(handle,out metrics));});
            snapshot=next;Metrics=metrics;
        }
        catch(COMException ex){healthy=false;if(!disposed)Faulted?.Invoke(ex);}
        finally{access.Release();}
        if(!disposed){Presented?.Invoke();if(HasPending)ScheduleFlush();}
    }
    private void ScheduleFlush()
    {
        if(disposed||flushScheduled)return;flushScheduled=true;
        if(!dispatcher.TryEnqueue(()=>{flushScheduled=false;if(disposed)return;if(!painting.IsCompleted)return;painting=PaintAsync();}))flushScheduled=false;
    }
    public async Task PresentPendingAsync(CancellationToken token=default)
    {
        while(!disposed){await painting.WaitAsync(token);if(!HasPending)return;painting=PaintAsync();}
    }
    public void Resize(double w,double h,double dpi){pendingSize=(w,h,dpi);ScheduleFlush();}
    public void View(double zoom,double x,double y,int rotation){pendingView=(zoom,x,y,rotation);ScheduleFlush();}
    public void HoldOriginal(bool value)
    {
        if(disposed||holdOriginal==value)return;holdOriginal=value;pendingOriginal=value;
        // This selector has no upload, shader draw, fence or AI call. The
        // cached texture is painted on the worker at the next refresh.
        if(access.Wait(0))try{Native.Check(Native.LumaSetOriginal(handle,value?1:0));Native.Check(Native.LumaCopyStatus(handle,out snapshot));}finally{access.Release();}
        ScheduleFlush();
    }
    public void Glass(GlassRect[] rects,bool dark){if(lastGlass is {} previous&&previous.Dark==dark&&previous.Rects.SequenceEqual(rects))return;lastGlass=(rects,dark);pendingGlass=(rects,dark);ScheduleFlush();}
    public async Task<byte[]> ReadGlassAsync(GlassRect rect,bool dark,uint w,uint h,CancellationToken token)
    {
        var pixels=new byte[checked((int)(w*h*4))];await access.WaitAsync(token);
        try{ObjectDisposedException.ThrowIf(disposed,this);await Task.Run(()=>Native.Check(Native.LumaReadGlass(handle,rect,dark?1:0,w,h,pixels,(uint)pixels.Length)),token);return pixels;}
        finally{access.Release();ScheduleFlush();}
    }
    public async Task ShowAsync(DecodedFrame frame,CancellationToken token,double zoom=1,double x=0,double y=0,int rotation=0)
    {
        using var lease=new FrameLease(frame);
        await access.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();ObjectDisposedException.ThrowIf(disposed,this);WorkerJobs++;
            pendingOriginal=holdOriginal;pendingView=(zoom,x,y,rotation);var batch=TakePending();RenderStatus next=default;RenderMetrics metrics=default;
            await Task.Run(()=>{
                // Geometry belongs to the surface, independent of a cancelled
                // photo. Apply it even if that upload becomes obsolete, so the
                // next photo cannot lose a consumed resize/glass request.
                Prepare(batch with{Original=null,View=null});token.ThrowIfCancellationRequested();
                Native.Check(Native.LumaPrepareFrame(handle,frame.DangerousGetHandle()));token.ThrowIfCancellationRequested();
                Prepare(batch with{Glass=null,Size=null});Native.Check(Native.LumaRepaint(handle));Native.Check(Native.LumaCopyStatus(handle,out next));Native.Check(Native.LumaGetRenderMetrics(handle,out metrics));
            });snapshot=next;Metrics=metrics;
        }
        finally{access.Release();ScheduleFlush();}
        if(!disposed)Presented?.Invoke();token.ThrowIfCancellationRequested();
    }
    public RenderStatus Status(){if(disposed||!access.Wait(0))return snapshot;try{Native.Check(Native.LumaCopyStatus(handle,out snapshot));return snapshot;}finally{access.Release();}}
    public async Task PollDisplayAsync()
    {
        if(disposed||!access.Wait(0))return;
        var previous=snapshot;RenderStatus next=default;
        try{await Task.Run(()=>Native.Check(Native.LumaProbeDisplay(handle,out next)));snapshot=next;}
        finally{access.Release();}
        if(disposed)return;
        if(previous.HdrActive!=next.HdrActive||previous.SdrWhite!=next.SdrWhite||previous.HdrEffective!=next.HdrEffective)pendingOriginal=holdOriginal;
        ScheduleFlush();
    }
    public Task InitializeEffectsAsync(CancellationToken token)=>RunAsync(()=>Native.Check(Native.LumaInitEffects(handle)),token);
    public async Task EffectsAsync(bool hdr,bool vsr,int preset,uint middleGray,uint peak,CancellationToken token)
    {
        await RunAsync(()=>{Native.Check(Native.LumaHdrTuning(handle,middleGray,peak));Native.Check(Native.LumaPrepareEffects(handle,hdr?1:0,vsr?1:0,0,(uint)preset));},token);
        pendingOriginal=holdOriginal;token.ThrowIfCancellationRequested();
        // The caller checks its photo generation before presenting the prepared texture.
    }
    public void Dispose()=>Dispose(true);
    public void Dispose(bool allowReuse)
    {
        if(disposed)return;disposed=true;healthy&=allowReuse;
        // Detach while the XAML owner is alive; worker jobs touch only D3D textures.
        if(handle!=IntPtr.Zero)Native.LumaDetach(handle);
        DisposedAsync=DestroyWhenIdleAsync();
    }
    private void Destroy(){if(handle!=IntPtr.Zero){Native.LumaDestroy(handle);handle=IntPtr.Zero;}}
    private async Task DestroyWhenIdleAsync(){await access.WaitAsync();try{var retired=handle;handle=IntPtr.Zero;await RenderDeviceCache.RetireAsync(retired,snapshot.SdkAvailable!=0,healthy);}finally{access.Release();}}
}
