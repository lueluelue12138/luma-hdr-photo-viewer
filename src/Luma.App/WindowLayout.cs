using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
namespace Luma.App;
internal static class WindowLayout
{
    public static void Configure(Window window,FrameworkElement root)
    {
        var icon=System.IO.Path.Combine(AppContext.BaseDirectory,"Assets","Luma.ico");
        if(System.IO.File.Exists(icon))window.AppWindow.SetIcon(icon);
        void UpdateCaption()
        {
            bool dark=root.ActualTheme==ElementTheme.Dark;
            var bar=window.AppWindow.TitleBar;
            bar.ButtonForegroundColor=dark?Microsoft.UI.Colors.White:Microsoft.UI.Colors.Black;
            bar.ButtonInactiveForegroundColor=dark?Microsoft.UI.Colors.Gray:Windows.UI.Color.FromArgb(255,110,110,110);
            bar.ButtonBackgroundColor=bar.ButtonInactiveBackgroundColor=Microsoft.UI.Colors.Transparent;
        }
        root.ActualThemeChanged+=(_,_)=>UpdateCaption();root.Loaded+=(_,_)=>UpdateCaption();
    }
    public static void SizeForDpi(Window window,FrameworkElement root,int width,int height)
    {
        double scale=root.XamlRoot?.RasterizationScale??1;
        var area=DisplayArea.GetFromWindowId(window.AppWindow.Id,DisplayAreaFallback.Primary).WorkArea;
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Min(width*scale,area.Width*.92),(int)Math.Min(height*scale,area.Height*.92)));
    }
}
