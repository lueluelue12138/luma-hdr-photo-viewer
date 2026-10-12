using System.Diagnostics;
using System.Text.Json;
using Luma.Core;

namespace Luma.App;

// Real WinUI windows and SDK evaluations. First-image and completed-processing
// times include file I/O/upload; they measure Present submission, not a display's
// physical scanout. Input fixtures and the database are isolated by the runner.
internal static class PhotoPerformanceVerification
{
    internal static async Task RunAsync(MainWindow host,string largePhoto)
    {
        var results=new List<object>();ViewerWindow? viewer=null;
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        void Evaluated(bool hdr,bool vsr,RenderStatus before)
        {
            var after=viewer!.VerificationStatus;
            Check(hdr?after.HdrEvaluations>before.HdrEvaluations:after.HdrEvaluations==before.HdrEvaluations,"New photo must really evaluate only the requested HDR mode");
            Check(vsr?after.VsrEvaluations>before.VsrEvaluations:after.VsrEvaluations==before.VsrEvaluations,"New photo must really evaluate only the requested VSR mode");
        }
        object Selection(string mode,string kind,int selected,Stopwatch timer,(long Decodes,long Hits,ulong Bytes,int Frames) before)
        {
            var cache=FrameCache.Shared.Metrics;var state=viewer!.VerificationStatus;
            return new{mode,kind,selected,firstPhotoMilliseconds=viewer.FirstFrameMilliseconds,completedMilliseconds=timer.Elapsed.TotalMilliseconds,decodes=cache.Decodes-before.Decodes,cacheHits=cache.Hits-before.Hits,state.HdrEffective,state.VsrEffective,state.HdrEvaluations,state.VsrEvaluations,frame=viewer.VerificationFrameInfo};
        }
        try
        {
            await host.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await ViewerPreferences.UpdateAsync(new());
            var timer=Stopwatch.StartNew();viewer=new(largePhoto,null,host);viewer.Activate();
            await viewer.FirstFrameReady.Task.WaitAsync(TimeSpan.FromSeconds(30));double first=timer.Elapsed.TotalMilliseconds;
            await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var actual=await Task.Run(()=>{var file=new FileInfo(largePhoto);return(Length:file.Length,Modified:file.LastWriteTimeUtc.Ticks);});
            var opened=(await App.Store.PhotosAsync(PhotoScope.Recent)).Single(p=>p.Path==SupportedFormats.Normalize(largePhoto));
            Check(opened.Size==actual.Length&&opened.Modified==actual.Modified,"Direct activation must persist real file metadata from the background decode");
            results.Add(new{mode="large-SDR",kind="new-window",firstPhotoMilliseconds=first,completedMilliseconds=timer.Elapsed.TotalMilliseconds,frame=viewer.VerificationFrameInfo,metadataCorrect=true});
            timer.Restart();await viewer.VerificationDetailAsync();await viewer.VerificationPresentationAsync();
            Check(viewer.VerificationFrameInfo?.Downsampled!=2,"Full-detail measurement must actually load full-resolution pixels");
            results.Add(new{mode="large-SDR",kind="full-detail",completedMilliseconds=timer.Elapsed.TotalMilliseconds,frame=viewer.VerificationFrameInfo});
            viewer.Close();await viewer.VerificationDisposeAsync();viewer=null;

            foreach(string mode in new[]{"SDR","HDR","VSR","HDR+VSR"})
            {
                bool hdr=mode.Contains("HDR"),vsr=mode.Contains("VSR");
                var photos=await Task.Run(()=>FolderIndexer.Siblings(Path.Combine(App.DataDirectory,mode,"00.jpg")));
                Check(photos.Count==12,"Each performance mode requires twelve isolated photographs");
                await ViewerPreferences.UpdateAsync(new(Hdr:hdr,Vsr:vsr));var before=FrameCache.Shared.Metrics;timer.Restart();
                viewer=new(photos[0].Path,photos,host);viewer.Activate();await viewer.FirstFrameReady.Task.WaitAsync(TimeSpan.FromSeconds(30));first=timer.Elapsed.TotalMilliseconds;
                await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));await viewer.VerificationPresentationAsync();
                Check(viewer.VerificationStatus.HdrEffective==(hdr?1:0)&&viewer.VerificationStatus.VsrEffective==(vsr?1:0),"Benchmark must use actual SDK effects for "+mode);
                if(!hdr)Check(viewer.VerificationStatus.HdrEvaluations==0,"HDR-off benchmark must not run TrueHDR");
                if(!vsr)Check(viewer.VerificationStatus.VsrEvaluations==0,"VSR-off benchmark must not run VSR");
                if(!hdr&&!vsr)Check(viewer.VerificationStatus.SdkAvailable==0,"SDR benchmark must not initialize NGX");
                results.Add(new{mode,kind="new-window",firstPhotoMilliseconds=first,completedMilliseconds=timer.Elapsed.TotalMilliseconds,decodes=FrameCache.Shared.Metrics.Decodes-before.Decodes,cacheHits=FrameCache.Shared.Metrics.Hits-before.Hits,frame=viewer.VerificationFrameInfo});
                foreach(int selected in new[]{7,2,9,4,11,1})
                {
                    var effectsBefore=viewer.VerificationStatus;before=FrameCache.Shared.Metrics;timer.Restart();await viewer.VerificationSelectAsync(selected);await viewer.VerificationPresentationAsync();Evaluated(hdr,vsr,effectsBefore);
                    Check(viewer.VerificationPhoto==photos[selected].Path,"Random jump must present the requested photo");results.Add(Selection(mode,"random",selected,timer,before));
                }
                for(int selected=2;selected<12;selected++)
                {
                    await Task.Delay(450);var effectsBefore=viewer.VerificationStatus;before=FrameCache.Shared.Metrics;timer.Restart();await viewer.VerificationSelectAsync(selected);await viewer.VerificationPresentationAsync();Evaluated(hdr,vsr,effectsBefore);
                    Check(viewer.VerificationPhoto==photos[selected].Path,"Prefetched navigation must keep the correct photo");results.Add(Selection(mode,"prefetched-or-cached",selected,timer,before));
                }
                before=FrameCache.Shared.Metrics;timer.Restart();var requests=Enumerable.Range(0,10).Select(n=>viewer.VerificationSelectAsync(n)).ToArray();await Task.WhenAll(requests);await viewer.VerificationPresentationAsync();
                Check(viewer.VerificationPhoto==photos[9].Path,"Overlapping requests must finish on the latest photo");
                Check(viewer.VerificationStatus.HdrEffective==(hdr?1:0)&&viewer.VerificationStatus.VsrEffective==(vsr?1:0),"Latest photo must keep the requested actual effects");
                results.Add(Selection(mode,"ten-overlapping-latest",9,timer,before));
                viewer.Close();await viewer.VerificationDisposeAsync();viewer=null;
            }

            // A disappeared file must stay selected and show an error, rather
            // than silently substituting a different sibling or launching gallery.
            string missing=Path.Combine(App.DataDirectory,"SDR","deleted.jpg");await ViewerPreferences.UpdateAsync(new());
            viewer=new(missing,null,host);viewer.Activate();bool failed=false;
            try{await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(10));}catch(Exception){failed=true;}
            Check(failed&&viewer.VerificationErrorVisible&&viewer.VerificationPhoto==missing,"Unavailable activation must show the requested photo's error");
            Check((await App.Store.PhotosAsync(PhotoScope.Recent)).All(p=>p.Path!=missing),"Failed activation must not record a successfully opened photo");
            results.Add(new{mode="unavailable-file",kind="direct-activation",errorVisible=true,requestedPathRetained=true});
            File.WriteAllText(Path.Combine(App.DataDirectory,"photo-performance.json"),JsonSerializer.Serialize(new{complete=true,results},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(App.DataDirectory,"photo-performance.json"),JsonSerializer.Serialize(new{complete=false,results,failure=ex.ToString()},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));App.Log(ex.ToString());Environment.ExitCode=1;
        }
        finally{if(viewer is not null){viewer.Close();await viewer.VerificationDisposeAsync();}host.Close();}
    }
}
