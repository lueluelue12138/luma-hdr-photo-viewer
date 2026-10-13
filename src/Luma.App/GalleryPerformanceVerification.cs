using System.Diagnostics;
using System.Text.Json;
using Luma.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Luma.App;

public sealed partial class MainWindow
{
    private Func<Task>? verificationBeforeScan;
    private IEnumerable<Button> VerificationRealizedCards()
    {
        // Inspect only realized children. Querying every index in a long album
        // would add O(album length) UI work to the diagnostic itself.
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(PhotosRepeat);i++)
            if(VisualTreeHelper.GetChild(PhotosRepeat,i) is Button card&&PhotosRepeat.GetElementIndex(card)>=0)yield return card;
    }
    internal async Task VerificationLargeGalleryAsync(string folder)
    {
        string report=Path.Combine(App.DataDirectory,"gallery-performance.json");using var stop=new CancellationTokenSource();
        var delays=new List<double>();var samples=new List<object>();var operations=new List<object>();var resources=new List<object>();Task heartbeat=Task.CompletedTask;string phase="scan";
        try
        {
            await VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));await RefreshAllAsync();var probeStartUtc=DateTime.UtcNow;var watch=Stopwatch.StartNew();
            long allocatedStart=GC.GetTotalAllocatedBytes();int[] gcStart=[GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)];
            heartbeat=Task.Run(async()=>{using var process=Process.GetCurrentProcess();long lastResource=-250;while(!stop.IsCancellationRequested){long posted=Stopwatch.GetTimestamp();string postedPhase=Volatile.Read(ref phase);long elapsed=watch.ElapsedMilliseconds;if(elapsed-lastResource>=250){process.Refresh();resources.Add(new{elapsed,phase=postedPhase,workingSetBytes=process.WorkingSet64,privateBytes=process.PrivateMemorySize64,cpuMilliseconds=process.TotalProcessorTime.TotalMilliseconds});lastResource=elapsed;}int gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2);DispatcherQueue.TryEnqueue(()=>{double ms=Stopwatch.GetElapsedTime(posted).TotalMilliseconds;delays.Add(ms);samples.Add(new{elapsed,postedPhase,handledPhase=phase,milliseconds=ms,gc0=GC.CollectionCount(0)-gc0,gc1=GC.CollectionCount(1)-gc1,gc2=GC.CollectionCount(2)-gc2,wic=Thumbnail.WicLoads,shell=Thumbnail.ShellLoads,cache=Thumbnail.CachedImages});});try{await Task.Delay(8,stop.Token);}catch(OperationCanceledException){break;}}});
            var snapshotReady=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resumeScan=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            verificationBeforeScan=()=>{snapshotReady.TrySetResult();return resumeScan.Task;};
            long id;
            try
            {
                var firstScan=RefreshAllAsync();await snapshotReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
                id=await App.Store.AddFolderAsync(folder);var requested=RefreshAllAsync(id);
                if(requested.IsCompleted)throw new InvalidOperationException("Refresh during an active scan was discarded");
                resumeScan.TrySetResult();await Task.WhenAll(firstScan,requested);
            }
            finally{resumeScan.TrySetResult();verificationBeforeScan=null;}
            phase="initial-layout";page="album";albumId=id;await ReloadAsync();var firstVisibleWait=Stopwatch.StartNew();int firstVisibleCount=0;
            while(true)
            {
                await Task.Delay(25);var cards=VerificationRealizedCards().ToArray();firstVisibleCount=cards.Length;
                if(cards.Length>0&&cards.All(c=>c.Tag is Photo photo&&FindThumbnail(c)?.DisplayedPath==photo.Path))break;
                if(firstVisibleWait.Elapsed>TimeSpan.FromSeconds(20))throw new InvalidOperationException("Initial thumbnails did not finish decoding");
            }
            firstVisibleWait.Stop();
            long firstMilliseconds=watch.ElapsedMilliseconds;int count=photos.Count;
            if(count<64)throw new InvalidOperationException($"Gallery performance fixture requires 64+ indexed photographs; got {count}");
            long before=Thumbnail.ShellLoads,wicBefore=Thumbnail.WicLoads,skipped=Thumbnail.SkippedLoads;int maxRealized=0;
            for(int n=0;n<48;n++)
            {
                phase=$"scroll-{n}";var operation=Stopwatch.StartNew();
                GalleryScroll.ChangeView(null,GalleryScroll.ScrollableHeight*((n*13)%47)/46,null,true);
                operations.Add(new{phase,operation="change-view",milliseconds=operation.Elapsed.TotalMilliseconds});
                await Task.Delay(24);
                operation.Restart();
                int realized=0;
                foreach(var card in VerificationRealizedCards())
                {
                    realized++;var thumbnail=FindThumbnail(card);
                    if(thumbnail?.DisplayedPath is {} path&&card.Tag is Photo photo&&!string.Equals(path,photo.Path,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("A recycled card displayed a previous photo");
                }
                maxRealized=Math.Max(maxRealized,realized);
                operations.Add(new{phase,operation="inspect-realized",milliseconds=operation.Elapsed.TotalMilliseconds});
            }
            phase="settle";await Task.Delay(500);
            // End at the start and require real decoded pixels in every visible
            // card, rather than declaring an all-placeholder list a success.
            phase="visible-ready";GalleryScroll.ChangeView(null,0,null,true);var visibleWait=Stopwatch.StartNew();int visibleThumbnails=0;
            while(true)
            {
                await Task.Delay(25);int realized=0;visibleThumbnails=0;
                foreach(var card in VerificationRealizedCards())
                {
                    realized++;var thumbnail=FindThumbnail(card);
                    if(thumbnail?.DisplayedPath is {} path&&card.Tag is Photo photo)
                    {
                        if(!string.Equals(path,photo.Path,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Visible card has stale thumbnail pixels");
                        visibleThumbnails++;
                    }
                }
                if(realized>0&&realized==visibleThumbnails)break;
                if(visibleWait.Elapsed>TimeSpan.FromSeconds(20))throw new InvalidOperationException("Visible thumbnails did not finish decoding");
            }
            visibleWait.Stop();
            stop.Cancel();await heartbeat;
            if(maxRealized>=count/2)throw new InvalidOperationException("The large gallery is not virtualizing its photo cards");
            var items=PhotosRepeat.ItemsSource;long unchangedLoads=Thumbnail.ShellLoads+Thumbnail.WicLoads;await RefreshAllAsync();
            if(!ReferenceEquals(items,PhotosRepeat.ItemsSource)||unchangedLoads!=Thumbnail.ShellLoads+Thumbnail.WicLoads)throw new InvalidOperationException("Unchanged large-album refresh discarded its photo/thumbnail cache");
            long diskCacheBytes=await Task.Run(()=>{string cache=Path.Combine(App.DataDirectory,"thumbnail-cache-v1");return Directory.Exists(cache)?Directory.EnumerateFiles(cache,"*.lth").Sum(p=>new FileInfo(p).Length):0;});
            File.WriteAllText(report,JsonSerializer.Serialize(new{complete=true,probeStartUtc,refreshCoalescingVerified=true,count,firstMilliseconds,firstVisibleCount,firstVisibleMilliseconds=firstVisibleWait.ElapsedMilliseconds,maxRealized,visibleThumbnails,visibleReadyMilliseconds=visibleWait.ElapsedMilliseconds,dispatcherLatency=delays,dispatcherSamples=samples,operations,resources,allocatedBytes=GC.GetTotalAllocatedBytes()-allocatedStart,gcCollections=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-gcStart[i]).ToArray(),shellLoads=Thumbnail.ShellLoads-before,wicLoads=Thumbnail.WicLoads-wicBefore,totalShellLoads=Thumbnail.ShellLoads,totalWicLoads=Thumbnail.WicLoads,diskCacheHits=ThumbnailDiskCache.Hits,diskCacheWrites=ThumbnailDiskCache.Writes,diskCacheBytes,skippedLoads=Thumbnail.SkippedLoads-skipped,unchangedRefreshLoads=Thumbnail.ShellLoads+Thumbnail.WicLoads-unchangedLoads,cachedThumbnails=Thumbnail.CachedImages,cachedThumbnailBytes=Thumbnail.CachedBytes},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{complete=false,failure=ex.ToString()}));Environment.ExitCode=1;}
        finally{stop.Cancel();await heartbeat;Close();}
    }
}
