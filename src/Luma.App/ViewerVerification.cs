using System.Diagnostics;
using System.Text.Json;
using Luma.Core;

namespace Luma.App;

// Explicit local integration mode, gated by --test-library. Exercises real WinUI
// windows, thumbnail I/O, SwapChainPanel attachment, GPU work and window teardown.
internal static class ViewerVerification
{
    internal static async Task RunAsync(MainWindow host,string path)
    {
        var results=new List<object>();int windows=0;string report=Path.Combine(App.DataDirectory,"viewer-lifecycle.json");
        void Save(bool complete)=>File.WriteAllText(report,JsonSerializer.Serialize(new{complete,windows,results},new JsonSerializerOptions{WriteIndented=true}));
        async Task<ViewerWindow> Open(string file,IReadOnlyList<Photo>? photos=null)
        {
            var timer=Stopwatch.StartNew();var viewer=new ViewerWindow(file,photos);windows++;viewer.Activate();
            await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            results.Add(new{scenario="open",window=windows,milliseconds=timer.ElapsedMilliseconds});Save(false);return viewer;
        }
        async Task Close(ViewerWindow viewer){viewer.Close();await viewer.VerificationDisposeAsync().WaitAsync(TimeSpan.FromSeconds(30));}
        void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);}
        try
        {
            await host.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var siblings=await Task.Run(()=>FolderIndexer.Siblings(path));
            var small=Path.Combine(Path.GetDirectoryName(report)!,"small.jpg");Check(File.Exists(small),"Place a small JPG fixture in the test library");
            // Repeat the user's reported full close/reopen cycle in one process.
            for(int n=0;n<20;n++)
            {
                bool vsr=n%5==4;var viewer=await Open(vsr?small:path,vsr?null:siblings);
                var baseline=viewer.VerificationStatus;Check(baseline.HdrEffective==0&&baseline.VsrEffective==0,"New windows must begin with original pixels");
                await viewer.VerificationEffectsAsync(!vsr,vsr);var status=viewer.VerificationStatus;
                Check(vsr?status.VsrEffective!=0&&status.HdrEvaluations==0:status.HdrEffective!=0&&status.VsrEvaluations==0,"Requested independent enhancement must really evaluate");
                viewer.VerificationZoom(n%2==0?.65:1.25);await Task.Delay(80);
                if(n%4==0){await viewer.VerificationPopupAsync();results.Add(new{scenario="expanded-popup-photo-glass"});}
                results.Add(new{scenario=vsr?"VSR-close-reopen":"HDR-close-reopen",hdr=status.HdrEvaluations,vsr=status.VsrEvaluations,gpuMiB=status.GpuBytes/1048576});
                await Close(viewer);Save(false);
            }
            // Independent windows use separate D3D contexts but serialize global NGX.
            for(int n=0;n<3;n++)
            {
                var a=await Open(path,siblings);var b=await Open(small);
                await Task.WhenAll(a.VerificationEffectsAsync(true,false),b.VerificationEffectsAsync(true,true));
                Check(a.VerificationStatus.HdrEffective!=0&&a.VerificationStatus.VsrEffective==0,"Concurrent HDR-only");
                Check(b.VerificationStatus.HdrEffective!=0&&b.VerificationStatus.VsrEffective!=0,"Concurrent combined HDR/VSR");
                await Close(a);await Close(b);results.Add(new{scenario="two-window-concurrent-enhancement"});Save(false);
            }
            var rapid=await Open(path,siblings);
            var requests=Enumerable.Range(0,8).Select(i=>rapid.VerificationSelectAsync(i%siblings.Count)).ToArray();await Task.WhenAll(requests);
            await rapid.VerificationEffectsAsync(true,false);Check(rapid.VerificationStatus.HdrEffective!=0,"Rapid navigation final frame");await Close(rapid);
            results.Add(new{scenario="eight-overlapping-photo-requests"});Save(false);
            // Close during GPU initialization, then during an in-flight SDK evaluation.
            for(int n=0;n<8;n++)
            {
                var early=new ViewerWindow(path,siblings);windows++;early.Activate();await Task.Delay(100);await Close(early);
                results.Add(new{scenario="close-during-initialization"});Save(false);
            }
            for(int n=0;n<4;n++)
            {
                var busy=await Open(path,siblings);var effect=busy.VerificationEffectsAsync(true,false);await Task.Delay(25);busy.Close();await effect;await busy.VerificationDisposeAsync();
                results.Add(new{scenario="close-during-HDR-evaluation"});Save(false);
            }
            // Uncached PNG + JPEG shell thumbnail requests on a fresh XAML tree.
            for(int n=0;n<5;n++){var v=await Open(path,siblings);await Task.Delay(400);await Close(v);}
            Save(true);App.Log($"PASS viewer lifecycle: {windows} windows; HDR/VSR close-reopen, concurrent NGX, rapid navigation, busy teardown");
        }
        catch(Exception ex){results.Add(new{failure=ex.ToString()});Save(false);App.Log("FAIL viewer lifecycle: "+ex);Environment.ExitCode=1;}
        finally{host.Close();}
    }
}
