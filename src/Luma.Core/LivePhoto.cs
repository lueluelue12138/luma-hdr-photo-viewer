using System.Buffers.Binary;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Luma.Core;

public sealed record LivePhotoAsset(string PhotoPath,string VideoPath,long Offset,long Length,string Kind,
    long? PhotoTimeMicroseconds,string? ContentIdentifier,bool NativeVideoHdr,long SourceLength,long SourceModified,long VideoModified)
{
    public bool Embedded=>!string.Equals(PhotoPath,VideoPath,StringComparison.OrdinalIgnoreCase)?false:Offset>0;
}

// Only the selected photo is probed, after its still preview is visible. Read
// bounded metadata and seek over media payloads; never decode or scan an album.
public static class LivePhotoReader
{
    const int MetadataLimit=4*1024*1024;
    const long VideoLimit=256L*1024*1024;
    static readonly XNamespace Camera="http://ns.google.com/photos/1.0/camera/";
    static readonly XNamespace Item="http://ns.google.com/photos/1.0/container/item/";
    static readonly string[] VideoExtensions=[".mov",".mp4",".m4v"];
    sealed record Metadata(List<byte[]> Xmp,List<byte[]> Exif);
    sealed record Movie(string? Identifier,bool Hdr,long Length);
    readonly record struct Box(long Start,long Size,int Header,string Type){public long Data=>Start+Header;public long End=>Start+Size;}

    public static Task<LivePhotoAsset?> DetectAsync(string photo,CancellationToken token=default)=>Task.Run(()=>Detect(photo,token),token);
    public static LivePhotoAsset? Detect(string photo,CancellationToken token=default)
    {
        string extension=Path.GetExtension(photo).ToLowerInvariant();
        if(extension is not (".jpg" or ".jpeg" or ".heic" or ".heif"))return null;
        try
        {
            using var file=new Reader(photo,token);
            var metadata=extension is ".jpg" or ".jpeg"?JpegMetadata(file):HeifMetadata(file);
            var documents=new List<XDocument>();
            foreach(var xmp in metadata.Xmp)
            {
                using var xml=XmlReader.Create(new MemoryStream(xmp),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MetadataLimit});
                documents.Add(XDocument.Load(xml));
            }
            // A separate packet must not override an explicit disabled flag.
            if(documents.Any(d=>d.Descendants().Attributes(Camera+"MotionPhoto").Select(a=>a.Value)
                .Concat(d.Descendants(Camera+"MotionPhoto").Select(e=>e.Value)).Any(v=>v!="1")))return null;
            bool modern=documents.Any(d=>d.Descendants().Attributes(Camera+"MotionPhoto").Any()||d.Descendants(Camera+"MotionPhoto").Any());
            foreach(var document in documents)
            {
                string? Value(string name)=>document.Descendants().Attributes(Camera+name).Select(a=>a.Value).FirstOrDefault()
                    ??document.Descendants(Camera+name).Select(e=>e.Value).FirstOrDefault();
                var motion=Value("MotionPhoto");
                if(motion=="1")
                {
                    var items=document.Descendants().Where(e=>e.Attribute(Item+"Mime") is not null).Take(33).ToList();
                    var videos=items.Where(e=>(string?)e.Attribute(Item+"Semantic")=="MotionPhoto").ToList();
                    if(items.Count<=32&&videos.Count==1&&videos[0]==items.LastOrDefault()
                        &&(string?)videos[0].Attribute(Item+"Mime") is "video/mp4" or "video/quicktime"
                        &&long.TryParse((string?)videos[0].Attribute(Item+"Length"),out long length))
                    {
                        var result=Embedded(file,photo,file.Length-length,length,"Android Motion Photo",Timestamp(Value("MotionPhotoPresentationTimestampUs")));
                        if(result is not null)return result;
                    }
                }
                // Legacy compatibility is deliberately not allowed to override
                // an explicit modern MotionPhoto value (including 0).
                else if(!modern&&motion is null&&Value("MicroVideo")=="1"&&long.TryParse(Value("MicroVideoOffset"),out long length))
                {
                    var result=Embedded(file,photo,file.Length-length,length,"Google MicroVideo",Timestamp(Value("MicroVideoPresentationTimestampUs")));
                    if(result is not null)return result;
                }
            }
            // Samsung / standard HEIF puts the movie in a bounded top-level mpvd.
            if(extension is ".heic" or ".heif")
                foreach(var box in file.Boxes(0,file.Length))if(box.Type=="mpvd")
                {
                    var result=Embedded(file,photo,box.Data,box.Size-box.Header,"HEIF Motion Photo",null);
                    if(result is not null)return result;
                }
            string? identifier=metadata.Exif.Select(AppleIdentifier).FirstOrDefault(s=>s is not null);
            if(identifier is null)return null;
            string folder=Path.GetDirectoryName(photo)!,stem=Path.GetFileNameWithoutExtension(photo);
            var preferred=VideoExtensions.SelectMany(ext=>new[]{Path.Combine(folder,stem+ext),Path.Combine(folder,stem+"_HEVC"+ext)});
            // Renamed pairs may still be found by identifier, with a fixed cap.
            // Matching names alone never prove a Live Photo relationship.
            var candidates=preferred.Concat(System.IO.Directory.EnumerateFiles(folder).Take(4096).Where(p=>VideoExtensions.Contains(Path.GetExtension(p),StringComparer.OrdinalIgnoreCase)).Take(128))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(134);
            foreach(var video in candidates)
            {
                token.ThrowIfCancellationRequested();if(!File.Exists(video))continue;
                try
                {
                    using var movie=new Reader(video,token);if(movie.Length>VideoLimit)continue;
                    var info=ReadMovie(movie,0,movie.Length);
                    if(info?.Identifier==identifier)return Asset(file,photo,video,0,movie.Length,"Apple Live Photo",null,identifier,info.Hdr);
                }
                catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException or OverflowException){ }
            }
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException or XmlException or OverflowException or ArgumentException){ }
        return null;
    }
    static long? Timestamp(string? s)=>long.TryParse(s,out long n)&&n>=0?n:null;
    static LivePhotoAsset Asset(Reader file,string photo,string video,long offset,long length,string kind,long? time,string? id,bool hdr)=>
        new(photo,video,offset,length,kind,time,id,hdr,file.Length,File.GetLastWriteTimeUtc(photo).Ticks,File.GetLastWriteTimeUtc(video).Ticks);
    static LivePhotoAsset? Embedded(Reader file,string photo,long offset,long length,string kind,long? time)
    {
        if(offset<8||length<24||length>VideoLimit||offset>file.Length-length)return null;
        var movie=ReadMovie(file,offset,length);
        return movie is null?null:Asset(file,photo,photo,offset,movie.Length,kind,time,null,movie.Hdr);
    }
    static Metadata JpegMetadata(Reader file)
    {
        List<byte[]> xmp=[],exif=[];
        if(!file.Bytes(0,2).SequenceEqual(new byte[]{255,216}))return new(xmp,exif);
        long at=2;int consumed=0;
        while(at+4<=file.Length&&consumed++<2048)
        {
            var header=file.Bytes(at,4);if(header[0]!=255)break;
            if(header[1] is 0xda or 0xd9)break;
            if(header[1]==255){at++;continue;}
            if(header[1] is >=0xd0 and <=0xd7 or 0x01){at+=2;continue;}
            int count=BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));if(count<2||at+2+count>file.Length)break;
            if(header[1]==0xe1)
            {
                var bytes=file.Bytes(at+4,count-2);
                if(bytes.AsSpan().StartsWith("Exif\0\0"u8))exif.Add(bytes[6..]);
                ReadOnlySpan<byte> prefix="http://ns.adobe.com/xap/1.0/\0"u8;
                if(bytes.AsSpan().StartsWith(prefix))xmp.Add(bytes[prefix.Length..]);
                if(xmp.Sum(b=>b.Length)+exif.Sum(b=>b.Length)>MetadataLimit)break;
            }
            at+=count+2;
        }
        return new(xmp,exif);
    }
    static Metadata HeifMetadata(Reader file)
    {
        List<byte[]> xmp=[],exif=[];
        var top=file.Boxes(0,file.Length).Take(256).ToList();
        var meta=top.FirstOrDefault(b=>b.Type=="meta");if(meta.Size==0||meta.Size>MetadataLimit)return new(xmp,exif);
        var children=file.Boxes(meta.Data+4,meta.Size-meta.Header-4).ToList();
        var types=new Dictionary<uint,string>();
        var iinf=children.FirstOrDefault(b=>b.Type=="iinf");
        if(iinf.Size!=0)
        {
            int version=file.Bytes(iinf.Data,1)[0],skip=version==0?6:8;
            foreach(var box in file.Boxes(iinf.Data+skip,iinf.Size-iinf.Header-skip).Where(b=>b.Type=="infe"))
            {
                var b=new Cursor(file.Bytes(box.Data,checked((int)(box.Size-box.Header))));int v=b.U8();b.Skip(3);if(v is not (2 or 3))continue;
                uint id=v==2?b.U16():b.U32();b.Skip(2);string type=b.Text(4);b.CString();
                if(type=="Exif")types[id]=type;
                else if(type=="mime"&&b.CString() is "application/rdf+xml" or "application/xml")types[id]="XMP";
            }
        }
        var iloc=children.FirstOrDefault(b=>b.Type=="iloc");if(iloc.Size==0||iloc.Size>MetadataLimit)return new(xmp,exif);
        var c=new Cursor(file.Bytes(iloc.Data,checked((int)(iloc.Size-iloc.Header))));int locVersion=c.U8();c.Skip(3);if(locVersion>2)return new(xmp,exif);
        int sizes=c.U8(),offsetSize=sizes>>4,lengthSize=sizes&15;sizes=c.U8();int baseSize=sizes>>4,indexSize=locVersion>0?sizes&15:0;
        int count=checked((int)(locVersion<2?c.U16():c.U32()));if(count>16384)throw new InvalidDataException();
        var idat=children.FirstOrDefault(b=>b.Type=="idat");
        for(int i=0;i<count;i++)
        {
            uint id=locVersion<2?c.U16():c.U32();int method=locVersion>0?c.U16()&15:0;int reference=c.U16();long origin=c.Number(baseSize);int extents=c.U16();
            if(extents>1024)throw new InvalidDataException();
            using var data=new MemoryStream();bool wanted=types.ContainsKey(id)&&reference==0&&(method==0||method==1&&idat.Size!=0);
            for(int j=0;j<extents;j++)
            {
                if(locVersion>0&&indexSize>0)c.Number(indexSize);
                long offset=c.Number(offsetSize),length=c.Number(lengthSize);
                if(wanted){if(length>MetadataLimit-data.Length)throw new InvalidDataException();long start=checked(origin+offset+(method==1?idat.Data:0));if(method==1&&(start<idat.Data||length>idat.End-start))throw new InvalidDataException();data.Write(file.Bytes(start,checked((int)length)));}
            }
            if(!wanted)continue;var payload=data.ToArray();
            if(types[id]=="XMP")xmp.Add(payload);
            else if(payload.Length>=4){long start=4L+BinaryPrimitives.ReadUInt32BigEndian(payload);if(start<payload.Length)exif.Add(payload[checked((int)start)..]);}
        }
        return new(xmp,exif);
    }
    static string? AppleIdentifier(byte[] data)
    {
        try
        {
            if(data.Length<8)return null;bool little=data[0]=='I'&&data[1]=='I';if(!little&&!(data[0]=='M'&&data[1]=='M'))return null;
            uint N32(int p)=>little?BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p)):BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p));
            ushort N16(int p)=>little?BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p)):BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p));
            int ifd=checked((int)N32(4));
            for(int depth=0;depth<2;depth++)
            {
                int count=N16(ifd),next=0;if(count>512)return null;
                for(int i=0;i<count;i++)
                {
                    int p=checked(ifd+2+i*12),tag=N16(p);
                    if(tag==0x8769)next=checked((int)N32(p+8));
                    if(tag!=0x927c)continue;
                    int size=checked((int)N32(p+4)),start=checked((int)N32(p+8));if(size<16||size>MetadataLimit||start>data.Length-size)return null;
                    var maker=data.AsSpan(start,size);if(!maker.StartsWith("Apple iOS\0"u8))return null;
                    bool be=maker[12]=='M'&&maker[13]=='M';
                    ushort M16(int at)=>be?BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(start+at)):BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(start+at));
                    uint M32(int at)=>be?BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(start+at)):BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(start+at));
                    int entries=M16(14);if(entries>512)return null;
                    for(int k=0;k<entries;k++)
                    {
                        int entry=16+k*12;if(entry+12>size)return null;if(M16(entry)!=17||M16(entry+2)!=2)continue;
                        int length=checked((int)M32(entry+4)),at=length<=4?entry+8:checked((int)M32(entry+8));
                        if(length<1||length>128||at<0||at>size-length)return null;
                        return Identifier(Encoding.UTF8.GetString(data,start+at,length));
                    }
                }
                if(next<=0)break;ifd=next;
            }
        }
        catch(Exception ex)when(ex is ArgumentOutOfRangeException or OverflowException){ }
        return null;
    }
    static string? Identifier(string text)=>Guid.TryParse(text.Trim('\0',' ','\r','\n'),out var id)?id.ToString("D"):null;
    static Movie? ReadMovie(Reader file,long offset,long length)
    {
        List<Box> boxes;
        try{boxes=file.Boxes(offset,length).ToList();}
        catch(InvalidDataException)
        {
        // Older Samsung JPEG containers include a SEF directory after the MP4.
        // A valid sefd atom may contain the same footer; never trim inside it.
        // Only retry a malformed range with a fully identified raw SEF footer.
        long originalLength=length;
        if(length>=20)
        {
            var tail=file.Bytes(offset+length-8,8);
            if(tail.AsSpan(4).SequenceEqual("SEFT"u8))
            {
                long directory=BinaryPrimitives.ReadUInt32LittleEndian(tail),start=offset+length-directory-8;
                if(directory>=12&&directory<=MetadataLimit&&start>=offset)
                {
                    var head=file.Bytes(start,12);uint count=BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(8));
                    if(head.AsSpan(0,4).SequenceEqual("SEFH"u8)&&count<=1024&&directory==12L+count*12)length-=directory+8;
                }
            }
        }
        if(length==originalLength)throw;
        boxes=file.Boxes(offset,length).ToList();
        }
        if(boxes.LastOrDefault() is {Type:"sefd"} footer)length-=footer.Size;
        if(!boxes.Any(b=>b.Type=="ftyp")||!boxes.Any(b=>b.Type=="mdat"))return null;
        var moov=boxes.FirstOrDefault(b=>b.Type=="moov");if(moov.Size==0||moov.Size>MetadataLimit)return null;
        string? identifier=null;bool hdr=false;
        Walk(moov.Data,moov.Size-moov.Header,0);
        return new(identifier,hdr,length);
        void Walk(long start,long size,int depth)
        {
            if(depth>10)throw new InvalidDataException();
            foreach(var box in file.Boxes(start,size))
            {
                if(box.Type=="meta")
                {
                    // QuickTime's original meta atom omits ISO's full-box flags.
                    int skip=BinaryPrimitives.ReadUInt32BigEndian(file.Bytes(box.Data,4))==0?4:0;
                    var children=file.Boxes(box.Data+skip,box.Size-box.Header-skip).ToList();var keys=children.FirstOrDefault(b=>b.Type=="keys");var ilst=children.FirstOrDefault(b=>b.Type=="ilst");
                    if(keys.Size!=0&&ilst.Size!=0)
                    {
                        var c=new Cursor(file.Bytes(keys.Data,checked((int)(keys.Size-keys.Header))));c.Skip(4);uint count=c.U32();if(count>1024)throw new InvalidDataException();uint keyIndex=0;
                        for(uint i=1;i<=count;i++){int n=checked((int)c.U32());if(n<8)throw new InvalidDataException();c.Skip(4);if(c.Text(n-8)=="com.apple.quicktime.content.identifier")keyIndex=i;}
                        foreach(var value in file.Boxes(ilst.Data,ilst.Size-ilst.Header))
                        {
                            uint number=BinaryPrimitives.ReadUInt32BigEndian(file.Bytes(value.Start+4,4));if(number!=keyIndex||keyIndex==0)continue;
                            var item=file.Boxes(value.Data,value.Size-value.Header).FirstOrDefault(b=>b.Type=="data");
                            if(item.Size>item.Header+8&&item.Size<256)identifier=Identifier(Encoding.UTF8.GetString(file.Bytes(item.Data+8,checked((int)(item.Size-item.Header-8)))));
                        }
                    }
                }
                else if(box.Type=="colr"&&box.Size>=box.Header+10)
                {
                    var c=new Cursor(file.Bytes(box.Data,10));if(c.Text(4) is "nclx" or "nclc"){c.U16();int transfer=c.U16();if(transfer is 16 or 18)hdr=true;}
                }
                else if(box.Type is "dvcC" or "dvvC" or "dvwC")hdr=true;
                else if(box.Type is "moov" or "trak" or "mdia" or "minf" or "stbl" or "udta")Walk(box.Data,box.Size-box.Header,depth+1);
                else if(box.Type=="stsd"&&box.Size>box.Header+8)Walk(box.Data+8,box.Size-box.Header-8,depth+1);
                else if(box.Type is "avc1" or "avc3" or "hvc1" or "hev1" or "av01" or "dvhe" or "dvh1")
                {if(box.Type is "dvhe" or "dvh1")hdr=true;if(box.Size>box.Header+78)Walk(box.Data+78,box.Size-box.Header-78,depth+1);}
            }
        }
    }
    sealed class Reader:IDisposable
    {
        readonly FileStream stream;readonly CancellationToken token;int boxes;
        public long Length=>stream.Length;
        public Reader(string path,CancellationToken cancellation){token=cancellation;stream=new(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,65536,FileOptions.RandomAccess);}
        public byte[] Bytes(long offset,int count)
        {
            token.ThrowIfCancellationRequested();if(offset<0||count<0||count>MetadataLimit||offset>Length-count)throw new InvalidDataException();
            byte[] data=new byte[count];stream.Position=offset;stream.ReadExactly(data);return data;
        }
        public IEnumerable<Box> Boxes(long start,long length)
        {
            if(start<0||length<0||start>Length-length)throw new InvalidDataException();long end=start+length;
            while(start<end)
            {
                token.ThrowIfCancellationRequested();
                // QuickTime sample descriptions can end in a zero terminator.
                if(end-start<8){if(Bytes(start,(int)(end-start)).All(b=>b==0))yield break;throw new InvalidDataException();}
                if(++boxes>32768)throw new InvalidDataException();
                var b=Bytes(start,8);long size=BinaryPrimitives.ReadUInt32BigEndian(b);int header=8;string type=Encoding.Latin1.GetString(b,4,4);
                if(size==1){if(end-start<16)throw new InvalidDataException();ulong extended=BinaryPrimitives.ReadUInt64BigEndian(Bytes(start+8,8));if(extended>long.MaxValue)throw new InvalidDataException();size=(long)extended;header=16;}
                else if(size==0)size=end-start;
                if(size<header||size>end-start)throw new InvalidDataException();yield return new(start,size,header,type);start+=size;
            }
        }
        public void Dispose()=>stream.Dispose();
    }
    sealed class Cursor(byte[] bytes)
    {
        int p;public int U8(){Need(1);return bytes[p++];}
        public ushort U16(){Need(2);ushort n=BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(p));p+=2;return n;}
        public uint U32(){Need(4);uint n=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(p));p+=4;return n;}
        public void Skip(int n){Need(n);p+=n;}
        public string Text(int n){Need(n);var s=Encoding.UTF8.GetString(bytes,p,n);p+=n;return s;}
        public string CString(){int start=p;while(p<bytes.Length&&bytes[p]!=0)p++;if(p==bytes.Length)throw new InvalidDataException();var s=Encoding.UTF8.GetString(bytes,start,p-start);p++;return s;}
        public long Number(int width){if(width>8)throw new InvalidDataException();ulong n=0;for(int i=0;i<width;i++)n=(n<<8)|(uint)U8();if(n>long.MaxValue)throw new InvalidDataException();return (long)n;}
        void Need(int n){if(n<0||p>bytes.Length-n)throw new InvalidDataException();}
    }
}
