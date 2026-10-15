using System.Runtime.InteropServices;
using Windows.Management.Deployment;
using Windows.System;

namespace Luma.App;

[StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
internal struct DisplayInfo
{
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Device;
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Name;
    public int HdrActive,SdrWhiteKnown;
    public float Peak,FullFrame,Minimum,SdrWhite;
    internal string DisplayName=>string.IsNullOrWhiteSpace(Name)?Device:Name;
    internal static string Nits(float value)=>float.IsFinite(value)&&value>0?$"{value:0.#} nits":"未报告";
}

internal static class HdrSetup
{
    internal static readonly Uri DisplaySettings=new("ms-settings:display");
    internal static readonly Uri CalibrationStore=new("ms-windows-store://pdp/?ProductId=9N7F2SM5D1LR");
    internal static readonly Uri CalibrationWeb=new("https://apps.microsoft.com/detail/9N7F2SM5D1LR");
    internal const string CalibrationFamily="MicrosoftCorporationII.WindowsHDRCalibration_8wekyb3d8bbwe";
    internal static int DisplayReads;
    [DllImport("Luma.Native.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int LumaGetDisplayInfo(IntPtr hwnd,out DisplayInfo info);
    internal static DisplayInfo ReadDisplay(IntPtr hwnd){Interlocked.Increment(ref DisplayReads);Native.Check(LumaGetDisplayInfo(hwnd,out var info));return info;}
    internal static async Task<bool> OpenCalibrationAsync()=>await OpenCalibrationAsync(async()=>{
        // Only enumerate packages when the user asks to open calibration.
        var package=await Task.Run(()=>new PackageManager().FindPackagesForUser("").FirstOrDefault(p=>p.Id.FamilyName==CalibrationFamily));
        if(package is null)return false;
        var entries=await package.GetAppListEntriesAsync();var app=entries.FirstOrDefault(e=>e.AppUserModelId==CalibrationFamily+"!App");
        return app is not null&&await app.LaunchAsync();
    },uri=>Launcher.LaunchUriAsync(uri).AsTask());
    internal static async Task<bool> OpenCalibrationAsync(Func<Task<bool>> installed,Func<Uri,Task<bool>> launch)
    {
        try{if(await installed())return true;}catch(Exception ex){App.Log("HDR calibration activation: "+ex.Message);}
        foreach(var uri in new[]{CalibrationStore,CalibrationWeb})try{if(await launch(uri))return true;}catch(Exception ex){App.Log("HDR calibration link: "+ex.Message);}
        return false;
    }
}
