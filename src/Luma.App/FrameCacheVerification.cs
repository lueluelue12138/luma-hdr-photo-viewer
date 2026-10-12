using System.Diagnostics;

namespace Luma.App;

internal static class FrameCacheVerification
{
    internal static async Task<object> RunAsync(string source,ViewerWindow viewer)
    {
        void Check(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
        string path=Path.Combine(App.DataDirectory,"decode-fixture.jpg");File.Copy(source,path,true);
        using var cache=new FrameCache();using var cancelled=new CancellationTokenSource();
        var first=cache.GetAsync(path,CancellationToken.None,2048,true);
        var second=cache.GetAsync(path,CancellationToken.None,2048);
        var abandoned=cache.GetAsync(path,cancelled.Token,2048);await Task.Delay(20);Check(!abandoned.IsCompleted,"Fixture must exercise cancellation during a real decode");cancelled.Cancel();
        try{using var unused=await abandoned;throw new InvalidOperationException("Cancelled consumer returned pixels");}catch(OperationCanceledException){}
        using var one=await first;using var two=await second;
        Check(cache.Metrics.Decodes==1&&one.Frame==two.Frame,"Foreground/prefetch must share one decode; cancelling one caller must preserve another");
        // Metadata changes invalidate a real cached image, including same-name edits.
        File.SetLastWriteTimeUtc(path,File.GetLastWriteTimeUtc(path).AddSeconds(5));
        using var updated=await cache.GetAsync(path,CancellationToken.None,2048);
        Check(cache.Metrics.Decodes==2&&updated.Frame!=one.Frame,"Modified file must decode fresh pixels");
        using(var evicting=new FrameCache(1,1))
        {
            using var lease=await evicting.GetAsync(path,CancellationToken.None,1024);
            Check(evicting.Metrics.Bytes==0,"Oversized entries must stay outside the cache budget");
            evicting.Dispose();await viewer.VerificationShowFrameAsync(lease.Frame);
        }
        await viewer.VerificationSelectAsync(0);
        var before=FrameCache.Shared.Metrics;var reopen=new List<double>();
        for(int n=0;n<4;n++)
        {
            var watch=Stopwatch.StartNew();var next=new ViewerWindow(source);next.Activate();
            try{await next.FirstFrameReady.Task.WaitAsync(TimeSpan.FromSeconds(30));reopen.Add(watch.Elapsed.TotalMilliseconds);await next.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));}
            finally{next.Close();await next.VerificationDisposeAsync();}
        }
        var after=FrameCache.Shared.Metrics;
        // Every size requested by these equal-sized windows is now cached.
        Check(after.Hits>before.Hits&&after.Bytes<=256ul*1024*1024,"Reopened windows must share a bounded decoded cache");
        return new{scenario="shared-decoded-cache",singleDecode=cache.Metrics.Decodes==2,cancelledConsumer=true,modifiedFile=true,evictedLeaseRendered=true,reopenedMilliseconds=reopen,additionalDecodes=after.Decodes-before.Decodes,hits=after.Hits-before.Hits,cachedBytes=after.Bytes};
    }
}
