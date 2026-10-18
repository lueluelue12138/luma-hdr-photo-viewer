using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Luma.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Luma.App;

public sealed partial class MainWindow
{
    internal async Task VerificationPhotoFilterAsync(bool reopen)
    {
        string report=Path.Combine(App.DataDirectory,reopen?"filter-reopen.json":"photo-filter.json");var results=new List<object>();
        using var stop=new CancellationTokenSource();Task heartbeat=Task.CompletedTask;var latencies=new List<double>();
        void Check(bool value,string why){if(!value)throw new InvalidOperationException(why);}
        async Task Until(Func<bool> condition)
        {
            var watch=Stopwatch.StartNew();while(!condition()){if(watch.Elapsed.TotalSeconds>45)throw new TimeoutException("Filter UI did not settle: "+TypeFilterStatus.Text);await Task.Delay(20);}
        }
        async Task SetTypes(PhotoType types)
        {
            HdrTypeCheck.IsChecked=(types&PhotoType.Hdr)!=0;LiveTypeCheck.IsChecked=(types&PhotoType.Live)!=0;StandardTypeCheck.IsChecked=(types&PhotoType.Standard)!=0;
            await typeSave;await ReloadAsync();await typeFilterTask;
            await Until(()=>selectedTypes==types&&(!HasTypeFilter||typeRequest?.Result is not null));
        }
        async Task Navigate(string target,long id=0)
        {
            Nav.SelectedItem=target=="album"?Nav.MenuItems.OfType<NavigationViewItem>().First(i=>i.Tag is long n&&n==id):Nav.MenuItems.OfType<NavigationViewItem>().First(i=>Equals(i.Tag,target));
            await ReloadAsync();await typeFilterTask;await Until(()=>page==target&&(!HasTypeFilter||typeRequest?.Result is not null));
        }
        try
        {
            await VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));await Task.Delay(100);await StartupRefresh;
            if(reopen)
            {
                await ReloadAsync();await typeFilterTask;
                Check(selectedTypes==PhotoType.Hdr&&HdrTypeCheck.IsChecked==true&&LiveTypeCheck.IsChecked==false,"New process must restore selected types");
                Check(AlbumsRepeat.ItemsSource is List<Album> shown&&shown.Count==1&&shown[0].MatchedCount==4,"Restored album filter must keep correct counts");
                results.Add(new{scenario="new-process-reopen",selected="HDR",matchingAlbums=1,matchingPhotos=4});
            }
            else
            {
                using var configuration=JsonDocument.Parse(File.ReadAllText(Path.Combine(App.DataDirectory,"..","fixtures.json")));
                var folders=configuration.RootElement.GetProperty("folders").EnumerateArray().Select(v=>v.GetString()!).ToArray();
                var expected=JsonSerializer.Deserialize<Dictionary<string,int>>(configuration.RootElement.GetProperty("expected").GetRawText())!;
                Check(typeIndex.Reads==0,"Unfiltered startup must not start an album-wide type scan");
                var ids=new List<long>();foreach(string folder in folders){long id=await App.Store.AddFolderAsync(folder);ids.Add(id);await App.Indexer.RefreshAsync((await App.Store.AlbumsAsync()).Single(a=>a.Id==id));}
                var originals=await App.Store.PhotosAsync();var plain=originals.Single(p=>p.Name=="01_plain.png");var hdr=originals.Single(p=>p.Name=="03_hdr_heic.heic");
                await App.Store.SetAlbumAsync(ids[0],cover:plain.Path,pinned:true);await App.Store.ToggleFavoriteAsync(plain);await App.Store.ToggleFavoriteAsync(hdr);
                await ReloadAsync();await Task.Delay(250);
                TypeFilterButton.Flyout.ShowAt(TypeFilterButton);await Task.Delay(100);
                ((IToggleProvider)new CheckBoxAutomationPeer(HdrTypeCheck).GetPattern(PatternInterface.Toggle)).Toggle();
                await typeSave;await ReloadAsync();await typeFilterTask;TypeFilterButton.Flyout.Hide();
                Check(AlbumsRepeat.ItemsSource is List<Album> hdrAlbums&&hdrAlbums.Count==1&&hdrAlbums[0].MatchedCount==4&&hdrAlbums[0].Cover!=plain.Path&&hdrAlbums[0].Pinned,"HDR album projection must preserve pin, count matches and replace an SDR cover");
                Check(typeIndex.Reads==7,"Only the seven scoped photo metadata entries should be classified");
                results.Add(new{scenario="hdr-album-overview",matchingAlbums=1,matchedPhotos=4,matchingCover=true,pinPreserved=true,reads=typeIndex.Reads});
                await SetTypes(PhotoType.Hdr|PhotoType.Live);
                Check(((List<Album>)AlbumsRepeat.ItemsSource).Single().MatchedCount==5,"Union must include standalone HDR, standalone Live and HDR Live once");
                await SetTypes(PhotoType.Standard);Check(((List<Album>)AlbumsRepeat.ItemsSource).Count==2,"Ordinary filter must keep both albums with ordinary photos");
                await SetTypes(PhotoType.Unknown);Check(((List<Album>)AlbumsRepeat.ItemsSource).Single(a=>a.Id==ids[0]).Cover==plain.Path,"Clearing filter must restore the chosen album cover");
                await Navigate("all");Check(photos.Count==7,"All photos must start with all seven fixtures");
                await SetTypes(PhotoType.Hdr);Check(photos.Count==4,"HDR includes JPEG, HEIC, linear JXR and HDR Live");
                await SetTypes(PhotoType.Live);Check(photos.Select(p=>p.Name).SequenceEqual(new[]{"05_live.jpg","06_hdr_live.jpg"}),"Live includes both SDR and HDR Live");
                await ViewerPreferences.UpdateAsync(ViewerPreferences.Current with{Hdr=false,Vsr=false});await Task.Delay(250);
                var previewPaths=photos.Select(p=>p.Path).ToArray();var previewCard=(Button)PhotosRepeat.TryGetElement(0);
                ((IInvokeProvider)new ButtonAutomationPeer(previewCard).GetPattern(PatternInterface.Invoke)).Invoke();await Until(()=>viewers.Count==1);
                var viewer=viewers[0];await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
                Check(viewer.VerificationFilterPaths.SequenceEqual(previewPaths),"Actual preview playlist must contain only the filtered photos");
                await viewer.VerificationSelectAsync(1);Check(viewer.VerificationPhoto==previewPaths[1],"Next in preview must navigate within the filtered playlist");
                viewer.Close();await viewer.VerificationDisposeAsync();await ReloadAsync();await typeFilterTask;
                results.Add(new{scenario="actual-preview-playlist",photos=2,nextWithinFilter=true});
                await SetTypes(PhotoType.Hdr|PhotoType.Live);Check(photos.Count==5,"Photo multiselect must use union");
                Check(typeIndex.Reads==7,"Switching scopes and types must reuse the session metadata index");
                await Navigate("favorites");Check(photos.Count==1&&photos[0].Path==hdr.Path,"Favorites scope intersects selected types");
                await Navigate("album",ids[1]);Check(photos.Count==0&&EmptyState.Visibility==Visibility.Visible&&EmptyTitle.Text.Contains("筛选"),"No matches must show a filter-specific empty state");
                await Navigate("album",ids[0]);Check(photos.Count==5,"Album scope must use its own filtered playlist");
                Search.Text="hdr";await ReloadAsync();await typeFilterTask;Check(photos.Count==4,"Filename search intersects the selected types");
                SortBox.SelectedIndex=1;await ReloadAsync();await typeFilterTask;
                await Until(()=>typeRequest is {Result:not null} active&&active.Key.Sort=="newest"&&active.Key.Search=="hdr");
                Check(photos.Select(p=>p.Name).SequenceEqual(new[]{"06_hdr_live.jpg","04_hdr_jxr.jxr","03_hdr_heic.heic","02_hdr_jpeg.jpg"}),"Photo ordering must survive filtering: "+string.Join(",",photos.Select(p=>p.Name)));
                Search.Text="";await ReloadAsync();await typeFilterTask;
                var source=PhotosRepeat.ItemsSource;await ReloadAsync();await typeFilterTask;
                Check(ReferenceEquals(source,PhotosRepeat.ItemsSource),"Unchanged filtered reload must preserve realized cards");
                results.Add(new{scenario="scope-search-sort",jpegHeicJxr=true,liveWithAndWithoutHdr=true,union=true,favorites=true,albumEmpty=true,search=true,sort=true,warmAdditionalReads=typeIndex.Reads-7,unchangedCards=true});

                await Navigate("all");await SetTypes(PhotoType.Unknown);
                var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                verificationTypeProbe=async(_,token)=>{entered.TrySetResult();await Task.Delay(Timeout.Infinite,token);return PhotoType.Hdr;};typeIndex.InvalidateFolder(folders[0]);
                HdrTypeCheck.IsChecked=true;await typeSave;await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var obsolete=typeFilterTask;
                ((IInvokeProvider)new ButtonAutomationPeer(ClearTypeFilterButton).GetPattern(PatternInterface.Invoke)).Invoke();await typeSave;await ReloadAsync();await obsolete;
                Check(!HasTypeFilter&&photos.Count==7&&TypeFilterBar.Visibility==Visibility.Collapsed,"Clear button must cancel pending work immediately and restore all photos");
                verificationTypeProbe=null;await SetTypes(PhotoType.Hdr);Check(photos.Count==4,"A new request after cancellation must run normally");
                results.Add(new{scenario="cancel-clear-retry",staleResultsSuppressed=true,allRestored=true,retry=true});

                var missing=new Photo(Path.Combine(App.DataDirectory,"reappearing.jpg"),"reappearing.jpg",10,10);
                await App.Store.ToggleFavoriteAsync(missing);await Navigate("favorites");
                Check(photos.Count==1&&typeRequest?.Result?.Unavailable==1&&TypeFilterStatus.Text.Contains("无法识别"),"Unreadable favorite must be reported without becoming an ordinary photo");
                await SetTypes(PhotoType.Standard);Check(photos.Count==1,"Missing favorite is excluded from ordinary static");
                File.Copy(Path.Combine(AppContext.BaseDirectory,"Assets","Square44x44Logo.png"),missing.Path);
                ((IInvokeProvider)new ButtonAutomationPeer(RefreshButton).GetPattern(PatternInterface.Invoke)).Invoke();
                await Until(()=>photos.Count==2&&typeRequest?.Result?.Unavailable==0);
                await App.Store.ToggleFavoriteAsync(missing);
                results.Add(new{scenario="unavailable-and-refresh",unknownNotOrdinary=true,unlinkedFavoriteRetry=true});
                await SetTypes(PhotoType.Hdr|PhotoType.Live);await Navigate("all");
                foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
                foreach(int width in new[]{1160,600,480})
                {
                    Root.RequestedTheme=theme;double scale=Root.XamlRoot.RasterizationScale;AppWindow.Resize(new((int)(width*scale),(int)(800*scale)));await Task.Delay(220);
                    var position=TypeFilterButton.TransformToVisual(Root).TransformPoint(new(0,0));
                    Check(position.X>=0&&position.X+TypeFilterButton.ActualWidth<=Root.ActualWidth,"Filter entry must remain accessible at narrow widths");
                    Check(TypeFilterBar.ActualWidth<=Gallery.ActualWidth&&TypeFilterStatus.ActualWidth>0,"Filter status must fit the gallery");
                    TypeFilterButton.Flyout.ShowAt(TypeFilterButton);await Task.Delay(80);Check(HdrTypeCheck.ActualTheme==theme,"Native filter popup must inherit theme");TypeFilterButton.Flyout.Hide();
                    var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(Root);var pixels=await bitmap.GetPixelsAsync();
                    using var output=File.Create(Path.Combine(App.DataDirectory,$"filter-{theme.ToString().ToLowerInvariant()}-{width}.png"));using var stream=output.AsRandomAccessStream();
                    var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
                    encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels.ToArray());await encoder.FlushAsync();
                    results.Add(new{scenario="layout",theme=theme.ToString(),width=Root.ActualWidth,filterX=position.X,filterWidth=TypeFilterButton.ActualWidth});
                }
                await Navigate("albums");await ReloadAsync();await typeFilterTask;var cards=AlbumsRepeat.ItemsSource;long loads=Thumbnail.ShellLoads+Thumbnail.WicLoads;
                await ReloadAsync();await typeFilterTask;Check(ReferenceEquals(cards,AlbumsRepeat.ItemsSource)&&loads==Thumbnail.ShellLoads+Thumbnail.WicLoads,"Unchanged filtered overview must preserve cards and add no thumbnail loads");
                results.Add(new{scenario="unchanged-overview",preservedCards=true,additionalThumbnailLoads=0});
                foreach(int width in new[]{480,1160})
                {
                    AppWindow.Resize(new((int)(width*Root.XamlRoot.RasterizationScale),(int)(800*Root.XamlRoot.RasterizationScale)));await Task.Delay(220);
                    var position=TypeFilterButton.TransformToVisual(Root).TransformPoint(new(0,0));
                    Check(PageTitle.ActualWidth>=50&&position.X+TypeFilterButton.ActualWidth<=Root.ActualWidth,"Album overview must fit both sort and filter at narrow widths");
                    results.Add(new{scenario="album-overview-layout",width=Root.ActualWidth,titleWidth=PageTitle.ActualWidth,filterX=position.X,albumSortWidth=AlbumSortBox.ActualWidth});
                }

                if(configuration.RootElement.GetProperty("largeSource").GetString() is {} large)
                {
                    await SetTypes(PhotoType.Unknown);long id=await App.Store.AddFolderAsync(large);await App.Indexer.RefreshAsync((await App.Store.AlbumsAsync()).Single(a=>a.Id==id));
                    await ReloadAsync();await Navigate("album",id);Check(photos.Count>=3000,"Real camera album needs 3000+ photos");int total=photos.Count;
                    heartbeat=Task.Run(async()=>{while(!stop.IsCancellationRequested){long posted=Stopwatch.GetTimestamp();DispatcherQueue.TryEnqueue(()=>latencies.Add(Stopwatch.GetElapsedTime(posted).TotalMilliseconds));try{await Task.Delay(8,stop.Token);}catch(OperationCanceledException){break;}}});
                    long before=typeIndex.Reads;var timer=Stopwatch.StartNew();await SetTypes(PhotoType.Hdr);timer.Stop();double firstMs=timer.Elapsed.TotalMilliseconds;int hdrCount=photos.Count;
                    long after=typeIndex.Reads;timer.Restart();await SetTypes(PhotoType.Standard);timer.Stop();
                    Check(typeIndex.Reads==after,"Changing types in the real album must perform zero additional metadata probes");
                    await Task.Delay(350);int realized=VerificationRealizedCards().Count();Check(realized<64,"Filtered real album must remain virtualized");
                    stop.Cancel();await heartbeat;var sorted=latencies.Order().ToArray();
                    results.Add(new{scenario="real-camera-album",photos=total,hdrCount,ordinaryCount=photos.Count,firstMilliseconds=firstMs,firstMetadataProbes=after-before,warmMilliseconds=timer.Elapsed.TotalMilliseconds,warmAdditionalProbes=typeIndex.Reads-after,realizedCards=realized,dispatcherSamples=sorted.Length,dispatcherP95=sorted.Length==0?0:sorted[(int)((sorted.Length-1)*.95)],dispatcherMax=sorted.LastOrDefault()});
                    App.Indexer.Forget(id);await App.Store.RemoveAlbumAsync(id);
                }
                await SetTypes(PhotoType.Hdr);await Navigate("albums");await typeSave;
            }
            File.WriteAllText(report,JsonSerializer.Serialize(new{complete=true,results},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{complete=false,results,error=ex.ToString()},new JsonSerializerOptions{WriteIndented=true}));App.Log(ex.ToString());Environment.ExitCode=1;}
        finally{verificationTypeProbe=null;stop.Cancel();await heartbeat;Close();}
    }
}

public sealed partial class ViewerWindow
{
    internal string[] VerificationFilterPaths=>sequence.Select(p=>p.Path).ToArray();
}
