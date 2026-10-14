using Luma.Core;
using System.Diagnostics;
using System.Text.Json;

namespace Luma.App;
internal static class HdrFormatVerification
{
    internal static async Task RunAsync(MainWindow owner,string folder,string? jxr)
    {
        var results=new List<object>();ViewerWindow? viewer=null;
        void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        void Save(bool complete)=>File.WriteAllText(Path.Combine(App.DataDirectory,"hdr-format-windows.json"),JsonSerializer.Serialize(new{complete,results},new JsonSerializerOptions{WriteIndented=true}));
        try
        {
            await owner.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(20));
            // This mode is accepted only with --test-library; production
            // preferences, album indexes and photos are never modified.
            await ViewerPreferences.UpdateAsync(new(false,false,1));
            var paths=new[]{"android-xmp.jpg","iso-21496.jpg","apple-old.jpg","apple-new.jpg","ordinary.jpg"}.Select(name=>Path.Combine(folder,name)).ToList();
            if(jxr is not null)paths.Add(jxr);
            foreach(var name in new[]{"apple-heic.heic","iso-heic.heic","pq-heic.heic","sdr-heic.heic"}){var path=Path.Combine(folder,name);if(File.Exists(path))paths.Add(path);}
            var photos=paths.Select(path=>{var f=new FileInfo(path);return new Photo(path,f.Name,f.Length,f.LastWriteTimeUtc.Ticks);}).ToList();
            viewer=new ViewerWindow(paths[0],photos,owner);viewer.Activate();await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            for(int n=0;n<paths.Count;n++)
            {
                if(n!=0)await viewer.VerificationSelectAsync(n);
                await viewer.VerificationPresentationAsync();
                var before=viewer.VerificationStatus;var frame=viewer.VerificationFrameInfo;
                int expected=Path.GetFileName(paths[n]) switch{"apple-heic.heic"=>3,"iso-heic.heic"=>4,"pq-heic.heic"=>1,"sdr-heic.heic"=>0,_=>n==4?0:n==5?1:2};
                Check(frame?.NativeHdr==expected&&before.NativeHdr==expected&&before.Fp16!=0,"Actual viewer frame/FP16 format mismatch");
                if(expected!=0)
                {
                    await viewer.VerificationEffectsAsync(true,true);var effects=viewer.VerificationStatus;
                    Check(effects.HdrEffective==0&&effects.VsrEffective==0&&effects.HdrEvaluations==before.HdrEvaluations&&effects.VsrEvaluations==before.VsrEvaluations,"Native HDR must skip both SDK evaluations even with persisted enhancement requests");
                    int jobs=viewer.VerificationWorkerJobs;var times=new List<double>();
                    for(int repeat=0;repeat<10;repeat++){
                        var watch=Stopwatch.StartNew();viewer.VerificationOriginal(true);viewer.VerificationOriginal(false);watch.Stop();times.Add(watch.Elapsed.TotalMilliseconds);
                    }
                    await viewer.VerificationPresentationAsync();
                    Check(viewer.VerificationWorkerJobs==jobs,"Original/SDR comparison must not queue processing");
                    Check(viewer.VerificationStatusText.Contains(expected==2?"增益图 JPEG":"原生 HDR")||before.HdrActive==0,"Native HDR label must identify the actual source");
                    await viewer.VerificationEffectsAsync(false,false);
                    results.Add(new{file=Path.GetFileName(paths[n]),nativeHdr=expected,fp16=before.Fp16,hdrActive=before.HdrActive,width=frame?.Width,height=frame?.Height,hdrCalls=effects.HdrEvaluations,vsrCalls=effects.VsrEvaluations,comparisonMilliseconds=times});
                }
                else results.Add(new{file=Path.GetFileName(paths[n]),nativeHdr=0,fp16=before.Fp16});
                Save(false);
            }
            viewer.Close();await viewer.VerificationDisposeAsync();viewer=null;
            foreach(var path in paths.Where(p=>p.EndsWith(".heic",StringComparison.OrdinalIgnoreCase)).Take(3)){
                viewer=new ViewerWindow(path,photos,owner);viewer.Activate();await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));await viewer.VerificationPresentationAsync();
                var state=viewer.VerificationStatus;Check(state.NativeHdr!=0&&state.Fp16!=0,"HEIC reopening must retain the actual native HDR path");
                results.Add(new{file=Path.GetFileName(path),scenario="reopen-native-HDR",nativeHdr=state.NativeHdr,fp16=state.Fp16,reused=viewer.VerificationDeviceReused});Save(false);
                viewer.Close();await viewer.VerificationDisposeAsync();viewer=null;
            }
            Save(true);App.Log("PASS native HDR format windows, mixed navigation, reopened HEIC, source status, SDK bypass and cached comparison");
        }
        catch(Exception e){Save(false);App.Log(e.ToString());throw;}
        finally{if(viewer is not null){viewer.Close();await viewer.VerificationDisposeAsync();}owner.Close();}
    }
}
