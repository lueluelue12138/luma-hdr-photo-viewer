using Luma.Core;

internal static class PhotoTypeFilterTests
{
    internal static async Task RunAsync(string root,Action<bool,string> assert)
    {
        var photos=Enumerable.Range(0,5).Select(i=>new Photo(Path.Combine(root,"types",$"{i}.jpg"),$"{i}.jpg",10,20)).ToList();
        PhotoType[] types=[PhotoType.Hdr,PhotoType.Live,PhotoType.Hdr|PhotoType.Live,PhotoType.Standard,PhotoType.Unknown];
        int active=0,maxActive=0;
        var index=new PhotoTypeIndex(async(p,token)=>
        {
            int now=Interlocked.Increment(ref active);InterlockedExtensions.Max(ref maxActive,now);
            try{await Task.Delay(2,token);return types[int.Parse(Path.GetFileNameWithoutExtension(p.Path))];}finally{Interlocked.Decrement(ref active);}
        });
        var first=await index.FilterAsync(photos,PhotoType.Hdr);
        assert(first.Photos.SequenceEqual(new[]{photos[0],photos[2]})&&first.Unavailable==1,"HDR filtering includes HDR Live and excludes unknown files");
        long reads=index.Reads;var live=await index.FilterAsync(photos,PhotoType.Live);
        assert(live.Photos.SequenceEqual(new[]{photos[1],photos[2]})&&index.Reads==reads,"Live selection reuses metadata with zero additional probes");
        var union=await index.FilterAsync(photos,PhotoType.Hdr|PhotoType.Live);
        assert(union.Photos.SequenceEqual(photos.Take(3)),"multiselect uses union and does not duplicate HDR Live photos");
        assert((await index.FilterAsync(photos,PhotoType.Standard)).Photos.SequenceEqual(new[]{photos[3]}),"ordinary static excludes HDR, Live and unreadable photos");
        foreach(var selected in new[]{PhotoType.Unknown,PhotoType.All,(PhotoType)128})
            assert((await index.FilterAsync(photos,selected)).Photos.SequenceEqual(photos)&&index.Reads==reads,"clear/all/invalid selection keeps every photo without probing");
        assert(maxActive<=2,"one filter has at most two metadata workers");
        var changed=photos.ToList();changed[0]=photos[0] with{Modified=21};types[0]=PhotoType.Standard;
        assert((await index.FilterAsync(changed,PhotoType.Hdr)).Photos.SequenceEqual(new[]{photos[2]})&&index.Reads==reads+1,"changed file stamps invalidate exactly that cached classification");
        index.InvalidateFolder(Path.Combine(root,"types"));reads=index.Reads;await index.FilterAsync(changed,PhotoType.Live);
        assert(index.Reads==reads+5&&index.Revision>0,"folder invalidation also refreshes negative Live results after sidecar changes");

        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);bool block=true;
        var cancellable=new PhotoTypeIndex(async(_,token)=>{entered.TrySetResult();if(block)await Task.Delay(Timeout.Infinite,token);return PhotoType.Hdr;});
        using(var cts=new CancellationTokenSource())
        {
            var waiting=cancellable.FilterAsync(photos,PhotoType.Hdr,token:cts.Token);await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));cts.Cancel();
            bool canceled=false;try{await waiting;}catch(OperationCanceledException){canceled=true;}
            assert(canceled,"canceling an obsolete filter propagates cancellation rather than publishing partial results");
        }
        block=false;assert((await cancellable.FilterAsync(photos,PhotoType.Hdr)).Photos.Count==5,"cancellation releases metadata slots for the next request");
        var inaccessible=new PhotoTypeIndex((_,_)=>throw new UnauthorizedAccessException());
        var failed=await inaccessible.FilterAsync(photos,PhotoType.Standard);
        assert(failed.Photos.Count==0&&failed.Unavailable==5,"unreadable files report unavailable instead of becoming ordinary photos");
        int concurrent=0,peak=0;
        var bounded=new PhotoTypeIndex(async(_,token)=>{int n=Interlocked.Increment(ref concurrent);InterlockedExtensions.Max(ref peak,n);try{await Task.Delay(3,token);return PhotoType.Hdr;}finally{Interlocked.Decrement(ref concurrent);}});
        await Task.WhenAll(bounded.FilterAsync(photos,PhotoType.Hdr),bounded.FilterAsync(photos,PhotoType.Live));
        assert(peak<=2,"overlapping requests share the same two metadata slots");
        assert(PhotoTypeFilter.FromFeatures(new(null,null))==PhotoType.Standard&&PhotoTypeFilter.FromFeatures(new("HDR",null))==PhotoType.Hdr,"metadata classification does not infer HDR from RTX display enhancement");
        var paths=(await new LibraryStore(Path.Combine(root,"library.db")).AlbumPhotoPathsAsync()).Values.SelectMany(p=>p).ToList();
        assert(paths.All(p=>!p.EndsWith("two.png",StringComparison.OrdinalIgnoreCase)),"album projection excludes missing photo links");
    }
    private static class InterlockedExtensions
    {
        internal static void Max(ref int value,int proposed){int seen;do{seen=Volatile.Read(ref value);if(proposed<=seen)return;}while(Interlocked.CompareExchange(ref value,proposed,seen)!=seen);}
    }
}
