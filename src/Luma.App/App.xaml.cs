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
            Store=new(Path.Combine(DataDirectory,"library.db")); await Store.InitializeAsync(); Indexer=new(Store);
            string? file=cli.FirstOrDefault(p=>File.Exists(p)&&SupportedFormats.IsSupported(p));
            try {var activation=AppInstance.GetCurrent().GetActivatedEventArgs();if(activation.Kind==ExtendedActivationKind.File&&activation.Data is FileActivatedEventArgs f)file=f.Files.FirstOrDefault()?.Path;}catch(Exception e){Log("Activation: "+e.Message);}
            if(file is not null) window=new ViewerWindow(file);
            else window=new MainWindow();
            window.Activate();
        }
        catch(Exception e){Log(e.ToString());throw;}
    }
    public static async Task<ElementTheme> ThemeAsync() => await Store.GetSettingAsync("theme") switch {null or "light"=>ElementTheme.Light,"dark"=>ElementTheme.Dark,_=>ElementTheme.Default};
}
