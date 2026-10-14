// Generate ISO gain-map JPEGs and independent full-resolution reference pixels
// using the unchanged official libultrahdr API. No Luma decoder is used here.
#include <ultrahdr_api.h>
#include <DirectXPackedVector.h>
#include <ultrahdr/gainmapmath.h>
#include <filesystem>
#include <fstream>
#include <vector>
#include <memory>
#include <cstdio>
void check(uhdr_error_info_t r){if(r.error_code!=UHDR_CODEC_OK){fprintf(stderr,"%s\n",r.detail);exit(1);}}
int wmain(int argc,wchar_t** argv){
    if(argc<3)return 2;
    std::filesystem::path path(argv[2]);
    if(std::wstring(argv[1])==L"encode"){
        unsigned w=argc>3?unsigned(_wtoi(argv[3])):320,h=argc>4?unsigned(_wtoi(argv[4])):240;
        std::vector<uint16_t> pixels(size_t(w)*h*4);
        for(unsigned y=0;y<h;y++)for(unsigned x=0;x<w;x++){
            size_t p=(size_t(y)*w+x)*4;float v=x<w/2?.25f:4.0f;
            pixels[p]=DirectX::PackedVector::XMConvertFloatToHalf(v);pixels[p+1]=DirectX::PackedVector::XMConvertFloatToHalf(y<h/2?v:v*.5f);pixels[p+2]=DirectX::PackedVector::XMConvertFloatToHalf(y<h/2?v:v*.2f);pixels[p+3]=DirectX::PackedVector::XMConvertFloatToHalf(1);
        }
        std::unique_ptr<uhdr_codec_private_t,decltype(&uhdr_release_encoder)> encoder(uhdr_create_encoder(),uhdr_release_encoder);
        uhdr_raw_image_t hdr{};hdr.fmt=UHDR_IMG_FMT_64bppRGBAHalfFloat;hdr.ct=UHDR_CT_LINEAR;hdr.cg=UHDR_CG_BT_709;hdr.range=UHDR_CR_FULL_RANGE;hdr.w=w;hdr.h=h;hdr.planes[0]=pixels.data();hdr.stride[0]=w;
        check(uhdr_enc_set_raw_image(encoder.get(),&hdr,UHDR_HDR_IMG));check(uhdr_enc_set_quality(encoder.get(),100,UHDR_BASE_IMG));check(uhdr_enc_set_quality(encoder.get(),100,UHDR_GAIN_MAP_IMG));check(uhdr_enc_set_using_multi_channel_gainmap(encoder.get(),1));
        check(uhdr_encode(encoder.get()));auto output=uhdr_get_encoded_stream(encoder.get());
        std::ofstream file(path,std::ios::binary);file.write(static_cast<char*>(output->data),output->data_sz);return file?0:1;
    }
    if(argc!=4||std::wstring(argv[1])!=L"reference")return 2;
    std::ifstream input(path,std::ios::binary);std::vector<char> bytes((std::istreambuf_iterator<char>(input)),std::istreambuf_iterator<char>());
    std::unique_ptr<uhdr_codec_private_t,decltype(&uhdr_release_decoder)> decoder(uhdr_create_decoder(),uhdr_release_decoder);
    uhdr_compressed_image_t compressed{};compressed.data=bytes.data();compressed.data_sz=compressed.capacity=bytes.size();compressed.cg=UHDR_CG_UNSPECIFIED;compressed.ct=UHDR_CT_UNSPECIFIED;compressed.range=UHDR_CR_UNSPECIFIED;
    check(uhdr_dec_set_image(decoder.get(),&compressed));check(uhdr_dec_set_out_img_format(decoder.get(),UHDR_IMG_FMT_64bppRGBAHalfFloat));check(uhdr_dec_set_out_color_transfer(decoder.get(),UHDR_CT_LINEAR));check(uhdr_decode(decoder.get()));
    auto raw=uhdr_get_decoded_image(decoder.get());auto toSrgb=ultrahdr::getGamutConversionFn(UHDR_CG_BT_709,raw->cg);auto half=static_cast<uint16_t*>(raw->planes[0]);
    std::ofstream output(std::filesystem::path(argv[3]),std::ios::binary);
    output.write(reinterpret_cast<char*>(&raw->w),4);output.write(reinterpret_cast<char*>(&raw->h),4);
    for(unsigned y=0;y<raw->h;y++)for(unsigned x=0;x<raw->w;x++){
        size_t p=(size_t(y)*raw->stride[0]+x)*4;ultrahdr::Color rgb{};rgb.r=DirectX::PackedVector::XMConvertHalfToFloat(half[p]);rgb.g=DirectX::PackedVector::XMConvertHalfToFloat(half[p+1]);rgb.b=DirectX::PackedVector::XMConvertHalfToFloat(half[p+2]);rgb=toSrgb(rgb);
        float floats[]={rgb.r*203/80,rgb.g*203/80,rgb.b*203/80,1};output.write(reinterpret_cast<char*>(floats),16);
    }
    return output?0:1;
}
