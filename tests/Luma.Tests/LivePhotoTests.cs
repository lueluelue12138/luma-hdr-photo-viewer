using System.Buffers.Binary;
using System.Text;
using Luma.Core;

internal static class LivePhotoTests
{
    internal static void Run(string root,Action<bool,string> assert)
    {
        string folder=Path.Combine(root,"live");Directory.CreateDirectory(folder);
        byte[] Text(string s)=>Encoding.UTF8.GetBytes(s);
        byte[] Join(params byte[][] values)=>values.SelectMany(b=>b).ToArray();
        byte[] U32(uint value){byte[] b=new byte[4];BinaryPrimitives.WriteUInt32BigEndian(b,value);return b;}
        byte[] Box(string type,params byte[][] data){var payload=Join(data);return Join(U32((uint)payload.Length+8),Text(type),payload);}
        byte[] Movie(string? id=null,bool hdr=false)
        {
            byte[] meta=id is null?[]:Box("meta",new byte[4],Box("keys",new byte[4],U32(1),Box("mdta",Text("com.apple.quicktime.content.identifier"))),
                Box("ilst",Box("\0\0\0\u0001",Box("data",new byte[8],Text(id)))));
            byte[] colour=hdr?Box("trak",Box("mdia",Box("minf",Box("stbl",Box("stsd",new byte[8],Box("hvc1",new byte[78],Box("colr",Text("nclx"),new byte[]{0,9,0,16,0,9,0}))))))):[];
            return Join(Box("ftyp",Text("isom"),new byte[4]),Box("mdat",new byte[4]),Box("moov",meta,colour));
        }
        byte[] Segment(byte[] payload){byte[] length=new byte[2];BinaryPrimitives.WriteUInt16BigEndian(length,(ushort)(payload.Length+2));return Join(new byte[]{255,225},length,payload);}
        byte[] Packet(string xml)=>Segment(Join(Text("http://ns.adobe.com/xap/1.0/\0"),Text(xml)));
        string Xml(string flags,long length=0)=>$"<x:xmpmeta xmlns:x='adobe:ns:meta/' xmlns:C='http://ns.google.com/photos/1.0/camera/' xmlns:I='http://ns.google.com/photos/1.0/container/item/' {flags}><Item I:Semantic='Primary' I:Mime='image/jpeg'/><Item I:Semantic='MotionPhoto' I:Mime='video/mp4' I:Length='{length}'/></x:xmpmeta>";
        string Write(string name,byte[] data){string path=Path.Combine(folder,name);File.WriteAllBytes(path,data);return path;}
        byte[] Jpeg(byte[] video,params byte[][] packets)=>Join(new byte[]{255,216},Join(packets),new byte[]{255,217},video);
        byte[] movie=Movie();
        var valid=LivePhotoReader.Detect(Write("valid.jpg",Jpeg(movie,Packet(Xml("C:MotionPhoto='1' C:MotionPhotoPresentationTimestampUs='123456'",movie.Length)))));
        assert(valid is {Embedded:true,NativeVideoHdr:false,PhotoTimeMicroseconds:123456}&&valid.Length==movie.Length,"Live embedded video uses bounded metadata and preserves still timestamp");
        assert(LivePhotoReader.Detect(Write("still.jpg",Jpeg([],Packet(Xml(""))))) is null,"plain JPEG never becomes Live by extension");
        assert(LivePhotoReader.Detect(Write("disabled.jpg",Jpeg(movie,Packet(Xml("C:MotionPhoto='1'",movie.Length)),Packet(Xml("C:MotionPhoto='0' C:MicroVideo='1' C:MicroVideoOffset='"+movie.Length+"'"))))) is null,"explicit disabled Motion Photo in any packet overrides positives and legacy");
        assert(LivePhotoReader.Detect(Write("invalid-offset.jpg",Jpeg(movie,Packet(Xml("C:MotionPhoto='1'",long.MaxValue))))) is null,"untrusted Live length cannot escape source boundaries");
        assert(LivePhotoReader.Detect(Write("truncated.jpg",Jpeg(movie[..^3],Packet(Xml("C:MotionPhoto='1'",movie.Length-3))))) is null,"truncated MP4 fails without decoding compressed media");
        assert(LivePhotoReader.Detect(Write("dtd.jpg",Jpeg(movie,Packet("<!DOCTYPE x [<!ENTITY a '1'>]>"+Xml("C:MotionPhoto='&a;'",movie.Length))))) is null,"Live XML prohibits document entities");
        byte[] pq=Movie(hdr:true);
        assert(LivePhotoReader.Detect(Write("pq.jpg",Jpeg(pq,Packet(Xml("C:MotionPhoto='1'",pq.Length))))) is {NativeVideoHdr:true},"PQ video is identified separately from static HDR");
        var legacy=LivePhotoReader.Detect(Write("legacy.jpg",Jpeg(movie,Packet(Xml($"C:MicroVideo='1' C:MicroVideoOffset='{movie.Length}'")))));
        assert(legacy?.Kind=="Google MicroVideo","legacy Google MicroVideo remains compatible");
        assert(LivePhotoReader.Detect(Write("modern-wins.jpg",Jpeg(movie,Packet(Xml($"C:MicroVideo='1' C:MicroVideoOffset='{movie.Length}'")),Packet(Xml("C:MotionPhoto='1'",1))))) is null,"invalid modern container cannot be bypassed through another legacy packet");
        byte[] footer=Join(Text("SEFH"),new byte[4],new byte[]{1,0,0,0},new byte[12],new byte[]{24,0,0,0},Text("SEFT"));
        byte[] raw=Join(movie,footer),boxed=Join(movie,Box("sefd",footer));
        assert(LivePhotoReader.Detect(Write("raw-sef.jpg",Jpeg(raw,Packet(Xml("C:MotionPhoto='1'",raw.Length)))))?.Length==movie.Length,"legacy Samsung raw SEF directory is validated and excluded");
        assert(LivePhotoReader.Detect(Write("atom-sef.jpg",Jpeg(boxed,Packet(Xml("C:MotionPhoto='1'",boxed.Length)))))?.Length==movie.Length,"Samsung sefd atom is not corrupted by stripping its nested footer");
        byte[] Exif(string id)
        {
            byte[] b=new byte[44+32+37];b[0]=b[1]=(byte)'I';
            void N16(int p,ushort n)=>BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p),n);
            void N32(int p,uint n)=>BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p),n);
            N16(2,42);N32(4,8);N16(8,1);N16(10,0x8769);N16(12,4);N32(14,1);N32(18,26);
            N16(26,1);N16(28,0x927c);N16(30,7);N32(32,69);N32(36,44);
            Text("Apple iOS\0").CopyTo(b,44);b[56]=b[57]=(byte)'I';N16(58,1);N16(60,17);N16(62,2);N32(64,37);N32(68,32);Text(id).CopyTo(b,76);
            return Segment(Join(Text("Exif\0\0"),b));
        }
        string uuid="1bc07f6e-cf5a-4508-ac2d-bb25935409e2";
        string apple=Write("apple.jpg",Jpeg([],Exif(uuid)));
        Write("apple.mov",Movie(Guid.NewGuid().ToString()));
        assert(LivePhotoReader.Detect(apple) is null,"same-name MOV with different content identifier is never paired");
        string renamed=Write("renamed.mov",Movie(uuid));
        assert(LivePhotoReader.Detect(apple) is {Embedded:false,ContentIdentifier:var found,VideoPath:var video}&&found==uuid&&video==renamed,"renamed Apple pair requires matching embedded identifiers");
        using var canceled=new CancellationTokenSource();canceled.Cancel();bool honored=false;
        try{LivePhotoReader.Detect(apple,canceled.Token);}catch(OperationCanceledException){honored=true;}
        assert(honored,"Live detection cancellation is not swallowed as bad metadata");
        var hdrXml="<x:xmpmeta xmlns:x='adobe:ns:meta/' xmlns:g='http://ns.adobe.com/hdr-gain-map/1.0/' g:Version='1.0'/>";
        var both=Write("both.jpg",Jpeg(movie,Packet(Xml("C:MotionPhoto='1'",movie.Length)),Packet(hdrXml)));
        assert(LivePhotoReader.ReadFeatures(both) is {HdrKind:not null,Live:not null},"photo traits independently preserve HDR metadata and Live payload");
        assert(LivePhotoReader.ReadFeatures(Write("HDR_in_filename.jpg",Jpeg([],Packet(Xml(""))))) is {HdrKind:null,Live:null},"feature labels never infer HDR or Live from file names");
        assert(LivePhotoReader.ReadFeatures(Write("fake-namespace.jpg",Jpeg([],Packet(hdrXml.Replace("http://ns.adobe.com/hdr-gain-map/1.0/","https://example.invalid/"))))) is {HdrKind:null},"HDR trait requires the actual metadata namespace");
        byte[] Heif(params byte[][] data)=>Join(Box("ftyp",Text("heic"),new byte[4]),Box("meta",new byte[4],Join(data)));
        byte[] primary=Box("pitm",new byte[4],new byte[]{0,1});
        byte[] props=Box("iprp",Box("ipco",Box("colr",Text("nclx"),new byte[]{0,9,0,16,0,9,0})),Box("ipma",new byte[4],U32(1),new byte[]{0,1,1,1}));
        assert(LivePhotoReader.ReadFeatures(Write("pq.heic",Heif(primary,props))) is {HdrKind:"PQ HEIC",Live:null},"HEIF HDR tag follows the primary item's NCLX transfer function");
        assert(LivePhotoReader.ReadFeatures(Write("unused-pq.heic",Heif(primary,Box("iprp",Box("ipco",Box("colr",Text("nclx"),new byte[]{0,9,0,16,0,9,0})))))) is {HdrKind:null},"unattached HDR colour property cannot label an SDR primary image");
    }
}
