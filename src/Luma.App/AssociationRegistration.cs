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
            using var application=progId.CreateSubKey("Application");
            application.SetValue("ApplicationName","Luma");
            application.SetValue("ApplicationDescription","Windows 原生图库和 HDR 图片查看器");
            application.SetValue("ApplicationIcon",$"\"{exe}\",0");
        }
        using(var application=Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\Luma.App.exe"))
        {
            application.SetValue("FriendlyAppName","Luma");
            using(var icon=application.CreateSubKey("DefaultIcon"))icon.SetValue("",$"\"{exe}\",0");
            using(var command=application.CreateSubKey(@"shell\open\command"))command.SetValue("",$"\"{exe}\" \"%1\"");
            using var types=application.CreateSubKey("SupportedTypes");
            foreach(string extension in SupportedFormats.Extensions)types.SetValue(extension,"");
        }
        foreach(string extension in SupportedFormats.Extensions)
        {
            using var openWith=Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids");openWith.SetValue("Luma.Photo",Array.Empty<byte>(),RegistryValueKind.None);
        }
        // The Start menu entry also lets the Windows app resolver identify this portable app.
        var shortcutPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Luma.lnk");
        var shellType=Type.GetTypeFromProgID("WScript.Shell",throwOnError:true)!;
        object shell=Activator.CreateInstance(shellType)!;
        object? shortcut=null;
        try
        {
            dynamic shellLink=((dynamic)shell).CreateShortcut(shortcutPath);
            shortcut=shellLink;
            shellLink.TargetPath=exe;
            shellLink.Arguments="";
            shellLink.WorkingDirectory=AppContext.BaseDirectory;
            shellLink.Description="Luma — Windows 原生图库";
            shellLink.IconLocation=exe+",0";
            shellLink.Save();
        }
        finally
        {
            if(shortcut is not null)Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
        // Flush the association notification before Settings enumerates the new handler.
        SHChangeNotify(0x08000000,0x1003,IntPtr.Zero,IntPtr.Zero);
        return new("ms-settings:defaultapps?registeredAppUser=Luma");
    }
    [DllImport("shell32.dll")]private static extern void SHChangeNotify(uint eventId,uint flags,IntPtr item1,IntPtr item2);
}
