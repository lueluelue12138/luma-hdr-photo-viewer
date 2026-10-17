using System.Runtime.InteropServices;
using System.Text.Json;
using Luma.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Luma.App;

// Explicit local integration mode, gated by --test-library. Checks real HWND
// geometry and frame restoration; it never changes the user's display or library.
internal static class FullscreenVerification
{
    internal static async Task RunAsync(MainWindow host,string path)
    {
        var results=new List<object>();ViewerWindow? viewer=null;
        string report=Path.Combine(App.DataDirectory,"fullscreen.json");
        try
        {
            await host.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await ViewerPreferences.UpdateAsync(new());
            viewer=new(path);viewer.Activate();
            await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await viewer.VerificationFullscreenAsync(host,results);
            File.WriteAllText(report,JsonSerializer.Serialize(new{complete=true,results},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex)
        {
            File.WriteAllText(report,JsonSerializer.Serialize(new{complete=false,results,failure=ex.ToString()},new JsonSerializerOptions{WriteIndented=true}));
            App.Log(ex.ToString());Environment.ExitCode=1;
        }
        finally{if(viewer is not null){viewer.Close();await viewer.VerificationDisposeAsync();}host.Close();}
    }
}

public sealed partial class ViewerWindow
{
    internal async Task VerificationFullscreenAsync(Window host,List<object> results)
    {
        void Check(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
        var presenter=(OverlappedPresenter)AppWindow.Presenter;
        var originalPosition=AppWindow.Position;var originalSize=AppWindow.Size;
        var hwnd=WinRT.Interop.WindowNative.GetWindowHandle(this);
        foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
        foreach(bool maximized in new[]{false,true})
        {
            ApplyTheme(theme);
            if(maximized)presenter.Maximize();else{presenter.Restore();AppWindow.MoveAndResize(new(originalPosition.X,originalPosition.Y,originalSize.Width,originalSize.Height));}
            await Task.Delay(250);
            long before=GetWindowLongPtr(hwnd,-16).ToInt64();var position=AppWindow.Position;var size=AppWindow.Size;
            for(int repeat=0;repeat<3;repeat++)
            {
                ToggleFullScreen();await Task.Delay(350);
                await VerificationPresentationAsync();
                GetClientRect(hwnd,out var client);var origin=new ScreenPoint();ClientToScreen(hwnd,ref origin);
                var screen=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Primary).OuterBounds;
                long frame=GetWindowLongPtr(hwnd,-16).ToInt64();
                results.Add(new{scenario="fullscreen",theme=theme.ToString(),maximized,repeat,windowStyle=frame,
                    window=new[]{AppWindow.Position.X,AppWindow.Position.Y,AppWindow.Size.Width,AppWindow.Size.Height},
                    client=new[]{origin.X,origin.Y,client.Right-client.Left,client.Bottom-client.Top},screen=new[]{screen.X,screen.Y,screen.Width,screen.Height},
                    root=new[]{Root.ActualWidth,Root.ActualHeight},scale=Root.XamlRoot.RasterizationScale,titleHeight=TitleRow.ActualHeight});
                Check(fullScreen&&AppWindow.Presenter.Kind==AppWindowPresenterKind.FullScreen,"Actual presenter must be fullscreen");
                Check(!ExtendsContentIntoTitleBar,"Fullscreen must disable the extended caption inset");
                Check((frame&0x00C40000)==0,"Fullscreen must not retain a caption or resizing frame");
                Check(origin.X==screen.X&&origin.Y==screen.Y&&client.Right==screen.Width&&client.Bottom==screen.Height,"Client must cover every monitor edge without a one-pixel gap");
                Check(TitleRow.ActualHeight==0&&Math.Abs(Root.ActualHeight*Root.XamlRoot.RasterizationScale-screen.Height)<.25,"XAML must fill the monitor without a one-pixel title inset");
                host.Activate();await Task.Delay(80);Activate();await Task.Delay(120);
                Check(GetWindowLongPtr(hwnd,-16).ToInt64()==frame&&!ExtendsContentIntoTitleBar,"Reactivation must not restore the fullscreen caption");
                ShowInfo(true);await Task.Delay(80);ShowInfo(false);
                Check(GetWindowLongPtr(hwnd,-16).ToInt64()==frame&&!ExtendsContentIntoTitleBar,"Details must not affect the fullscreen frame");
                ToggleFullScreen();await Task.Delay(350);
                long restored=GetWindowLongPtr(hwnd,-16).ToInt64();
                Check(!fullScreen&&ReferenceEquals(AppWindow.Presenter,presenter),"Windowed presenter must be restored");
                Check(restored==before,"Windowed caption, border and resize styles must restore exactly");
                Check(presenter.State==(maximized?OverlappedPresenterState.Maximized:OverlappedPresenterState.Restored),"Windowed maximization state must restore");
                Check(AppWindow.Position==position&&AppWindow.Size==size,"Windowed bounds must restore without drift");
                Check(TitleRow.ActualHeight==44&&ExtendsContentIntoTitleBar,"Windowed title row and caption must restore");
                results.Add(new{scenario="restored",theme=theme.ToString(),maximized,repeat,windowStyle=restored});
            }
        }
    }
    [StructLayout(LayoutKind.Sequential)]private struct ScreenPoint{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct ClientBounds{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetClientRect(IntPtr hwnd,out ClientBounds rect);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool ClientToScreen(IntPtr hwnd,ref ScreenPoint point);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
}
