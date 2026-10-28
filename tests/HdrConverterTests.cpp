#include "../tools/HdrJpeg/Converter.h"
#include <fstream>
#include <iostream>
#include <functional>
using namespace hdrjpeg;
void require(bool ok,const char* message){if(!ok)throw std::runtime_error(message);}
void fixture(const fs::path& path,UINT w,UINT h,REFGUID requested,const std::function<std::array<float,4>(UINT,UINT)>& pixel,unsigned orientation=1){
    ComPtr<IWICImagingFactory> wic;check(CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&wic)),L"fixture WIC");
    ComPtr<IWICStream> stream;check(wic->CreateStream(&stream),L"stream");check(stream->InitializeFromFilename(path.c_str(),GENERIC_WRITE),L"fixture file");
    ComPtr<IWICBitmapEncoder> enc;check(wic->CreateEncoder(GUID_ContainerFormatWmp,nullptr,&enc),L"encoder");check(enc->Initialize(stream.Get(),WICBitmapEncoderNoCache),L"init");
    ComPtr<IWICBitmapFrameEncode> frame;ComPtr<IPropertyBag2> options;check(enc->CreateNewFrame(&frame,&options),L"frame");
    PROPBAG2 prop{};prop.pstrName=const_cast<wchar_t*>(L"Lossless");VARIANT value{};value.vt=VT_BOOL;value.boolVal=VARIANT_TRUE;check(options->Write(1,&prop,&value),L"lossless");
    check(frame->Initialize(options.Get()),L"init frame");check(frame->SetSize(w,h),L"size");WICPixelFormatGUID format=requested;check(frame->SetPixelFormat(&format),L"format");require(IsEqualGUID(format,requested),"fixture format substituted");
    if(orientation!=1){ComPtr<IWICMetadataQueryWriter> metadata;check(frame->GetMetadataQueryWriter(&metadata),L"metadata");PROPVARIANT v{};v.vt=VT_UI2;v.uiVal=USHORT(orientation);check(metadata->SetMetadataByName(L"/ifd/{ushort=274}",&v),L"orientation");}
    bool bytes=IsEqualGUID(format,GUID_WICPixelFormat32bppBGRA);UINT stride=w*(bytes?4:16);std::vector<BYTE> storage(size_t(stride)*h);
    for(UINT y=0;y<h;y++)for(UINT x=0;x<w;x++){auto c=pixel(x,y);if(bytes){for(int j=0;j<4;j++)storage[(size_t(y)*w+x)*4+j]=BYTE(std::clamp(c[j],0.0f,1.0f)*255);}else memcpy(storage.data()+(size_t(y)*w+x)*16,c.data(),16);}
    check(frame->WritePixels(h,stride,UINT(storage.size()),storage.data()),L"pixels");check(frame->Commit(),L"commit frame");check(enc->Commit(),L"commit");
}
std::vector<BYTE> read(const fs::path& p){std::ifstream f(p,std::ios::binary);return {std::istreambuf_iterator<char>(f),{}};}
struct Decoded{UINT w,h;int gamut;std::vector<float> pixels;};
Decoded decode(const fs::path& p){
    auto bytes=read(p);require(is_uhdr_image(bytes.data(),int(bytes.size()))!=0,"not Ultra HDR");
    require(std::search(bytes.begin(),bytes.end(),"http://ns.adobe.com/hdr-gain-map/1.0/","http://ns.adobe.com/hdr-gain-map/1.0/"+35)!=bytes.end(),"XMP missing");
    require(std::search(bytes.begin(),bytes.end(),"urn:iso:std:iso:ts:21496:-1","urn:iso:std:iso:ts:21496:-1"+26)!=bytes.end(),"ISO missing");
    std::unique_ptr<uhdr_codec_private_t,decltype(&uhdr_release_decoder)> dec(uhdr_create_decoder(),uhdr_release_decoder);
    uhdr_compressed_image_t data{};data.data=bytes.data();data.data_sz=data.capacity=bytes.size();codec(uhdr_dec_set_image(dec.get(),&data));
    codec(uhdr_dec_set_out_img_format(dec.get(),UHDR_IMG_FMT_64bppRGBAHalfFloat));codec(uhdr_dec_set_out_color_transfer(dec.get(),UHDR_CT_LINEAR));codec(uhdr_dec_probe(dec.get()));
    require(uhdr_dec_get_gainmap_width(dec.get())==uhdr_dec_get_image_width(dec.get()),"gain map is not full size");
    auto meta=uhdr_dec_get_gainmap_metadata(dec.get());require(meta&&meta->hdr_capacity_max>1,"HDR capacity missing");
    auto authoring=uhdr_dec_get_exif(dec.get());require(authoring&&luma::absolute_hdr::contains({static_cast<const uint8_t*>(authoring->data),authoring->data_sz}),"absolute luminance authoring description missing");
    codec(uhdr_decode(dec.get()));
    auto* image=uhdr_get_decoded_image(dec.get());Decoded out{image->w,image->h,int(image->cg)};out.pixels.resize(size_t(out.w)*out.h*3);
    for(UINT y=0;y<out.h;y++)for(UINT x=0;x<out.w;x++)for(int c=0;c<3;c++)out.pixels[(size_t(y)*out.w+x)*3+c]=ultrahdr::halfToFloat(static_cast<uint16_t*>(image->planes[0])[(size_t(y)*image->stride[0]+x)*4+c]);
    return out;
}
int wmain(int argc,wchar_t** argv){
    if(argc==4&&std::wstring(argv[1])==L"--convert"){
        try{auto r=convert(argv[2],argv[3]);std::cout<<"Converted "<<r.width<<"x"<<r.height<<", "<<r.bytes<<" bytes; peak="<<r.peakNits<<" nits; clipped="<<r.clippedPixels<<"\n";return 0;}catch(...){return 1;}
    }
    if(argc<2)return 2;ComScope com;fs::path root=fs::path(argv[1])/std::to_wstring(GetTickCount64());fs::create_directories(root);
    try{
        auto exif=luma::absolute_hdr::exif();require(luma::absolute_hdr::contains(exif),"absolute HDR metadata round trip");
        require(luma::absolute_hdr::contains(std::span<const uint8_t>(exif).subspan(6)),"TIFF metadata round trip");
        for(size_t n=0;n<exif.size();n++)require(!luma::absolute_hdr::contains(std::span<const uint8_t>(exif).first(n)),"truncated authoring tag accepted");
        exif.back()=1;require(!luma::absolute_hdr::contains(exif),"invalid authoring tag accepted");
        const float levels[]={0,.05f,.5f,2.5375f,5.075f,10,20,50};
        auto source=root/L"灰阶 HDR 原片.jxr";fixture(source,512,64,GUID_WICPixelFormat128bppRGBAFloat,[&](UINT x,UINT){float v=levels[x/64];return std::array<float,4>{v,v,v,1};});
        auto output=root/L"灰阶_UltraHDR.jpg";auto result=convert(source,output);require(result.width==512&&result.height==64,"dimensions changed");
        auto decoded=decode(output);require(decoded.w==512&&decoded.h==64,"decoded dimensions changed");
        std::cout<<"Output gamut: "<<decoded.gamut<<"\n";
        for(int i=0;i<8;i++){
            float actual=decoded.pixels[(32*512+i*64+32)*3]*203,expected=levels[i]*80;
            std::cout<<"Endpoint nits expected="<<expected<<" decoded="<<actual<<"\n";
            require(std::abs(actual-expected)<std::max(1.0f,expected*.08f),"absolute luminance endpoint mismatch");
        }
        // Independent SDR decoder must read the primary JPEG without HDR support.
        ComPtr<IWICImagingFactory> wic;check(CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&wic)),L"SDR WIC");ComPtr<IWICBitmapDecoder> jpeg;
        check(wic->CreateDecoderFromFilename(output.c_str(),nullptr,GENERIC_READ,WICDecodeMetadataCacheOnDemand,&jpeg),L"SDR compatibility");ComPtr<IWICBitmapFrameDecode> frame;check(jpeg->GetFrame(0,&frame),L"SDR frame");
        ComPtr<IWICFormatConverter> sdr;check(wic->CreateFormatConverter(&sdr),L"SDR converter");check(sdr->Initialize(frame.Get(),GUID_WICPixelFormat24bppRGB,WICBitmapDitherTypeNone,nullptr,0,WICBitmapPaletteTypeCustom),L"SDR format");
        std::vector<BYTE> rgb(512*64*3);check(sdr->CopyPixels(nullptr,512*3,UINT(rgb.size()),rgb.data()),L"SDR pixels");require(rgb[(32*512+3*64+32)*3]>100,"SDR base too dark");
        auto before=read(output);bool refused=false;try{convert(source,output);}catch(const Error&){refused=true;}require(refused&&read(output)==before,"existing output overwritten");
        require(availableName(root,source)!=output,"collision name check");
        auto collisionSource=root/L"灰阶.jxr";require(availableName(root,collisionSource).filename()==L"灰阶_UltraHDR (1).jpg","collision numbering");
        std::atomic_bool stop=true;auto cancelOut=root/L"cancelled.jpg";refused=false;try{convert(source,cancelOut,&stop);}catch(const Error&){refused=true;}require(refused&&!fs::exists(cancelOut),"cancelled output written");
        auto ordinary=root/L"ordinary.jxr";fixture(ordinary,64,64,GUID_WICPixelFormat32bppBGRA,[](UINT,UINT){return std::array<float,4>{.5f,.5f,.5f,1};});
        refused=false;try{convert(ordinary,root/L"not-hdr.jpg");}catch(const Error&){refused=true;}require(refused&&!fs::exists(root/L"not-hdr.jpg"),"SDR JXR accepted as HDR");
        {std::ofstream bad(root/L"corrupt.jxr");bad<<"not an image";}refused=false;try{convert(root/L"corrupt.jxr",root/L"corrupt.jpg");}catch(const Error&){refused=true;}require(refused&&!fs::exists(root/L"corrupt.jpg"),"corrupt input accepted");
        auto odd=root/L"odd.jxr";fixture(odd,129,65,GUID_WICPixelFormat128bppRGBAFloat,[](UINT,UINT){return std::array<float,4>{4,2,.5f,1};});
        convert(odd,root/L"odd.jpg");auto oddPixels=decode(root/L"odd.jpg");require(oddPixels.w==129&&oddPixels.h==65,"odd dimensions changed");
        for(unsigned o=2;o<=8;o++){
            auto f=root/(L"orientation"+std::to_wstring(o)+L".jxr");auto jpg=f;jpg.replace_extension(L".jpg");fixture(f,64,32,GUID_WICPixelFormat128bppRGBAFloat,[](UINT x,UINT y){float v=1.0f+(x>=32?2.0f:0)+(y>=16?4.0f:0);return std::array<float,4>{v,v,v,1};},o);
            convert(f,jpg);auto d=decode(jpg);require(d.w==(o>=5?32u:64u)&&d.h==(o>=5?64u:32u),"orientation dimensions wrong");
            const int order[8][4]={{1,3,5,7},{3,1,7,5},{7,5,3,1},{5,7,1,3},{1,5,3,7},{5,1,7,3},{7,3,5,1},{3,7,1,5}};
            for(int corner=0;corner<4;corner++){UINT x=corner%2?d.w-6:5,y=corner/2?d.h-6:5;float value=d.pixels[(size_t(y)*d.w+x)*3]*203;require(std::abs(value-order[o-1][corner]*80)<30,"EXIF orientation corner mismatch");}
        }
        auto wide=root/L"wide-gamut.jxr";fixture(wide,64,64,GUID_WICPixelFormat128bppRGBAFloat,[](UINT,UINT){return std::array<float,4>{-.5f,4,.5f,1};});auto wideResult=convert(wide,root/L"wide-gamut.jpg");require(wideResult.clippedPixels==0,"negative scRGB was clipped before gamut conversion");decode(root/L"wide-gamut.jpg");
        auto clip=root/L"clipped.jxr";fixture(clip,64,64,GUID_WICPixelFormat128bppRGBAFloat,[](UINT,UINT){return std::array<float,4>{200,200,200,1};});
        refused=false;try{convert(clip,root/L"clipped.jpg");}catch(const Error&){refused=true;}require(refused&&!fs::exists(root/L"clipped.jpg"),"out of range image silently clipped");
        if(argc>=3){unsigned count=0;for(const auto& entry:fs::directory_iterator(argv[2]))if(entry.path().extension()==L".jxr"&&entry.path().filename().wstring().starts_with(L"3")){
            auto begin=GetTickCount64();auto converted=root/entry.path().filename();converted.replace_extension(L".jpg");auto r=convert(entry.path(),converted);decode(converted);
            std::cout<<"Real screenshot "<<++count<<": "<<r.width<<"x"<<r.height<<", "<<r.bytes<<" bytes, "<<GetTickCount64()-begin<<" ms, clipped="<<r.clippedPixels<<"\n";}require(count==6,"expected six real JXR screenshots");}
        std::cout<<"PASS: linear luminance, dual metadata, full RGB gain map, SDR JPEG, no overwrite, collision names, cancellation, corrupt/SDR rejection, odd dimensions, 7 EXIF orientations, wide gamut and refusal to clip.\n";
        std::wofstream location(fs::path(argv[1])/L"latest.txt");location<<root.wstring();return 0;
    }catch(const Error& e){int n=WideCharToMultiByte(CP_UTF8,0,e.message.c_str(),-1,nullptr,0,nullptr,nullptr);std::string text(n,'\0');WideCharToMultiByte(CP_UTF8,0,e.message.c_str(),-1,text.data(),n,nullptr,nullptr);std::cerr<<"FAIL: "<<text.c_str()<<"\n";}
    catch(const std::exception& e){std::cerr<<"FAIL: "<<e.what()<<"\n";}return 1;
}
