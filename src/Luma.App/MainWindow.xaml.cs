using Luma.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using System.Diagnostics;

namespace Luma.App;
public sealed partial class MainWindow : Window
{
    private List<Album> albums=[];private List<Photo> photos=[];
    private string page="albums",sort="name";private long albumId;private int generation;private bool ready,closed,scanning;
    private CancellationTokenSource? searchCancellation;private readonly CancellationTokenSource lifetime=new();
    private readonly List<ViewerWindow> viewers=[];
    internal TaskCompletionSource VerificationReady {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public MainWindow()
    {
        InitializeComponent();ExtendsContentIntoTitleBar=true;SetTitleBar(TitleDrag);SystemBackdrop=new MicaBackdrop();
        WindowLayout.Configure(this,Root);
        App.RegisterGallery(this);
        WindowLayout.SizeForDpi(this,Root,1160,800);AppWindow.TitleBar.PreferredHeightOption=TitleBarHeightOption.Standard;AppWindow.TitleBar.ButtonBackgroundColor=Colors.Transparent;AppWindow.TitleBar.ButtonInactiveBackgroundColor=Colors.Transparent;
        Root.Loaded+=Loaded;Root.SizeChanged+=(_,_)=>{bool compact=Root.ActualWidth<950;Nav.PaneDisplayMode=compact?NavigationViewPaneDisplayMode.LeftCompact:NavigationViewPaneDisplayMode.Left;Nav.IsPaneToggleButtonVisible=compact;ImportLabel.Visibility=Root.ActualWidth<720?Visibility.Collapsed:Visibility.Visible;OpenPhotoLabel.Visibility=Root.ActualWidth<950?Visibility.Collapsed:Visibility.Visible;SortBox.Visibility=Root.ActualWidth<650||page is "albums" or "settings" or "recent"?Visibility.Collapsed:Visibility.Visible;Gallery.Padding=new Thickness(Root.ActualWidth<720?16:28,22,Root.ActualWidth<720?16:28,16);Search.Width=Math.Min(370,Math.Max(170,Root.ActualWidth-300));Search.HorizontalAlignment=Root.ActualWidth<800?HorizontalAlignment.Left:HorizontalAlignment.Center;Search.Margin=Root.ActualWidth<800?new Thickness(130,0,0,0):new Thickness(0);};Closed+=(_,_)=>{closed=true;lifetime.Cancel();searchCancellation?.Cancel();App.Indexer.FolderChanged-=FolderChanged;};
        App.Indexer.FolderChanged+=FolderChanged;
    }
    private async void Loaded(object sender,RoutedEventArgs e)
    {
        if(closed)return;
        Root.Loaded-=Loaded;await WindowLayout.RevealAsync(this,Root);if(closed)return;ThemeBox.SelectedIndex=Root.RequestedTheme switch{ElementTheme.Light=>1,ElementTheme.Dark=>2,_=>0};ready=true;Nav.SelectedItem=AlbumsItem;await ReloadAsync();VerificationReady.TrySetResult();
        var cli=Environment.GetCommandLineArgs();int import=Array.IndexOf(cli,"--import");
        if(import>=0&&import+1<cli.Length&&Directory.Exists(cli[import+1]))await ImportAsync(cli[import+1]);
        else _=RefreshAllAsync();
    }
    private async Task ReloadAsync()
    {
        int g=++generation;var all=await App.Store.AlbumsAsync();if(closed||g!=generation)return;
        albums=all;
        ready=false;
        while(Nav.MenuItems.Count>5)Nav.MenuItems.RemoveAt(Nav.MenuItems.Count-1);
        foreach(var a in albums)Nav.MenuItems.Add(new NavigationViewItem{Content=a.Name,Tag=a.Id,Icon=new FontIcon{Glyph="\uE8B7"}});
        if(page=="album")Nav.SelectedItem=Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(n=>n.Tag is long id&&id==albumId);
        ready=true;
        if(page=="settings")return;
        if(page=="albums")
        {
            var filtered=albums.Where(a=>a.Name.Contains(Search.Text,StringComparison.CurrentCultureIgnoreCase)).ToList();AlbumsRepeat.ItemsSource=filtered;AlbumsRepeat.Visibility=Visibility.Visible;PhotosRepeat.Visibility=Visibility.Collapsed;PageTitle.Text="相册";CountText.Text=$"{filtered.Count} 个相册";EmptyState.Visibility=filtered.Count==0?Visibility.Visible:Visibility.Collapsed;SortBox.Visibility=Visibility.Collapsed;
        }
        else
        {
            var a=albums.FirstOrDefault(a=>a.Id==albumId);var scope=page switch{"favorites"=>PhotoScope.Favorites,"recent"=>PhotoScope.Recent,"album"=>PhotoScope.Album,_=>PhotoScope.All};
            var result=await App.Store.PhotosAsync(scope,albumId,Search.Text,sort);if(closed||g!=generation)return;photos=result;PhotosRepeat.ItemsSource=photos;PhotosRepeat.Visibility=Visibility.Visible;AlbumsRepeat.Visibility=Visibility.Collapsed;EmptyState.Visibility=photos.Count==0?Visibility.Visible:Visibility.Collapsed;CountText.Text=$"{photos.Count} 张照片";PageTitle.Text=page switch{"favorites"=>"收藏","recent"=>"最近打开","album"=>a?.Name??"相册",_=>"所有照片"};SortBox.Visibility=page=="recent"||Root.ActualWidth<650?Visibility.Collapsed:Visibility.Visible;FooterText.Text=a?.Offline==true?"文件夹暂时离线，重新连接后刷新即可恢复":a?.Folder??"照片保留在原文件夹";
        }
        EmptyTitle.Text=albums.Count==0?"让照片的光影，有个家":"这里还没有照片";EmptyDescription.Text=albums.Count==0?"导入一个文件夹，开始整理你的照片。\n照片始终保留在原位置。":"试试其他搜索词，或导入新的文件夹。";
    }
    private async void NavigationChanged(NavigationView sender,NavigationViewSelectionChangedEventArgs args)
    {
        if(!ready)return;
        if(args.IsSettingsSelected){page="settings";Gallery.Visibility=Visibility.Collapsed;SettingsPage.Visibility=Visibility.Visible;Motion.Enter(SettingsPage);return;}
        if(args.SelectedItem is not NavigationViewItem item)return;
        if(item.Tag is long id){page="album";albumId=id;sort=albums.FirstOrDefault(a=>a.Id==id)?.Sort??"name";SortBox.SelectedIndex=sort switch{"newest"=>1,"oldest"=>2,_=>0};}else{page=item.Tag?.ToString()??"all";albumId=0;}
        Gallery.Visibility=Visibility.Visible;SettingsPage.Visibility=Visibility.Collapsed;await ReloadAsync();Motion.Enter(Gallery);
    }
    private async void SearchChanged(AutoSuggestBox sender,AutoSuggestBoxTextChangedEventArgs args)
    {
        if(!ready)return;searchCancellation?.Cancel();searchCancellation?.Dispose();searchCancellation=new();var token=searchCancellation.Token;
        try{await Task.Delay(220,token);await ReloadAsync();}catch(OperationCanceledException){}
    }
    private async void ImportClick(object sender,RoutedEventArgs e)
    {
        try
        {
            var picker=new FolderPicker();picker.FileTypeFilter.Add("*");WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var folder=await picker.PickSingleFolderAsync();if(folder is not null)await ImportAsync(folder.Path);
        }catch(Exception ex){await ErrorAsync("无法导入文件夹",ex.Message);}
    }
    private async Task ImportAsync(string path)
    {
        long id=await App.Store.AddFolderAsync(path);var a=(await App.Store.AlbumsAsync()).First(a=>a.Id==id);ScanProgress.Visibility=Visibility.Visible;ScanProgress.IsActive=true;ScanText.Text="正在建立索引…";
        try{var result=await App.Indexer.RefreshAsync(a,lifetime.Token);ScanText.Text=result.Error is null?"":"部分文件夹无法读取，已保留已有索引";page="albums";Nav.SelectedItem=AlbumsItem;await ReloadAsync();}
        catch(OperationCanceledException){}
        finally{ScanProgress.IsActive=false;ScanProgress.Visibility=Visibility.Collapsed;}
    }
    private async Task RefreshAllAsync()
    {
        if(scanning||closed)return;scanning=true;ScanProgress.IsActive=true;ScanProgress.Visibility=Visibility.Visible;
        try{foreach(var a in await App.Store.AlbumsAsync()){ScanText.Text=$"正在刷新 {a.Name}…";var r=await App.Indexer.RefreshAsync(a,lifetime.Token);if(r.Error is not null)App.Log(r.Error);}if(!closed){ScanText.Text="";await ReloadAsync();}}
        catch(OperationCanceledException){}catch(Exception ex){App.Log(ex.ToString());ScanText.Text="刷新失败，可稍后重试";}
        finally{scanning=false;ScanProgress.IsActive=false;ScanProgress.Visibility=Visibility.Collapsed;}
    }
    private void FolderChanged(long id)=>DispatcherQueue.TryEnqueue(async()=>{if(closed)return;var a=albums.FirstOrDefault(a=>a.Id==id);if(a is null)return;try{await App.Indexer.RefreshAsync(a,lifetime.Token);await ReloadAsync();}catch(OperationCanceledException){}catch(Exception ex){App.Log(ex.Message);}});
    private void RefreshClick(object sender,RoutedEventArgs e)=>_=RefreshAllAsync();
    private async void SortChanged(object sender,SelectionChangedEventArgs e){if(!ready)return;sort=(SortBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()??"name";if(page=="album")await App.Store.SetAlbumAsync(albumId,sort:sort);await ReloadAsync();}
    private void AlbumClick(object sender,RoutedEventArgs e){if(((Button)sender).Tag is Album a){var nav=Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(n=>n.Tag is long id&&id==a.Id);Nav.SelectedItem=nav;}}
    private void CardEntered(object sender,PointerRoutedEventArgs e)=>Motion.Hover((FrameworkElement)sender,true);
    private void CardExited(object sender,PointerRoutedEventArgs e)=>Motion.Hover((FrameworkElement)sender,false);
    private void PhotoClick(object sender,RoutedEventArgs e)
    {
        if(((Button)sender).Tag is not Photo p)return;var v=new ViewerWindow(p.Path,photos,this);viewers.Add(v);v.Closed+=async(_,_)=>{viewers.Remove(v);await ReloadAsync();};v.Activate();
    }
    private async Task ErrorAsync(string title,string message){if(closed)return;await new ContentDialog{XamlRoot=Root.XamlRoot,Title=title,Content=message,CloseButtonText="关闭"}.ShowAsync();}
    private void AlbumRightTapped(object sender,RightTappedRoutedEventArgs e)
    {
        if(((Button)sender).Tag is not Album a)return;var menu=new MenuFlyout();
        Add(menu,"重命名相册",async()=>{var box=new TextBox{Text=a.Name,PlaceholderText="相册名称",MaxLength=120};var d=new ContentDialog{XamlRoot=Root.XamlRoot,Title="重命名相册",Content=box,PrimaryButtonText="保存",CloseButtonText="取消",DefaultButton=ContentDialogButton.Primary};if(await d.ShowAsync()==ContentDialogResult.Primary&&!string.IsNullOrWhiteSpace(box.Text)){await App.Store.SetAlbumAsync(a.Id,name:box.Text.Trim());await ReloadAsync();}});
        Add(menu,"刷新",async()=>{await App.Indexer.RefreshAsync(a);await ReloadAsync();});
        Add(menu,"在资源管理器中打开",()=>{OpenFolder(a.Folder);return Task.CompletedTask;});
        menu.Items.Add(new MenuFlyoutSeparator());Add(menu,"从图库移除（保留原文件）",async()=>{var d=new ContentDialog{XamlRoot=Root.XamlRoot,Title=$"移除“{a.Name}”？",Content="只移除相册索引，原始照片仍保留在文件夹中。",PrimaryButtonText="移除相册",CloseButtonText="取消"};if(await d.ShowAsync()==ContentDialogResult.Primary){App.Indexer.Forget(a.Id);await App.Store.RemoveAlbumAsync(a.Id);await ReloadAsync();}});menu.ShowAt((FrameworkElement)sender);e.Handled=true;
    }
    private void PhotoRightTapped(object sender,RightTappedRoutedEventArgs e)
    {
        if(((Button)sender).Tag is not Photo p)return;var menu=new MenuFlyout();Add(menu,p.Favorite?"取消收藏":"收藏",async()=>{await App.Store.ToggleFavoriteAsync(p);await ReloadAsync();});if(page=="album")Add(menu,"设为相册封面",async()=>{await App.Store.SetAlbumAsync(albumId,cover:p.Path);await ReloadAsync();});Add(menu,"在资源管理器中显示",()=>{Process.Start(new ProcessStartInfo("explorer.exe",$"/select,\"{p.Path}\""){UseShellExecute=true});return Task.CompletedTask;});menu.ShowAt((FrameworkElement)sender);e.Handled=true;
    }
    private void Add(MenuFlyout menu,string text,Func<Task> action){var i=new MenuFlyoutItem{Text=text};i.Click+=async(_,_)=>{try{await action();}catch(Exception ex){await ErrorAsync("操作未完成",ex.Message);}};menu.Items.Add(i);}
    private static void OpenFolder(string folder)=>Process.Start(new ProcessStartInfo("explorer.exe",$"\"{folder}\""){UseShellExecute=true});
    private async void ThemeChanged(object sender,SelectionChangedEventArgs e){if(!ready)return;var theme=(ThemeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()??"system";await App.Store.SetSettingAsync("theme",theme);App.Theme=await App.ThemeAsync();Root.RequestedTheme=App.Theme;foreach(var v in viewers)v.ApplyTheme(Root.RequestedTheme);}
    private async void DefaultAppsClick(object sender,RoutedEventArgs e)
    {
        try
        {
            var uri=AssociationRegistration.SettingsUri();
            await Task.Delay(1000);
            if(!await Launcher.LaunchUriAsync(uri))await ErrorAsync("无法打开默认应用设置","请在 Windows 设置的“应用 → 默认应用”中搜索 Luma。");
        }
        catch(Exception ex){await ErrorAsync("无法打开默认应用设置",ex.Message);}
    }
    private async void OpenPhotoClick(object sender,RoutedEventArgs e)
    {
        try{var picker=new FileOpenPicker();foreach(var ext in SupportedFormats.Extensions)picker.FileTypeFilter.Add(ext);WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var f=await picker.PickSingleFileAsync();if(f is not null)new ViewerWindow(f.Path,null,this).Activate();}catch(Exception ex){await ErrorAsync("无法打开照片",ex.Message);}
    }
    private void DragOver(object sender,DragEventArgs e)
    {
        if(e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)){e.AcceptedOperation=Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;e.DragUIOverride.Caption="打开照片或导入文件夹";}
    }
    private async void DropItems(object sender,DragEventArgs e)
    {
        try{foreach(var item in await e.DataView.GetStorageItemsAsync()){if(item is Windows.Storage.StorageFolder)await ImportAsync(item.Path);else if(SupportedFormats.IsSupported(item.Path)){new ViewerWindow(item.Path,null,this).Activate();break;}}}catch(Exception ex){await ErrorAsync("无法打开拖入的文件",ex.Message);}
    }
    private void DataFolderClick(object sender,RoutedEventArgs e)=>OpenFolder(App.DataDirectory);
}
