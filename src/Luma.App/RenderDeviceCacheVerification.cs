using System.Diagnostics;
using System.Text.Json;
using Luma.Core;

namespace Luma.App;
internal static class RenderDeviceCacheVerification
{
    internal static async Task RunAsync(MainWindow host,string path)
    {
        var results=new List<object>();var viewers=new List<ViewerWindow>();string report=Path.Combine(App.DataDirectory,"device-cache.json");
        void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
        async Task<ViewerWindow> OpenAsync(string scenario,bool hdr,bool vsr,bool expectedReuse)
        {
            await ViewerPreferences.UpdateAsync(new(Hdr:hdr,Vsr:vsr));var timer=Stopwatch.StartNew();
            var viewer=new ViewerWindow(path,FolderIndexer.Siblings(path),host);viewers.Add(viewer);viewer.Activate();
            await viewer.FirstFrameReady.Task.WaitAsync(TimeSpan.FromSeconds(30));long first=timer.ElapsedMilliseconds;
            await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));var status=viewer.VerificationStatus;var render=viewer.VerificationRenderMetrics;
            Check(viewer.VerificationDeviceReused==expectedReuse,"Unexpected device ownership transfer: "+scenario);
            Check(render.UnfrostedPhotoPresents==0&&render.BlurBuilds>0,"Reused window exposed an unfrosted photo");
            Check(status.HdrEffective==(hdr?1:0)&&status.VsrEffective==(vsr?1:0),"Requested real enhancement did not run: "+scenario);
            Check(status.HdrEvaluations==(hdr?1:0)&&status.VsrEvaluations==(vsr?1:0),"Old evaluation counts/results survived a new lifetime: "+scenario);
            if(!hdr&&!vsr)Check(status.SdkAvailable==0,"SDR window acquired an initialized SDK device");
            using var process=Process.GetCurrentProcess();process.Refresh();
            results.Add(new{scenario,firstMilliseconds=first,completeMilliseconds=timer.ElapsedMilliseconds,viewer.VerificationDeviceReused,status.GpuBytes,status.HdrEvaluations,status.VsrEvaluations,process.WorkingSet64,process.PrivateMemorySize64,cache=RenderDeviceCache.Metrics});return viewer;
        }
        async Task CloseAsync(ViewerWindow viewer){viewer.Close();await viewer.VerificationDisposeAsync();viewers.Remove(viewer);Check(RenderDeviceCache.Metrics.Retained<=1,"More than one idle device retained");}
        try
        {
            await host.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));await RenderDeviceCache.ClearAsync();
            var fresh=await OpenAsync("fresh-HDR",true,false,false);await CloseAsync(fresh);results.Add(new{scenario="HDR-retired",memory=await RenderDeviceCache.MemoryAsync()});
            for(int n=0;n<3;n++){var warm=await OpenAsync("reopened-HDR",true,false,true);await CloseAsync(warm);}
            var sdr=await OpenAsync("SDR-does-not-inherit-SDK",false,false,false);await CloseAsync(sdr);
            var first=await OpenAsync("first-active-HDR",true,false,true);
            var second=await OpenAsync("second-active-HDR",true,false,false);
            Check(first.VerificationStatus.HdrEffective==1,"Second window invalidated first window's effects");
            await CloseAsync(first);await CloseAsync(second);results.Add(new{scenario="two-windows-one-idle-slot",memory=await RenderDeviceCache.MemoryAsync()});
            var before=RenderDeviceCache.Metrics;
            await Task.Delay(RenderDeviceCache.RetentionMilliseconds+500);
            Check(RenderDeviceCache.Metrics.Retained==0&&RenderDeviceCache.Metrics.Expired>before.Expired,"Idle model/device did not expire");
            results.Add(new{scenario="after-expiry-memory",memory=await Task.Run(()=>{IntPtr probe=IntPtr.Zero;try{Native.Check(Native.LumaCreateFast(IntPtr.Zero,IntPtr.Zero,out probe));Native.Check(Native.LumaVideoMemory(probe,out var budget,out var usage));return new{budgetBytes=budget,processLocalGpuBytes=usage};}finally{if(probe!=IntPtr.Zero)Native.LumaDestroy(probe);}})});
            var afterExpiry=await OpenAsync("fresh-after-expiry",true,false,false);await CloseAsync(afterExpiry);
            await RenderDeviceCache.ClearAsync();Check(RenderDeviceCache.Metrics.Retained==0,"Explicit cache drain failed");
            results.Add(new{scenario="expiry-and-drain",cache=RenderDeviceCache.Metrics});
            File.WriteAllText(report,JsonSerializer.Serialize(new{complete=true,results},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{complete=false,results,failure=ex.ToString()},new JsonSerializerOptions{WriteIndented=true}));App.Log(ex.ToString());Environment.ExitCode=1;}
        finally{foreach(var viewer in viewers.ToArray()){viewer.Close();await viewer.VerificationDisposeAsync();}await RenderDeviceCache.ClearAsync();host.Close();}
    }
}
