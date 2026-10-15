using System.Buffers.Binary;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Luma.Core;

public sealed record PhotoFeatures(string? HdrKind,LivePhotoAsset? Live);

public static partial class LivePhotoReader
{
    // File traits only: no decoder, pixel reconstruction, GPU or SDK startup.
    // HDR here means declared HDR metadata; successful presentation is verified
    // separately by the viewer. Never label an ordinary SDR file as RTX HDR.
    public static PhotoFeatures ReadFeatures(string path,CancellationToken token=default)
    {
        string? hdr=null;
        try
        {
            using var file=new Reader(path,token);
            hdr=Path.GetExtension(path).ToLowerInvariant() switch
            {".jpg" or ".jpeg"=>JpegHdr(file),".heic" or ".heif"=>HeifHdr(file),_=>null};
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException or XmlException or ArgumentException or OverflowException){}
        return new(hdr,Detect(path,token));
    }
    static string? JpegHdr(Reader file)
    {
        var starts=new Queue<long>();starts.Enqueue(0);var seen=new HashSet<long>();
        while(starts.TryDequeue(out long origin)&&seen.Count<4)
        {
            if(!seen.Add(origin)||!file.Bytes(origin,2).SequenceEqual(new byte[]{255,216}))continue;
            long at=origin+2;int read=0;
            for(int segments=0;segments<512&&at+4<=file.Length;segments++)
            {
                var h=file.Bytes(at,4);if(h[0]!=255||h[1] is 0xda or 0xd9)break;
                if(h[1]==255){at++;continue;}if(h[1] is >=0xd0 and <=0xd7 or 1){at+=2;continue;}
                int n=BinaryPrimitives.ReadUInt16BigEndian(h.AsSpan(2));if(n<2||at+2+n>file.Length)break;
                if(h[1] is 0xe1 or 0xe2)
                {
                    read+=n;if(read>MetadataLimit)break;
                    var data=file.Bytes(at+4,n-2);
                    if(h[1]==0xe2&&data.AsSpan().StartsWith("MPF\0"u8))
                        foreach(long offset in MpfImages(data,at+8,file.Length))starts.Enqueue(offset);
                    if(origin>0&&h[1]==0xe2&&data.AsSpan().StartsWith("urn:iso:std:iso:ts:21496:-1\0"u8))return "ISO 增益图 JPEG";
                    ReadOnlySpan<byte> prefix="http://ns.adobe.com/xap/1.0/\0"u8;
                    if(h[1]==0xe1&&data.AsSpan().StartsWith(prefix))
                    {
                        using var xml=XmlReader.Create(new MemoryStream(data[prefix.Length..]),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MetadataLimit});
                        var document=XDocument.Load(xml);
                        var names=document.Descendants().Select(e=>e.Name).Concat(document.Descendants().Attributes().Select(a=>a.Name)).ToArray();
                        if(names.Any(x=>x.NamespaceName=="http://ns.adobe.com/hdr-gain-map/1.0/"&&x.LocalName=="Version"))return "HDR 增益图 JPEG";
                        if(names.Any(x=>x.NamespaceName.StartsWith("http://ns.apple.com/",StringComparison.Ordinal)&&x.LocalName=="HDRGainMapVersion"))return "Apple 增益图 JPEG";
                    }
                }
                at+=n+2;
            }
        }
        return null;
    }
    static List<long> MpfImages(byte[] packet,long origin,long fileLength)
    {
        var data=packet[4..];var result=new List<long>();if(data.Length<8)return result;
        bool little=data[0]=='I'&&data[1]=='I';if(!little&&!(data[0]=='M'&&data[1]=='M'))return result;
        ushort N16(int p)=>little?BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p)):BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p));
        uint N32(int p)=>little?BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p)):BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p));
        int start=checked((int)N32(4)),count=N16(start);if(count>512)return result;
        for(int i=0;i<count;i++)
        {
            int p=checked(start+2+i*12);if(N16(p)!=0xb002)continue;
            int bytes=checked((int)N32(p+4)),at=checked((int)N32(p+8));if(bytes<16||bytes>16*16||bytes%16!=0||at<0||at>data.Length-bytes)return result;
            for(int j=0;j<bytes/16;j++){long length=N32(at+j*16+4),offset=N32(at+j*16+8),absolute=checked(origin+offset);if(offset>0&&length>=4&&absolute>=0&&absolute<=fileLength-length)result.Add(absolute);}
        }
        return result;
    }
    static string? HeifHdr(Reader file)
    {
        var meta=file.Boxes(0,file.Length).FirstOrDefault(b=>b.Type=="meta");if(meta.Size==0||meta.Size>MetadataLimit)return null;
        var children=file.Boxes(meta.Data+4,meta.Size-meta.Header-4).ToList();
        var items=new Dictionary<uint,string>();var attached=new Dictionary<uint,List<int>>();var refs=new List<(string Type,uint From,List<uint> To)>();
        var properties=new List<Box>();uint primary=0;
        foreach(var box in children)
        {
            if(box.Type=="pitm"){var c=new Cursor(file.Bytes(box.Data,checked((int)(box.Size-box.Header))));int v=c.U8();c.Skip(3);primary=v==0?c.U16():c.U32();}
            if(box.Type=="iinf")
            {
                int skip=file.Bytes(box.Data,1)[0]==0?6:8;
                foreach(var item in file.Boxes(box.Data+skip,box.Size-box.Header-skip).Where(b=>b.Type=="infe"))
                {var c=new Cursor(file.Bytes(item.Data,checked((int)(item.Size-item.Header))));int v=c.U8();c.Skip(3);if(v is not (2 or 3))continue;uint id=v==2?c.U16():c.U32();c.Skip(2);items[id]=c.Text(4);}
            }
            if(box.Type=="iref")
            {
                int v=file.Bytes(box.Data,1)[0];if(v>1)continue;
                foreach(var link in file.Boxes(box.Data+4,box.Size-box.Header-4))
                {var c=new Cursor(file.Bytes(link.Data,checked((int)(link.Size-link.Header))));uint from=v==0?c.U16():c.U32();int n=c.U16();if(n>4096)throw new InvalidDataException();var targets=new List<uint>();for(int j=0;j<n;j++)targets.Add(v==0?c.U16():c.U32());refs.Add((link.Type,from,targets));}
            }
            if(box.Type=="iprp")foreach(var entry in file.Boxes(box.Data,box.Size-box.Header))
            {
                if(entry.Type=="ipco")properties=file.Boxes(entry.Data,entry.Size-entry.Header).ToList();
                if(entry.Type!="ipma")continue;
                var c=new Cursor(file.Bytes(entry.Data,checked((int)(entry.Size-entry.Header))));int v=c.U8(),flags=c.U8()<<16|c.U8()<<8|c.U8();uint n=c.U32();if(n>4096)throw new InvalidDataException();
                for(int j=0;j<n;j++){uint id=v==0?c.U16():c.U32();int count=c.U8();var indexes=new List<int>();for(int k=0;k<count;k++)indexes.Add((flags&1)!=0?c.U16()&0x7fff:c.U8()&0x7f);attached[id]=indexes;}
            }
        }
        IEnumerable<Box> Props(uint id)=>attached.TryGetValue(id,out var indexes)?indexes.Where(n=>n>0&&n<=properties.Count).Select(n=>properties[n-1]):[];
        foreach(var link in refs)
        {
            if(link.Type=="dimg"&&items.GetValueOrDefault(link.From)=="tmap"&&link.To.Count==2&&(link.From==primary||link.To[0]==primary)&&link.To.All(items.ContainsKey))return "ISO 增益图 HEIC";
            if(link.Type=="auxl"&&link.To.Contains(primary)&&Props(link.From).Any(b=>b.Type=="auxC"&&b.Size>b.Header+4&&Encoding.UTF8.GetString(file.Bytes(b.Data+4,checked((int)(b.Size-b.Header-4)))).TrimEnd('\0')=="urn:com:apple:photo:2020:aux:hdrgainmap"))return "Apple 增益图 HEIC";
        }
        foreach(var colour in Props(primary).Where(b=>b.Type=="colr"&&b.Size>=b.Header+10))
        {var c=new Cursor(file.Bytes(colour.Data,10));if(c.Text(4)!="nclx")continue;c.U16();int transfer=c.U16();if(transfer==16)return "PQ HEIC";if(transfer==18)return "HLG HEIC";}
        return null;
    }
}
