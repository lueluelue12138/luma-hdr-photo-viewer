#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <vector>
#include <cstdio>
using Microsoft::WRL::ComPtr;
int wmain(int argc,wchar_t** argv){if(argc!=2)return 2;CoInitializeEx(nullptr,COINIT_MULTITHREADED);
    ComPtr<IWICImagingFactory> f;HRESULT hr=CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&f));
    ComPtr<IWICStream> stream;if(SUCCEEDED(hr))hr=f->CreateStream(&stream);if(SUCCEEDED(hr))hr=stream->InitializeFromFilename(argv[1],GENERIC_WRITE);
    ComPtr<IWICBitmapEncoder> encoder;if(SUCCEEDED(hr))hr=f->CreateEncoder(GUID_ContainerFormatWmp,nullptr,&encoder);if(SUCCEEDED(hr))hr=encoder->Initialize(stream.Get(),WICBitmapEncoderNoCache);
    ComPtr<IWICBitmapFrameEncode> frame;ComPtr<IPropertyBag2> options;if(SUCCEEDED(hr))hr=encoder->CreateNewFrame(&frame,&options);if(SUCCEEDED(hr))hr=frame->Initialize(options.Get());
    if(SUCCEEDED(hr))hr=frame->SetSize(64,32);WICPixelFormatGUID format=GUID_WICPixelFormat128bppRGBAFloat;if(SUCCEEDED(hr))hr=frame->SetPixelFormat(&format);
    std::vector<float> pixels(64*32*4);for(size_t i=0;i<pixels.size();i+=4){pixels[i]=4;pixels[i+1]=2;pixels[i+2]=.5f;pixels[i+3]=1;}
    ComPtr<IWICBitmap> source;if(SUCCEEDED(hr))hr=f->CreateBitmapFromMemory(64,32,GUID_WICPixelFormat128bppRGBAFloat,64*16,UINT(pixels.size()*4),(BYTE*)pixels.data(),&source);
    if(SUCCEEDED(hr))hr=frame->WriteSource(source.Get(),nullptr);if(SUCCEEDED(hr))hr=frame->Commit();if(SUCCEEDED(hr))hr=encoder->Commit();
    printf("HDR fixture WIC result: 0x%08X\n",unsigned(hr));return FAILED(hr)?1:0;
}
