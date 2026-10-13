namespace Luma.App;

// A retired device has no window, photo, output, blur surface or feature instance. Its context is
// exclusively transferred after all work stops; live windows never share it.
internal static class RenderDeviceCache
{
    internal const int RetentionMilliseconds=10000;
    private sealed record Entry(IntPtr Handle,bool Sdk,long Deadline);
    private static readonly object gate=new();
    private static Entry? idle;
    private static long hits,creates,expired,rejected;
    internal record struct CacheMetrics(int Retained,long Hits,long Creates,long Expired,long Rejected);
    internal static CacheMetrics Metrics {get{lock(gate)return new(idle is null?0:1,hits,creates,expired,rejected);}}
    internal static Task<object> MemoryAsync()=>Task.Run<object>(()=>
    {
        lock(gate)
        {
            ulong budget=0,usage=0,photoBytes=0;
            if(idle is {} entry){Native.Check(Native.LumaVideoMemory(entry.Handle,out budget,out usage));Native.Check(Native.LumaCopyStatus(entry.Handle,out var status));photoBytes=status.GpuBytes;}
            return new{cache=Metrics,budgetBytes=budget,processLocalGpuBytes=usage,idlePhotoSurfaceBytes=photoBytes};
        }
    });
    internal static async Task<(IntPtr Handle,bool Reused)> AcquireAsync(IntPtr hwnd,bool enhancements)
    {
        Entry? cached=null;
        lock(gate)
        {
            // Preserve the full warm SDK slot for enhancement requests. Native
            // lifetime coordination may reuse an already registered bare device;
            // opening SDR still performs no new SDK initialization or AI work.
            if(idle is {} entry&&(enhancements||!entry.Sdk)){cached=entry;idle=null;}
        }
        return await Task.Run(()=>
        {
            if(cached is not null)
            {
                bool valid=cached.Deadline>Environment.TickCount64&&Native.LumaBindWindow(cached.Handle,hwnd)==0;
                if(valid){lock(gate)hits++;return(cached.Handle,true);}
                Native.LumaDestroy(cached.Handle);lock(gate)rejected++;
            }
            Native.Check(Native.LumaCreateFast(IntPtr.Zero,hwnd,out var next));lock(gate)creates++;return(next,false);
        });
    }
    internal static async Task RetireAsync(IntPtr handle,bool sdk,bool healthy)
    {
        if(handle==IntPtr.Zero)return;
        bool reusable=healthy&&await Task.Run(()=>Native.LumaResetForReuse(handle)==0);
        if(!reusable){await Task.Run(()=>Native.LumaDestroy(handle));lock(gate)rejected++;return;}
        var next=new Entry(handle,sdk,Environment.TickCount64+RetentionMilliseconds);Entry? discard=null;bool stored=false;
        lock(gate)
        {
            // One full-renderer slot; prefer a warmed SDK session over an SDR device.
            if(idle is null||idle.Deadline<=Environment.TickCount64||sdk&&!idle.Sdk){discard=idle;idle=next;stored=true;}
            else{discard=next;rejected++;}
        }
        if(stored)_=ExpireAsync(next);
        if(discard is not null)await Task.Run(()=>Native.LumaDestroy(discard.Handle));
    }
    private static async Task ExpireAsync(Entry entry)
    {
        await Task.Delay(RetentionMilliseconds).ConfigureAwait(false);
        bool destroy=false;lock(gate){if(ReferenceEquals(idle,entry)){idle=null;expired++;destroy=true;}}
        if(destroy)await Task.Run(()=>Native.LumaDestroy(entry.Handle)).ConfigureAwait(false);
    }
    internal static async Task ClearAsync()
    {
        Entry? entry;lock(gate){entry=idle;idle=null;}
        if(entry is not null)await Task.Run(()=>Native.LumaDestroy(entry.Handle));
    }
}
