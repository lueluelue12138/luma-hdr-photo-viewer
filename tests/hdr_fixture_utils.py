"""Independent compatible JPEG gain-map fixture container writer."""
import io,struct
from PIL import Image

def jpeg(im):
    buffer=io.BytesIO();im.save(buffer,'JPEG',quality=100,subsampling=0);return buffer.getvalue()
def app1(data):return b'\xff\xe1'+struct.pack('>H',len(data)+2)+data
def xmp(text):return app1(b'http://ns.adobe.com/xap/1.0/\x00'+text.encode())
def gain_jpeg(base,gain,description):
    secondary=gain[:2]+xmp(description)+gain[2:]
    primary_xml=f'<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"><rdf:Description xmlns:hdrgm="http://ns.adobe.com/hdr-gain-map/1.0/" hdrgm:Version="1.0" xmlns:Container="http://ns.google.com/photos/1.0/container/" xmlns:Item="http://ns.google.com/photos/1.0/container/item/"><Container:Directory><rdf:Seq><rdf:li rdf:parseType="Resource"><Container:Item Item:Semantic="Primary" Item:Mime="image/jpeg"/></rdf:li><rdf:li rdf:parseType="Resource"><Container:Item Item:Semantic="GainMap" Item:Mime="image/jpeg" Item:Length="{len(secondary)}"/></rdf:li></rdf:Seq></Container:Directory></rdf:Description></rdf:RDF></x:xmpmeta>'
    primary=base[:2]+xmp(primary_xml)+base[2:]
    # MPF offsets are relative to the TIFF header inside the APP2 payload.
    ifd=struct.pack('<H',3)+struct.pack('<HHI4s',0xb000,7,4,b'0100')+struct.pack('<HHII',0xb001,4,1,2)+struct.pack('<HHII',0xb002,7,32,50)+bytes(4)
    primary_size=len(primary)+90
    entries=struct.pack('<IIIHH',0x20030000,primary_size,0,0,0)+struct.pack('<IIIHH',0,len(secondary),primary_size-10,0,0)
    mpf=b'MPF\x00II\x2a\x00'+struct.pack('<I',8)+ifd+entries
    segment=b'\xff\xe2'+struct.pack('>H',len(mpf)+2)+mpf
    assert len(segment)==90
    return primary[:2]+segment+primary[2:]+secondary
