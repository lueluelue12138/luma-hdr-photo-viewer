using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Luma.App;

// Lossless, disposable derived pixels. Source photographs are only read.
internal static class ThumbnailDiskCache
{
    internal readonly record struct Stamp(long Length,long Modified);
    private const uint Magic=0x31544D4C;
    private const long Budget=256*1024*1024,MaxFile=4*1024*1024+8192;
    private static readonly object Sync=new();
    private static Dictionary<string,(long Bytes,long Used)>? entries;
    private static long bytes,tick,hits,writes;
    private static long indexedGeneration=-1;
    internal static long? VerificationBudget;
    internal static long Bytes=>Interlocked.Read(ref bytes);
    internal static long Hits=>Interlocked.Read(ref hits);
    internal static long Writes=>Interlocked.Read(ref writes);
    internal static Stamp SourceStamp(string path){var file=new FileInfo(path);return new(file.Length,file.LastWriteTimeUtc.Ticks);}
    private static string Folder=>Path.Combine(App.DataDirectory,"thumbnail-cache-v1");
    private static byte[] Digest(FrameInfo info,ReadOnlySpan<byte> pixels)
    {
        Span<byte> header=stackalloc byte[28];BinaryPrimitives.WriteUInt32LittleEndian(header,info.Width);BinaryPrimitives.WriteUInt32LittleEndian(header[4..],info.Height);BinaryPrimitives.WriteUInt32LittleEndian(header[8..],info.OriginalWidth);BinaryPrimitives.WriteUInt32LittleEndian(header[12..],info.OriginalHeight);BinaryPrimitives.WriteInt32LittleEndian(header[16..],info.Downsampled);BinaryPrimitives.WriteUInt64LittleEndian(header[20..],info.Bytes);
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);hash.AppendData(header);hash.AppendData(pixels);return hash.GetHashAndReset();
    }
    private static string CachePath(string path,uint edge,Stamp stamp)
    {
        string identity=Path.GetFullPath(path).ToUpperInvariant()+"\0"+edge+"\0"+stamp.Length+"\0"+stamp.Modified;
        return Path.Combine(Folder,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))+".lth");
    }
    internal static (FrameInfo info,byte[] pixels)? Read(string source,uint edge,Stamp stamp,ArrayPool<byte> pool)
    {
        byte[]? pixels=null;
        try
        {
            string path=CachePath(source,edge,stamp);
            using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
            if(file.Length>MaxFile)return null;
            using var header=new BinaryReader(file,Encoding.UTF8,true);
            if(header.ReadUInt32()!=Magic)return null;
            var info=new FrameInfo{Width=header.ReadUInt32(),Height=header.ReadUInt32(),OriginalWidth=header.ReadUInt32(),OriginalHeight=header.ReadUInt32(),Downsampled=header.ReadInt32(),Bytes=header.ReadUInt64()};
            if(info.Width==0||info.Height==0||Math.Max(info.Width,info.Height)>edge||edge>1024||info.OriginalWidth==0||info.OriginalHeight==0||info.Bytes!=info.Width*(ulong)info.Height*4)return null;
            var digest=header.ReadBytes(32);if(digest.Length!=32)return null;
            pixels=pool.Rent(checked((int)info.Bytes));
            using(var compressed=new ZLibStream(file,CompressionMode.Decompress,true))
            {compressed.ReadExactly(pixels.AsSpan(0,(int)info.Bytes));if(compressed.ReadByte()!=-1)return null;}
            if(!CryptographicOperations.FixedTimeEquals(digest,Digest(info,pixels.AsSpan(0,(int)info.Bytes)))||SourceStamp(source)!=stamp)return null;
            lock(Sync){if(entries is not null&&entries.TryGetValue(path,out var item))entries[path]=(item.Bytes,++tick);}
            // Persist recency for the next process's lazy index rebuild.
            try{File.SetLastWriteTimeUtc(path,DateTime.UtcNow);}catch(IOException){}catch(UnauthorizedAccessException){}
            Interlocked.Increment(ref hits);var result=(info,pixels);pixels=null;return result;
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException or OverflowException){return null;}
        finally{if(pixels is not null)pool.Return(pixels);}
    }
    internal static void Store(string source,uint edge,Stamp stamp,FrameInfo info,byte[] pixels)
    {
        if(info.NativeHdr!=0||info.Bytes>4*1024*1024)return;
        string final=CachePath(source,edge,stamp),temporary=final+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            if(SourceStamp(source)!=stamp)return;
            Directory.CreateDirectory(Folder);
            using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                using var header=new BinaryWriter(file,Encoding.UTF8,true);header.Write(Magic);header.Write(info.Width);header.Write(info.Height);header.Write(info.OriginalWidth);header.Write(info.OriginalHeight);header.Write(info.Downsampled);header.Write(info.Bytes);header.Write(Digest(info,pixels.AsSpan(0,(int)info.Bytes)));header.Flush();
                using var compressed=new ZLibStream(file,CompressionLevel.Fastest,true);compressed.Write(pixels.AsSpan(0,(int)info.Bytes));
            }
            long length=new FileInfo(temporary).Length;if(length>MaxFile||SourceStamp(source)!=stamp)return;
            lock(Sync)
            {
                // Other Luma processes can share this directory. A busy writer
                // merely skips this optional cache write instead of blocking.
                using var gate=new FileStream(Path.Combine(Folder,"index.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
                long generation=0;if(gate.Length==8){using var reader=new BinaryReader(gate,Encoding.UTF8,true);generation=reader.ReadInt64();}
                if(entries is null||generation!=indexedGeneration)
                {
                    entries=new(StringComparer.OrdinalIgnoreCase);bytes=0;tick=0;
                    foreach(var file in new DirectoryInfo(Folder).EnumerateFiles("*.lth").OrderBy(f=>f.LastWriteTimeUtc))
                        if(file.Name.Length==68&&file.Name[..64].All(Uri.IsHexDigit)){entries[file.FullName]=(file.Length,++tick);bytes+=file.Length;}
                }
                entries.TryGetValue(final,out var old);
                long budget=VerificationBudget??Budget;if(length>budget)return;
                while(bytes-old.Bytes+length>budget||entries.Count-(old.Bytes>0?1:0)>=4096)
                {
                    var oldest=entries.Where(p=>p.Key!=final).MinBy(p=>p.Value.Used);File.Delete(oldest.Key);entries.Remove(oldest.Key);bytes-=oldest.Value.Bytes;
                }
                File.Move(temporary,final,true);entries[final]=(length,++tick);bytes+=length-old.Bytes;
                gate.Position=0;using var writer=new BinaryWriter(gate,Encoding.UTF8,true);writer.Write(generation+1);writer.Flush();gate.SetLength(8);indexedGeneration=generation+1;
                Interlocked.Increment(ref writes);
            }
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){ /* Cache failure never prevents viewing. */ }
        finally{try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}
