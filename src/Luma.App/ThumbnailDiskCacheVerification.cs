using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Luma.App;

internal static class ThumbnailDiskCacheVerification
{
    internal static async Task RunAsync(MainWindow host)
    {
        string report=Path.Combine(App.DataDirectory,"thumbnail-disk-cache.json");int checks=0;var pool=ArrayPool<byte>.Create(4*1024*1024,4);
        void Check(bool valid,string name){if(!valid)throw new InvalidOperationException(name);checks++;}
        bool Equal(string path,uint edge,byte[] expected)
        {
            var cached=ThumbnailDiskCache.Read(path,edge,ThumbnailDiskCache.SourceStamp(path),pool);
            if(cached is not {} result)return false;
            try{return result.pixels.AsSpan(0,(int)result.info.Bytes).SequenceEqual(expected);}finally{pool.Return(result.pixels);}
        }
        try
        {
            await host.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Task.Run(()=>
            {
                string source=Path.Combine(App.DataDirectory,"owned-source.bin"),folder=Path.Combine(App.DataDirectory,"thumbnail-cache-v1");File.WriteAllBytes(source,[1,2,3]);var stamp=ThumbnailDiskCache.SourceStamp(source);
                var info=new FrameInfo{Width=2,Height=2,OriginalWidth=2,OriginalHeight=2,Bytes=16};byte[] pixels=[25,50,100,128,0,0,0,0,0,128,255,255,40,20,6,64];
                ThumbnailDiskCache.Store(source,64,stamp,info,pixels);Check(Equal(source,64,pixels),"Premultiplied pixels changed after lossless cache roundtrip");
                Check(ThumbnailDiskCache.Read(source,128,stamp,pool) is null,"Different DPI/size reused insufficient pixels");
                string file=Directory.GetFiles(folder,"*.lth").Single();using(var corrupt=new FileStream(file,FileMode.Open,FileAccess.Write)){corrupt.Position=32;corrupt.WriteByte(200);}
                Check(ThumbnailDiskCache.Read(source,64,stamp,pool) is null,"Corrupt cache pixels were accepted");
                ThumbnailDiskCache.Store(source,64,stamp,info,pixels);Check(Equal(source,64,pixels),"Corrupt cache did not regenerate");
                using(var corrupt=new FileStream(file,FileMode.Open,FileAccess.Write)){corrupt.Position=12;corrupt.WriteByte(3);}
                Check(ThumbnailDiskCache.Read(source,64,stamp,pool) is null,"Corrupt cache dimensions were accepted");ThumbnailDiskCache.Store(source,64,stamp,info,pixels);
                File.AppendAllText(source,"changed");Check(ThumbnailDiskCache.Read(source,64,ThumbnailDiskCache.SourceStamp(source),pool) is null,"Modified source reused stale cache");
                long writes=ThumbnailDiskCache.Writes;info.NativeHdr=1;ThumbnailDiskCache.Store(source,64,ThumbnailDiskCache.SourceStamp(source),info,pixels);Check(ThumbnailDiskCache.Writes==writes,"Native HDR was persisted as SDR");
                // A small isolated budget exercises actual on-disk eviction.
                ThumbnailDiskCache.VerificationBudget=1100*1024;info=new(){Width=512,Height=256,OriginalWidth=512,OriginalHeight=256,Bytes=512*256*4};pixels=new byte[(int)info.Bytes];RandomNumberGenerator.Fill(pixels);
                var paths=Enumerable.Range(0,3).Select(n=>Path.Combine(App.DataDirectory,$"quota-{n}.bin")).ToArray();foreach(var path in paths)File.WriteAllBytes(path,[1]);
                foreach(string path in paths[..2])ThumbnailDiskCache.Store(path,512,ThumbnailDiskCache.SourceStamp(path),info,pixels);
                Check(Equal(paths[0],512,pixels),"Recent cache read failed");ThumbnailDiskCache.Store(paths[2],512,ThumbnailDiskCache.SourceStamp(paths[2]),info,pixels);
                Check(Equal(paths[0],512,pixels)&&Equal(paths[2],512,pixels),"Recently used or newest cache entry was evicted");
                Check(ThumbnailDiskCache.Read(paths[1],512,ThumbnailDiskCache.SourceStamp(paths[1]),pool) is null,"Oldest unused entry was not evicted");
                Check(Directory.EnumerateFiles(folder,"*.lth").Sum(p=>new FileInfo(p).Length)<=1100*1024,"Cache exceeded its disk budget");
                Check(!Directory.EnumerateFiles(folder,"*.tmp").Any(),"Temporary cache files remained after completed writes");
            });
            File.WriteAllText(report,JsonSerializer.Serialize(new{complete=true,checks}));
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{complete=false,checks,failure=ex.ToString()}));Environment.ExitCode=1;}
        finally{ThumbnailDiskCache.VerificationBudget=null;host.Close();}
    }
}
