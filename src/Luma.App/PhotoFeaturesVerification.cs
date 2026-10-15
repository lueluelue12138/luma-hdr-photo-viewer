using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Luma.App;
public sealed partial class MainWindow
{
    internal async Task VerificationPhotoFeaturesAsync(string folder)
    {
        var results=new Dictionary<string,object>();var materials=new List<object>();var materialThemes=new HashSet<ElementTheme>();string report=Path.Combine(App.DataDirectory,"photo-features.json");
        var expected=JsonSerializer.Deserialize<Dictionary<string,bool[]>>(File.ReadAllText(Path.Combine(App.DataDirectory,"expected.json")))!;
        void Save(bool complete,string? error=null)=>File.WriteAllText(report,JsonSerializer.Serialize(new{complete,results,materials,reads=PhotoFeaturesCache.Reads,hits=PhotoFeaturesCache.Hits,error},new JsonSerializerOptions{WriteIndented=true}));
        static PhotoFeatureBadges? Badges(DependencyObject parent)
        {
            if(parent is PhotoFeatureBadges value)return value;
            for(int n=0;n<VisualTreeHelper.GetChildrenCount(parent);n++)if(Badges(VisualTreeHelper.GetChild(parent,n)) is {} found)return found;
            return null;
        }
        try
        {
            await VerificationReady.Task;long id=await App.Store.AddFolderAsync(folder);await RefreshAllAsync();page="album";albumId=id;sort="name";await ReloadAsync();await Task.Delay(500);
            foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
            {
                Root.RequestedTheme=theme;
                for(int step=0;step<=12;step++)
                {
                    GalleryScroll.ChangeView(null,GalleryScroll.ScrollableHeight*step/12,null,true);await Task.Delay(220);
                    var watch=System.Diagnostics.Stopwatch.StartNew();
                    while(VerificationRealizedCards().Select(c=>Badges(c)).Any(b=>b is not null&&b.VerificationFeatures is null))
                    {if(watch.Elapsed.TotalSeconds>15)throw new TimeoutException("Visible photo badges timed out");await Task.Delay(30);}
                    foreach(var card in VerificationRealizedCards())
                    {
                        var badge=Badges(card);if(badge?.FilePath is not {} path||!expected.TryGetValue(Path.GetFileName(path),out var flags))continue;
                        if(badge.VerificationHdr!=flags[0]||badge.VerificationLive!=flags[1])throw new InvalidOperationException("Wrong actual badge state: "+Path.GetFileName(path));
                        if(badge.VerificationBrush is not AcrylicBrush acrylic||badge.ActualTheme!=theme||badge.VerificationForeground is not SolidColorBrush text)throw new InvalidOperationException("Badge must use native Acrylic with inherited theme");
                        bool dark=theme==ElementTheme.Dark;
                        if(dark?acrylic.TintColor.R>80||text.Color.R<180:acrylic.TintColor.R<180||text.Color.R>80)throw new InvalidOperationException("Badge material / text did not adapt to theme");
                        if(materialThemes.Add(theme))materials.Add(new{theme=theme.ToString(),material="AcrylicBrush",tint=acrylic.TintColor.ToString(),text=text.Color.ToString()});
                        results[Path.GetFileName(path)]=new{hdr=badge.VerificationHdr,live=badge.VerificationLive,kind=badge.VerificationFeatures?.HdrKind};
                    }
                }
                GalleryScroll.ChangeView(null,0,null,true);await Task.Delay(350);
                var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(Root);var pixels=await bitmap.GetPixelsAsync();
                using var output=File.Create(Path.Combine(App.DataDirectory,"features-"+theme.ToString().ToLowerInvariant()+".png"));using var stream=output.AsRandomAccessStream();
                var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
                encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels.ToArray());await encoder.FlushAsync();
            }
            if(results.Count!=expected.Count)throw new InvalidOperationException($"Only checked {results.Count}/{expected.Count} photo badges");
            Save(true);
        }
        catch(Exception ex){Save(false,ex.ToString());throw;}
        finally{Close();}
    }
}
