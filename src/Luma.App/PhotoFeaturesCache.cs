using Luma.Core;

namespace Luma.App;

// Optional visible-card work, independent from thumbnail decode / first frame.
// Stamps and a short TTL catch external sidecar edits; no album-wide probing.
internal static class PhotoFeaturesCache
{
    private static readonly SemaphoreSlim slots=new(2);
    private static readonly object gate=new();
    private sealed record Entry(long Size,long Modified,long Used,long ReadAt,PhotoFeatures Value);
    private static readonly Dictionary<string,Entry> entries=new(StringComparer.OrdinalIgnoreCase);
    private static long clock,reads,hits;
    internal static long Reads=>Interlocked.Read(ref reads);
    internal static long Hits=>Interlocked.Read(ref hits);
    internal static event Action<string>? Invalidated;
    internal static void InvalidateFolder(string folder)
    {
        string prefix=Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder))+Path.DirectorySeparatorChar;
        lock(gate)foreach(string path in entries.Keys.Where(p=>p.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).ToArray())entries.Remove(path);
        Invalidated?.Invoke(prefix);
    }
    internal static async Task<PhotoFeatures> ReadAsync(string path,CancellationToken token)
    {
        await slots.WaitAsync(token);
        try{return await Task.Run(()=>Read(path,token),token);}
        finally{slots.Release();}
    }
    private static PhotoFeatures Read(string path,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var file=new FileInfo(path);long size=file.Length,modified=file.LastWriteTimeUtc.Ticks;
        Entry? cached;lock(gate)entries.TryGetValue(path,out cached);
        if(cached is not null&&cached.Size==size&&cached.Modified==modified&&Environment.TickCount64-cached.ReadAt<60000)
        {
            var live=cached.Value.Live;
            if(live is null||live.Embedded||File.Exists(live.VideoPath)&&File.GetLastWriteTimeUtc(live.VideoPath).Ticks==live.VideoModified)
            {lock(gate)entries[path]=cached with{Used=++clock};Interlocked.Increment(ref hits);return cached.Value;}
        }
        var value=LivePhotoReader.ReadFeatures(path,token);Interlocked.Increment(ref reads);
        if(value.HdrKind is null&&Path.GetExtension(path).ToLowerInvariant() is ".jxr" or ".wdp" or ".hdp" or ".tif" or ".tiff")
            try{Native.Check(Native.LumaProbeFloatHdr(path,out int hdr));if(hdr!=0)value=value with{HdrKind="线性 HDR"};}catch(System.Runtime.InteropServices.COMException){}
        token.ThrowIfCancellationRequested();
        var after=new FileInfo(path);if(after.Length!=size||after.LastWriteTimeUtc.Ticks!=modified)return value;
        lock(gate)
        {
            entries[path]=new(size,modified,++clock,Environment.TickCount64,value);
            if(entries.Count>4096)entries.Remove(entries.MinBy(e=>e.Value.Used).Key);
        }
        return value;
    }
}
