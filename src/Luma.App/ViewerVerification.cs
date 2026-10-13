using System.Diagnostics;
using System.Text.Json;
using Luma.Core;

namespace Luma.App;

// Explicit local integration mode, gated by --test-library. Exercises real WinUI
// windows, thumbnail I/O, SwapChainPanel attachment, GPU work and window teardown.
internal static class ViewerVerification
{
    internal static async Task RunInteractionAsync(MainWindow host,string path)
    {
        string report=Path.Combine(App.DataDirectory,"viewer-interaction.json");var results=new List<object>();ViewerWindow? viewer=null,idleViewer=null;
        void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        void Bounds(Microsoft.UI.Xaml.Window window)
        {
            var a=Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(window.AppWindow.Id,Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;var p=window.AppWindow.Position;var s=window.AppWindow.Size;
            Check(p.X>=a.X&&p.Y>=a.Y&&p.X+s.Width<=a.X+a.Width&&p.Y+s.Height<=a.Y+a.Height,"Window must stay inside the current monitor work area");
            Check(Math.Abs(p.X+s.Width/2.0-a.X-a.Width/2.0)<=2&&Math.Abs(p.Y+s.Height/2.0-a.Y-a.Height/2.0)<=2,"Window must be centered");
            results.Add(new{scenario="centered-window",position=new[]{p.X,p.Y},size=new[]{s.Width,s.Height},work=new[]{a.X,a.Y,a.Width,a.Height}});
        }
        try
        {
            await host.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));Bounds(host);await ViewerPreferences.UpdateAsync(new());
            var photos=FolderIndexer.Siblings(path);Check(photos.Count>=12,"Interaction test needs at least 12 photos");
            var opening=Stopwatch.StartNew();viewer=new(path,photos,host);viewer.Activate();await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));Bounds(viewer);
            results.Add(new{scenario="first-window",milliseconds=opening.ElapsedMilliseconds,firstFrame=viewer.FirstFrameMilliseconds,sdrGpuMiB=viewer.VerificationStatus.GpuBytes/1048576});
            Check(viewer.VerificationStatus.HdrEvaluations==0&&viewer.VerificationStatus.VsrEvaluations==0&&viewer.VerificationStatus.SdkAvailable==0,"SDR viewing must not initialize or invoke NGX");
            foreach(int selected in new[]{7,2,9,4,11,1})
            {
                var timer=Stopwatch.StartNew();await viewer.VerificationSelectAsync(selected);
                Check(viewer.VerificationPhoto==photos[selected].Path,"Random navigation displayed a stale photo");results.Add(new{scenario="random-SDR",selected,milliseconds=timer.ElapsedMilliseconds,firstFrame=viewer.FirstFrameMilliseconds});
            }
            for(int selected=2;selected<12;selected++)
            {
                await Task.Delay(450);var timer=Stopwatch.StartNew();await viewer.VerificationSelectAsync(selected);
                results.Add(new{scenario="prefetched-SDR",selected,milliseconds=timer.ElapsedMilliseconds,firstFrame=viewer.FirstFrameMilliseconds});
            }
            Check(viewer.VerificationStatus.HdrEvaluations==0&&viewer.VerificationStatus.VsrEvaluations==0,"SDR navigation must never run AI");
            viewer.VerificationZoom(.65);viewer.VerificationPan(400,-300);Check(viewer.VerificationView.X==0&&viewer.VerificationView.Y==0,"Zoomed-out photo must be centered");
            viewer.VerificationZoom(2);viewer.VerificationPan(150,-80);Check(Math.Abs(viewer.VerificationView.X)>1||Math.Abs(viewer.VerificationView.Y)>1,"Zoomed-in photo must pan");
            await viewer.VerificationDetailAsync();Check(viewer.VerificationFrameInfo?.Downsampled!=2,"Zoom must load actual full resolution");
            viewer.VerificationPan(1e9,-1e9);Check(Math.Abs(viewer.VerificationView.X)<100000&&Math.Abs(viewer.VerificationView.Y)<100000,"Pan must be constrained");viewer.VerificationZoom(1);Check(viewer.VerificationView.X==0&&viewer.VerificationView.Y==0,"Fit must recenter");
            results.Add(new{scenario="zoom-pan-fit-full-detail",width=viewer.VerificationFrameInfo?.Width,height=viewer.VerificationFrameInfo?.Height});
            var overlapping=Enumerable.Range(0,10).Select(n=>viewer.VerificationSelectAsync(n)).ToArray();await Task.WhenAll(overlapping);Check(viewer.VerificationPhoto==photos[9].Path,"Fast navigation must finish on the latest selection");
            results.Add(new{scenario="ten-overlapping-requests"});await viewer.VerificationScrollAsync();
            await viewer.VerificationChromeAndMotionAsync();results.Add(new{scenario="manual-chrome-selected-outline-wheel-animation-fullscreen-restore"});
            await viewer.VerificationEffectsAsync(true,false);Check(viewer.VerificationStatus.HdrEffective!=0&&viewer.VerificationStatus.VsrEvaluations==0,"Lazy initialization must preserve real independent HDR");
            results.Add(new{scenario="lazy-HDR",gpuMiB=viewer.VerificationStatus.GpuBytes/1048576});
            var cached=viewer.VerificationStatus;int jobs=viewer.VerificationWorkerJobs;var comparisonMs=new List<double>();
            for(int n=0;n<20;n++)
            {
                var timer=Stopwatch.StartNew();viewer.VerificationOriginal(true);comparisonMs.Add(timer.Elapsed.TotalMilliseconds);await viewer.VerificationPresentationAsync();
                Check(viewer.VerificationStatus.HdrEffective==0&&viewer.VerificationStatus.VsrEffective==0,"Holding original must select original pixels immediately");
                Check(!viewer.VerificationStatusText.Contains("正在处理"),"Cached comparison must not show enhancement processing");
                for(int repeat=0;repeat<10;repeat++)viewer.VerificationOriginal(true);
                timer.Restart();viewer.VerificationOriginal(false);comparisonMs.Add(timer.Elapsed.TotalMilliseconds);await viewer.VerificationPresentationAsync();
                Check(viewer.VerificationStatus.HdrEffective!=0,"Releasing original must immediately restore cached HDR");
                Check(!viewer.VerificationStatusText.Contains("正在处理"),"Cached restoration must not show enhancement processing");
            }
            Check(viewer.VerificationWorkerJobs==jobs,"Comparison must not enqueue GPU waits, uploads or initialization");
            Check(viewer.VerificationStatus.HdrEvaluations==cached.HdrEvaluations&&viewer.VerificationStatus.VsrEvaluations==cached.VsrEvaluations,"Comparison must not run AI again");
            results.Add(new{scenario="cached-original-comparison",switches=comparisonMs.Count,milliseconds=comparisonMs,additionalWorkerJobs=viewer.VerificationWorkerJobs-jobs,additionalHdrCalls=viewer.VerificationStatus.HdrEvaluations-cached.HdrEvaluations});
            viewer.VerificationOriginal(true);await viewer.VerificationSelectAsync(0);
            Check(viewer.VerificationStatus.HdrEffective==0,"Hold must survive new frame upload and background enhancement");
            cached=viewer.VerificationStatus;jobs=viewer.VerificationWorkerJobs;viewer.VerificationOriginal(false);await viewer.VerificationPresentationAsync();
            Check(viewer.VerificationStatus.HdrEffective!=0&&viewer.VerificationStatus.HdrEvaluations==cached.HdrEvaluations&&viewer.VerificationWorkerJobs==jobs,"HDR prepared while holding original must restore without recomputing");
            var loading=viewer.VerificationSelectAsync(1);await Task.Delay(25);viewer.VerificationOriginal(true);await loading;
            Check(viewer.VerificationStatus.HdrEffective==0,"Latest original hold must survive in-flight image and AI work");
            viewer.VerificationOriginal(false);await viewer.VerificationPresentationAsync();Check(viewer.VerificationStatus.HdrEffective!=0,"Release after in-flight work must restore HDR");
            results.Add(new{scenario="original-comparison-during-photo-processing"});
            idleViewer=new(path,photos,host);idleViewer.Activate();await idleViewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));await idleViewer.VerificationEffectsAsync(true,false);
            results.Add(await host.VerificationGalleryAsync(Path.Combine(App.DataDirectory,"album-fixture")));
            File.WriteAllText(report,JsonSerializer.Serialize(new{complete=true,results},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{complete=false,results,failure=ex.ToString()},new JsonSerializerOptions{WriteIndented=true}));App.Log(ex.ToString());Environment.ExitCode=1;}
        finally{if(idleViewer is not null){idleViewer.Close();await idleViewer.VerificationDisposeAsync();}if(viewer is not null){viewer.Close();await viewer.VerificationDisposeAsync();}host.Close();}
    }
    internal static async Task RunPreferencesAsync(MainWindow host,string path)
    {
        ViewerWindow? viewer=null;string report=Path.Combine(App.DataDirectory,"viewer-preferences.json");
        try
        {
            await host.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));viewer=new(path);viewer.Activate();await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var prefs=viewer.VerificationPreferences;var status=viewer.VerificationStatus;
            if(!prefs.Hdr||prefs.Vsr||status.HdrEffective==0||status.VsrEffective!=0)throw new InvalidOperationException("Saved HDR-only preference did not survive a fresh process");
            File.WriteAllText(report,JsonSerializer.Serialize(new{complete=true,prefs.Hdr,prefs.Vsr,status.HdrEffective,status.VsrEffective}));
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{complete=false,failure=ex.ToString()}));App.Log(ex.ToString());Environment.ExitCode=1;}
        finally{if(viewer is not null){viewer.Close();await viewer.VerificationDisposeAsync();}host.Close();}
    }
    internal static async Task RunAsync(MainWindow host,string path)
    {
        var results=new List<object>();var openWindows=new HashSet<ViewerWindow>();int windows=0;string report=Path.Combine(App.DataDirectory,"viewer-lifecycle.json");
        void Save(bool complete)=>File.WriteAllText(report,JsonSerializer.Serialize(new{complete,windows,results},new JsonSerializerOptions{WriteIndented=true}));
        async Task<ViewerWindow> Open(string file,IReadOnlyList<Photo>? photos=null)
        {
            var timer=Stopwatch.StartNew();var viewer=new ViewerWindow(file,photos);openWindows.Add(viewer);viewer.Closed+=(_,_)=>openWindows.Remove(viewer);windows++;viewer.Activate();
            await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            results.Add(new{scenario="open",window=windows,milliseconds=timer.ElapsedMilliseconds});Save(false);return viewer;
        }
        async Task Close(ViewerWindow viewer){viewer.Close();openWindows.Remove(viewer);await viewer.VerificationDisposeAsync().WaitAsync(TimeSpan.FromSeconds(30));}
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
                Check(vsr?status.VsrEffective!=0&&status.HdrEvaluations==0:status.HdrEffective!=0&&status.VsrEvaluations==0,$"Requested independent enhancement must really evaluate; HDR={status.HdrMessage}; VSR={status.VsrMessage}; SDK={status.SdkAvailable}; result=0x{status.NgxResult:X8}; deviceReused={viewer.VerificationDeviceReused}");
                int jobs=viewer.VerificationWorkerJobs;viewer.VerificationOriginal(true);await viewer.VerificationPresentationAsync();
                Check(viewer.VerificationStatus.HdrEffective==0&&viewer.VerificationStatus.VsrEffective==0,"Original comparison must hide both enhancements");viewer.VerificationOriginal(false);await viewer.VerificationPresentationAsync();
                var restored=viewer.VerificationStatus;Check(restored.HdrEffective==status.HdrEffective&&restored.VsrEffective==status.VsrEffective&&restored.HdrEvaluations==status.HdrEvaluations&&restored.VsrEvaluations==status.VsrEvaluations&&viewer.VerificationWorkerJobs==jobs,"Independent HDR/VSR comparison must restore cached output without processing");
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
            // Persist real user preferences, then check new windows and a storage
            // reload. The default stays VSR-off, but an explicit choice is retained.
            Check(App.Theme==Microsoft.UI.Xaml.ElementTheme.Default,"Unset theme must follow Windows");
            var remembered=await Open(small);await remembered.VerificationPersistAsync(true,true);var options=remembered.VerificationPreferences;await Close(remembered);
            await ViewerPreferences.LoadAsync();Check(ViewerPreferences.Current==options,"Viewer settings survive storage reload");
            var combined=await Open(small);Check(combined.VerificationPreferences.Hdr&&combined.VerificationPreferences.Vsr&&combined.VerificationStatus.HdrEffective!=0&&combined.VerificationStatus.VsrEffective!=0,"New photo automatically applies saved HDR/VSR");
            await combined.VerificationPersistAsync(true,false);await Close(combined);
            var hdrOnly=await Open(path,siblings);Check(hdrOnly.VerificationStatus.HdrEffective!=0&&hdrOnly.VerificationStatus.VsrEffective==0,"Saved HDR-only does not run VSR");await hdrOnly.VerificationScrollAsync();await Close(hdrOnly);
            var direct=await Open(path);await direct.VerificationScrollAsync();await Close(direct);
            results.Add(new{scenario="direct-file-filmstrip-scrollbar-binding"});
            await ViewerPreferences.UpdateAsync(new(Hdr:true,Vsr:false));
            results.Add(new{scenario="persisted-enhancement-and-independent-filmstrip-scroll"});Save(false);
            Save(true);App.Log($"PASS viewer lifecycle: {windows} windows; HDR/VSR close-reopen, concurrent NGX, rapid navigation, busy teardown");
        }
        catch(Exception ex){results.Add(new{failure=ex.ToString()});Save(false);App.Log("FAIL viewer lifecycle: "+ex);Environment.ExitCode=1;}
        finally{foreach(var viewer in openWindows.ToArray()){viewer.Close();await viewer.VerificationDisposeAsync();}host.Close();}
    }
}
