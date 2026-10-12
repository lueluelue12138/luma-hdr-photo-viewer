using System.Diagnostics;
using System.Text.Json;
using Luma.Core;

namespace Luma.App;

public sealed partial class ViewerWindow
{
    internal async Task<object> VerificationResponsivenessAsync(bool hdr)
    {
        var delays=new List<double>();var paintTimes=new List<double>();
        using var stop=new CancellationTokenSource();
        var heartbeat=Task.Run(async()=>
        {
            while(!stop.IsCancellationRequested)
            {
                var posted=Stopwatch.GetTimestamp();
                DispatcherQueue.TryEnqueue(()=>delays.Add(Stopwatch.GetElapsedTime(posted).TotalMilliseconds));
                try{await Task.Delay(8,stop.Token);}catch(OperationCanceledException){break;}
            }
        });
        var before=renderer!.Metrics;var timer=Stopwatch.StartNew();var effect=hdr?VerificationEffectsAsync(true,false):Task.CompletedTask;
        try
        {
            for(int n=0;n<90;n++)
            {
                ZoomAt(Surface.ActualWidth*.6,Surface.ActualHeight*.5,n%6<3?60:-60);
                if(n%12==0){panX=40;panY=-20;UpdateView();}
                if(n%20==0){ToggleControls();}
                paintTimes.Add(renderer.Metrics.LastDrawMilliseconds);await Task.Delay(16);
            }
            await effect;await Task.Delay(300);await VerificationPresentationAsync();
        }
        finally{stop.Cancel();await heartbeat;}
        var after=renderer.Metrics;
        return new{scenario=hdr?"HDR-processing-input":"SDR-input",elapsed=timer.ElapsedMilliseconds,dispatcherLatency=delays,renderSubmissions=after.Presents-before.Presents,blurBuilds=after.BlurBuilds-before.BlurBuilds,paintTimes,hdr=renderer.CachedStatus.HdrEffective,vsr=renderer.CachedStatus.VsrEvaluations};
    }
    internal async Task<object> VerificationFadeCacheAsync()
    {
        ShowControls(true);await Task.Delay(300);await VerificationPresentationAsync();var before=renderer!.Metrics;
        ToggleControls();await Task.Delay(350);ToggleControls();await Task.Delay(300);await VerificationPresentationAsync();var after=renderer.Metrics;
        if(after.BlurBuilds-before.BlurBuilds>2)throw new InvalidOperationException("Static-photo chrome animation must reuse its Gaussian result");
        return new{scenario="static-chrome-cache",presents=after.Presents-before.Presents,blurBuilds=after.BlurBuilds-before.BlurBuilds};
    }
}

internal static class ViewerPerformanceVerification
{
    internal static async Task RunAsync(MainWindow host,string path)
    {
        var results=new List<object>();ViewerWindow? viewer=null;string report=Path.Combine(App.DataDirectory,"viewer-performance.json");
        try
        {
            await host.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));var photos=FolderIndexer.Siblings(path);
            await ViewerPreferences.UpdateAsync(new());viewer=new(path,photos,host);viewer.Activate();await viewer.FirstFrameReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var first=viewer.VerificationRenderMetrics;
            if(first.PhotoPresents==0||first.UnfrostedPhotoPresents!=0)throw new InvalidOperationException("The initial photo must be composed with its floating glass in the same Present");
            results.Add(new{scenario="first-photo-glass",viewer.FirstFrameMilliseconds,first.PhotoPresents,first.UnfrostedPhotoPresents,first.BlurBuilds});await viewer.VerificationReady.Task;
            results.Add(await viewer.VerificationResponsivenessAsync(false));results.Add(await viewer.VerificationFadeCacheAsync());results.Add(await viewer.VerificationResponsivenessAsync(true));
            viewer.Close();await viewer.VerificationDisposeAsync();viewer=null;
            // Repeat the user's saved-HDR startup case, including real NGX work.
            await ViewerPreferences.UpdateAsync(new(Hdr:true,Vsr:false));
            for(int n=0;n<3;n++)
            {
                var timer=Stopwatch.StartNew();viewer=new(path,photos,host);viewer.Activate();await viewer.FirstFrameReady.Task.WaitAsync(TimeSpan.FromSeconds(30));first=viewer.VerificationRenderMetrics;
                if(first.UnfrostedPhotoPresents!=0||first.BlurBuilds==0)throw new InvalidOperationException("Saved-HDR startup exposed an unfrosted photo frame");
                results.Add(new{scenario="saved-HDR-first-photo-glass",window=n+1,openMilliseconds=timer.ElapsedMilliseconds,viewer.FirstFrameMilliseconds,first.PhotoPresents,first.UnfrostedPhotoPresents,first.BlurBuilds});
                await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));if(viewer.VerificationStatus.HdrEffective==0||viewer.VerificationStatus.VsrEvaluations!=0)throw new InvalidOperationException("Saved-HDR startup must actually evaluate TrueHDR-only");
                viewer.Close();await viewer.VerificationDisposeAsync();viewer=null;
            }
            File.WriteAllText(report,JsonSerializer.Serialize(new{complete=true,results},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{complete=false,results,failure=ex.ToString()},new JsonSerializerOptions{WriteIndented=true}));App.Log(ex.ToString());Environment.ExitCode=1;}
        finally{if(viewer is not null){viewer.Close();await viewer.VerificationDisposeAsync();}host.Close();}
    }
}
