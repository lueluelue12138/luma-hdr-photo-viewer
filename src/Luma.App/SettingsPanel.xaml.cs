using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Luma.App;

// Constructed on the first settings navigation. The gallery's initial layout
// need not create templates/text/buttons belonging to an invisible page.
public sealed partial class SettingsPanel : UserControl
{
    private IntPtr displayWindow;
    private bool readingDisplay,refreshPending,displayActive;
    internal void DeactivateDisplay(){displayActive=false;refreshPending=false;}
    internal Task DisplayRefresh {get;private set;}=Task.CompletedTask;
    public SettingsPanel(){InitializeComponent();VersionText.Text=$"Luma {typeof(App).Assembly.GetName().Version?.ToString(3)} · Windows 原生图库";}
    internal void RefreshDisplay(IntPtr hwnd)
    {
        displayWindow=hwnd;displayActive=true;
        if(readingDisplay){refreshPending=true;return;}
        DisplayRefresh=RefreshDisplayAsync();
    }
    private async Task RefreshDisplayAsync()
    {
        readingDisplay=true;RefreshDisplayButton.IsEnabled=false;
        try{
            do{
                refreshPending=false;
                try{var info=await Task.Run(()=>HdrSetup.ReadDisplay(displayWindow));if(displayActive)ShowDisplay(info);}
                catch(Exception ex){App.Log("Settings display query: "+ex.Message);if(displayActive)ShowDisplay(null);}
            }while(refreshPending);
        }finally{readingDisplay=false;if(displayActive)RefreshDisplayButton.IsEnabled=true;}
    }
    private void ShowDisplay(DisplayInfo? value)
    {
        if(value is not {} info){DisplayNameText.Text="当前显示器信息暂不可用";HdrStateText.Text="无法读取 Windows HDR 状态";PeakText.Text=WhiteText.Text="—";FullFrameText.Text="";DisplayHintText.Text="远程会话或显示配置切换时可能无法读取。请到系统设置确认，或稍后刷新。";return;}
        DisplayNameText.Text=info.DisplayName;ToolTipService.SetToolTip(DisplayNameText,info.Device);
        bool active=info.HdrActive!=0;HdrStateText.Text=active?"Windows HDR · 已开启":"Windows HDR · 未开启";
        PeakText.Text=active?DisplayInfo.Nits(info.Peak):"—";WhiteText.Text=active&&info.SdrWhiteKnown!=0?DisplayInfo.Nits(info.SdrWhite):"—";
        DisplayHintText.Text=active?"当前窗口所在屏幕 · 系统报告值，非实测。移到其他屏幕后会更新。":"先在 Windows 中开启此屏幕的 HDR，再查看峰值与 SDR 白色亮度。";
        FullFrameText.Text=active?$"Windows 报告的全屏亮度：{DisplayInfo.Nits(info.FullFrame)}。小面积峰值与全屏亮度通常不同。":"开启 HDR 后可查看系统报告的全屏亮度。";
    }
    private void RefreshDisplayClick(object sender,RoutedEventArgs e)=>RefreshDisplay(displayWindow);
    private async void HdrSettingsClick(object sender,RoutedEventArgs e)
    {
        try{if(!await Launcher.LaunchUriAsync(HdrSetup.DisplaySettings))ShowError("请手动打开 Windows 设置 → 系统 → 显示 → HDR。");}
        catch(Exception ex){App.Log(ex.Message);ShowError("请手动打开 Windows 设置 → 系统 → 显示 → HDR。");}
    }
    private async void CalibrationClick(object sender,RoutedEventArgs e)
    {
        CalibrationButton.IsEnabled=false;
        try{if(!await HdrSetup.OpenCalibrationAsync())ShowError("请在 Microsoft Store 中搜索 Windows HDR Calibration，安装后打开。");}
        finally{CalibrationButton.IsEnabled=true;}
    }
    private void ShowError(string message){ActionError.Message=message;ActionError.IsOpen=true;ActionError.StartBringIntoView();}
    internal int SelectedThemeIndex {get=>ThemeBox.SelectedIndex;set=>ThemeBox.SelectedIndex=value;}
    internal event SelectionChangedEventHandler? ThemeSelectionChanged;
    internal event RoutedEventHandler? DefaultAppsRequested;
    internal event RoutedEventHandler? DataFolderRequested;
    private void ThemeChanged(object sender,SelectionChangedEventArgs e)=>ThemeSelectionChanged?.Invoke(sender,e);
    private void DefaultAppsClick(object sender,RoutedEventArgs e)=>DefaultAppsRequested?.Invoke(sender,e);
    private void DataFolderClick(object sender,RoutedEventArgs e)=>DataFolderRequested?.Invoke(sender,e);
}
