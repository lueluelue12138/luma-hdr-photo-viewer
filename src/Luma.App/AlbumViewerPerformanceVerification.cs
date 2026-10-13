using System.Diagnostics;
using System.Text.Json;
using Luma.Core;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Luma.App;

public sealed partial class MainWindow
{
    // Explicit diagnostic with an isolated database and a read-only source.
    internal async Task VerificationAlbumViewerAsync(string folder)
    {
        string report=Path.Combine(App.DataDirectory,"album-viewer-performance.json");
        var results=new List<object>();ViewerWindow? viewer=null;
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        object Resource(string phase)
        {
            using var process=Process.GetCurrentProcess();var cache=FrameCache.Shared.Metrics;
            return new{phase,workingSetBytes=process.WorkingSet64,privateBytes=process.PrivateMemorySize64,cache.Bytes,cache.Frames,thumbnailBytes=Thumbnail.CachedBytes,thumbnailCount=Thumbnail.CachedImages,largerThumbnailCacheHits=Thumbnail.LargerCacheHits};
        }
        var resources=new List<object>();
        try
        {
            await VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            long id=await App.Store.AddFolderAsync(folder);await RefreshAllAsync(id);
            page="album";albumId=id;await ReloadAsync();
            Check(photos.Count>=64,"Long-album viewer verification requires 64+ photos");
            await ViewerPreferences.UpdateAsync(new());
            var timer=Stopwatch.StartNew();viewer=new(photos[0].Path,photos,this);viewer.Activate();
            await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            results.Add(new{scenario="new-window",milliseconds=timer.Elapsed.TotalMilliseconds,firstFrame=viewer.FirstFrameMilliseconds});
            resources.Add(Resource("opened"));
            // Deterministic jumps span the complete album, twice, rather than
            // benchmarking the first few photos or repeated copies of one JPG.
            for(int cycle=0;cycle<2;cycle++)
            {
                for(int n=0;n<20;n++)
                {
                    int selected=(int)((n+1)*104729L%photos.Count);
                    var before=FrameCache.Shared.Metrics;timer.Restart();await viewer.VerificationSelectAsync(selected);await viewer.VerificationPresentationAsync();
                    Check(!viewer.VerificationErrorVisible&&viewer.VerificationPhoto==photos[selected].Path,"Random jump did not present its requested photograph");
                    var cache=FrameCache.Shared.Metrics;
                    Check(cache.Bytes<=256ul*1024*1024&&cache.Frames<=16,"Decoded cache exceeded its configured budget");
                    Check(viewer.VerificationRenderMetrics.UnfrostedPhotoPresents==0,"A photo was presented before its glass was ready");
                    Check(viewer.VerificationStatus.SdkAvailable==0&&viewer.VerificationStatus.HdrEvaluations==0&&viewer.VerificationStatus.VsrEvaluations==0,"SDR stress must not initialize or run NVIDIA");
                    results.Add(new{scenario="random-SDR",cycle,selected,milliseconds=timer.Elapsed.TotalMilliseconds,firstFrame=viewer.FirstFrameMilliseconds,decodes=cache.Decodes-before.Decodes,cacheHits=cache.Hits-before.Hits,frame=viewer.VerificationFrameInfo});
                    resources.Add(Resource($"random-{cycle}-{n}"));
                }
            }
            int start=photos.Count/2;await viewer.VerificationSelectAsync(start);
            for(int n=1;n<=12;n++)
            {
                await Task.Delay(450);timer.Restart();await viewer.VerificationSelectAsync(start+n);await viewer.VerificationPresentationAsync();
                Check(!viewer.VerificationErrorVisible&&viewer.VerificationPhoto==photos[start+n].Path,"Adjacent navigation displayed stale pixels");
                results.Add(new{scenario="prefetched-SDR",selected=start+n,milliseconds=timer.Elapsed.TotalMilliseconds,firstFrame=viewer.FirstFrameMilliseconds});
            }
            var latest=Enumerable.Range(0,10).Select(n=>viewer.VerificationSelectAsync(photos.Count-10+n)).ToArray();await Task.WhenAll(latest);await viewer.VerificationPresentationAsync();
            Check(!viewer.VerificationErrorVisible&&viewer.VerificationPhoto==photos[^1].Path,"Overlapping requests did not finish on the final photo");
            await Task.Delay(1000);resources.Add(Resource("settled"));
            string fixture=Path.Combine(App.DataDirectory,"source","thumbnail-dedupe.png");
            var reached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var resume=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int decodes=0;
            var large=new Thumbnail{FilePath=fixture,Width=320,Height=160};var small=new Thumbnail{FilePath=fixture,DecodeLogicalEdge=112,Width=88,Height=62};
            Thumbnail.VerificationBeforeDecode=path=>{if(path!=fixture)return Task.CompletedTask;decodes++;reached.TrySetResult();return resume.Task;};
            try
            {
                Root.Children.Add(large);await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));Root.Children.Add(small);await Task.Delay(100);resume.TrySetResult();
                timer.Restart();while(large.DisplayedPath!=fixture||small.DisplayedPath!=fixture){if(timer.Elapsed>TimeSpan.FromSeconds(10))throw new InvalidOperationException("Shared thumbnail did not complete");await Task.Delay(20);}
                Check(decodes==1&&ReferenceEquals(large.VerificationImageSource,small.VerificationImageSource),"Two thumbnail sizes decoded the same file twice");
                Check(large.VerificationImageSource is WriteableBitmap bitmap&&bitmap.PixelWidth>=320,"Compact request replaced the gallery's high-resolution thumbnail");
            }
            finally{resume.TrySetResult();Thumbnail.VerificationBeforeDecode=null;Root.Children.Remove(small);Root.Children.Remove(large);}
            File.WriteAllText(report,JsonSerializer.Serialize(new{complete=true,count=photos.Count,results,resources,thumbnailRequestSharingVerified=true,overlappingLatestVerified=true,unfrostedPhotoPresents=viewer.VerificationRenderMetrics.UnfrostedPhotoPresents},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{complete=false,results,resources,failure=ex.ToString()}));App.Log(ex.ToString());Environment.ExitCode=1;}
        finally{if(viewer is not null){viewer.Close();await viewer.VerificationDisposeAsync();}Close();}
    }
}
