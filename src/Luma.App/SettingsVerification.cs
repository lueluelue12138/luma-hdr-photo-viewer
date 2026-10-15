using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;

namespace Luma.App;
public sealed partial class MainWindow
{
    internal async Task VerificationSettingsAsync()
    {
        var results=new List<object>();
        void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
        void Save(bool complete)=>File.WriteAllText(Path.Combine(App.DataDirectory,"settings.json"),JsonSerializer.Serialize(new{complete,results},new JsonSerializerOptions{WriteIndented=true}));
        try{
            await VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(20));await StartupRefresh;
            Check(settingsPanel is null&&HdrSetup.DisplayReads==0,"Gallery startup must not create settings or query displays");
            Nav.SelectedItem=Nav.SettingsItem;var panel=settingsPanel??throw new InvalidOperationException("Settings did not open");
            var watch=Stopwatch.StartNew();await panel.DisplayRefresh;watch.Stop();
            results.Add(new{scenario="live-display",waitMilliseconds=watch.Elapsed.TotalMilliseconds,state=panel.VerificationDisplay});
            int reads=HdrSetup.DisplayReads;var position=AppWindow.Position;
            for(int n=0;n<20;n++)AppWindow.Move(new(position.X+n,position.Y));
            await Task.Delay(700);await panel.DisplayRefresh;
            Check(HdrSetup.DisplayReads-reads<=1,"Window moves must coalesce rather than querying every drag event");
            results.Add(new{scenario="debounced-moves",moves=20,queries=HdrSetup.DisplayReads-reads});
            await panel.VerificationDisplayStatesAsync();
            results.Add(new{scenario="display-states",hdrPeak3675Unclamped=true,sdrHidesPeak=true,unavailableClearsValues=true,invalidNumbersNotFabricated=true});
            var called=new List<Uri>();
            Check(await HdrSetup.OpenCalibrationAsync(()=>Task.FromResult(true),u=>{called.Add(u);return Task.FromResult(true);})&&called.Count==0,"An installed app must not also open the Store");
            Check(await HdrSetup.OpenCalibrationAsync(()=>Task.FromResult(false),u=>{called.Add(u);return Task.FromResult(true);})&&called.SequenceEqual(new[]{HdrSetup.CalibrationStore}),"A missing app must open the product's Store page");
            called.Clear();Check(await HdrSetup.OpenCalibrationAsync(()=>throw new InvalidOperationException("Fixture activation failure"),u=>{called.Add(u);return Task.FromResult(u.Scheme=="https");})&&called.SequenceEqual(new[]{HdrSetup.CalibrationStore,HdrSetup.CalibrationWeb}),"Activation errors and unavailable Store must fall back to the product webpage");
            Check(!await HdrSetup.OpenCalibrationAsync(()=>Task.FromResult(false),_=>Task.FromResult(false)),"Complete activation failure must be reported");
            results.Add(new{scenario="calibration-routing",installed=true,missing=true,activationException=true,storeFailureWebFallback=true,totalFailure=true,noExternalAppsLaunched=true});
            foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark}){
                Root.RequestedTheme=theme;
                foreach(int width in new[]{1160,600}){
                    AppWindow.Resize(new((int)(width*Root.XamlRoot.RasterizationScale),(int)(800*Root.XamlRoot.RasterizationScale)));await Task.Delay(350);await panel.DisplayRefresh;
                    var layout=await panel.VerificationRenderAsync($"settings-{theme.ToString().ToLowerInvariant()}-{width}.png");
                    results.Add(new{scenario="layout",theme=theme.ToString(),windowWidth=width,layout});
                }
            }
            await panel.VerificationGuideRenderAsync();
            Nav.SelectedItem=AlbumsItem;reads=HdrSetup.DisplayReads;
            for(int n=0;n<12;n++)AppWindow.Move(new(position.X+n,position.Y));
            await Task.Delay(600);Check(HdrSetup.DisplayReads==reads,"Hidden settings must not query monitors on moves");
            results.Add(new{scenario="hidden-settings",moves=12,queries=0});
            Nav.SelectedItem=Nav.SettingsItem;await panel.DisplayRefresh;
            Check(HdrSetup.DisplayReads==reads+1,"Returning to settings must refresh stale monitor data");
            results.Add(new{scenario="reopen",freshQuery=true,state=panel.VerificationDisplay});Save(true);
        }catch{Save(false);throw;}finally{Close();}
    }
}
public sealed partial class SettingsPanel
{
    internal object VerificationDisplay=>new{name=DisplayNameText.Text,hdr=HdrStateText.Text,peak=PeakText.Text,white=WhiteText.Text,hint=DisplayHintText.Text};
    internal Task VerificationDisplayStatesAsync()
    {
        void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
        ShowDisplay(new DisplayInfo{Device="Fixture",Name="Fixture HDR",HdrActive=1,Peak=3675,FullFrame=650,SdrWhiteKnown=1,SdrWhite=203});
        Check(PeakText.Text=="3675 nits"&&WhiteText.Text=="203 nits","Reported monitor peaks must not be capped at the RTX tuning limit");
        ShowDisplay(new DisplayInfo{Device="SDR",Peak=1600});Check(PeakText.Text=="—"&&WhiteText.Text=="—"&&HdrStateText.Text.Contains("未开启"),"SDR must not advertise an active HDR peak");
        ShowDisplay(null);Check(PeakText.Text=="—"&&WhiteText.Text=="—"&&DisplayNameText.Text.Contains("不可用"),"Failure must not leave another monitor's data");
        foreach(float n in new[]{0,-1,float.NaN,float.PositiveInfinity})Check(DisplayInfo.Nits(n)=="未报告","Invalid metadata must not be invented");
        RefreshDisplay(displayWindow);return DisplayRefresh;
    }
    internal async Task<object> VerificationRenderAsync(string file,bool resetTop=true)
    {
        if(resetTop)SettingsScroll.ChangeView(null,0,null,true);await Task.Delay(200);UpdateLayout();
        void CheckBounds(FrameworkElement e){var p=e.TransformToVisual(SettingsScroll).TransformPoint(new(0,0));if(p.X<0||p.X+e.ActualWidth>SettingsScroll.ActualWidth+1)throw new InvalidOperationException("Setting escapes its horizontal viewport: "+e.Name);}
        foreach(var e in new FrameworkElement[]{SettingsContent,HdrSettingsButton,CalibrationButton,ThemeBox,PeakText,WhiteText})CheckBounds(e);
        var target=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await target.RenderAsync(SettingsScroll);var pixels=await target.GetPixelsAsync();
        using var stream=File.OpenWrite(Path.Combine(App.DataDirectory,file));using var random=stream.AsRandomAccessStream();
        var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,random);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)target.PixelWidth,(uint)target.PixelHeight,96,96,pixels.ToArray());await encoder.FlushAsync();
        return new{width=SettingsScroll.ActualWidth,viewport=SettingsScroll.ViewportHeight,content=SettingsContent.ActualWidth,scrollable=SettingsScroll.ScrollableHeight};
    }
    internal async Task VerificationGuideRenderAsync()
    {
        CalibrationGuide.IsExpanded=true;await Task.Delay(250);CalibrationGuide.StartBringIntoView();await Task.Delay(200);
        await VerificationRenderAsync("settings-guide-dark-600.png",false);
        SettingsScroll.ChangeView(null,SettingsScroll.ScrollableHeight,null,true);await Task.Delay(200);
        await VerificationRenderAsync("settings-about-dark-600.png",false);
    }
}
