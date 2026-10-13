using System.Diagnostics;
using System.Text.Json;
using Luma.Core;
using Microsoft.UI.Xaml.Controls;

namespace Luma.App;

public sealed partial class MainWindow
{
    private Func<Task>? verificationBeforeScan;
    internal async Task VerificationLargeGalleryAsync(string folder)
    {
        string report=Path.Combine(App.DataDirectory,"gallery-performance.json");using var stop=new CancellationTokenSource();
        var delays=new List<double>();Task heartbeat=Task.CompletedTask;
        try
        {
            await VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));await RefreshAllAsync();var watch=Stopwatch.StartNew();
            heartbeat=Task.Run(async()=>{while(!stop.IsCancellationRequested){long posted=Stopwatch.GetTimestamp();DispatcherQueue.TryEnqueue(()=>delays.Add(Stopwatch.GetElapsedTime(posted).TotalMilliseconds));try{await Task.Delay(8,stop.Token);}catch(OperationCanceledException){break;}}});
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
            page="album";albumId=id;await ReloadAsync();await Task.Delay(300);
            long firstMilliseconds=watch.ElapsedMilliseconds;int count=photos.Count;
            if(count<64)throw new InvalidOperationException($"Gallery performance fixture requires 64+ indexed photographs; got {count}");
            long before=Thumbnail.ShellLoads,wicBefore=Thumbnail.WicLoads,skipped=Thumbnail.SkippedLoads;int maxRealized=0;
            for(int n=0;n<48;n++)
            {
                GalleryScroll.ChangeView(null,GalleryScroll.ScrollableHeight*((n*13)%47)/46,null,true);
                await Task.Delay(24);
                int realized=0;
                for(int i=0;i<count;i++)if(PhotosRepeat.TryGetElement(i) is Button card)
                {
                    realized++;var thumbnail=FindThumbnail(card);
                    if(thumbnail?.DisplayedPath is {} path&&card.Tag is Photo photo&&!string.Equals(path,photo.Path,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("A recycled card displayed a previous photo");
                }
                maxRealized=Math.Max(maxRealized,realized);
            }
            await Task.Delay(500);
            // End at the start and require real decoded pixels in every visible
            // card, rather than declaring an all-placeholder list a success.
            GalleryScroll.ChangeView(null,0,null,true);var visibleWait=Stopwatch.StartNew();int visibleThumbnails=0;
            while(true)
            {
                await Task.Delay(25);int realized=0;visibleThumbnails=0;
                for(int i=0;i<count;i++)if(PhotosRepeat.TryGetElement(i) is Button card)
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
            stop.Cancel();await heartbeat;
            if(maxRealized>=count/2)throw new InvalidOperationException("The large gallery is not virtualizing its photo cards");
            var items=PhotosRepeat.ItemsSource;long unchangedLoads=Thumbnail.ShellLoads+Thumbnail.WicLoads;await RefreshAllAsync();
            if(!ReferenceEquals(items,PhotosRepeat.ItemsSource)||unchangedLoads!=Thumbnail.ShellLoads+Thumbnail.WicLoads)throw new InvalidOperationException("Unchanged large-album refresh discarded its photo/thumbnail cache");
            File.WriteAllText(report,JsonSerializer.Serialize(new{complete=true,refreshCoalescingVerified=true,count,firstMilliseconds,maxRealized,visibleThumbnails,visibleReadyMilliseconds=visibleWait.ElapsedMilliseconds,dispatcherLatency=delays,shellLoads=Thumbnail.ShellLoads-before,wicLoads=Thumbnail.WicLoads-wicBefore,skippedLoads=Thumbnail.SkippedLoads-skipped,unchangedRefreshLoads=Thumbnail.ShellLoads+Thumbnail.WicLoads-unchangedLoads,cachedThumbnails=Thumbnail.CachedImages,cachedThumbnailBytes=Thumbnail.CachedBytes},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{complete=false,failure=ex.ToString()}));Environment.ExitCode=1;}
        finally{stop.Cancel();await heartbeat;Close();}
    }
}
