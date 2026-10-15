using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Luma.Core;

namespace Luma.App;
internal static class LivePhotoVerification
{
    internal static async Task RenderSymbolAsync(LivePhotoIcon icon)
    {
        var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(icon,240,240);
        var pixels=await bitmap.GetPixelsAsync();using var output=File.Create(Path.Combine(App.DataDirectory,"live-symbol.png"));using var stream=output.AsRandomAccessStream();
        var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels.ToArray());await encoder.FlushAsync();
    }
    internal static async Task AudioAsync(MainWindow owner,string path,bool write)
    {
        ViewerWindow? viewer=null;var results=new List<object>();string report=Path.Combine(App.DataDirectory,"live-audio-"+(write?"write":"read")+".json");
        void Save(bool complete,string? error=null)=>File.WriteAllText(report,JsonSerializer.Serialize(new{complete,results,error},new JsonSerializerOptions{WriteIndented=true}));
        try
        {
            await owner.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            if(write){await App.Store.SetSettingAsync("viewer.preferences","{\"Hdr\":false,\"Vsr\":false,\"Preset\":1}");await ViewerPreferences.LoadAsync();}
            viewer=new(path,owner:owner);viewer.Activate();await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            results.Add(await viewer.VerificationLiveAudioAsync(write));
            results.Add(await viewer.VerificationFilmstripDetailsAsync());
            results.Add(await viewer.VerificationFillWindowAsync());
            viewer.Close();await viewer.VerificationDisposeAsync();viewer=null;
            if(write)
            {
                string copy=Path.Combine(App.DataDirectory,"another-live"+Path.GetExtension(path));File.Copy(path,copy);
                viewer=new(copy,owner:owner);viewer.Activate();await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
                results.Add(await viewer.VerificationLiveAudioAsync(false));viewer.Close();await viewer.VerificationDisposeAsync();viewer=null;
            }
            Save(true);
        }
        catch(Exception ex){Save(false,ex.ToString());throw;}
        finally{if(viewer is not null){viewer.Close();await viewer.VerificationDisposeAsync();}owner.Close();}
    }
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
