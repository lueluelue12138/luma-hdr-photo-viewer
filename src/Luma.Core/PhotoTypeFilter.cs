using System.Diagnostics;

namespace Luma.Core;

[Flags]
public enum PhotoType { Unknown=0, Hdr=1, Live=2, Standard=4, All=Hdr|Live|Standard }

public static class PhotoTypeFilter
{
    public static PhotoType Normalize(int value)=>value>=0&&value<=(int)PhotoType.All?(PhotoType)value:PhotoType.Unknown;
    public static PhotoType FromFeatures(PhotoFeatures features)
    {
        var type=(features.HdrKind is null?PhotoType.Unknown:PhotoType.Hdr)|(features.Live is null?PhotoType.Unknown:PhotoType.Live);
        return type==PhotoType.Unknown?PhotoType.Standard:type;
    }
    public static bool Matches(PhotoType type,PhotoType selected)=>selected is PhotoType.Unknown or PhotoType.All||(type&selected)!=0;
}

public sealed record PhotoFilterProgress(int Completed,int Total);
public sealed record PhotoFilterResult(List<Photo> Photos,int Unavailable);

// Session-only metadata index. No pixel decoding; global bounded workers,
// cancellable requests and stamp-based reuse keep repeated toggles inexpensive.
public sealed class PhotoTypeIndex(Func<Photo,CancellationToken,Task<PhotoType>> probe)
{
    private sealed record Entry(long Size,long Modified,PhotoType Type);
    private readonly Dictionary<string,Entry> entries=new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate=new();
    private readonly SemaphoreSlim slots=new(2);
    private long revision,reads;
    public long Revision {get{lock(gate)return revision;}}
    public long Reads=>Interlocked.Read(ref reads);
    public void InvalidateFolder(string folder)
    {
        string prefix=Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder))+Path.DirectorySeparatorChar;
        lock(gate){revision++;foreach(string path in entries.Keys.Where(p=>p.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).ToArray())entries.Remove(path);}
    }
    public Task<PhotoFilterResult> FilterAsync(IReadOnlyList<Photo> source,PhotoType selected,IProgress<PhotoFilterProgress>? progress=null,CancellationToken token=default)=>Task.Run(async()=>
    {
        token.ThrowIfCancellationRequested();selected=PhotoTypeFilter.Normalize((int)selected);
        if(selected is PhotoType.Unknown or PhotoType.All)return new PhotoFilterResult(source.ToList(),0);
        var types=new PhotoType[source.Count];var missing=new List<int>();long version;
        lock(gate)
        {
            version=revision;
            for(int i=0;i<source.Count;i++)
            {
                var p=source[i];
                if(entries.TryGetValue(p.Path,out var entry)&&entry.Size==p.Size&&entry.Modified==p.Modified)types[i]=entry.Type;
                else missing.Add(i);
            }
        }
        int completed=source.Count-missing.Count,next=-1;var progressGate=new object();var watch=Stopwatch.StartNew();
        progress?.Report(new(completed,source.Count));
        async Task Worker()
        {
            while(true)
            {
                token.ThrowIfCancellationRequested();int at=Interlocked.Increment(ref next);if(at>=missing.Count)return;
                int i=missing[at];var photo=source[i];PhotoType type;
                await slots.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    Interlocked.Increment(ref reads);
                    try{type=await probe(photo,token).ConfigureAwait(false);}
                    catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException){type=PhotoType.Unknown;}
                }
                finally{slots.Release();}
                token.ThrowIfCancellationRequested();types[i]=type;
                lock(gate)if(revision==version)
                {
                    if(entries.Count>=16384&&!entries.ContainsKey(photo.Path))entries.Remove(entries.Keys.First());
                    entries[photo.Path]=new(photo.Size,photo.Modified,type);
                }
                int done=Interlocked.Increment(ref completed);
                lock(progressGate)if(watch.ElapsedMilliseconds>=100||done==source.Count){progress?.Report(new(done,source.Count));watch.Restart();}
            }
        }
        await Task.WhenAll(Worker(),Worker()).ConfigureAwait(false);token.ThrowIfCancellationRequested();
        var result=new List<Photo>();int unavailable=0;
        for(int i=0;i<source.Count;i++){if(types[i]==PhotoType.Unknown)unavailable++;if(PhotoTypeFilter.Matches(types[i],selected))result.Add(source[i]);}
        return new PhotoFilterResult(result,unavailable);
    },token);
}
