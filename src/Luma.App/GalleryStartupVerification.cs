using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace Luma.App;

// Begin before constructing the gallery: a ready-only probe misses startup
// layout, initial covers and the automatic refresh of existing albums.
internal sealed class GalleryStartupVerification : IDisposable
{
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private readonly CancellationTokenSource stop=new();
    private readonly List<object> samples=[];
    private readonly List<object> frames=[];
    private readonly Task heartbeat;
    private string phase="construct";
    private int outstanding;
    private long previousFrame;
    private readonly long allocated=GC.GetTotalAllocatedBytes();
    private readonly int[] gc=[GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)];
    internal GalleryStartupVerification()
    {
        var queue=DispatcherQueue.GetForCurrentThread();
        CompositionTarget.Rendering+=Frame;
        heartbeat=Task.Run(async()=>
        {
            while(!stop.IsCancellationRequested)
            {
                if(Interlocked.CompareExchange(ref outstanding,1,0)==0)
                {
                    var posted=Stopwatch.GetTimestamp();string at=Volatile.Read(ref phase);double since=clock.Elapsed.TotalMilliseconds;
                    if(!queue.TryEnqueue(()=>{samples.Add(new{since,phase=at,handledPhase=phase,delay=Stopwatch.GetElapsedTime(posted).TotalMilliseconds});Interlocked.Exchange(ref outstanding,0);}))break;
                }
                try{await Task.Delay(8,stop.Token);}catch(OperationCanceledException){break;}
            }
        });
    }
    private void Frame(object? sender,object args)
    {
        long now=Stopwatch.GetTimestamp();if(previousFrame!=0)frames.Add(new{since=clock.Elapsed.TotalMilliseconds,phase,interval=Stopwatch.GetElapsedTime(previousFrame,now).TotalMilliseconds});previousFrame=now;
    }
    internal async Task RunAsync(MainWindow window)
    {
        var moves=new List<object>();double ready=0,refreshed=0;var position=window.AppWindow.Position;
        try
        {
            phase="initial-layout";await window.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));ready=clock.Elapsed.TotalMilliseconds;
            phase="automatic-refresh";
            // Window moves are app-level integration checks. Frame/dispatcher
            // timings are not physical mouse-to-screen latency measurements.
            for(int n=0;n<100;n++)
            {
                var time=Stopwatch.StartNew();window.AppWindow.Move(new(position.X+n%3*8,position.Y+n%3*8));moves.Add(new{since=clock.Elapsed.TotalMilliseconds,milliseconds=time.Elapsed.TotalMilliseconds});
                if(window.StartupRefresh.IsCompleted&&refreshed==0){refreshed=clock.Elapsed.TotalMilliseconds;phase="settled";}
                await Task.Delay(40);
            }
            await window.StartupRefresh.WaitAsync(TimeSpan.FromSeconds(60));
            if(refreshed==0)refreshed=clock.Elapsed.TotalMilliseconds;phase="settled";await Task.Delay(1000);
            stop.Cancel();await heartbeat;window.AppWindow.Move(position);
            long startupAllocated=GC.GetTotalAllocatedBytes()-allocated;
            bool deferredSettingsVerified=await window.VerificationSettingsPageAsync();
            File.WriteAllText(Path.Combine(App.DataDirectory,"gallery-startup.json"),JsonSerializer.Serialize(new{complete=true,deferredSettingsVerified,readyMilliseconds=ready,refreshMilliseconds=refreshed,samples,frames,moves,allocatedBytes=startupAllocated,gcCollections=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-gc[i]).ToArray(),wicLoads=Thumbnail.WicLoads,diskHits=ThumbnailDiskCache.Hits},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(App.DataDirectory,"gallery-startup.json"),JsonSerializer.Serialize(new{complete=false,failure=ex.ToString(),samples,frames}));Environment.ExitCode=1;}
        finally{stop.Cancel();await heartbeat;window.Close();}
    }
    public void Dispose(){stop.Cancel();CompositionTarget.Rendering-=Frame;stop.Dispose();}
}
