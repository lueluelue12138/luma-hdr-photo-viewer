using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Luma.App;
public sealed class Thumbnail : Grid
{
    public static readonly DependencyProperty FilePathProperty=DependencyProperty.Register(nameof(FilePath),typeof(string),typeof(Thumbnail),new PropertyMetadata(null,(d,e)=>((Thumbnail)d).Refresh()));
    public string? FilePath {get=>(string?)GetValue(FilePathProperty);set=>SetValue(FilePathProperty,value);}
    private readonly Image image=new(){Stretch=Stretch.UniformToFill};
    private readonly FontIcon placeholder=new(){Glyph="\uEB9F",FontSize=24,Opacity=0.25};
    private static readonly SemaphoreSlim Slots=new(4);
    private long generation;
    private bool active;
    public Thumbnail(){Children.Add(placeholder);Children.Add(image);Loaded+=(_,_)=>{active=true;Refresh();};Unloaded+=(_,_)=>{active=false;generation++;image.Source=null;};}
    private async void Refresh()
    {
        var g=++generation;var path=FilePath;image.Source=null;placeholder.Visibility=Visibility.Visible;if(!active||string.IsNullOrEmpty(path))return;
        try
        {
            await Slots.WaitAsync();
            try
            {
                if(g!=generation||!active)return;
                var file=await StorageFile.GetFileFromPathAsync(path);
                using var thumb=await file.GetThumbnailAsync(ThumbnailMode.PicturesView,320,ThumbnailOptions.UseCurrentScale);
                var bitmap=new BitmapImage(){DecodePixelWidth=320};
                if(thumb is not null&&thumb.Type==ThumbnailType.Image)await bitmap.SetSourceAsync(thumb);
                else {using var stream=await file.OpenReadAsync();await bitmap.SetSourceAsync(stream);}
                if(g!=generation||!active)return;image.Source=bitmap;placeholder.Visibility=Visibility.Collapsed;
            }
            finally{Slots.Release();}
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException){ /* Offline/unsupported thumbnail has an honest placeholder. */ }
    }
}
