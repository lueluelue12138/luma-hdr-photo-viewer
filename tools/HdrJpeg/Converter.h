#pragma once
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <ultrahdr_api.h>
#pragma warning(push)
#pragma warning(disable:4201 4244)
#include <ultrahdr/gainmapmath.h>
#include <ultrahdr/jpegr.h>
#include <ultrahdr/icc.h>
#pragma warning(pop)
#include "../../src/Shared/AbsoluteHdr.h"
#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <filesystem>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

namespace hdrjpeg {
using Microsoft::WRL::ComPtr;
namespace fs = std::filesystem;
struct Error { std::wstring message; };
inline void check(HRESULT hr, const wchar_t* message) {
    if (FAILED(hr)) { wchar_t code[32]; swprintf_s(code,L" (0x%08X)",unsigned(hr)); throw Error{std::wstring(message)+code}; }
}
inline void codec(uhdr_error_info_t result) {
    if (result.error_code != UHDR_CODEC_OK) {
        std::string detail=result.has_detail?result.detail:"Ultra HDR codec error";
        throw Error{L"Ultra HDR 编码失败："+std::wstring(detail.begin(),detail.end())};
    }
}
struct ComScope {
    HRESULT hr=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
    ComScope(){if(FAILED(hr)&&hr!=RPC_E_CHANGED_MODE)check(hr,L"无法初始化图像组件");}
    ~ComScope(){if(SUCCEEDED(hr))CoUninitialize();}
};
struct Result { UINT width{},height{}; float peakNits{}; size_t bytes{},clippedPixels{}; };
inline bool linearFormat(REFGUID f) {
    const GUID formats[]={GUID_WICPixelFormat128bppRGBAFloat,GUID_WICPixelFormat128bppRGBFloat,
        GUID_WICPixelFormat64bppRGBAHalf,GUID_WICPixelFormat48bppRGBHalf,GUID_WICPixelFormat64bppRGBHalf,
        GUID_WICPixelFormat128bppRGBAFixedPoint,GUID_WICPixelFormat128bppRGBFixedPoint,
        GUID_WICPixelFormat64bppRGBAFixedPoint,GUID_WICPixelFormat64bppRGBFixedPoint,GUID_WICPixelFormat48bppRGBFixedPoint};
    for(const auto& known:formats)if(IsEqualGUID(f,known))return true;
    return false;
}
inline void cancelled(const std::atomic_bool* stop){if(stop&&stop->load())throw Error{L"已取消"};}

// The output name is created exclusively. Partial files are removed on write failure;
// an existing file (including one created concurrently) is never replaced.
inline void saveExclusive(const fs::path& path,const void* data,size_t bytes) {
    HANDLE file=CreateFileW(path.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE)throw Error{GetLastError()==ERROR_FILE_EXISTS?L"同名文件已存在，请换一个名称":L"无法保存，请检查文件夹权限、路径和可用空间"};
    const auto* p=static_cast<const BYTE*>(data);size_t remaining=bytes;bool ok=true;
    while(remaining){DWORD written=0,chunk=DWORD(std::min<size_t>(remaining,1<<20));if(!WriteFile(file,p,chunk,&written,nullptr)||!written){ok=false;break;}p+=written;remaining-=written;}
    if(ok)ok=FlushFileBuffers(file)!=FALSE;
    CloseHandle(file);
    if(!ok){DeleteFileW(path.c_str());throw Error{L"写入未完成，请检查磁盘空间后重试"};}
}

inline Result convert(const fs::path& input,const fs::path& output,const std::atomic_bool* stop=nullptr) {
    ComScope com;cancelled(stop);
    ComPtr<IWICImagingFactory> wic;
    check(CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&wic)),L"无法启动 Windows 图像解码器");
    ComPtr<IWICBitmapDecoder> decoder;
    check(wic->CreateDecoderFromFilename(input.c_str(),nullptr,GENERIC_READ,WICDecodeMetadataCacheOnDemand,&decoder),L"无法读取图片，文件可能已损坏或不可访问");
    GUID container{};check(decoder->GetContainerFormat(&container),L"无法识别图片格式");
    if(!IsEqualGUID(container,GUID_ContainerFormatWmp))throw Error{L"请选择 Windows HDR 截图（JXR / WDP / HDP）"};
    ComPtr<IWICBitmapFrameDecode> frame;check(decoder->GetFrame(0,&frame),L"无法读取图片");
    WICPixelFormatGUID format{};check(frame->GetPixelFormat(&format),L"无法识别像素格式");
    if(!linearFormat(format))throw Error{L"这张 JXR 不是支持的线性 HDR 图片；普通 8 / 16 位图片不会被当成 HDR"};
    ComPtr<IWICBitmapSource> source=frame;
    ComPtr<IWICMetadataQueryReader> meta;
    if(SUCCEEDED(frame->GetMetadataQueryReader(&meta))){
        PROPVARIANT orientation;PropVariantInit(&orientation);
        if(SUCCEEDED(meta->GetMetadataByName(L"/ifd/{ushort=274}",&orientation))&&orientation.vt==VT_UI2&&orientation.uiVal>=2&&orientation.uiVal<=8){
            const WICBitmapTransformOptions transforms[]={WICBitmapTransformRotate0,WICBitmapTransformFlipHorizontal,WICBitmapTransformRotate180,WICBitmapTransformFlipVertical,
                WICBitmapTransformOptions(WICBitmapTransformRotate270|WICBitmapTransformFlipHorizontal),WICBitmapTransformRotate90,
                WICBitmapTransformOptions(WICBitmapTransformRotate90|WICBitmapTransformFlipHorizontal),WICBitmapTransformRotate270};
            ComPtr<IWICBitmapFlipRotator> rotated;check(wic->CreateBitmapFlipRotator(&rotated),L"无法读取方向");
            check(rotated->Initialize(source.Get(),transforms[orientation.uiVal-1]),L"无法应用图片方向");source=rotated;
        }PropVariantClear(&orientation);
    }
    Result result;check(source->GetSize(&result.width,&result.height),L"无法读取图片尺寸");
    UINT w=result.width,h=result.height;
    if(w<8||h<8||w>16384||h>16384||uint64_t(w)*h>40000000)throw Error{L"图片尺寸需为 8–16384 像素，且不超过 4000 万像素；请先裁切后重试"};
    ComPtr<IWICFormatConverter> floats;check(wic->CreateFormatConverter(&floats),L"无法转换像素");
    check(floats->Initialize(source.Get(),GUID_WICPixelFormat128bppRGBAFloat,WICBitmapDitherTypeNone,nullptr,0,WICBitmapPaletteTypeCustom),L"此 JXR 像素格式暂不支持");
    std::vector<uint16_t> hdr(size_t(w)*h*4);
    std::vector<float> strip(size_t(w)*64*4);
    // WIC linear scRGB: sRGB primaries, 1 = 80 cd/m². libultrahdr linear:
    // 1 = 203 cd/m². Convert to Rec.2020 before clamping to preserve wide gamut
    // colors represented by negative scRGB components. No display setting is read.
    for(UINT y=0;y<h;y+=64){
        cancelled(stop);UINT rows=std::min(64u,h-y);WICRect rect{0,INT(y),INT(w),INT(rows)};
        check(floats->CopyPixels(&rect,w*16,w*rows*16,reinterpret_cast<BYTE*>(strip.data())),L"HDR 像素读取失败");
        for(size_t i=0;i<size_t(w)*rows;i++){
            const float* p=strip.data()+i*4;
            for(int c=0;c<4;c++)if(!std::isfinite(p[c]))throw Error{L"图片含有无效的 HDR 数值，未生成输出"};
            float alpha=std::clamp(p[3],0.0f,1.0f)*80.0f/203.0f;
            std::array<float,3> rgb={
                (.6274039f*p[0]+.3292830f*p[1]+.0433131f*p[2])*alpha,
                (.0690973f*p[0]+.9195404f*p[1]+.0113623f*p[2])*alpha,
                (.0163914f*p[0]+.0880133f*p[1]+.8955953f*p[2])*alpha};
            bool clipped=false;size_t at=(size_t(y)*w+i)*4;
            for(int c=0;c<3;c++){if(rgb[c]<-.0001f||rgb[c]>10000.0f/203.0f)clipped=true;
                result.peakNits=std::max(result.peakNits,rgb[c]*203.0f);
                hdr[at+c]=ultrahdr::floatToHalf(std::clamp(rgb[c],0.0f,10000.0f/203.0f));}
            hdr[at+3]=ultrahdr::floatToHalf(1);if(clipped)result.clippedPixels++;
        }
    }
    cancelled(stop);
    if(result.clippedPixels)throw Error{L"原图含有超出 Rec.2020 或 10000 nits 的色值，当前无法保真转换，未生成文件"};
    std::unique_ptr<uhdr_codec_private_t,decltype(&uhdr_release_encoder)> encoder(uhdr_create_encoder(),uhdr_release_encoder);
    if(!encoder)throw Error{L"内存不足，请关闭其他大图后重试"};
    uhdr_raw_image_t image{};image.fmt=UHDR_IMG_FMT_64bppRGBAHalfFloat;image.cg=UHDR_CG_BT_2100;image.ct=UHDR_CT_LINEAR;
    image.range=UHDR_CR_FULL_RANGE;image.w=w;image.h=h;image.planes[0]=hdr.data();image.stride[0]=w;
    codec(uhdr_enc_set_raw_image(encoder.get(),&image,UHDR_HDR_IMG));
    codec(uhdr_enc_set_quality(encoder.get(),100,UHDR_BASE_IMG));codec(uhdr_enc_set_quality(encoder.get(),100,UHDR_GAIN_MAP_IMG));
    codec(uhdr_enc_set_using_multi_channel_gainmap(encoder.get(),1));codec(uhdr_enc_set_gainmap_scale_factor(encoder.get(),1));
    codec(uhdr_enc_set_preset(encoder.get(),UHDR_USAGE_BEST_QUALITY));codec(uhdr_enc_set_output_format(encoder.get(),UHDR_CODEC_JPG));
    // LINEAR's library default is 10000 nits even for a 1600-nit screenshot.
    // Capacity is the *display adaptation* target, not the theoretical format limit.
    codec(uhdr_enc_set_target_display_peak_brightness(encoder.get(),std::clamp(result.peakNits,203.0f,10000.0f)));
    // HDR-only encoding silently overrides BEST_QUALITY and computes gains before
    // JPEG compression. Instead supply a quality-100 4:4:4 primary, then let the
    // library derive the map from that actual compressed primary (encode API-3).
    std::vector<uint8_t> sdrPixels(size_t(w)*h*4),planes(size_t(w)*h*3);
    uhdr_raw_image_t sdr{};sdr.fmt=UHDR_IMG_FMT_32bppRGBA8888;sdr.w=w;sdr.h=h;sdr.stride[0]=w;sdr.planes[0]=sdrPixels.data();
    ultrahdr::UltraHdr processor;codec(processor.toneMap(&image,&sdr));
    size_t area=size_t(w)*h;
    for(size_t p=0;p<area;p++){
        float r=sdrPixels[p*4],g=sdrPixels[p*4+1],b=sdrPixels[p*4+2];
        auto byte=[](float v){return uint8_t(std::clamp(std::lround(v),0l,255l));};
        planes[p]=byte(.299f*r+.587f*g+.114f*b);
        planes[area+p]=byte(-.168736f*r-.331264f*g+.5f*b+128);
        planes[area*2+p]=byte(.5f*r-.418688f*g-.081312f*b+128);
    }
    cancelled(stop);
    uhdr_raw_image_t yuv{};yuv.fmt=UHDR_IMG_FMT_24bppYCbCr444;yuv.w=w;yuv.h=h;yuv.cg=sdr.cg;yuv.ct=UHDR_CT_SRGB;yuv.range=UHDR_CR_FULL_RANGE;
    for(int c=0;c<3;c++){yuv.planes[c]=planes.data()+area*c;yuv.stride[c]=w;}
    auto icc=ultrahdr::IccHelper::writeIccProfile(UHDR_CT_SRGB,sdr.cg);
    ultrahdr::JpegEncoderHelper baseEncoder;codec(baseEncoder.compressImage(&yuv,100,icc->getData(),icc->getLength()));
    auto base=baseEncoder.getCompressedImage();auto metadata=luma::absolute_hdr::exif();
    std::vector<uint8_t> primary;primary.reserve(base.data_sz+metadata.size()+4);
    auto bytes=static_cast<const uint8_t*>(base.data);primary.insert(primary.end(),bytes,bytes+2);
    size_t segment=metadata.size()+2;primary.insert(primary.end(),{255,225,uint8_t(segment>>8),uint8_t(segment)});
    primary.insert(primary.end(),metadata.begin(),metadata.end());primary.insert(primary.end(),bytes+2,bytes+base.data_sz);
    base.data=primary.data();base.data_sz=base.capacity=primary.size();base.cg=sdr.cg;base.ct=UHDR_CT_SRGB;base.range=UHDR_CR_FULL_RANGE;
    codec(uhdr_enc_set_compressed_image(encoder.get(),&base,UHDR_SDR_IMG));
    codec(uhdr_encode(encoder.get()));cancelled(stop);
    auto* encoded=uhdr_get_encoded_stream(encoder.get());
    if(!encoded||!encoded->data||!encoded->data_sz||encoded->data_sz>INT_MAX||!is_uhdr_image(encoded->data,int(encoded->data_sz)))throw Error{L"输出增益图检查失败，未保存图片"};
    result.bytes=encoded->data_sz;saveExclusive(output,encoded->data,encoded->data_sz);return result;
}
inline fs::path availableName(const fs::path& folder,const fs::path& source){
    auto stem=source.stem().wstring()+L"_UltraHDR";
    for(unsigned n=0;n<100000;n++){auto path=folder/(stem+(n?L" ("+std::to_wstring(n)+L")":L"")+L".jpg");if(!fs::exists(path))return path;}
    throw Error{L"同名文件过多，请选择其他文件夹"};
}
}
