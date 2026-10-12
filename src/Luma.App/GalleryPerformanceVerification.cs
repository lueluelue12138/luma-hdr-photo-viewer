using System.Diagnostics;
using System.Text.Json;
using Luma.Core;
using Microsoft.UI.Xaml.Controls;

namespace Luma.App;

public sealed partial class MainWindow
{
    internal async Task VerificationLargeGalleryAsync(string folder)
    {
        string report=Path.Combine(App.DataDirectory,"gallery-performance.json");using var stop=new CancellationTokenSource();
        var delays=new List<double>();Task heartbeat=Task.CompletedTask;
        try
        {
            await VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));var watch=Stopwatch.StartNew();
            long id=await App.Store.AddFolderAsync(folder);await RefreshAllAsync();
            page="album";albumId=id;await ReloadAsync();await Task.Delay(300);
            long firstMilliseconds=watch.ElapsedMilliseconds;int count=photos.Count;
            if(count<500)throw new InvalidOperationException("Gallery performance fixture requires 500+ indexed photographs");
            heartbeat=Task.Run(async()=>{while(!stop.IsCancellationRequested){long posted=Stopwatch.GetTimestamp();DispatcherQueue.TryEnqueue(()=>delays.Add(Stopwatch.GetElapsedTime(posted).TotalMilliseconds));try{await Task.Delay(8,stop.Token);}catch(OperationCanceledException){break;}}});
            long before=Thumbnail.ShellLoads,skipped=Thumbnail.SkippedLoads;int maxRealized=0;
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
            await Task.Delay(500);stop.Cancel();await heartbeat;
            if(maxRealized>=count/2)throw new InvalidOperationException("The large gallery is not virtualizing its photo cards");
            var items=PhotosRepeat.ItemsSource;long unchangedLoads=Thumbnail.ShellLoads;await RefreshAllAsync();
            if(!ReferenceEquals(items,PhotosRepeat.ItemsSource)||unchangedLoads!=Thumbnail.ShellLoads)throw new InvalidOperationException("Unchanged large-album refresh discarded its photo/thumbnail cache");
            File.WriteAllText(report,JsonSerializer.Serialize(new{complete=true,count,firstMilliseconds,maxRealized,dispatcherLatency=delays,shellLoads=Thumbnail.ShellLoads-before,skippedLoads=Thumbnail.SkippedLoads-skipped,unchangedRefreshLoads=Thumbnail.ShellLoads-unchangedLoads,cachedThumbnails=Thumbnail.CachedImages},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{complete=false,failure=ex.ToString()}));Environment.ExitCode=1;}
        finally{stop.Cancel();await heartbeat;Close();}
    }
}
