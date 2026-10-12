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
    private sealed class ImageRequest
    {
        internal readonly List<Func<bool>> Consumers=[];
        internal Task<BitmapImage> Loading=null!;
    }
    private static readonly Dictionary<string,ImageRequest> Pending=new(StringComparer.OrdinalIgnoreCase);
    private static long used,cacheBytes,cacheVersion;
    internal static long ShellLoads {get;private set;}
    internal static long SkippedLoads {get;private set;}
    private static event Action<string>? Invalidated;
    internal static void InvalidateFolder(string folder)
    {
        string prefix=Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder))+Path.DirectorySeparatorChar;
        foreach(var entry in Images.Where(p=>p.Key.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).ToArray()){Images.Remove(entry.Key);cacheBytes-=entry.Value.Bytes;}
        // In-flight loads from the previous scan must not repopulate stale pixels.
        Pending.Clear();cacheVersion++;Invalidated?.Invoke(prefix);
    }
    internal static int CachedImages=>Images.Count;
    internal string? DisplayedPath {get;private set;}
    private long generation;
    private bool active;
    public Thumbnail(){Children.Add(placeholder);Children.Add(image);Loaded+=(_,_)=>{active=true;Invalidated+=Invalidate;Refresh();};Unloaded+=(_,_)=>{active=false;Invalidated-=Invalidate;generation++;image.Source=null;};}
    private void Invalidate(string prefix){if(FilePath?.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)==true)Refresh();}
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
                    var bitmap=await GetImageAsync(paths[candidate],()=>g==generation&&active);if(g!=generation||!active)return;image.Source=bitmap;DisplayedPath=paths[candidate];placeholder.Visibility=Visibility.Collapsed;return;
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
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or OperationCanceledException){ /* Offline/unsupported thumbnail has an honest placeholder. */ }
    }
    private static async Task<BitmapImage> GetImageAsync(string path,Func<bool> needed)
    {
        if(Images.TryGetValue(path,out var cached)){Images[path]=(cached.Image,cached.Bytes,++used);return cached.Image;}
        if(!Pending.TryGetValue(path,out var request)){request=new();request.Consumers.Add(needed);Pending[path]=request;request.Loading=LoadImageAsync(path,request);}
        else request.Consumers.Add(needed);
        try{return await request.Loading;}
        finally{request.Consumers.Remove(needed);if(Pending.TryGetValue(path,out var current)&&current==request&&request.Loading.IsCompleted)Pending.Remove(path);}
    }
    private static async Task<BitmapImage> LoadImageAsync(string path,ImageRequest request)
    {
        long version=cacheVersion;await Slots.WaitAsync();
        try
        {
            // A recycled/offscreen card no longer needs its queued Shell RPC.
            // This check runs on XAML, before dispatching any filesystem work.
            if(!request.Consumers.Any(needed=>needed())){SkippedLoads++;throw new OperationCanceledException();}
            ShellLoads++;
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
                if(Images.Remove(path,out var old))cacheBytes-=old.Bytes;
                Images[path]=(bitmap,size,++used);cacheBytes+=size;
                foreach(var item in Images.OrderBy(p=>p.Value.Used).ToArray()){if(cacheBytes<=32*1024*1024&&Images.Count<=96)break;Images.Remove(item.Key);cacheBytes-=item.Value.Bytes;}
            }
            return bitmap;
        }
        finally{Slots.Release();}
    }
}
