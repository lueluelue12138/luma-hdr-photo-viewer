using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Luma.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Luma.App;

// Isolated integration mode only: uses generated fixtures under --test-library.
public sealed partial class MainWindow
{
    internal async Task VerificationAlbumOrganizationAsync(bool readOnly)
    {
        var results=new List<object>();string report=Path.Combine(App.DataDirectory,readOnly?"album-reopen.json":"album-organization.json");
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        async Task Until(Func<Task<bool>> condition)
        {
            var watch=Stopwatch.StartNew();while(!await condition()){if(watch.Elapsed.TotalSeconds>15)throw new TimeoutException("Album UI operation did not settle");await Task.Delay(30);}await Task.Delay(100);
        }
        async Task SelectSort(string value)
        {
            AlbumSortBox.SelectedItem=AlbumSortBox.Items.OfType<ComboBoxItem>().First(i=>Equals(i.Tag,value));
            await Until(async()=>await App.Store.GetSettingAsync("albumOverviewSort")==value&&albums.SequenceEqual(await App.Store.AlbumsAsync(value))&&AlbumsRepeat.ItemsSource is List<Album> shown&&shown.SequenceEqual(albums));
        }
        async Task InvokePin(long id)
        {
            var album=albums.Single(a=>a.Id==id);bool expected=!album.Pinned;
            var card=AlbumsRepeat.TryGetElement(albums.FindIndex(a=>a.Id==id)) as Button??throw new InvalidOperationException("Album card not realized");
            Check(card.Tag is Album bound&&bound==album,"Actual card must bind its album for click and context actions");
            var menu=CreateAlbumMenu(album);menu.ShowAt(card);await Task.Delay(150);
            var item=(MenuFlyoutItem)menu.Items[0];Check(item.Text==(expected?"置顶相册":"取消置顶"),"Menu action must follow current pin state");
            ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();menu.Hide();
            await Until(()=>Task.FromResult(albums.Single(a=>a.Id==id).Pinned==expected));
            var nav=Nav.MenuItems.OfType<NavigationViewItem>().First(i=>i.Tag is long n&&n==id);
            Check(nav.Icon is FontIcon icon&&icon.Glyph==(expected?"\uE718":"\uE8B7"),"Sidebar must synchronize pin state");
            Check(CreateAlbumMenu(albums.Single(a=>a.Id==id),pinOnly:true).Items.Count==1,"Sidebar context menu must expose its pin action without unrelated album actions");
        }
        try
        {
            await VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));await Task.Delay(100);await StartupRefresh;
            if(readOnly)
            {
                Check(albumSort=="most"&&Equals(((ComboBoxItem)AlbumSortBox.SelectedItem).Tag,"most"),"Restart must restore overview sort before first display");
                Check(albums.Count==3&&albums[0].Name=="工作"&&albums[0].Pinned,"Restart must preserve pin and its first position");
                Check(albums.Single(a=>a.Name=="家人").Sort=="oldest","Restart must preserve independent photo sort");
                results.Add(new{scenario="new-process-reopen",sort=albumSort,albums=albums.Select(a=>new{a.Name,a.Pinned,a.Sort})});
            }
            else
            {
                Check(albums.Count==0,"Write verification requires a fresh isolated library");
                string[] names=["旅行 · 2026 国庆云南之旅的完整照片与回忆","家人","工作"];int[] counts=[3,2,1];var ids=new List<long>();
                for(int n=0;n<names.Length;n++)
                {
                    string folder=Path.Combine(App.DataDirectory,"fixtures",names[n]);Directory.CreateDirectory(folder);long id=await App.Store.AddFolderAsync(folder);ids.Add(id);
                    for(int p=0;p<counts[n];p++)File.Copy(Path.Combine(AppContext.BaseDirectory,"Assets","Square44x44Logo.png"),Path.Combine(folder,$"{p}.png"));
                    var album=(await App.Store.AlbumsAsync()).Single(a=>a.Id==id);await App.Indexer.RefreshAsync(album);
                }
                await App.Store.SetAlbumAsync(ids[1],sort:"oldest");await ReloadAsync();await Task.Delay(400);
                await InvokePin(ids[2]);Check(albums[0].Id==ids[2],"Pinned album must lead the overview");
                foreach(string key in new[]{"name-desc","newest","oldest","most","least","name"})
                {
                    await SelectSort(key);Check(albums[0].Id==ids[2],"Every ordering must keep the pin first");
                    Check(Nav.MenuItems.OfType<NavigationViewItem>().Where(i=>i.Tag is long).Select(i=>(long)i.Tag).SequenceEqual(albums.Select(a=>a.Id)),"Sidebar and overview order must agree");
                    results.Add(new{scenario="dropdown",sort=key,names=albums.Select(a=>a.Name).ToArray()});
                }
                await InvokePin(ids[2]);Check(!albums.Any(a=>a.Pinned),"Unpin must restore ordinary ordering");await InvokePin(ids[2]);
                Search.Text="旅行";await ReloadAsync();Check(AlbumsRepeat.ItemsSource is List<Album> filtered&&filtered.Count==1&&filtered[0].Id==ids[0],"Search must retain its own matches even when a different album is pinned");
                Search.Text="";await ReloadAsync();
                foreach(var item in AlbumSortBox.Items)AlbumSortBox.SelectedItem=item;
                await SelectSort("most");await albumSortSave;
                Check(await App.Store.GetSettingAsync("albumOverviewSort")=="most","Rapid selections must persist the last chosen ordering");
                results.Add(new{scenario="rapid-sort-selections",lastSelection="most",persisted=true});await Task.Delay(700);
                var items=AlbumsRepeat.ItemsSource;long loads=Thumbnail.WicLoads+Thumbnail.ShellLoads;
                await RefreshAllAsync();Check(ReferenceEquals(items,AlbumsRepeat.ItemsSource),"Unchanged refresh under nondefault sort must preserve album cards");
                Check(loads==Thumbnail.WicLoads+Thumbnail.ShellLoads,"Unchanged refresh must add no thumbnail loads");
                results.Add(new{scenario="unchanged-refresh",additionalThumbnailLoads=0,preservedCards=true});
                foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
                foreach(int width in new[]{1160,600,480})
                {
                    Root.RequestedTheme=theme;double scale=Root.XamlRoot.RasterizationScale;AppWindow.Resize(new((int)(width*scale),(int)(800*scale)));await Task.Delay(300);
                    var sortPosition=AlbumSortBox.TransformToVisual(Root).TransformPoint(new(0,0));
                    var titlePosition=PageTitle.TransformToVisual(Root).TransformPoint(new(0,0));
                    Check(AlbumSortBox.Visibility==Visibility.Visible&&sortPosition.X>=0&&sortPosition.X+AlbumSortBox.ActualWidth<=Root.ActualWidth,"Album sort must remain accessible at narrow widths");
                    Check(titlePosition.X+PageTitle.ActualWidth<=OpenPhotoButton.TransformToVisual(Root).TransformPoint(new(0,0)).X+1,"Header title must not overlap actions");
                    var pinnedCard=(Button)AlbumsRepeat.TryGetElement(0);var icon=((FrameworkElement)pinnedCard.Content).FindName("AlbumPinIcon") as FontIcon;
                    Check(icon is not null&&icon.ActualWidth>0&&icon.ActualTheme==theme,"Pinned marker must be visible and inherit the theme");
                    Check(((FrameworkElement)pinnedCard.Content).FindName("AlbumNameText") is TextBlock name&&name.Text==albums[0].Name,"Pinned card must render its name");
                    Check(((FrameworkElement)pinnedCard.Content).FindName("AlbumPhotoCountText") is TextBlock count&&count.Text==albums[0].CountLabel,"Pinned card must render its photo count");
                    Check(FindThumbnail(pinnedCard)?.DisplayedPath==albums[0].Cover,"Pinned card must retain its loaded cover");
                    var target=new RenderTargetBitmap();await target.RenderAsync(Root);var pixels=await target.GetPixelsAsync();
                    using var output=File.Create(Path.Combine(App.DataDirectory,$"albums-{theme.ToString().ToLowerInvariant()}-{width}.png"));using var stream=output.AsRandomAccessStream();
                    var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
                    encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)target.PixelWidth,(uint)target.PixelHeight,96,96,pixels.ToArray());await encoder.FlushAsync();
                    results.Add(new{scenario="layout",theme=theme.ToString(),width=Root.ActualWidth,sortX=sortPosition.X,sortWidth=AlbumSortBox.ActualWidth,pinVisible=true});
                }
                Nav.SelectedItem=Nav.MenuItems.OfType<NavigationViewItem>().First(i=>i.Tag is long id&&id==ids[1]);
                await Until(()=>Task.FromResult(page=="album"&&PhotosRepeat.Visibility==Visibility.Visible));
                Check(AlbumSortBox.Visibility==Visibility.Collapsed&&SortBox.Visibility==Visibility.Collapsed&&sort=="oldest","Photo page must keep its own sort and narrow responsive behavior");
                AppWindow.Resize(new((int)(1160*Root.XamlRoot.RasterizationScale),(int)(800*Root.XamlRoot.RasterizationScale)));await Task.Delay(250);
                Check(SortBox.Visibility==Visibility.Visible&&SortBox.SelectedIndex==2,"Photo sorter must remain independent");
                Nav.SelectedItem=AlbumsItem;await ReloadAsync();Check(albumSort=="most"&&AlbumSortBox.Visibility==Visibility.Visible,"Returning to albums must restore its sorter");
                results.Add(new{scenario="navigation",independentPhotoSort=true,restoredAlbumSort=true,search=true,menuPinUnpin=true});
            }
            File.WriteAllText(report,JsonSerializer.Serialize(new{complete=true,results},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{complete=false,results,error=ex.ToString()},new JsonSerializerOptions{WriteIndented=true}));App.Log(ex.ToString());Environment.ExitCode=1;}
        finally{Close();}
    }
}
