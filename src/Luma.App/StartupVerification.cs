using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;

namespace Luma.App;

// Uses the normal gallery/file-activation path in an isolated test library.
internal static class StartupVerification
{
    internal static async Task RunAsync(Window window)
    {
        var results=new List<object>();string report=Path.Combine(App.DataDirectory,"startup.json");
        using var process=Process.GetCurrentProcess();
        double SinceStart()=>(DateTime.UtcNow-process.StartTime.ToUniversalTime()).TotalMilliseconds;
        object Snapshot(string phase){process.Refresh();return new{phase,milliseconds=SinceStart(),cpuMilliseconds=process.TotalProcessorTime.TotalMilliseconds,workingBytes=process.WorkingSet64,privateBytes=process.PrivateMemorySize64,gpuTextureBytes=window is ViewerWindow viewer?viewer.VerificationStatus.GpuBytes:0,presents=window is ViewerWindow v?v.VerificationRenderMetrics.Presents:0};}
        try
        {
            if(window is ViewerWindow viewer)
            {
                await viewer.FirstFrameReady.Task.WaitAsync(TimeSpan.FromSeconds(30));results.Add(Snapshot("first-photo"));
                await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));results.Add(Snapshot("viewer-ready"));
            }
            else await ((MainWindow)window).VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            results.Add(Snapshot("ready"));
            File.WriteAllText(Path.Combine(App.DataDirectory,"startup-ready.json"),JsonSerializer.Serialize(results));
            await Task.Delay(1500);results.Add(Snapshot("idle-start"));
            for(int n=0;n<8;n++){await Task.Delay(1000);results.Add(Snapshot("idle"));}
            File.WriteAllText(report,JsonSerializer.Serialize(new{complete=true,results},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{complete=false,results,failure=ex.ToString()}));Environment.ExitCode=1;}
        finally{window.Close();if(window is ViewerWindow viewer)await viewer.VerificationDisposeAsync();}
    }
}
