namespace Luma.App;

// Process-wide decoded pixels; each viewer owns a SafeHandle lease, never the
// cache itself. GPU textures/NGX state remain private to their render context.
internal sealed class FrameCache : IDisposable
{
    internal static FrameCache Shared {get;}=new();
    private readonly SemaphoreSlim foreground=new(1),background=new(1);
    private readonly Dictionary<string,Entry> frames=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,Request> pending=new(StringComparer.OrdinalIgnoreCase);
    private readonly object sync=new();
    private readonly ulong budget;private readonly int limit;
    private long tick,decodes,hits;private bool disposed;
    private readonly record struct Stamp(long Modified,long Length);
    private sealed record Entry(DecodedFrame Frame,Stamp Stamp,long Used);
    private sealed class Request(string key,string path,uint edge,Stamp stamp)
    {
        internal readonly string Key=key,Path=path;internal readonly uint Edge=edge;internal readonly Stamp Stamp=stamp;
        internal readonly TaskCompletionSource<DecodedFrame> Completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Consumers;internal bool Started,Promoted;
        internal FrameLease? Retained;
    }
    internal FrameCache(ulong budget=256*1024*1024,int limit=16){this.budget=budget;this.limit=limit;}
    internal (long Decodes,long Hits,ulong Bytes,int Frames) Metrics
    {
        get{lock(sync)return(decodes,hits,frames.Values.Aggregate(0ul,(n,e)=>n+e.Frame.Info.Bytes),frames.Count);}
    }
    private static Stamp FileStamp(string path){var file=new FileInfo(path);return new(file.LastWriteTimeUtc.Ticks,file.Length);}
    public async Task<FrameLease> GetAsync(string path,CancellationToken token,uint maxEdge=0,bool prefetch=false)
    {
        // A network/removable disk metadata read can block; never do it on XAML.
        token.ThrowIfCancellationRequested();var stamp=await Task.Run(()=>FileStamp(path),token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();string key=path+"\0"+maxEdge;string requestKey=key+"\0"+stamp.Modified+"\0"+stamp.Length;
        Request request;bool start=false,promote=false;
        lock(sync)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(frames.TryGetValue(key,out var hit)&&hit.Stamp==stamp){frames[key]=hit with{Used=++tick};hits++;return new(hit.Frame);}
            if(!pending.TryGetValue(requestKey,out request!)){request=new(key,path,maxEdge,stamp);pending.Add(requestKey,request);start=true;}
            else if(!prefetch&&!request.Started&&!request.Promoted){request.Promoted=true;promote=true;}
            request.Consumers++;
        }
        if(start)_=LoadAsync(request,requestKey,prefetch?background:foreground);
        // An active selection can claim a queued prefetch through the foreground
        // lane. Only the first claimant decodes; the other releases its slot.
        if(promote)_=LoadAsync(request,requestKey,foreground);
        try
        {
            var frame=await request.Completion.Task.WaitAsync(token).ConfigureAwait(false);
            lock(sync){token.ThrowIfCancellationRequested();return new(frame);}
        }
        finally
        {
            lock(sync){request.Consumers--;if(request.Consumers==0&&request.Completion.Task.IsCompleted)Release(request,requestKey);}
        }
    }
    private void Release(Request request,string key)
    {
        request.Retained?.Dispose();request.Retained=null;
        if(pending.TryGetValue(key,out var value)&&value==request)pending.Remove(key);
    }
    private async Task LoadAsync(Request request,string requestKey,SemaphoreSlim workers)
    {
        await workers.WaitAsync().ConfigureAwait(false);DecodedFrame? decoded=null;bool claimed=false;
        try
        {
            lock(sync)
            {
                if(request.Started)return;request.Started=true;claimed=true;
                ObjectDisposedException.ThrowIf(disposed,this);
                if(request.Consumers==0){request.Completion.TrySetCanceled();Release(request,requestKey);return;}
                decodes++;
            }
            decoded=await Task.Run(()=>{Native.Check(Native.LumaDecodeSized(request.Path,request.Edge,out var info,out var pointer));return new DecodedFrame(pointer,info);}).ConfigureAwait(false);
            // Never cache pixels under a stamp that changed while WIC was reading.
            bool unchanged=FileStamp(request.Path)==request.Stamp;
            lock(sync)
            {
                ObjectDisposedException.ThrowIf(disposed,this);
                request.Retained=new(decoded);
                if(unchanged){if(frames.Remove(request.Key,out var old))old.Frame.Dispose();frames[request.Key]=new(decoded,request.Stamp,++tick);TrimLocked();}
                else decoded.Dispose();
                request.Completion.TrySetResult(decoded);decoded=null;
                if(request.Consumers==0)Release(request,requestKey);
            }
        }
        catch(Exception ex)
        {
            if(claimed)lock(sync){request.Completion.TrySetException(ex);if(request.Consumers==0){_ = request.Completion.Task.Exception;Release(request,requestKey);}}
        }
        finally{decoded?.Dispose();workers.Release();}
    }
    private void TrimLocked()
    {
        ulong total=frames.Values.Aggregate(0ul,(n,e)=>n+e.Frame.Info.Bytes);
        foreach(var item in frames.OrderBy(x=>x.Value.Used).ToArray())
        {if(total<=budget&&frames.Count<=limit)break;frames.Remove(item.Key);total-=item.Value.Frame.Info.Bytes;item.Value.Frame.Dispose();}
    }
    public void Trim(string current){lock(sync)TrimLocked();}
    public void Dispose(){lock(sync){disposed=true;foreach(var entry in frames.Values)entry.Frame.Dispose();frames.Clear();}}
}
