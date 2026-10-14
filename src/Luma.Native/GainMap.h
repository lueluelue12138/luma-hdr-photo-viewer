#pragma once
#include <fstream>
#include <DirectXPackedVector.h>
#pragma warning(push)
#pragma warning(disable:4201 4244) // unchanged reference-library headers
#include <ultrahdr_api.h>
#include <ultrahdr/ultrahdrcommon.h>
#include <ultrahdr/gainmapmath.h>
#include <ultrahdr/icc.h>
#pragma warning(pop)

#include <xmllite.h>
#include <shlwapi.h>
#pragma comment(lib,"xmllite.lib")
#pragma comment(lib,"shlwapi.lib")

struct AppleGainMetadata {unsigned version{};float headroom{};};
AppleGainMetadata appleGainMetadata(const std::vector<uint8_t>& xml){
    ComPtr<IStream> stream;stream.Attach(SHCreateMemStream(xml.data(),UINT(xml.size())));if(!stream)throw std::bad_alloc();
    ComPtr<IXmlReader> reader;check(CreateXmlReader(__uuidof(IXmlReader),reinterpret_cast<void**>(reader.GetAddressOf()),nullptr));check(reader->SetProperty(XmlReaderProperty_DtdProcessing,DtdProcessing_Prohibit));check(reader->SetProperty(XmlReaderProperty_MaxElementDepth,64));check(reader->SetInput(stream.Get()));
    AppleGainMetadata out;std::wstring field;XmlNodeType type;HRESULT hr;
    auto valueFor=[&](const std::wstring& name,const wchar_t* value){if(name!=L"HDRGainMapHeadroom"&&name!=L"HDRGainMapVersion")return;wchar_t* end=nullptr;double v=wcstod(value,&end);if(!end||*end||!std::isfinite(v))throw Failure{WINCODEC_ERR_BADIMAGE};if(name==L"HDRGainMapHeadroom")out.headroom=float(v);if(name==L"HDRGainMapVersion"&&(v==65536||v==131072))out.version=unsigned(v);};
    while((hr=reader->Read(&type))==S_OK){
        if(type==XmlNodeType_Element){const wchar_t *name=nullptr,*space=nullptr;check(reader->GetLocalName(&name,nullptr));check(reader->GetNamespaceUri(&space,nullptr));field=space&&wcscmp(space,L"http://ns.apple.com/HDRGainMap/1.0/")==0?name:L"";
            if(reader->MoveToFirstAttribute()==S_OK){do{const wchar_t *attribute,*ns,*value;check(reader->GetLocalName(&attribute,nullptr));check(reader->GetNamespaceUri(&ns,nullptr));check(reader->GetValue(&value,nullptr));if(ns&&wcscmp(ns,L"http://ns.apple.com/HDRGainMap/1.0/")==0)valueFor(attribute,value);}while(reader->MoveToNextAttribute()==S_OK);check(reader->MoveToElement());}}
        else if(type==XmlNodeType_Text&&!field.empty()){const wchar_t* value;check(reader->GetValue(&value,nullptr));valueFor(field,value);field.clear();}
    }check(hr);if(!out.version)throw Failure{WINCODEC_ERR_UNSUPPORTEDOPERATION,L"Apple HDR 增益图版本尚不支持"};return out;
}

// Metadata parsing and gain reconstruction are Google's pinned reference
// implementation. WIC decodes the two JPEGs at the requested preview size;
// the C API's resize effect would first allocate/reconstruct full-size HDR.
struct GainMapInput {
    std::unique_ptr<uhdr_codec_private_t,decltype(&uhdr_release_decoder)> decoder{uhdr_create_decoder(),uhdr_release_decoder};
    bool apple=false;float appleHeadroom=1;
};
std::vector<uint8_t> appleJpegXml(uhdr_mem_block_t* data){
    if(!data||data->data_sz<4)return {};auto bytes=static_cast<uint8_t*>(data->data);size_t p=2;
    while(p+4<data->data_sz&&p<1024*1024){if(bytes[p++]!=255)break;unsigned marker=bytes[p++];if(marker==0xda||marker==0xd9)break;size_t n=(bytes[p]<<8)|bytes[p+1];p+=2;if(n<2||n-2>data->data_sz-p)throw Failure{WINCODEC_ERR_BADIMAGE};n-=2;
        std::string_view value(reinterpret_cast<char*>(bytes+p),n);std::string prefix="http://ns.adobe.com/xap/1.0/";prefix.push_back(0);
        if(marker==0xe1&&value.starts_with(prefix)&&value.find("http://ns.apple.com/HDRGainMap/1.0/")!=std::string::npos)return {bytes+p+prefix.size(),bytes+p+n};p+=n;
    }return {};
}
void checkGain(uhdr_error_info_t result){
    if(result.error_code!=UHDR_CODEC_OK){
        std::string detail=result.has_detail?result.detail:"Unsupported gain map";
        throw Failure{WINCODEC_ERR_BADIMAGE,L"HDR 增益图无法解码："+std::wstring(detail.begin(),detail.end())};
    }
}
std::unique_ptr<GainMapInput> probeGainMap(const wchar_t* path){
    std::ifstream stream(std::filesystem::path(path),std::ios::binary);
    if(!stream)return nullptr;
    if(stream.get()!=0xff||stream.get()!=0xd8)return nullptr;
    bool mpf=false,explicitHdr=false;
    // JPEG APP metadata precedes scan data. Ordinary camera JPEGs never load
    // their entire file just to look for an optional second image.
    for(unsigned segments=0;segments<256&&stream&&stream.tellg()<1024*1024;segments++){
        if(stream.get()!=0xff)break;
        int marker;do{marker=stream.get();}while(marker==0xff);
        if(marker==0xda||marker==0xd9||marker<0)break;
        if(marker==0x01||(marker>=0xd0&&marker<=0xd7))continue;
        int high=stream.get(),low=stream.get();if(high<0||low<0)break;
        unsigned length=(high<<8)|low;if(length<2)break;length-=2;
        if(marker==0xe1||marker==0xe2){
            std::string data(length,'\0');if(!stream.read(data.data(),length))break;
            mpf|=data.starts_with(std::string("MPF\0",4));
            explicitHdr|=data.find("hdr-gain-map")!=std::string::npos||data.find("21496")!=std::string::npos||data.find("HDRGainMap")!=std::string::npos;
        }else stream.seekg(length,std::ios::cur);
    }
    if(!mpf&&!explicitHdr)return nullptr;
    stream.clear();stream.seekg(0,std::ios::end);auto size=stream.tellg();
    if(size<=0||size>256ll*1024*1024)throw Failure{E_OUTOFMEMORY,L"HDR JPEG 压缩文件超出 256 MiB 解码预算"};
    stream.seekg(0);std::vector<uint8_t> bytes(static_cast<size_t>(size));
    if(!stream.read(reinterpret_cast<char*>(bytes.data()),size))throw Failure{WINCODEC_ERR_BADIMAGE};
    auto gain=std::make_unique<GainMapInput>();if(!gain->decoder)throw std::bad_alloc();
    uhdr_compressed_image_t input{};input.data=bytes.data();input.data_sz=input.capacity=bytes.size();
    input.cg=UHDR_CG_UNSPECIFIED;input.ct=UHDR_CT_UNSPECIFIED;input.range=UHDR_CR_UNSPECIFIED;
    checkGain(uhdr_dec_set_image(gain->decoder.get(),&input));
    auto result=uhdr_dec_probe(gain->decoder.get());
    if(result.error_code!=UHDR_CODEC_OK){if(!explicitHdr)return nullptr;checkGain(result);}
    int w=uhdr_dec_get_image_width(gain->decoder.get()),h=uhdr_dec_get_image_height(gain->decoder.get());
    if(w<=0||h<=0||uint64_t(w)*h>128ull*1024*1024)throw Failure{E_OUTOFMEMORY,L"HDR JPEG 源尺寸超出解码预算"};
    auto appleXml=appleJpegXml(uhdr_dec_get_gainmap_image(gain->decoder.get()));
    if(!appleXml.empty()){auto a=appleGainMetadata(appleXml);gain->apple=true;gain->appleHeadroom=a.headroom>0?a.headroom:uhdr_dec_get_gainmap_metadata(gain->decoder.get())->hdr_capacity_max;if(!std::isfinite(gain->appleHeadroom)||gain->appleHeadroom<1||gain->appleHeadroom>256)throw Failure{WINCODEC_ERR_BADIMAGE};}
    return gain;
}
struct GainJpegPixels {UINT width{},height{};std::vector<uint8_t> pixels;uhdr_color_gamut_t gamut=UHDR_CG_UNSPECIFIED;};
float gainLinear8(uint8_t value){static const auto table=[](){std::array<float,256> t{};for(unsigned i=0;i<256;i++)t[i]=ultrahdr::srgbInvOetf(i/255.0f);return t;}();return table[value];}
float gainRec7098(uint8_t value){static const auto table=[](){std::array<float,256> t{};for(unsigned i=0;i<256;i++){float v=i/255.0f;t[i]=v<.081f?v/4.5f:std::pow((v+.099f)/1.099f,1/.45f);}return t;}();return table[value];}
uint8_t gainSrgb8(float value){static const auto table=[](){std::array<uint8_t,65536> t{};for(unsigned i=0;i<65536;i++)t[i]=uint8_t(std::round(ultrahdr::srgbOetf(i/65535.0f)*255));return t;}();return table[unsigned(std::round(std::clamp(value,0.0f,1.0f)*65535))];}
GainJpegPixels gainJpeg(IWICImagingFactory* wic,uhdr_mem_block_t* data,UINT width,UINT height,bool readProfile=true){
    if(!data||!data->data||!data->data_sz||data->data_sz>UINT_MAX)throw Failure{WINCODEC_ERR_BADIMAGE};
    ComPtr<IWICStream> stream;check(wic->CreateStream(&stream));
    check(stream->InitializeFromMemory(static_cast<BYTE*>(data->data),UINT(data->data_sz)));
    ComPtr<IWICBitmapDecoder> decoder;check(wic->CreateDecoderFromStream(stream.Get(),nullptr,WICDecodeMetadataCacheOnDemand,&decoder));
    ComPtr<IWICBitmapFrameDecode> first;check(decoder->GetFrame(0,&first));UINT ow,oh;check(first->GetSize(&ow,&oh));
    if(!width){width=ow;height=oh;}
    GainJpegPixels result{width,height};
    UINT count=0;if(readProfile&&SUCCEEDED(first->GetColorContexts(0,nullptr,&count))&&count){
        ComPtr<IWICColorContext> profile;check(wic->CreateColorContext(&profile));IWICColorContext* contexts[]={profile.Get()};
        if(SUCCEEDED(first->GetColorContexts(1,contexts,&count))){
            UINT size=0;if(SUCCEEDED(profile->GetProfileBytes(0,nullptr,&size))&&size){
                // IccHelper expects a JPEG ICC APP2 payload, including its
                // 14-byte identifier/sequence prefix. WIC returns raw ICC.
                std::vector<uint8_t> icc(size+14);memcpy(icc.data(),"ICC_PROFILE\0\1\1",14);check(profile->GetProfileBytes(size,icc.data()+14,&size));
                result.gamut=ultrahdr::IccHelper::readIccColorGamut(icc.data(),icc.size());
                if(result.gamut==UHDR_CG_UNSPECIFIED)throw Failure{WINCODEC_ERR_UNSUPPORTEDPIXELFORMAT,L"HDR JPEG 使用了尚不支持的 ICC 色彩空间"};
            }
        }
    }
    ComPtr<IWICBitmapSource> source=first;std::vector<uint8_t> reduced;
    if(width!=ow||height!=oh){
        ComPtr<IWICBitmapSourceTransform> fast;if(SUCCEEDED(first.As(&fast))){
            UINT fw=width,fh=height;WICPixelFormatGUID format=GUID_WICPixelFormat24bppRGB;BOOL supported=FALSE;
            if(SUCCEEDED(fast->GetClosestSize(&fw,&fh))&&SUCCEEDED(fast->GetClosestPixelFormat(&format))&&(IsEqualGUID(format,GUID_WICPixelFormat24bppRGB)||IsEqualGUID(format,GUID_WICPixelFormat24bppBGR))&&SUCCEEDED(fast->DoesSupportTransform(WICBitmapTransformRotate0,&supported))&&supported){
                if(uint64_t(fw)*fh*3>256ull*1024*1024)throw Failure{E_OUTOFMEMORY};
                reduced.resize(size_t(fw)*fh*3);check(fast->CopyPixels(nullptr,fw,fh,&format,WICBitmapTransformRotate0,fw*3,UINT(reduced.size()),reduced.data()));
                ComPtr<IWICBitmap> bitmap;check(wic->CreateBitmapFromMemory(fw,fh,format,fw*3,UINT(reduced.size()),reduced.data(),&bitmap));source=bitmap;
            }
        }
        ComPtr<IWICBitmapScaler> scaler;check(wic->CreateBitmapScaler(&scaler));check(scaler->Initialize(source.Get(),width,height,WICBitmapInterpolationModeFant));source=scaler;
    }
    ComPtr<IWICFormatConverter> converter;check(wic->CreateFormatConverter(&converter));check(converter->Initialize(source.Get(),GUID_WICPixelFormat24bppRGB,WICBitmapDitherTypeNone,nullptr,0,WICBitmapPaletteTypeCustom));
    result.pixels.resize(size_t(width)*height*3);check(converter->CopyPixels(nullptr,width*3,UINT(result.pixels.size()),result.pixels.data()));
    return result;
}
struct GainReconstruction {std::vector<uint8_t> hdr,sdr,base;GainDisplay display;};
GainReconstruction reconstructGainMap(IWICImagingFactory* wic,GainMapInput& input,UINT width,UINT height){
    auto decoder=input.decoder.get();
    auto base=gainJpeg(wic,uhdr_dec_get_base_image(decoder),width,height);
    UINT gw=UINT(uhdr_dec_get_gainmap_width(decoder)),gh=UINT(uhdr_dec_get_gainmap_height(decoder));
    if(!gw||!gh||uint64_t(gw)*gh>128ull*1024*1024)throw Failure{WINCODEC_ERR_BADIMAGE};
    double ratio=std::min({1.0,double(width)/gw,double(height)/gh});
    auto map=gainJpeg(wic,uhdr_dec_get_gainmap_image(decoder),input.apple?width:std::max(1u,UINT(std::ceil(gw*ratio))),input.apple?height:std::max(1u,UINT(std::ceil(gh*ratio))));
    auto parsed=uhdr_dec_get_gainmap_metadata(decoder);if(!parsed)throw Failure{WINCODEC_ERR_BADIMAGE};auto value=*parsed;auto metadata=&value;
    if(input.apple){metadata->hdr_capacity_min=1;metadata->hdr_capacity_max=input.appleHeadroom;}
    ultrahdr::uhdr_gainmap_metadata_ext_t extended(*metadata,ultrahdr::kJpegrVersion);
    uhdr_raw_image_t sdr{},gain{},hdr{};
    // v2.0.2's RGB/YUV classifier excludes RGB888; use the fully supported
    // RGBA8888 base path rather than modifying the upstream implementation.
    std::vector<uint8_t> baseRgba(size_t(width)*height*4);
    for(size_t p=0;p<size_t(width)*height;p++){memcpy(baseRgba.data()+p*4,base.pixels.data()+p*3,3);baseRgba[p*4+3]=255;}
    sdr.fmt=UHDR_IMG_FMT_32bppRGBA8888;gain.fmt=UHDR_IMG_FMT_24bppRGB888;sdr.cg=base.gamut;gain.cg=map.gamut;
    sdr.ct=UHDR_CT_SRGB;sdr.range=gain.range=UHDR_CR_FULL_RANGE;
    sdr.w=width;sdr.h=height;sdr.planes[0]=baseRgba.data();sdr.stride[0]=width;
    gain.w=map.width;gain.h=map.height;gain.planes[0]=map.pixels.data();gain.stride[0]=map.width;
    std::vector<uint16_t> half(size_t(width)*height*4);
    hdr.fmt=UHDR_IMG_FMT_64bppRGBAHalfFloat;hdr.ct=UHDR_CT_LINEAR;hdr.w=width;hdr.h=height;hdr.stride[0]=width;hdr.planes[0]=half.data();
    if(!input.apple){ultrahdr::UltraHdr processor;checkGain(processor.applyGainMap(&sdr,&gain,&extended,UHDR_CT_LINEAR,hdr.fmt,FLT_MAX,&hdr));}
    else hdr.cg=sdr.cg==UHDR_CG_UNSPECIFIED?UHDR_CG_BT_709:sdr.cg;
    auto toSrgb=ultrahdr::getGamutConversionFn(UHDR_CG_BT_709,hdr.cg);
    auto baseToSrgb=ultrahdr::getGamutConversionFn(UHDR_CG_BT_709,base.gamut==UHDR_CG_UNSPECIFIED?UHDR_CG_BT_709:base.gamut);
    GainReconstruction result;result.hdr.resize(size_t(width)*height*16);result.sdr=std::move(baseRgba);
    result.display.minimum=metadata->hdr_capacity_min;result.display.maximum=metadata->hdr_capacity_max;result.display.model=input.apple?2:1;result.display.gamut=int(hdr.cg);
    memcpy(result.display.offsetSdr,metadata->offset_sdr,12);memcpy(result.display.offsetHdr,metadata->offset_hdr,12);
    if(hdr.cg!=UHDR_CG_BT_709)result.base=result.sdr;
    auto floats=reinterpret_cast<float*>(result.hdr.data());
    for(size_t p=0;p<size_t(width)*height;p++){
        ultrahdr::Color color{};
        if(input.apple){color.r=gainLinear8(base.pixels[p*3]);color.g=gainLinear8(base.pixels[p*3+1]);color.b=gainLinear8(base.pixels[p*3+2]);color=color*(1+(input.appleHeadroom-1)*gainRec7098(map.pixels[p*3]));}
        else{color.r=DirectX::PackedVector::XMConvertHalfToFloat(half[p*4]);color.g=DirectX::PackedVector::XMConvertHalfToFloat(half[p*4+1]);color.b=DirectX::PackedVector::XMConvertHalfToFloat(half[p*4+2]);}color=toSrgb(color);
        // libultrahdr linear output: diffuse white=203 nits. DXGI scRGB: 1=80
        // nits. Keep wide-gamut negative components; never clamp HDR to 8-bit.
        floats[p*4]=color.r*203.0f/80;floats[p*4+1]=color.g*203.0f/80;floats[p*4+2]=color.b*203.0f/80;floats[p*4+3]=1;
        for(unsigned c=0;c<3;c++)if(!std::isfinite(floats[p*4+c]))throw Failure{WINCODEC_ERR_BADIMAGE};
        ultrahdr::Color original{};original.r=base.pixels[p*3]/255.0f;original.g=base.pixels[p*3+1]/255.0f;original.b=base.pixels[p*3+2]/255.0f;
        if(base.gamut==UHDR_CG_UNSPECIFIED||base.gamut==UHDR_CG_BT_709){memcpy(result.sdr.data()+p*4,base.pixels.data()+p*3,3);}
        else{
            original=baseToSrgb(ultrahdr::srgbInvOetfLUT(original));original.r=std::max(0.0f,original.r);original.g=std::max(0.0f,original.g);original.b=std::max(0.0f,original.b);original=ultrahdr::srgbOetf(original);
            result.sdr[p*4]=uint8_t(std::round(std::clamp(original.r,0.0f,1.0f)*255));result.sdr[p*4+1]=uint8_t(std::round(std::clamp(original.g,0.0f,1.0f)*255));result.sdr[p*4+2]=uint8_t(std::round(std::clamp(original.b,0.0f,1.0f)*255));
        }
        result.sdr[p*4+3]=255;
    }
    return result;
}
