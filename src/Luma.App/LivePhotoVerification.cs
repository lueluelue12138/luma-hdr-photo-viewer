using System.Text.Json;
using Luma.Core;

namespace Luma.App;
internal static class LivePhotoVerification
{
    internal static async Task RunAsync(MainWindow owner,string path)
    {
        ViewerWindow? viewer=null;string report=Path.Combine(App.DataDirectory,"live-photo.json");object? result=null;
        void Save(bool complete,string? error=null)=>File.WriteAllText(report,JsonSerializer.Serialize(new{complete,result,error},new JsonSerializerOptions{WriteIndented=true}));
        try
        {
            await owner.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await ViewerPreferences.UpdateAsync(new());
            viewer=new ViewerWindow(path,owner:owner);viewer.Activate();await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            result=await viewer.VerificationLiveAsync();Save(false);
            await viewer.VerificationLiveCloseAsync();viewer=null;Save(true);
        }
        catch(Exception ex){Save(false,ex.ToString());throw;}
        finally{if(viewer is not null){viewer.Close();await viewer.VerificationDisposeAsync();}owner.Close();}
    }
}
