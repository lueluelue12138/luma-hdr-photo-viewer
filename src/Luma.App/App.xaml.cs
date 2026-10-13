using Luma.Core;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Luma.App;
public partial class App : Application
{
    public static LibraryStore Store { get; private set; } = null!;
    public static FolderIndexer Indexer { get; private set; } = null!;
    public static string DataDirectory { get; private set; } = "";
    private Window? window;
    private static MainWindow? gallery;
    public static void RegisterGallery(MainWindow value){gallery=value;value.Closed+=(_,_)=>{if(gallery==value)gallery=null;};}
    public static void ShowGallery(){if(gallery is null)gallery=new MainWindow();gallery.Activate();}
    public static ElementTheme Theme { get; internal set; } = ElementTheme.Default;
    public App() { InitializeComponent(); UnhandledException+=(_,e)=>Log(e.Exception.ToString()); }
    public static void Log(string message)
    {
        try { Directory.CreateDirectory(DataDirectory); File.AppendAllText(Path.Combine(DataDirectory,"luma.log"),$"{DateTimeOffset.Now:O} {message}\n"); }catch(IOException){}
    }
    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var cli=Environment.GetCommandLineArgs().Skip(1).ToArray();
        var test=Array.IndexOf(cli,"--test-library");
        DataDirectory=test>=0&&test+1<cli.Length?Path.GetFullPath(cli[test+1]):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Luma");
        try
        {
            Store=new(Path.Combine(DataDirectory,"library.db")); await Store.InitializeAsync(); Indexer=new(Store);Theme=await ThemeAsync();await ViewerPreferences.LoadAsync();
            Log($"Launch {Environment.ProcessId}");
            string? file=LaunchArguments.PhotoPath(cli);
            if(test>=0&&cli.Contains("--verify-device-cache"))
            {
                window=new MainWindow();window.Activate();await RenderDeviceCacheVerification.RunAsync((MainWindow)window,file??throw new ArgumentException("Verification requires a photo"));return;
            }
            if(test>=0&&cli.Contains("--verify-photo-performance"))
            {
                window=new MainWindow();window.Activate();await PhotoPerformanceVerification.RunAsync((MainWindow)window,file??throw new ArgumentException("Verification requires a photo"));return;
            }
            if(test>=0&&(cli.Contains("--verify-gallery-performance")||cli.Contains("--verify-album-viewer")))
            {
                Thumbnail.PreferWic=!cli.Contains("--shell-control");
                int source=Array.IndexOf(cli,"--gallery-source");
                string folder=source>=0&&source+1<cli.Length?Path.GetFullPath(cli[source+1]):Path.Combine(DataDirectory,"source");
                window=new MainWindow();window.Activate();
                if(cli.Contains("--verify-album-viewer"))await ((MainWindow)window).VerificationAlbumViewerAsync(folder);
                else await ((MainWindow)window).VerificationLargeGalleryAsync(folder);
                return;
            }
            if(test>=0&&cli.Contains("--verify-startup"))
            {
                window=file is null?new MainWindow():new ViewerWindow(file);window.Activate();await StartupVerification.RunAsync(window);return;
            }
            if(test>=0&&cli.Contains("--verify-viewer-performance"))
            {
                window=new MainWindow();window.Activate();await ViewerPerformanceVerification.RunAsync((MainWindow)window,file??throw new ArgumentException("Verification requires a photo"));return;
            }
            if(test>=0&&cli.Contains("--verify-viewer-interaction"))
            {
                window=new MainWindow();window.Activate();await ViewerVerification.RunInteractionAsync((MainWindow)window,file??throw new ArgumentException("Verification requires a photo"));return;
            }
            if(test>=0&&cli.Contains("--verify-viewer-preferences"))
            {
                window=new MainWindow();window.Activate();await ViewerVerification.RunPreferencesAsync((MainWindow)window,file??throw new ArgumentException("Verification requires a photo"));return;
            }
            if(test>=0&&cli.Contains("--verify-viewer-lifecycle"))
            {
                window=new MainWindow();window.Activate();await ViewerVerification.RunAsync((MainWindow)window,file??throw new ArgumentException("Verification requires a photo"));return;
            }
            try {var activation=AppInstance.GetCurrent().GetActivatedEventArgs();if(activation.Kind==ExtendedActivationKind.File&&activation.Data is FileActivatedEventArgs f)file=f.Files.FirstOrDefault()?.Path;}catch(Exception e){Log("Activation: "+e.Message);}
            if(file is not null) window=new ViewerWindow(file);
            else window=new MainWindow();
            window.Activate();
        }
        catch(Exception e){Log(e.ToString());throw;}
    }
    public static async Task<ElementTheme> ThemeAsync() => await Store.GetSettingAsync("theme") switch {"light"=>ElementTheme.Light,"dark"=>ElementTheme.Dark,_=>ElementTheme.Default};
}
