using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Luma.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace Luma.App;

internal sealed record LiveRenderOptions(bool Hdr,bool Original,int Preset,uint MiddleGray,uint Peak);

// Windows owns demux, decode, the playback clock and audio. Only rendering is
// intercepted: decoded frames go to our GPU surface and real TrueHDR/FP16 path.
internal sealed class LivePlayback
{
    private readonly DispatcherQueue dispatcher;
    private readonly CancellationTokenSource cancellation=new();
    private readonly object gate=new();
    private readonly MediaPlayer player;
    private readonly MediaSource source;
    private readonly MediaPlaybackItem item;
    private readonly Stream input;
    private readonly IRandomAccessStream stream;
    private IDirect3DSurface? surface;
    private Task frame=Task.CompletedTask;
    private bool stopped,notified;
    private long started;
    public double FirstFrameMilliseconds {get;private set;}
    private LiveRenderOptions options;
    private readonly TaskCompletionSource opened=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public NativeRenderer Renderer {get;}
    public uint Width {get;private set;}
    public uint Height {get;private set;}
    public int Frames {get;private set;}
    // Notifications coalesced while busy, not measured video/display drops.
    public int CoalescedNotifications {get;private set;}
    public List<double> FrameMilliseconds {get;}=[];
    public double Duration=>player.PlaybackSession.NaturalDuration.TotalSeconds;
    public double Position=>player.PlaybackSession.Position.TotalSeconds;
    public bool Muted {get=>player.IsMuted;set=>player.IsMuted=value;}
    internal bool VerificationLoop {set=>player.IsLoopingEnabled=value;}
    internal int AudioTracks=>item.AudioTracks.Count;
    public Task Disposed {get;private set;}=Task.CompletedTask;
    public event Action? FirstFrame;
    public event Action<string?>? Ended;
    private LivePlayback(NativeRenderer renderer,Stream input,DispatcherQueue dispatcher,LiveRenderOptions options)
    {
        Renderer=renderer;this.input=input;this.dispatcher=dispatcher;this.options=options;
        stream=input.AsRandomAccessStream();source=MediaSource.CreateFromStream(stream,"video/mp4");item=new(source);
        player=new MediaPlayer{IsVideoFrameServerEnabled=true,IsMuted=true,AutoPlay=false,IsLoopingEnabled=false};
        // A local preview should not take over the user's system transport keys.
        player.CommandManager.IsEnabled=false;
        player.MediaOpened+=Opened;player.MediaFailed+=Failed;player.MediaEnded+=Finished;player.VideoFrameAvailable+=FrameAvailable;
    }
    public static async Task<LivePlayback> CreateAsync(LivePhotoAsset asset,SwapChainPanel panel,IntPtr hwnd,LiveRenderOptions options,CancellationToken token)
    {
        long started=Stopwatch.GetTimestamp();
        if(asset.NativeVideoHdr)throw new NotSupportedException("这段 Live 视频包含原生 HDR，当前先保留静态 HDR 照片；动态 HDR 保真播放正在完善。");
        var renderer=await NativeRenderer.CreateAsync(panel,hwnd,token,options.Hdr);
        Stream? input=null;LivePlayback? playback=null;
        try
        {
            input=new BoundedVideoStream(asset);
            playback=new(renderer,input,panel.DispatcherQueue,options){started=started};input=null;
            playback.player.Source=playback.item;
            await playback.opened.Task.WaitAsync(TimeSpan.FromSeconds(15),token);
            token.ThrowIfCancellationRequested();
            uint width=playback.player.PlaybackSession.NaturalVideoWidth,height=playback.player.PlaybackSession.NaturalVideoHeight;
            if(width==0||height==0||playback.Duration<=0||playback.Duration>60)throw new NotSupportedException("Live 视频尺寸或时长不受支持，仍可查看静态照片。");
            // Inspect Windows' decoded-track metadata too; never feed a known
            // PQ/HLG source through an 8-bit SDR surface and call it native HDR.
            foreach(var track in playback.item.VideoTracks)
            {
                var properties=track.GetEncodingProperties().Properties;
                var transfer=new Guid("5fb0fce9-be5c-4935-a811-ec838f8eed93"); // MF_MT_TRANSFER_FUNCTION
                if(properties.TryGetValue(transfer,out object value)&&Convert.ToUInt32(value) is 15 or 16)
                    throw new NotSupportedException("检测到原生 HDR Live 视频；当前保留静态 HDR，暂不对视频重复应用 RTX HDR。");
            }
            playback.Width=width;playback.Height=height;
            playback.surface=await renderer.CreateVideoSurfaceAsync(width,height,token);
            return playback;
        }
        catch{if(playback is not null){playback.Stop();await playback.Disposed;}else{input?.Dispose();renderer.Dispose();await renderer.DisposedAsync;}throw;}
    }
    public void SetOptions(LiveRenderOptions value)=>Volatile.Write(ref options,value);
    public void Play(){lock(gate){if(!stopped)player.Play();}}
    private void Opened(MediaPlayer sender,object args)=>opened.TrySetResult();
    private void Failed(MediaPlayer sender,MediaPlayerFailedEventArgs args)
    {
        string message=$"Live 视频暂时无法播放（{args.ExtendedErrorCode?.HResult:X8}）。HEVC 视频可能需要 Windows HEVC 视频扩展。静态照片不受影响。";
        opened.TrySetException(new InvalidOperationException(message));NotifyEnd(message);
    }
    private void Finished(MediaPlayer sender,object args)=>NotifyEnd(null);
    private void NotifyEnd(string? message)=>dispatcher.TryEnqueue(()=>{if(!stopped)Ended?.Invoke(message);});
    private async void FrameAvailable(MediaPlayer sender,object args)
    {
        Task work;
        lock(gate)
        {
            if(stopped||surface is null)return;
            if(!frame.IsCompleted){CoalescedNotifications++;return;}
            work=frame=RenderAsync(sender);
        }
        try{await work;}catch(OperationCanceledException){}catch(Exception ex){NotifyEnd("Live 播放已停止："+ex.Message);}
    }
    private async Task RenderAsync(MediaPlayer sender)
    {
        var watch=Stopwatch.StartNew();
        bool rendered=await Renderer.VideoFrameAsync(()=>sender.CopyFrameToVideoSurface(surface!),Volatile.Read(ref options),cancellation.Token);
        if(!rendered){CoalescedNotifications++;return;}
        Frames++;if(FrameMilliseconds.Count<1800)FrameMilliseconds.Add(watch.Elapsed.TotalMilliseconds);
        if(!notified){notified=true;FirstFrameMilliseconds=Stopwatch.GetElapsedTime(started).TotalMilliseconds;dispatcher.TryEnqueue(()=>{if(!stopped)FirstFrame?.Invoke();});}
    }
    public void Stop()
    {
        Task pending;
        lock(gate){if(stopped)return;stopped=true;cancellation.Cancel();pending=frame;}
        player.VideoFrameAvailable-=FrameAvailable;player.MediaOpened-=Opened;player.MediaFailed-=Failed;player.MediaEnded-=Finished;
        try{player.Pause();}catch(Exception ex){App.Log("Live pause: "+ex.Message);}
        // Detach XAML while its owner is still alive, then retire GPU resources
        // after the sole in-flight frame. No blocking waits on the UI thread.
        Renderer.Dispose();Disposed=DisposeMediaAsync(pending);
    }
    private async Task DisposeMediaAsync(Task pending)
    {
        try{await pending;}catch(Exception ex){App.Log("Live frame stopped: "+ex.Message);}
        try{player.Source=null;player.Dispose();item.Source.Dispose();surface?.Dispose();stream.Dispose();input.Dispose();}
        finally{cancellation.Dispose();await Renderer.DisposedAsync;}
    }
    private sealed class BoundedVideoStream:Stream
    {
        private readonly FileStream file;private readonly long origin,length;private long position;
        public BoundedVideoStream(LivePhotoAsset asset)
        {
            var info=new FileInfo(asset.PhotoPath);
            if(info.Length!=asset.SourceLength||info.LastWriteTimeUtc.Ticks!=asset.SourceModified)throw new IOException("照片已更改，请重新打开。");
            file=new(asset.VideoPath,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete,65536,FileOptions.Asynchronous|FileOptions.RandomAccess);
            origin=asset.Offset;length=asset.Length;
            if(origin<0||length<=0||origin>file.Length-length||File.GetLastWriteTimeUtc(asset.VideoPath).Ticks!=asset.VideoModified){file.Dispose();throw new InvalidDataException("Live 视频已更改，请重新打开照片。");}
        }
        public override bool CanRead=>true;public override bool CanSeek=>true;public override bool CanWrite=>false;
        public override long Length=>length;public override long Position{get=>position;set=>Seek(value,SeekOrigin.Begin);}
        public override int Read(byte[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
        public override int Read(Span<byte> buffer){file.Position=origin+position;int n=file.Read(buffer[..(int)Math.Min(buffer.Length,length-position)]);position+=n;return n;}
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){file.Position=origin+position;int n=await file.ReadAsync(buffer[..(int)Math.Min(buffer.Length,length-position)],token);position+=n;return n;}
        public override long Seek(long offset,SeekOrigin whence){long next=checked((whence==SeekOrigin.Begin?0:whence==SeekOrigin.Current?position:length)+offset);if(next<0||next>length)throw new IOException("Invalid video seek");return position=next;}
        public override void Flush(){}public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing){if(disposing)file.Dispose();base.Dispose(disposing);}
    }
}
