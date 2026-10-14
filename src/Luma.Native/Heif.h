#pragma once
#include <ultrahdr/jpegrutils.h>
#include <ultrahdr/gainmapmetadata.h>
// Only the bounded HEIF item graph is parsed here. HEVC pixels, grids,
// crops and item transforms remain the installed Windows WIC codec's job.
struct HeifInput {
    struct Box {size_t payload,end;std::string type;};
    struct Item {std::string type;size_t flags{};std::vector<unsigned> properties;std::vector<std::pair<size_t,size_t>> extents;};
    struct Ref {std::string type;unsigned from;std::vector<unsigned> to;};
    std::vector<uint8_t> bytes;std::vector<std::vector<uint8_t>> selections;std::map<unsigned,Item> items;std::vector<Box> properties;std::vector<Ref> refs;
    unsigned primary{},base{},gain{},tmap{};size_t pitm{};unsigned idSize{};bool apple=false;
    ultrahdr::uhdr_gainmap_metadata_ext_t metadata{ultrahdr::kJpegrVersion};
    uint64_t number(size_t& p,size_t end,unsigned n)const{
        if(n>8||p>end||n>end-p)throw Failure{WINCODEC_ERR_BADIMAGE,L"HEIC 元数据长度无效"};
        uint64_t v=0;for(unsigned j=0;j<n;j++)v=(v<<8)|bytes[p++];return v;
    }
    std::vector<Box> boxes(size_t p,size_t end,bool top=false)const{
        std::vector<Box> out;
        while(p<end){size_t start=p;auto size=number(p,end,4);auto type=std::string(reinterpret_cast<const char*>(bytes.data()+p),size_t(0));
            if(end-p<4)throw Failure{WINCODEC_ERR_BADIMAGE};type.assign(reinterpret_cast<const char*>(bytes.data()+p),4);p+=4;
            if(size==1)size=number(p,end,8);if(!size)size=end-start;
            if(size<p-start||size>end-start||out.size()>=4096)throw Failure{WINCODEC_ERR_BADIMAGE};
            out.push_back({p,start+size,type});p=start+size;
            if(top&&type=="meta")break; // some transfers append non-box checksums after mdat
        }return out;
    }
    std::vector<uint8_t> data(unsigned id)const{
        auto it=items.find(id);if(it==items.end())throw Failure{WINCODEC_ERR_BADIMAGE};std::vector<uint8_t> out;
        for(auto [offset,length]:it->second.extents){if(offset>bytes.size()||length>bytes.size()-offset||length>1024*1024-out.size())throw Failure{WINCODEC_ERR_BADIMAGE,L"HEIC HDR 元数据超出预算"};out.insert(out.end(),bytes.begin()+offset,bytes.begin()+offset+length);}return out;
    }
    std::vector<unsigned> targets(const std::string& type,unsigned id)const{
        for(auto& r:refs)if(r.type==type&&r.from==id)return r.to;return {};
    }
    std::vector<uint8_t> described(unsigned id,const std::string& type)const{
        for(auto& r:refs)if(r.type=="cdsc"&&std::find(r.to.begin(),r.to.end(),id)!=r.to.end()&&items.at(r.from).type==type)return data(r.from);return {};
    }
    bool property(unsigned id,const std::string& type,const std::string& content)const{
        for(auto n:items.at(id).properties){if(!n||n>properties.size())throw Failure{WINCODEC_ERR_BADIMAGE};auto& b=properties[n-1];if(b.type==type){std::string s(reinterpret_cast<const char*>(bytes.data()+b.payload),b.end-b.payload);if(s.find(content)!=std::string::npos)return true;}}return false;
    }
    uhdr_color_gamut_t baseNclxGamut()const{
        for(auto n:items.at(base).properties){if(!n||n>properties.size())throw Failure{WINCODEC_ERR_BADIMAGE};auto& b=properties[n-1];if(b.type!="colr"||b.end-b.payload<11||memcmp(bytes.data()+b.payload,"nclx",4))continue;size_t p=b.payload+4;auto primaries=number(p,b.end,2),transfer=number(p,b.end,2);
            if(transfer!=13)throw Failure{WINCODEC_ERR_UNSUPPORTEDPIXELFORMAT,L"HEIC 增益图底图的 NCLX 曲线尚不支持"};
            if(primaries==1)return UHDR_CG_BT_709;if(primaries==12)return UHDR_CG_DISPLAY_P3;if(primaries==9)return UHDR_CG_BT_2100;throw Failure{WINCODEC_ERR_UNSUPPORTEDPIXELFORMAT,L"HEIC 增益图底图的 NCLX 色域尚不支持"};
        }return UHDR_CG_BT_709;
    }
    explicit HeifInput(const wchar_t* path){
        std::ifstream file(std::filesystem::path(path),std::ios::binary);if(!file)throw Failure{WINCODEC_ERR_BADIMAGE};file.seekg(0,std::ios::end);auto length=file.tellg();
        if(length<16)throw Failure{WINCODEC_ERR_BADIMAGE};if(length>256ll*1024*1024)throw Failure{E_OUTOFMEMORY,L"HEIC 压缩文件超出 256 MiB 解码预算"};file.seekg(0);bytes.resize(size_t(length));if(!file.read(reinterpret_cast<char*>(bytes.data()),length))throw Failure{WINCODEC_ERR_BADIMAGE};
        Box meta{};for(auto& b:boxes(0,bytes.size(),true))if(b.type=="meta")meta=b;if(!meta.end)throw Failure{WINCODEC_ERR_BADIMAGE};
        if(meta.end-meta.payload>16*1024*1024)throw Failure{E_OUTOFMEMORY};auto children=boxes(meta.payload+4,meta.end);size_t idat=0;Box iloc{};
        for(auto& b:children){size_t p=b.payload;
            if(b.type=="pitm"){auto version=number(p,b.end,1);number(p,b.end,3);idSize=version?4:2;pitm=p;primary=unsigned(number(p,b.end,idSize));}
            else if(b.type=="iinf"){auto version=number(p,b.end,1);number(p,b.end,3);number(p,b.end,version?4:2);
                for(auto& info:boxes(p,b.end)){if(info.type!="infe")continue;size_t q=info.payload;auto v=number(q,info.end,1);if(v!=2&&v!=3)throw Failure{WINCODEC_ERR_UNSUPPORTEDOPERATION};size_t flags=q;number(q,info.end,3);unsigned id=unsigned(number(q,info.end,v==3?4:2));number(q,info.end,2);if(info.end-q<4||!items[id].type.empty())throw Failure{WINCODEC_ERR_BADIMAGE};items[id].type=std::string(reinterpret_cast<char*>(bytes.data()+q),4);items[id].flags=flags;}
            }else if(b.type=="iref"){auto version=number(p,b.end,1);number(p,b.end,3);unsigned ids=version?4:2;
                for(auto& link:boxes(p,b.end)){size_t q=link.payload;Ref r{link.type,unsigned(number(q,link.end,ids)),{}};auto count=number(q,link.end,2);if(count>4096)throw Failure{WINCODEC_ERR_BADIMAGE};for(unsigned j=0;j<count;j++)r.to.push_back(unsigned(number(q,link.end,ids)));refs.push_back(std::move(r));}
            }else if(b.type=="iprp"){
                for(auto& prop:boxes(p,b.end))if(prop.type=="ipco")properties=boxes(prop.payload,prop.end);
                for(auto& prop:boxes(p,b.end))if(prop.type=="ipma"){size_t q=prop.payload;auto v=number(q,prop.end,1),flags=number(q,prop.end,3),count=number(q,prop.end,4);if(count>4096)throw Failure{WINCODEC_ERR_BADIMAGE};for(unsigned j=0;j<count;j++){auto id=unsigned(number(q,prop.end,v?4:2)),n=unsigned(number(q,prop.end,1));for(unsigned k=0;k<n;k++)items[id].properties.push_back(unsigned(number(q,prop.end,(flags&1)?2:1))&((flags&1)?0x7fff:0x7f));}}
            }else if(b.type=="idat")idat=b.payload;else if(b.type=="iloc")iloc=b;
        }
        if(!pitm||!items.contains(primary))throw Failure{WINCODEC_ERR_BADIMAGE};
        if(iloc.end){size_t p=iloc.payload;auto version=number(p,iloc.end,1);number(p,iloc.end,3);auto sizes=number(p,iloc.end,1),sizes2=number(p,iloc.end,1);unsigned os=unsigned(sizes>>4),ls=unsigned(sizes&15),bs=unsigned(sizes2>>4),ix=version?unsigned(sizes2&15):0;auto count=number(p,iloc.end,version<2?2:4);if(version>2||count>4096)throw Failure{WINCODEC_ERR_BADIMAGE};
            for(unsigned j=0;j<count;j++){auto id=unsigned(number(p,iloc.end,version<2?2:4));auto method=version?(number(p,iloc.end,2)&15):0;auto index=number(p,iloc.end,2),offset=number(p,iloc.end,bs),n=number(p,iloc.end,2);if(index||method>1||n>4096)throw Failure{WINCODEC_ERR_UNSUPPORTEDOPERATION};for(unsigned k=0;k<n;k++){number(p,iloc.end,ix);auto begin=number(p,iloc.end,os),size=number(p,iloc.end,ls),origin=method?idat:0;if(offset>bytes.size()||begin>bytes.size()-offset||origin>bytes.size()-offset-begin||size>bytes.size()-origin-offset-begin)throw Failure{WINCODEC_ERR_BADIMAGE};items[id].extents.push_back({size_t(origin+offset+begin),size_t(size)});}}
        }
        base=primary;
        // ISO tone-map items can coexist with an SDR primary (as on iPadOS).
        for(auto& [id,item]:items)if(item.type=="tmap"){auto images=targets("dimg",id);if(images.size()==2&&(id==primary||images[0]==primary)){tmap=id;base=images[0];gain=images[1];break;}}
        if(tmap){auto packet=data(tmap);if(packet.empty()||packet[0]!=0)throw Failure{WINCODEC_ERR_UNSUPPORTEDOPERATION,L"HEIC ISO 增益图版本尚不支持"};packet.erase(packet.begin());ultrahdr::uhdr_gainmap_metadata_frac fractions;checkGain(ultrahdr::uhdr_gainmap_metadata_frac::decodeGainmapMetadata(packet,&fractions));checkGain(ultrahdr::uhdr_gainmap_metadata_frac::gainmapMetadataFractionToFloat(&fractions,&metadata));
            // The reference reconstruction currently handles an SDR base in
            // its own color space. Do not silently misinterpret reverse maps.
            if(fractions.backwardDirection||!metadata.use_base_cg)throw Failure{WINCODEC_ERR_UNSUPPORTEDOPERATION,L"HEIC HDR 底图或替代色彩空间尚不支持"};
        }else{
            for(auto& r:refs)if(r.type=="auxl"&&std::find(r.to.begin(),r.to.end(),primary)!=r.to.end()&&property(r.from,"auxC","urn:com:apple:photo:2020:aux:hdrgainmap")){gain=r.from;apple=true;break;}
            if(apple){auto xml=described(gain,"mime"),exif=described(primary,"Exif");if(xml.empty())throw Failure{WINCODEC_ERR_BADIMAGE,L"Apple HEIC 缺少增益图版本元数据"};auto appleMetadata=appleGainMetadata(xml);
                if(appleMetadata.headroom>0){metadata.hdr_capacity_min=1;metadata.hdr_capacity_max=appleMetadata.headroom;metadata.use_base_cg=true;for(unsigned c=0;c<3;c++){metadata.min_content_boost[c]=1;metadata.max_content_boost[c]=appleMetadata.headroom;metadata.gamma[c]=1;metadata.offset_sdr[c]=metadata.offset_hdr[c]=0;}}
                else{std::string prefix="http://ns.adobe.com/xap/1.0/";prefix.push_back(0);xml.insert(xml.begin(),prefix.begin(),prefix.end());checkGain(ultrahdr::getMetadataFromXMP(xml.data(),xml.size(),exif.data(),int(exif.size()),&metadata));}
            }
        }
    }
    ComPtr<IWICBitmapDecoder> decoder(IWICImagingFactory* wic,unsigned id){
        // Select an existing item in an in-memory copy, retaining its grid,
        // color profile and orientation. The user's file is never modified.
        if(!items.contains(id)||items.at(id).type.empty())throw Failure{WINCODEC_ERR_BADIMAGE};selections.push_back(bytes);auto& selected=selections.back();for(unsigned j=0;j<idSize;j++)selected[pitm+j]=uint8_t(id>>(8*(idSize-j-1)));size_t flags=items.at(id).flags;selected[flags+2]&=0xfe;
        ComPtr<IWICStream> stream;check(wic->CreateStream(&stream));check(stream->InitializeFromMemory(selected.data(),UINT(selected.size())));ComPtr<IWICBitmapDecoder> out;
        auto hr=wic->CreateDecoderFromStream(stream.Get(),nullptr,WICDecodeMetadataCacheOnLoad,&out);
        // WIC may read stream bytes lazily: leave this selection in place until
        // pixels have been copied; subsequent selections use a separate copy.
        check(hr);return out;
    }
};

GainReconstruction reconstructHeif(IWICImagingFactory* wic,HeifInput& input,IWICBitmapFrameDecode* first,UINT width,UINT height){
    // This is an SDR-base gain map. PQ/HLG files instead use WIC's float
    // scRGB representation and never enter this reconstruction.
    uhdr_mem_block_t baseBlock{};baseBlock.data=input.selections.empty()?input.bytes.data():input.selections.back().data();baseBlock.data_sz=input.bytes.size();
    auto base=gainJpeg(wic,&baseBlock,width,height);if(base.gamut==UHDR_CG_UNSPECIFIED)base.gamut=input.baseNclxGamut();std::vector<uint8_t> rgba(size_t(width)*height*4);
    for(size_t p=0;p<size_t(width)*height;p++){memcpy(rgba.data()+p*4,base.pixels.data()+p*3,3);rgba[p*4+3]=255;}
    GainReconstruction out;out.hdr.resize(size_t(width)*height*16);out.sdr=rgba;out.display.gamut=base.gamut==UHDR_CG_UNSPECIFIED?0:int(base.gamut);out.display.minimum=input.metadata.hdr_capacity_min;out.display.maximum=input.metadata.hdr_capacity_max;out.display.model=input.apple?2:1;
    if(!std::isfinite(out.display.minimum)||!std::isfinite(out.display.maximum)||out.display.minimum<1||out.display.maximum<out.display.minimum||out.display.maximum>256)throw Failure{WINCODEC_ERR_BADIMAGE,L"HEIC HDR 亮度元数据无效"};
    memcpy(out.display.offsetSdr,input.metadata.offset_sdr,12);memcpy(out.display.offsetHdr,input.metadata.offset_hdr,12);
    if(out.display.gamut!=0)out.base=rgba;
    auto toSrgb=ultrahdr::getGamutConversionFn(UHDR_CG_BT_709,uhdr_color_gamut_t(out.display.gamut));auto floats=reinterpret_cast<float*>(out.hdr.data());
    std::vector<uint8_t> gainBytes;std::vector<uint16_t> half;
    if(input.apple){
        ComPtr<IWICBitmapSourceTransform> transform;check(first->QueryInterface(IID_PPV_ARGS(&transform)));WICPixelFormatGUID format=GUID_WICPixelFormat8bppGain;check(transform->GetClosestPixelFormat(&format));
        if(!IsEqualGUID(format,GUID_WICPixelFormat8bppGain))throw Failure{WINCODEC_ERR_UNSUPPORTEDPIXELFORMAT,L"Windows HEIF 扩展无法读取 Apple HDR 增益图"};
        gainBytes.resize(size_t(width)*height);check(transform->CopyPixels(nullptr,width,height,&format,WICBitmapTransformRotate0,width,UINT(gainBytes.size()),gainBytes.data()));
    }else{
        auto gainDecoder=input.decoder(wic,input.gain);(void)gainDecoder;uhdr_mem_block_t block{};block.data=input.selections.back().data();block.data_sz=input.bytes.size();auto map=gainJpeg(wic,&block,width,height,false);
        uhdr_raw_image_t sdr{},gain{},hdr{};sdr.fmt=UHDR_IMG_FMT_32bppRGBA8888;sdr.cg=base.gamut;sdr.ct=UHDR_CT_SRGB;sdr.range=UHDR_CR_FULL_RANGE;sdr.w=width;sdr.h=height;sdr.planes[0]=rgba.data();sdr.stride[0]=width;
        gain.fmt=UHDR_IMG_FMT_24bppRGB888;gain.cg=base.gamut;gain.w=width;gain.h=height;gain.planes[0]=map.pixels.data();gain.stride[0]=width;
        half.resize(size_t(width)*height*4);hdr.fmt=UHDR_IMG_FMT_64bppRGBAHalfFloat;hdr.ct=UHDR_CT_LINEAR;hdr.w=width;hdr.h=height;hdr.stride[0]=width;hdr.planes[0]=half.data();
        ultrahdr::UltraHdr processor;checkGain(processor.applyGainMap(&sdr,&gain,&input.metadata,UHDR_CT_LINEAR,hdr.fmt,FLT_MAX,&hdr));
    }
    for(size_t p=0;p<size_t(width)*height;p++){
        ultrahdr::Color linear{};linear.r=gainLinear8(base.pixels[p*3]);linear.g=gainLinear8(base.pixels[p*3+1]);linear.b=gainLinear8(base.pixels[p*3+2]);auto color=linear;
        if(input.apple){color=linear*(1+(out.display.maximum-1)*gainRec7098(gainBytes[p]));}
        else{color.r=DirectX::PackedVector::XMConvertHalfToFloat(half[p*4]);color.g=DirectX::PackedVector::XMConvertHalfToFloat(half[p*4+1]);color.b=DirectX::PackedVector::XMConvertHalfToFloat(half[p*4+2]);}
        color=toSrgb(color);floats[p*4]=color.r*203/80;floats[p*4+1]=color.g*203/80;floats[p*4+2]=color.b*203/80;floats[p*4+3]=1;
        for(unsigned c=0;c<3;c++)if(!std::isfinite(floats[p*4+c]))throw Failure{WINCODEC_ERR_BADIMAGE};
        if(out.display.gamut!=0){auto s=toSrgb(linear);out.sdr[p*4]=gainSrgb8(s.r);out.sdr[p*4+1]=gainSrgb8(s.g);out.sdr[p*4+2]=gainSrgb8(s.b);}
    }return out;
}

std::unique_ptr<HeifInput> probeHeif(const wchar_t* path){
    auto ext=std::filesystem::path(path).extension().wstring();std::transform(ext.begin(),ext.end(),ext.begin(),towlower);
    if(ext!=L".heic"&&ext!=L".heif")return nullptr;return std::make_unique<HeifInput>(path);
}
