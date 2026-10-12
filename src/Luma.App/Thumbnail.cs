using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

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
                // Shell thumbnail RPC can synchronously pump window messages even when
                // its API is named Async. Never invoke it from a XAML layout/Loaded callback.
                var bytes=await Task.Run(async()=>
                {
                    var file=await StorageFile.GetFileFromPathAsync(path);
                    using var thumb=await file.GetThumbnailAsync(ThumbnailMode.PicturesView,320,ThumbnailOptions.UseCurrentScale);
                    using var stream=thumb is not null&&thumb.Type==ThumbnailType.Image?null:await file.OpenReadAsync();
                    var source=stream??thumb;
                    if(source is null||source.Size>64*1024*1024)throw new IOException("Thumbnail source exceeds its memory budget.");
                    using var reader=new DataReader(source.GetInputStreamAt(0));await reader.LoadAsync((uint)source.Size);var data=new byte[(int)source.Size];reader.ReadBytes(data);return data;
                });
                if(g!=generation||!active)return;
                using var memory=new InMemoryRandomAccessStream();using(var writer=new DataWriter(memory)){writer.WriteBytes(bytes);await writer.StoreAsync();writer.DetachStream();}memory.Seek(0);
                var bitmap=new BitmapImage(){DecodePixelWidth=320};
                await bitmap.SetSourceAsync(memory);
                if(g!=generation||!active)return;image.Source=bitmap;placeholder.Visibility=Visibility.Collapsed;
            }
            finally{Slots.Release();}
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException){ /* Offline/unsupported thumbnail has an honest placeholder. */ }
    }
}
