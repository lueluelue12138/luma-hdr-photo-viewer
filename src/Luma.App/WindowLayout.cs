using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
namespace Luma.App;
internal static class WindowLayout
{
    public static void Configure(Window window,FrameworkElement root)
    {
        root.RequestedTheme=App.Theme;
        int cloak=1;DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(window),13,ref cloak,4);
        var icon=System.IO.Path.Combine(AppContext.BaseDirectory,"Assets","Luma.ico");
        if(System.IO.File.Exists(icon))window.AppWindow.SetIcon(icon);
        bool closed=false;window.Closed+=(_,_)=>closed=true;
        void UpdateCaption()
        {
            // XAML can notify theme/layout while its native window is tearing
            // down, before or after Closed. The AppWindow may already be gone.
            if(closed||window.AppWindow?.TitleBar is not {} bar)return;
            bool dark=root.ActualTheme==ElementTheme.Dark;
            bar.ButtonForegroundColor=dark?Microsoft.UI.Colors.White:Microsoft.UI.Colors.Black;
            bar.ButtonInactiveForegroundColor=dark?Microsoft.UI.Colors.Gray:Windows.UI.Color.FromArgb(255,110,110,110);
            bar.ButtonBackgroundColor=bar.ButtonInactiveBackgroundColor=Microsoft.UI.Colors.Transparent;
        }
        root.ActualThemeChanged+=(_,_)=>UpdateCaption();root.Loaded+=(_,_)=>UpdateCaption();
    }
    public static async Task RevealAsync(Window window,FrameworkElement root)
    {
        if(window.AppWindow is null)return;
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int frames=0;
        bool closed=false;void Closed(object sender,WindowEventArgs args){closed=true;ready.TrySetCanceled();}
        window.Closed+=Closed;
        void Rendered(object? sender,object args){if(++frames>=2)ready.TrySetResult();}
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering+=Rendered;
        try{await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));}
        catch(TimeoutException){App.Log("Initial composition frame timed out; revealing prepared shell");}
        catch(OperationCanceledException){return;}
        finally{Microsoft.UI.Xaml.Media.CompositionTarget.Rendering-=Rendered;window.Closed-=Closed;}
        if(closed||root.XamlRoot is null||window.AppWindow is null)return;
        window.AppWindow.Hide();int cloak=0;DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(window),13,ref cloak,4);window.Activate();
        App.Log($"Window revealed {window.Title}: {window.AppWindow.Size.Width}x{window.AppWindow.Size.Height}, composition frames {frames}");
    }
    public static void SizeForDpi(Window window,FrameworkElement root,int width,int height)
    {
        double scale=GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window))/96.0;
        var area=DisplayArea.GetFromWindowId(window.AppWindow.Id,DisplayAreaFallback.Primary).WorkArea;
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Min(width*scale,area.Width*.92),(int)Math.Min(height*scale,area.Height*.92)));
    }
    [DllImport("user32.dll")]private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
}
