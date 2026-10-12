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
    public static readonly DependencyProperty AlbumIdProperty=DependencyProperty.Register(nameof(AlbumId),typeof(long),typeof(Thumbnail),new PropertyMetadata(0L,(d,e)=>((Thumbnail)d).Refresh()));
    public long AlbumId {get=>(long)GetValue(AlbumIdProperty);set=>SetValue(AlbumIdProperty,value);}
    private readonly Image image=new(){Stretch=Stretch.UniformToFill};
    private readonly FontIcon placeholder=new(){Glyph="\uEB9F",FontSize=24,Opacity=0.25};
    private static readonly SemaphoreSlim Slots=new(4);
    private static readonly Dictionary<string,(BitmapImage Image,long Bytes,long Used)> Images=new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string,Task<BitmapImage>> Pending=new(StringComparer.OrdinalIgnoreCase);
    private static long used,cacheBytes,cacheVersion;
    internal static void ClearCache(){Images.Clear();cacheBytes=0;Pending.Clear();cacheVersion++;}
    internal static int CachedImages=>Images.Count;
    internal string? DisplayedPath {get;private set;}
    private long generation;
    private bool active;
    public Thumbnail(){Children.Add(placeholder);Children.Add(image);Loaded+=(_,_)=>{active=true;Refresh();};Unloaded+=(_,_)=>{active=false;generation++;image.Source=null;};}
    private async void Refresh()
    {
        var g=++generation;var path=FilePath;image.Source=null;DisplayedPath=null;placeholder.Visibility=Visibility.Visible;if(!active||string.IsNullOrEmpty(path))return;
        var paths=new List<string>{path};
        try
        {
            for(int candidate=0;candidate<paths.Count;candidate++)
            {
                if(g!=generation||!active)return;
                try
                {
                    var bitmap=await GetImageAsync(paths[candidate]);if(g!=generation||!active)return;image.Source=bitmap;DisplayedPath=paths[candidate];placeholder.Visibility=Visibility.Collapsed;return;
                }
                catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
                {
                    if(candidate==0&&AlbumId>0)
                    {
                        var photos=await App.Store.PhotosAsync(Luma.Core.PhotoScope.Album,AlbumId);
                        if(g!=generation||!active)return;paths.AddRange(photos.Select(p=>p.Path).Where(p=>!string.Equals(p,path,StringComparison.OrdinalIgnoreCase)));
                    }
                }
            }
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException){ /* Offline/unsupported thumbnail has an honest placeholder. */ }
    }
    private static async Task<BitmapImage> GetImageAsync(string path)
    {
        if(Images.TryGetValue(path,out var cached)){Images[path]=(cached.Image,cached.Bytes,++used);return cached.Image;}
        if(!Pending.TryGetValue(path,out var loading)){loading=LoadImageAsync(path);Pending[path]=loading;}
        try{return await loading;}finally{if(Pending.TryGetValue(path,out var current)&&current==loading)Pending.Remove(path);}
    }
    private static async Task<BitmapImage> LoadImageAsync(string path)
    {
        long version=cacheVersion;await Slots.WaitAsync();
        try
        {
            // Keep Shell RPC off the UI thread; reuse the decoded, bounded image
            // when virtualization unloads/reloads a card during live resizing.
            var bytes=await Task.Run(async()=>
            {
                var file=await StorageFile.GetFileFromPathAsync(path);
                using var thumb=await file.GetThumbnailAsync(ThumbnailMode.PicturesView,320,ThumbnailOptions.UseCurrentScale);
                using var stream=thumb is not null&&thumb.Type==ThumbnailType.Image?null:await file.OpenReadAsync();
                var source=stream??thumb;if(source is null||source.Size>64*1024*1024)throw new IOException("Thumbnail source exceeds its memory budget.");
                using var reader=new DataReader(source.GetInputStreamAt(0));await reader.LoadAsync((uint)source.Size);var data=new byte[(int)source.Size];reader.ReadBytes(data);return data;
            });
            using var memory=new InMemoryRandomAccessStream();using(var writer=new DataWriter(memory)){writer.WriteBytes(bytes);await writer.StoreAsync();writer.DetachStream();}memory.Seek(0);
            var bitmap=new BitmapImage(){DecodePixelWidth=320};await bitmap.SetSourceAsync(memory);
            long size=(long)bitmap.PixelWidth*bitmap.PixelHeight*4;
            if(version==cacheVersion&&size<=32*1024*1024)
            {
                Images[path]=(bitmap,size,++used);cacheBytes+=size;
                foreach(var item in Images.OrderBy(p=>p.Value.Used).ToArray()){if(cacheBytes<=32*1024*1024&&Images.Count<=96)break;Images.Remove(item.Key);cacheBytes-=item.Value.Bytes;}
            }
            return bitmap;
        }
        finally{Slots.Release();}
    }
}
