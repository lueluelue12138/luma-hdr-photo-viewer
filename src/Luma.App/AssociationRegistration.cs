using Luma.Core;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace Luma.App;
internal static class AssociationRegistration
{
    // Only advertises supported handlers. Windows keeps the user's chosen defaults.
    public static Uri SettingsUri()
    {
        try
        {
            var family=Package.Current.Id.FamilyName;
            return new("ms-settings:defaultapps?registeredAUMID="+Uri.EscapeDataString(family+"!App"));
        }
        catch(Exception ex)when(ex is InvalidOperationException or COMException){}
        string exe=Environment.ProcessPath??Path.Combine(AppContext.BaseDirectory,"Luma.App.exe");
        const string capabilityPath=@"Software\Luma\Capabilities";
        using(var capabilities=Registry.CurrentUser.CreateSubKey(capabilityPath))
        {
            capabilities.SetValue("ApplicationName","Luma");capabilities.SetValue("ApplicationDescription","Windows 原生图库和 HDR 图片查看器");capabilities.SetValue("ApplicationIcon",$"\"{exe}\",0");
            using var associations=capabilities.CreateSubKey("FileAssociations");
            foreach(string extension in SupportedFormats.Extensions)associations.SetValue(extension,"Luma.Photo");
        }
        using(var applications=Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))applications.SetValue("Luma",capabilityPath);
        using(var progId=Registry.CurrentUser.CreateSubKey(@"Software\Classes\Luma.Photo"))
        {
            progId.SetValue("","Luma 照片");using(var icon=progId.CreateSubKey("DefaultIcon"))icon.SetValue("",$"\"{exe}\",0");
            using(var command=progId.CreateSubKey(@"shell\open\command"))command.SetValue("",$"\"{exe}\" \"%1\"");
        }
        foreach(string extension in SupportedFormats.Extensions)
        {
            using var openWith=Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids");openWith.SetValue("Luma.Photo",Array.Empty<byte>(),RegistryValueKind.None);
        }
        SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);
        return new("ms-settings:defaultapps?registeredAppUser=Luma");
    }
    [DllImport("shell32.dll")]private static extern void SHChangeNotify(uint eventId,uint flags,IntPtr item1,IntPtr item2);
}
