using Luma.Core;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage;

namespace Luma.App;
internal sealed record PhotoMetadata(string Path,long Length,DateTimeOffset Created,DateTimeOffset Modified,DateTimeOffset? Taken,uint Width,uint Height,double? Latitude,double? Longitude,IReadOnlyList<PhotoProperty> Rows,double ReadMilliseconds,bool Cached=false)
{
    public string? Value(string key)=>Rows.FirstOrDefault(r=>r.Key==key)?.Value;
}
internal static class PhotoMetadataReader
{
    private readonly record struct Stamp(string Path,long Length,long Modified);
    private static readonly SemaphoreSlim gate=new(2);
    private static readonly Dictionary<Stamp,PhotoMetadata> cache=new();
    private static readonly LinkedList<Stamp> lru=new();
    internal static int Reads {get;private set;}
    internal static int CachedCount {get{lock(cache)return cache.Count;}}
    // No work runs on gallery import/startup or closed detail panels. The OS
    // property handler reads metadata; Luma requests no pixel decode or AI.
    internal static async Task<PhotoMetadata> ReadAsync(string path,CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try{return await Task.Run(async()=>{
            var watch=System.Diagnostics.Stopwatch.StartNew();token.ThrowIfCancellationRequested();var fileInfo=new FileInfo(path);var stamp=new Stamp(SupportedFormats.Normalize(path).ToUpperInvariant(),fileInfo.Length,fileInfo.LastWriteTimeUtc.Ticks);
            lock(cache)if(cache.TryGetValue(stamp,out var hit)){lru.Remove(stamp);lru.AddFirst(stamp);return hit with{Cached=true,ReadMilliseconds=watch.Elapsed.TotalMilliseconds};}
            var file=await StorageFile.GetFileFromPathAsync(path).AsTask(token).ConfigureAwait(false);
            var image=await file.Properties.GetImagePropertiesAsync().AsTask(token).ConfigureAwait(false);
            var raw=await image.RetrievePropertiesAsync(PhotoMetadataFormatting.PropertyKeys).AsTask(token).ConfigureAwait(false);
            var data=raw.Where(p=>p.Value is not null).ToDictionary(p=>p.Key,p=>p.Value);
            if(!string.IsNullOrWhiteSpace(image.CameraManufacturer))data["System.Photo.CameraManufacturer"]=image.CameraManufacturer;
            if(!string.IsNullOrWhiteSpace(image.CameraModel))data["System.Photo.CameraModel"]=image.CameraModel;
            DateTimeOffset? taken=image.DateTaken.Year>1601?image.DateTaken:null;
            double? lat=image.Latitude,lon=image.Longitude;
            if(lat is null||lon is null||!double.IsFinite(lat.Value)||!double.IsFinite(lon.Value)||Math.Abs(lat.Value)>90||Math.Abs(lon.Value)>180){lat=lon=null;}
            var result=new PhotoMetadata(path,fileInfo.Length,new(fileInfo.CreationTimeUtc),new(fileInfo.LastWriteTimeUtc),taken,image.Width,image.Height,lat,lon,PhotoMetadataFormatting.Rows(data),watch.Elapsed.TotalMilliseconds);
            token.ThrowIfCancellationRequested();lock(cache){Reads++;cache[stamp]=result;lru.Remove(stamp);lru.AddFirst(stamp);while(cache.Count>64){var last=lru.Last!.Value;lru.RemoveLast();cache.Remove(last);}}
            return result;
        },token).ConfigureAwait(false);}finally{gate.Release();}
    }
}
