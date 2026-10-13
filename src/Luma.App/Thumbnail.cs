using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Buffers;

namespace Luma.App;
public sealed class Thumbnail : Grid
{
    public static readonly DependencyProperty FilePathProperty=DependencyProperty.Register(nameof(FilePath),typeof(string),typeof(Thumbnail),new PropertyMetadata(null,(d,e)=>((Thumbnail)d).Refresh()));
    public string? FilePath {get=>(string?)GetValue(FilePathProperty);set=>SetValue(FilePathProperty,value);}
    public static readonly DependencyProperty AlbumIdProperty=DependencyProperty.Register(nameof(AlbumId),typeof(long),typeof(Thumbnail),new PropertyMetadata(0L,(d,e)=>((Thumbnail)d).Refresh()));
    public long AlbumId {get=>(long)GetValue(AlbumIdProperty);set=>SetValue(AlbumIdProperty,value);}
    private readonly Image image=new(){Stretch=Stretch.UniformToFill,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private readonly FontIcon placeholder=new(){Glyph="\uEB9F",FontSize=24,Opacity=0.25};
    private static readonly SemaphoreSlim Slots=new(4);
    // Four workers, at most 1024 x 1024 x 4 bytes per staging buffer.
    // A dedicated pool also bounds retained buckets instead of growing the
    // process-wide pool through unrelated image sizes (under 32 MiB retained).
    private static readonly ArrayPool<byte> Pixels=ArrayPool<byte>.Create(4*1024*1024,4);
    private static readonly Dictionary<string,(ImageSource Image,long Bytes,long Used)> Images=new(StringComparer.OrdinalIgnoreCase);
    private sealed class ImageRequest
    {
        internal readonly List<Func<bool>> Consumers=[];
        internal Task<ImageSource> Loading=null!;
    }
    private static readonly Dictionary<string,ImageRequest> Pending=new(StringComparer.OrdinalIgnoreCase);
    private static long used,cacheBytes,cacheVersion;
    internal static long ShellLoads {get;private set;}
    internal static long WicLoads {get;private set;}
    internal static bool PreferWic {get;set;}=true; // controlled only by the isolated gallery verifier
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
    internal static long CachedBytes=>cacheBytes;
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
                    uint edge=(uint)Math.Clamp(Math.Ceiling(320*(XamlRoot?.RasterizationScale??1)),320,1024);
                    var bitmap=await GetImageAsync(paths[candidate],edge,()=>g==generation&&active);if(g!=generation||!active)return;image.Source=bitmap;DisplayedPath=paths[candidate];placeholder.Visibility=Visibility.Collapsed;return;
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
    private static async Task<ImageSource> GetImageAsync(string path,uint edge,Func<bool> needed)
    {
        string key=path+"|"+edge; // Windows filenames cannot contain '|'.
        if(Images.TryGetValue(key,out var cached)){Images[key]=(cached.Image,cached.Bytes,++used);return cached.Image;}
        if(!Pending.TryGetValue(key,out var request)){request=new();request.Consumers.Add(needed);Pending[key]=request;request.Loading=LoadImageAsync(path,edge,key,request);}
        else request.Consumers.Add(needed);
        try{return await request.Loading;}
        finally{request.Consumers.Remove(needed);if(Pending.TryGetValue(key,out var current)&&current==request&&request.Loading.IsCompleted)Pending.Remove(key);}
    }
    private static async Task<ImageSource> LoadImageAsync(string path,uint edge,string key,ImageRequest request)
    {
        long version=cacheVersion;await Slots.WaitAsync();
        try
        {
            // A recycled/offscreen card no longer needs its queued Shell RPC.
            // This check runs on XAML, before dispatching any filesystem work.
            if(!request.Consumers.Any(needed=>needed())){SkippedLoads++;throw new OperationCanceledException();}
            ImageSource bitmap;long size;(FrameInfo info,byte[] pixels)? decoded=null;
            if(PreferWic)
            {
                WicLoads++;
                // Supported SDR formats use scaled WIC/ICC/EXIF directly,
                // avoiding Shell RPC and a second encoded bitmap decode.
                decoded=await Task.Run<(FrameInfo info,byte[] pixels)?>(()=>
                {
                    try
                    {
                        Native.Check(Native.LumaDecodeSized(path,edge,out var info,out var pointer));
                        using var frame=new DecodedFrame(pointer,info);
                        if(info.NativeHdr!=0)return null; // preserve Windows' HDR thumbnail provider
                        var pixels=Pixels.Rent(checked((int)info.Bytes));
                        try{Native.Check(Native.LumaCopySdrBgra(pointer,pixels,info.Bytes));return(info,pixels);}
                        catch{Pixels.Return(pixels);throw;}
                    }
                    catch(System.Runtime.InteropServices.COMException){return null;}
                });
            }
            if(decoded is {} sdr)
            {
                try
                {
                    var writable=new WriteableBitmap((int)sdr.info.Width,(int)sdr.info.Height);
                    // Pool buckets can be larger than the decoded frame.
                    // Copy only valid pixels, then return the staging buffer.
                    using(var stream=writable.PixelBuffer.AsStream())stream.Write(sdr.pixels,0,checked((int)sdr.info.Bytes));
                    writable.Invalidate();bitmap=writable;size=sdr.info.Width*(long)sdr.info.Height*4;
                }
                finally{Pixels.Return(sdr.pixels);}
            }
            else
            {
                ShellLoads++;
                // Other formats retain the Windows thumbnail provider.
                var bytes=await Task.Run(async()=>
                {
                    var file=await StorageFile.GetFileFromPathAsync(path);
                    using var thumb=await file.GetThumbnailAsync(ThumbnailMode.PicturesView,320,ThumbnailOptions.UseCurrentScale);
                    using var stream=thumb is not null&&thumb.Type==ThumbnailType.Image?null:await file.OpenReadAsync();
                    var source=stream??thumb;if(source is null||source.Size>64*1024*1024)throw new IOException("Thumbnail source exceeds its memory budget.");
                    using var reader=new DataReader(source.GetInputStreamAt(0));await reader.LoadAsync((uint)source.Size);var data=new byte[(int)source.Size];reader.ReadBytes(data);return data;
                });
                using var memory=new InMemoryRandomAccessStream();using(var writer=new DataWriter(memory)){writer.WriteBytes(bytes);await writer.StoreAsync();writer.DetachStream();}memory.Seek(0);
                var encoded=new BitmapImage(){DecodePixelWidth=320};await encoded.SetSourceAsync(memory);
                bitmap=encoded;size=(long)encoded.PixelWidth*encoded.PixelHeight*4;
            }
            if(version==cacheVersion&&size<=32*1024*1024)
            {
                if(Images.Remove(key,out var old))cacheBytes-=old.Bytes;
                Images[key]=(bitmap,size,++used);cacheBytes+=size;
                while(cacheBytes>32*1024*1024||Images.Count>96)
                {var item=Images.MinBy(p=>p.Value.Used);Images.Remove(item.Key);cacheBytes-=item.Value.Bytes;}
            }
            return bitmap;
        }
        finally{Slots.Release();}
    }
}
