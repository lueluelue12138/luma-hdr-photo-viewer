using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Dispatching;
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
    public static readonly DependencyProperty DecodeLogicalEdgeProperty=DependencyProperty.Register(nameof(DecodeLogicalEdge),typeof(double),typeof(Thumbnail),new PropertyMetadata(320.0,(d,e)=>((Thumbnail)d).Refresh()));
    public double DecodeLogicalEdge {get=>(double)GetValue(DecodeLogicalEdgeProperty);set=>SetValue(DecodeLogicalEdgeProperty,value);}
    private readonly Image image=new(){Stretch=Stretch.UniformToFill,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private readonly FontIcon placeholder=new(){Glyph="\uEB9F",FontSize=24,Opacity=0.25};
    private static readonly SemaphoreSlim Slots=new(4);
    // Four workers, at most 1024 x 1024 x 4 bytes per staging buffer.
    // A dedicated pool also bounds retained buckets instead of growing the
    // process-wide pool through unrelated image sizes (under 32 MiB retained).
    private static readonly ArrayPool<byte> Pixels=ArrayPool<byte>.Create(4*1024*1024,4);
    private static readonly Dictionary<string,(ImageSource Image,long Bytes,long Used,uint Edge)> Images=new(StringComparer.OrdinalIgnoreCase);
    private sealed class ImageRequest
    {
        internal uint Edge;
        internal readonly List<Func<bool>> Consumers=[];
        internal Task<ImageSource> Loading=null!;
    }
    private static readonly Dictionary<string,Dictionary<uint,ImageRequest>> Pending=new(StringComparer.OrdinalIgnoreCase);
    private static long used,cacheBytes,cacheVersion;
    internal static long ShellLoads {get;private set;}
    private static long wicLoads;
    internal static long WicLoads=>Interlocked.Read(ref wicLoads);
    internal static long LargerCacheHits {get;private set;}
    internal static Func<string,Task>? VerificationBeforeDecode;
    internal ImageSource? VerificationImageSource=>image.Source;
    internal static bool PreferWic {get;set;}=true; // controlled only by the isolated gallery verifier
    internal static bool UseDiskCache {get;set;}=true;
    internal static long SkippedLoads {get;private set;}
    private static event Action<string>? Invalidated;
    internal static void InvalidateFolder(string folder)
    {
        PhotoFeaturesCache.InvalidateFolder(folder);
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
                    uint edge=(uint)Math.Clamp(Math.Ceiling(DecodeLogicalEdge*(XamlRoot?.RasterizationScale??1)),64,1024);
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
        if(Images.TryGetValue(key,out var cached)){Images[key]=(cached.Image,cached.Bytes,++used,cached.Edge);return cached.Image;}
        // A gallery image can already satisfy the compact filmstrip. Reuse it
        // without reading the same file again or duplicating a cache entry.
        string prefix=path+"|";string? larger=null;uint closest=uint.MaxValue;
        foreach(var candidate in Images)
            if(candidate.Value.Edge>=edge&&candidate.Value.Edge<closest&&candidate.Key.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)){larger=candidate.Key;closest=candidate.Value.Edge;}
        if(larger is not null){cached=Images[larger];Images[larger]=(cached.Image,cached.Bytes,++used,cached.Edge);LargerCacheHits++;return cached.Image;}
        // Index loading requests by path: a fast gallery scroll can queue many
        // different files, so finding a compatible size must not scan them all.
        if(!Pending.TryGetValue(path,out var sizes)){sizes=[];Pending.Add(path,sizes);}
        if(!sizes.TryGetValue(edge,out var request))request=sizes.Values.Where(r=>r.Edge>edge).MinBy(r=>r.Edge);
        if(request is null){request=new(){Edge=edge};request.Consumers.Add(needed);sizes[edge]=request;request.Loading=LoadImageAsync(path,edge,key,request);}
        else request.Consumers.Add(needed);
        try{return await request.Loading;}
        finally
        {
            request.Consumers.Remove(needed);
            if(Pending.TryGetValue(path,out var current)&&current.TryGetValue(request.Edge,out var same)&&same==request&&request.Loading.IsCompleted)
            {current.Remove(request.Edge);if(current.Count==0)Pending.Remove(path);}
        }
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
                if(VerificationBeforeDecode is {} verification)await verification(path);
                // Supported SDR formats use scaled WIC/ICC/EXIF directly,
                // avoiding Shell RPC and a second encoded bitmap decode.
                decoded=await Task.Run<(FrameInfo info,byte[] pixels)?>(()=>
                {
                    try
                    {
                        var stamp=ThumbnailDiskCache.SourceStamp(path);
                        if(UseDiskCache&&ThumbnailDiskCache.Read(path,edge,stamp,Pixels) is {} cached)return cached;
                        Interlocked.Increment(ref wicLoads);
                        Native.Check(Native.LumaDecodeThumbnail(path,edge,out var info,out var pointer));
                        using var frame=new DecodedFrame(pointer,info);
                        if(info.NativeHdr!=0)return null; // preserve Windows' HDR thumbnail provider
                        var pixels=Pixels.Rent(checked((int)info.Bytes));
                        try{Native.Check(Native.LumaCopySdrBgra(pointer,pixels,info.Bytes));if(UseDiskCache)ThumbnailDiskCache.Store(path,edge,stamp,info,pixels);return(info,pixels);}
                        catch{Pixels.Return(pixels);throw;}
                    }
                    catch(System.Runtime.InteropServices.COMException){return null;}
                });
            }
            if(decoded is {} sdr)
            {
                try
                {
                    var completed=new TaskCompletionSource<ImageSource>(TaskCreationOptions.RunContinuationsAsynchronously);
                    // Several covers can finish decoding together. Admit each
                    // XAML bitmap/upload at low priority so queued input and
                    // window/layout work run before the optional thumbnail.
                    if(!DispatcherQueue.GetForCurrentThread().TryEnqueue(DispatcherQueuePriority.Low,()=>
                    {
                        try
                        {
                            var writable=new WriteableBitmap((int)sdr.info.Width,(int)sdr.info.Height);
                            using(var stream=writable.PixelBuffer.AsStream())stream.Write(sdr.pixels,0,checked((int)sdr.info.Bytes));
                            writable.Invalidate();completed.TrySetResult(writable);
                        }
                        catch(Exception ex){completed.TrySetException(ex);}
                    }))throw new OperationCanceledException();
                    bitmap=await completed.Task;size=sdr.info.Width*(long)sdr.info.Height*4;
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
                    using var thumb=await file.GetThumbnailAsync(ThumbnailMode.PicturesView,edge,ThumbnailOptions.None);
                    using var stream=thumb is not null&&thumb.Type==ThumbnailType.Image?null:await file.OpenReadAsync();
                    var source=stream??thumb;if(source is null||source.Size>64*1024*1024)throw new IOException("Thumbnail source exceeds its memory budget.");
                    using var reader=new DataReader(source.GetInputStreamAt(0));await reader.LoadAsync((uint)source.Size);var data=new byte[(int)source.Size];reader.ReadBytes(data);return data;
                });
                using var memory=new InMemoryRandomAccessStream();using(var writer=new DataWriter(memory)){writer.WriteBytes(bytes);await writer.StoreAsync();writer.DetachStream();}memory.Seek(0);
                var encoded=new BitmapImage(){DecodePixelWidth=(int)edge};await encoded.SetSourceAsync(memory);
                bitmap=encoded;size=(long)encoded.PixelWidth*encoded.PixelHeight*4;
            }
            if(version==cacheVersion&&size<=32*1024*1024)
            {
                if(Images.Remove(key,out var old))cacheBytes-=old.Bytes;
                Images[key]=(bitmap,size,++used,edge);cacheBytes+=size;
                while(cacheBytes>32*1024*1024||Images.Count>96)
                {var item=Images.MinBy(p=>p.Value.Used);Images.Remove(item.Key);cacheBytes-=item.Value.Bytes;}
            }
            return bitmap;
        }
        finally{Slots.Release();}
    }
}
