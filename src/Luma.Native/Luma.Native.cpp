#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_6.h>
#include <d3dcompiler.h>
#include <d2d1_1.h>
#include <d2d1effects.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <string>
#include <vector>
#include <algorithm>
#include <cmath>
#include <memory>
#include <new>
#include <cstdint>
#include <filesystem>
#include <mutex>
#ifdef LUMA_NVIDIA
#pragma warning(push)
#pragma warning(disable:4100)
#include <nvsdk_ngx_helpers_truehdr.h>
#include <nvsdk_ngx_helpers_vsr.h>
#pragma warning(pop)
#pragma comment(lib,"nvsdk_ngx_s.lib")
#pragma comment(lib,"advapi32.lib")
#endif
using Microsoft::WRL::ComPtr;
#ifdef LUMA_NVIDIA
// NGX is process-wide and not thread safe, including devices in different windows.
std::mutex ngxMutex;
#endif
#pragma comment(lib,"d3d11.lib")
#pragma comment(lib,"dxgi.lib")
#pragma comment(lib,"d3dcompiler.lib")
#pragma comment(lib,"d2d1.lib")
#pragma comment(lib,"dxguid.lib")
#pragma comment(lib,"windowscodecs.lib")
#pragma comment(lib,"ole32.lib")
#pragma comment(lib,"user32.lib")

// WinUI 3 Microsoft.UI.Xaml.Media.DXInterop.h ABI, not the UWP panel ABI.
struct __declspec(uuid("63aad0b8-7c24-40ff-85a8-640d944cc325")) PanelNative : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE SetSwapChain(IDXGISwapChain*) = 0;
};
thread_local std::wstring lastError;
struct Failure { HRESULT hr; };
void check(HRESULT hr) { if(FAILED(hr)) throw Failure{hr}; }
void report(HRESULT hr) { wchar_t* s=nullptr; FormatMessageW(FORMAT_MESSAGE_ALLOCATE_BUFFER|FORMAT_MESSAGE_FROM_SYSTEM|FORMAT_MESSAGE_IGNORE_INSERTS,nullptr,hr,0,(wchar_t*)&s,0,nullptr); lastError=s?s:L"Native rendering error"; if(s)LocalFree(s); }
template<class F> HRESULT safe(F f) noexcept {try{ f(); lastError.clear(); return S_OK; }catch(Failure e){report(e.hr);return e.hr;}catch(std::bad_alloc&){lastError=L"Insufficient memory";return E_OUTOFMEMORY;}catch(...){lastError=L"Unexpected native error";return E_FAIL;}}
struct FrameInfo {uint32_t width,height,originalWidth,originalHeight; int32_t nativeHdr,downsampled; uint64_t bytes;};
struct Frame {FrameInfo info{}; std::vector<uint8_t> pixels;};
struct Status { wchar_t gpu[128]; wchar_t display[128]; int32_t nvidia,hdrActive,fp16,nativeHdr; float maxNits,sdrWhite; uint64_t gpuBytes; int32_t hdrEffective,vsrEffective,sdkAvailable,hdrAvailable,vsrAvailable; uint32_t ngxResult,hdrEvaluations,vsrEvaluations; wchar_t hdrMessage[256],vsrMessage[256];};
#include "Glass.h"

std::unique_ptr<Frame> decode(const wchar_t* path,UINT maxEdge=0) {
    HRESULT init=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
    struct Uninit{bool yes;~Uninit(){if(yes)CoUninitialize();}} uninit{SUCCEEDED(init)};
    ComPtr<IWICImagingFactory> wic; check(CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&wic)));
    ComPtr<IWICBitmapDecoder> decoder; check(wic->CreateDecoderFromFilename(path,nullptr,GENERIC_READ,WICDecodeMetadataCacheOnDemand,&decoder));
    ComPtr<IWICBitmapFrameDecode> first; check(decoder->GetFrame(0,&first));
    UINT ow,oh; check(first->GetSize(&ow,&oh)); if(!ow||!oh)throw Failure{E_INVALIDARG};
    WICPixelFormatGUID format; check(first->GetPixelFormat(&format));
    // Float JPEG XR is encoded in linear scRGB. Integer-depth files/gain maps are not guessed HDR.
    bool hdr=IsEqualGUID(format,GUID_WICPixelFormat128bppRGBAFloat)||IsEqualGUID(format,GUID_WICPixelFormat128bppRGBFloat)||IsEqualGUID(format,GUID_WICPixelFormat64bppRGBAHalf)||IsEqualGUID(format,GUID_WICPixelFormat48bppRGBHalf)||IsEqualGUID(format,GUID_WICPixelFormat64bppRGBHalf);
    ComPtr<IWICBitmapSource> source=first;
    // Decode JPEG at its codec's native reduced resolution before ICC conversion.
    // A screen-sized preview should not transform 33 million pixels first.
    double budget=hdr?16000000.0:64000000.0;
    double ratio=std::min({1.0,16384.0/ow,16384.0/oh,std::sqrt(budget/(double(ow)*oh))});
    if(maxEdge)ratio=std::min(ratio,double(maxEdge)/std::max(ow,oh));
    UINT width=std::max(1u,UINT(std::ceil(ow*ratio))),height=std::max(1u,UINT(std::ceil(oh*ratio)));
    std::vector<uint8_t> reduced;
    GUID container{};decoder->GetContainerFormat(&container);
    if(!hdr&&ratio<1&&IsEqualGUID(container,GUID_ContainerFormatJpeg)) {
        ComPtr<IWICBitmapSourceTransform> fast;
        if(SUCCEEDED(first.As(&fast))) {
            UINT fw=width,fh=height;WICPixelFormatGUID pixel=GUID_WICPixelFormat24bppBGR;BOOL supported=FALSE;
            if(SUCCEEDED(fast->GetClosestSize(&fw,&fh))&&SUCCEEDED(fast->GetClosestPixelFormat(&pixel))&&IsEqualGUID(pixel,GUID_WICPixelFormat24bppBGR)&&SUCCEEDED(fast->DoesSupportTransform(WICBitmapTransformRotate0,&supported))&&supported&&uint64_t(fw)*fh*3<=256ull*1024*1024) {
                reduced.resize(size_t(fw)*fh*3);
                if(SUCCEEDED(fast->CopyPixels(nullptr,fw,fh,&pixel,WICBitmapTransformRotate0,fw*3,UINT(reduced.size()),reduced.data()))) {
                    ComPtr<IWICBitmap> bitmap;check(wic->CreateBitmapFromMemory(fw,fh,pixel,fw*3,UINT(reduced.size()),reduced.data(),&bitmap));source=bitmap;
                }
            }
        }
    }
    if(ratio<1) {ComPtr<IWICBitmapScaler> scaler;check(wic->CreateBitmapScaler(&scaler));check(scaler->Initialize(source.Get(),width,height,WICBitmapInterpolationModeFant));source=scaler;}
    if(!hdr) {
        UINT count=0; first->GetColorContexts(0,nullptr,&count);
        if(count) {
            ComPtr<IWICColorContext> input,output; check(wic->CreateColorContext(&input)); check(wic->CreateColorContext(&output));
            IWICColorContext* contexts[]={input.Get()};
            if(SUCCEEDED(first->GetColorContexts(1,contexts,&count))&&SUCCEEDED(output->InitializeFromExifColorSpace(1))) {
                ComPtr<IWICColorTransform> transform; check(wic->CreateColorTransformer(&transform));
                // If a supplied profile cannot be applied, fail rather than silently display wrong colors.
                check(transform->Initialize(source.Get(),input.Get(),output.Get(),GUID_WICPixelFormat32bppRGBA)); source=transform;
            }
        }
    }
    ComPtr<IWICFormatConverter> converter;check(wic->CreateFormatConverter(&converter));
    check(converter->Initialize(source.Get(),hdr?GUID_WICPixelFormat128bppRGBAFloat:GUID_WICPixelFormat32bppRGBA,WICBitmapDitherTypeNone,nullptr,0,WICBitmapPaletteTypeCustom));source=converter;
    UINT orientation=1; ComPtr<IWICMetadataQueryReader> metadata;
    if(SUCCEEDED(first->GetMetadataQueryReader(&metadata))) {
        PROPVARIANT value{};
        HRESULT hr=metadata->GetMetadataByName(L"/app1/ifd/{ushort=274}",&value);
        if(FAILED(hr))hr=metadata->GetMetadataByName(L"/ifd/{ushort=274}",&value);
        if(SUCCEEDED(hr)&&value.vt==VT_UI2)orientation=value.uiVal;PropVariantClear(&value);
    }
    WICBitmapTransformOptions opt=WICBitmapTransformRotate0;
    switch(orientation){case 2:opt=WICBitmapTransformFlipHorizontal;break;case 3:opt=WICBitmapTransformRotate180;break;case 4:opt=WICBitmapTransformFlipVertical;break;case 5:opt=WICBitmapTransformOptions(WICBitmapTransformRotate90|WICBitmapTransformFlipHorizontal);break;case 6:opt=WICBitmapTransformRotate90;break;case 7:opt=WICBitmapTransformOptions(WICBitmapTransformRotate270|WICBitmapTransformFlipHorizontal);break;case 8:opt=WICBitmapTransformRotate270;break;}
    if(opt!=WICBitmapTransformRotate0){ComPtr<IWICBitmapFlipRotator> rotate;check(wic->CreateBitmapFlipRotator(&rotate));check(rotate->Initialize(source.Get(),opt));source=rotate;}
    check(source->GetSize(&width,&height)); if(orientation>=5&&orientation<=8)std::swap(ow,oh);
    auto frame=std::make_unique<Frame>();frame->info={width,height,ow,oh,hdr?1:0,ratio<1?(maxEdge?2:1):0,uint64_t(width)*height*(hdr?16:4)};
    frame->pixels.resize(size_t(frame->info.bytes)); check(source->CopyPixels(nullptr,width*(hdr?16:4),UINT(frame->pixels.size()),frame->pixels.data()));
    return frame;
}

const char* shader=R"(
cbuffer View : register(b0) { float2 scale; float2 offset; float white; float nativeHDR; float rotation; float gammaInput; };
Texture2D photo : register(t0); SamplerState linearSampler : register(s0);
struct Vertex {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;};
Vertex VS(uint id:SV_VertexID) {
 float2 p[6]={float2(-1,1),float2(1,1),float2(-1,-1),float2(-1,-1),float2(1,1),float2(1,-1)};
 float2 u[6]={float2(0,0),float2(1,0),float2(0,1),float2(0,1),float2(1,0),float2(1,1)};
 Vertex v;v.pos=float4(p[id]*scale+offset,0,1);float2 t=u[id];
 if(rotation==1)t=float2(t.y,1-t.x);else if(rotation==2)t=1-t;else if(rotation==3)t=float2(1-t.y,t.x);v.uv=t;return v;
}
float4 PS(Vertex v):SV_TARGET {
 float4 c=photo.Sample(linearSampler,v.uv);float3 rgb=c.rgb;
 if(gammaInput>0.5)rgb=lerp(rgb/12.92,pow((rgb+0.055)/1.055,2.4),step(0.04045,rgb));
 if(nativeHDR<0.5)rgb*=white;
 // Premultiplied alpha exposes the native window backdrop outside the photo.
 return float4(rgb*c.a,c.a);
}
float4 PSInput(Vertex v):SV_TARGET {
 float3 color=photo.Sample(linearSampler,v.uv).rgb;
 float3 srgb=lerp(color*12.92,1.055*pow(max(color,0),1/2.4)-0.055,step(0.0031308,color));
 return float4(saturate(srgb),1);
}
)";
struct Renderer {
    HWND hwnd{};ComPtr<PanelNative> panel;ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> context;ComPtr<IDXGIFactory6> factory;ComPtr<IDXGIAdapter1> adapter;
    ComPtr<IDXGISwapChain3> swap;ComPtr<ID3D11RenderTargetView> target;ComPtr<ID3D11Texture2D> offscreen;
    ComPtr<ID3D11VertexShader> vs;ComPtr<ID3D11PixelShader> ps,inputPs;ComPtr<ID3D11Buffer> constants;ComPtr<ID3D11SamplerState> sampler;ComPtr<ID3D11ShaderResourceView> image,originalView,hdrView,vsrView;
    ComPtr<ID3D11Texture2D> originalTexture,sdkInput,hdrTexture,vsrTexture;bool requestedHdr=false,requestedVsr=false,holdOriginal=false,hdrCached=false,vsrCached=false,sourceAlpha=false;unsigned cachedPeak=0;int hdrCapability=0,vsrCapability=0;UINT vsrWidth=0,vsrHeight=0;
    std::unique_ptr<GlassRenderer> glass;
    float compositionDpi=1;
#ifdef LUMA_NVIDIA
    NVSDK_NGX_Parameter* hdrParams=nullptr;NVSDK_NGX_Parameter* vsrParams=nullptr;NVSDK_NGX_Handle* hdrFeature=nullptr;NVSDK_NGX_Handle* vsrFeature=nullptr;bool ngxInitialized=false;
#endif
    uint32_t width=1,height=1,iw=1,ih=1,processWidth=1,processHeight=1;float zoom=1,panX=0,panY=0;int rotation=0;unsigned currentPreset=1,middleGrayOverride=0,peakOverride=0;Status status{};uint64_t textureBudget=1024ull*1024*1024;
    bool effectsInitialized=false;
    Renderer(IUnknown* p,HWND h,bool deferEffects=false):hwnd(h) {
        if(p)check(p->QueryInterface(IID_PPV_ARGS(&panel)));
        check(CreateDXGIFactory1(IID_PPV_ARGS(&factory)));
        for(UINT i=0;;i++){ComPtr<IDXGIAdapter1> a;if(factory->EnumAdapterByGpuPreference(i,DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE,IID_PPV_ARGS(&a))==DXGI_ERROR_NOT_FOUND)break;DXGI_ADAPTER_DESC1 desc{};a->GetDesc1(&desc);if(!(desc.Flags&DXGI_ADAPTER_FLAG_SOFTWARE)&&desc.VendorId==0x10de){adapter=a;break;}}
        HRESULT hr=D3D11CreateDevice(adapter.Get(),adapter?D3D_DRIVER_TYPE_UNKNOWN:D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context);
        if(FAILED(hr)){adapter.Reset();check(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context));}
        if(!adapter){ComPtr<IDXGIDevice> d;device.As(&d);ComPtr<IDXGIAdapter> a;d->GetAdapter(&a);a.As(&adapter);}
        DXGI_ADAPTER_DESC1 desc{};adapter->GetDesc1(&desc);wcsncpy_s(status.gpu,desc.Description,_TRUNCATE);status.nvidia=desc.VendorId==0x10de;
        textureBudget=std::clamp(uint64_t(desc.DedicatedVideoMemory)/4,256ull*1024*1024,1024ull*1024*1024);
        ComPtr<ID3DBlob> v,pixel,err;check(D3DCompile(shader,strlen(shader),nullptr,nullptr,nullptr,"VS","vs_5_0",D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&v,&err));check(D3DCompile(shader,strlen(shader),nullptr,nullptr,nullptr,"PS","ps_5_0",D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&pixel,&err));
        check(device->CreateVertexShader(v->GetBufferPointer(),v->GetBufferSize(),nullptr,&vs));check(device->CreatePixelShader(pixel->GetBufferPointer(),pixel->GetBufferSize(),nullptr,&ps));
        ComPtr<ID3DBlob> input;check(D3DCompile(shader,strlen(shader),nullptr,nullptr,nullptr,"PSInput","ps_5_0",D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&input,&err));check(device->CreatePixelShader(input->GetBufferPointer(),input->GetBufferSize(),nullptr,&inputPs));
        D3D11_BUFFER_DESC cb{};cb.ByteWidth=32;cb.Usage=D3D11_USAGE_DEFAULT;cb.BindFlags=D3D11_BIND_CONSTANT_BUFFER;check(device->CreateBuffer(&cb,nullptr,&constants));
        D3D11_SAMPLER_DESC sd{};sd.Filter=D3D11_FILTER_MIN_MAG_MIP_LINEAR;sd.AddressU=sd.AddressV=sd.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;sd.MaxLOD=D3D11_FLOAT32_MAX;check(device->CreateSamplerState(&sd,&sampler));
        glass=std::make_unique<GlassRenderer>(device.Get());
        if(panel)attach(p);
        status.fp16=1;resize(1,1);probe();
        wcscpy_s(status.hdrMessage,L"打开显示增强面板以检测 RTX HDR");wcscpy_s(status.vsrMessage,L"打开显示增强面板以检测 VSR");
        if(!deferEffects)initEffects();
    }
    void attach(IUnknown* p){
        check(p->QueryInterface(IID_PPV_ARGS(&panel)));
        DXGI_SWAP_CHAIN_DESC1 sc{};sc.Width=1;sc.Height=1;sc.Format=DXGI_FORMAT_R16G16B16A16_FLOAT;sc.SampleDesc.Count=1;sc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;sc.BufferCount=2;sc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;sc.Scaling=DXGI_SCALING_STRETCH;sc.AlphaMode=DXGI_ALPHA_MODE_PREMULTIPLIED;
            ComPtr<IDXGISwapChain1> created;check(factory->CreateSwapChainForComposition(device.Get(),&sc,nullptr,&created));check(created.As(&swap));
            UINT support=0;check(swap->CheckColorSpaceSupport(DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709,&support));if(!(support&DXGI_SWAP_CHAIN_COLOR_SPACE_SUPPORT_FLAG_PRESENT))throw Failure{DXGI_ERROR_UNSUPPORTED};
            check(swap->SetColorSpace1(DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709));check(panel->SetSwapChain(swap.Get()));
        target.Reset();offscreen.Reset();resize(1,1);
    }
    void detach(){if(panel){panel->SetSwapChain(nullptr);panel.Reset();}}
    void shutdownEffects(){
#ifdef LUMA_NVIDIA
        std::lock_guard lock(ngxMutex);
        if(hdrFeature){NVSDK_NGX_D3D11_ReleaseFeature(hdrFeature);hdrFeature=nullptr;}if(vsrFeature){NVSDK_NGX_D3D11_ReleaseFeature(vsrFeature);vsrFeature=nullptr;}
        if(hdrParams){NVSDK_NGX_D3D11_DestroyParameters(hdrParams);hdrParams=nullptr;}if(vsrParams){NVSDK_NGX_D3D11_DestroyParameters(vsrParams);vsrParams=nullptr;}
        if(ngxInitialized){NVSDK_NGX_D3D11_Shutdown1(device.Get());ngxInitialized=false;}
#endif
    }
    ~Renderer(){if(panel)panel->SetSwapChain(nullptr);if(context){context->ClearState();context->Flush();}}
    void initEffects(){
        if(effectsInitialized)return;effectsInitialized=true;
        wcscpy_s(status.hdrMessage,L"缺少 NVIDIA RTX Video SDK 1.1");wcscpy_s(status.vsrMessage,L"缺少 NVIDIA RTX Video SDK 1.1");
#ifdef LUMA_NVIDIA
        std::lock_guard lock(ngxMutex);
        if(!status.nvidia){wcscpy_s(status.hdrMessage,L"需要兼容的 NVIDIA RTX GPU");wcscpy_s(status.vsrMessage,status.hdrMessage);return;}
        HMODULE module=nullptr;GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,reinterpret_cast<LPCWSTR>(&LumaDecodeAddress),&module);
        wchar_t path[32768]{};GetModuleFileNameW(module,path,32768);auto directory=std::filesystem::path(path).parent_path().wstring();
        const wchar_t* search[]={directory.c_str()};NVSDK_NGX_FeatureCommonInfo common{};common.PathListInfo.Path=search;common.PathListInfo.Length=1;
        wchar_t local[32768]{};GetEnvironmentVariableW(L"LOCALAPPDATA",local,32768);auto data=std::filesystem::path(local)/L"Luma"/L"ngx";std::filesystem::create_directories(data);
        auto result=NVSDK_NGX_D3D11_Init(0,data.c_str(),device.Get(),&common);status.ngxResult=uint32_t(result);
        if(NVSDK_NGX_FAILED(result)){swprintf_s(status.hdrMessage,L"NGX 初始化失败 (0x%08X)",status.ngxResult);wcscpy_s(status.vsrMessage,status.hdrMessage);return;}ngxInitialized=true;
        result=NVSDK_NGX_D3D11_GetCapabilityParameters(&hdrParams);status.ngxResult=uint32_t(result);
        if(NVSDK_NGX_FAILED(result)){swprintf_s(status.hdrMessage,L"无法取得 NGX 能力 (0x%08X)",status.ngxResult);wcscpy_s(status.vsrMessage,status.hdrMessage);return;}
        status.sdkAvailable=1;int available=0;hdrParams->Get(NVSDK_NGX_Parameter_TrueHDR_Available,&available);status.hdrAvailable=available;
        if(available)wcscpy_s(status.hdrMessage,L"TrueHDR 可用，尚未开启");else{int driver=0;hdrParams->Get(NVSDK_NGX_Parameter_TrueHDR_NeedsUpdatedDriver,&driver);wcscpy_s(status.hdrMessage,driver?L"TrueHDR 需要更新 NVIDIA 驱动":L"TrueHDR 运行库未报告支持");}
        hdrParams->Get(NVSDK_NGX_Parameter_VSR_Available,&available);status.vsrAvailable=available;wcscpy_s(status.vsrMessage,available?L"VSR 可用，默认关闭":L"VSR 运行库未报告支持");hdrCapability=status.hdrAvailable;vsrCapability=status.vsrAvailable;
        if(originalTexture&&sourceAlpha){status.hdrAvailable=status.vsrAvailable=0;wcscpy_s(status.hdrMessage,L"透明图片保留原始透明度，暂不支持增强");wcscpy_s(status.vsrMessage,L"透明图片暂不支持 VSR");}
        else if(originalTexture&&vsrWidth<=iw*1.05f&&status.vsrAvailable){status.vsrAvailable=0;wcscpy_s(status.vsrMessage,L"当前显存预算不足以放大这张照片，HDR 仍可独立使用");}
#endif
    }
    // Stable address within this module for its DLL search directory.
    static void LumaDecodeAddress(){}
    void probe(bool evaluate=true) {
        check(device->GetDeviceRemovedReason());
        int oldHdr=status.hdrActive,oldEffective=status.hdrEffective;float oldWhite=status.sdrWhite,oldPeak=status.maxNits;
        status.hdrActive=0;status.maxNits=0;status.sdrWhite=1;status.display[0]=0;
        HMONITOR monitor=MonitorFromWindow(hwnd,MONITOR_DEFAULTTONEAREST);
        // Enumerate every adapter: a hybrid laptop may render on NVIDIA and scan out on AMD.
        ComPtr<IDXGIFactory1> fresh;check(CreateDXGIFactory1(IID_PPV_ARGS(&fresh)));
        for(UINT ai=0;;ai++){ComPtr<IDXGIAdapter1> a;if(fresh->EnumAdapters1(ai,&a)==DXGI_ERROR_NOT_FOUND)break;
            for(UINT oi=0;;oi++){ComPtr<IDXGIOutput> output;if(a->EnumOutputs(oi,&output)==DXGI_ERROR_NOT_FOUND)break;ComPtr<IDXGIOutput6> o;if(FAILED(output.As(&o)))continue;DXGI_OUTPUT_DESC1 d{};if(FAILED(o->GetDesc1(&d))||d.Monitor!=monitor)continue;
                wcsncpy_s(status.display,d.DeviceName,_TRUNCATE);status.hdrActive=d.ColorSpace==DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020;status.maxNits=d.MaxLuminance;
            }
        }
        UINT pc=0,mc=0;if(GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS,&pc,&mc)==ERROR_SUCCESS){std::vector<DISPLAYCONFIG_PATH_INFO> paths(pc);std::vector<DISPLAYCONFIG_MODE_INFO> modes(mc);
            if(QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS,&pc,paths.data(),&mc,modes.data(),nullptr)==ERROR_SUCCESS)for(UINT i=0;i<pc;i++){
                DISPLAYCONFIG_SOURCE_DEVICE_NAME name{};name.header={DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,sizeof(name),paths[i].sourceInfo.adapterId,paths[i].sourceInfo.id};
                if(DisplayConfigGetDeviceInfo(&name.header)!=ERROR_SUCCESS||wcscmp(name.viewGdiDeviceName,status.display)!=0)continue;
                DISPLAYCONFIG_SDR_WHITE_LEVEL white{};white.header={DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL,sizeof(white),paths[i].targetInfo.adapterId,paths[i].targetInfo.id};
                if(status.hdrActive&&DisplayConfigGetDeviceInfo(&white.header)==ERROR_SUCCESS)status.sdrWhite=white.SDRWhiteLevel/1000.0f;
            }
        }
        if(image){if(evaluate&&requestedHdr&&status.hdrActive&&(oldHdr!=status.hdrActive||oldPeak!=status.maxNits||oldWhite!=status.sdrWhite))effects(requestedHdr,requestedVsr,holdOriginal,currentPreset);else selectImage();if(oldHdr!=status.hdrActive||oldWhite!=status.sdrWhite||oldEffective!=status.hdrEffective)draw();}
    }
    void resize(UINT w,UINT h){w=std::clamp(w,1u,16384u);h=std::clamp(h,1u,16384u);if(target&&w==width&&h==height)return;
        context->OMSetRenderTargets(0,nullptr,nullptr);target.Reset();offscreen.Reset();
        ComPtr<ID3D11Texture2D> buffer;
        if(swap){check(swap->ResizeBuffers(2,w,h,DXGI_FORMAT_R16G16B16A16_FLOAT,0));check(swap->GetBuffer(0,IID_PPV_ARGS(&buffer)));}
        else {D3D11_TEXTURE2D_DESC d{};d.Width=w;d.Height=h;d.MipLevels=d.ArraySize=1;d.Format=DXGI_FORMAT_R16G16B16A16_FLOAT;d.SampleDesc.Count=1;d.BindFlags=D3D11_BIND_RENDER_TARGET;check(device->CreateTexture2D(&d,nullptr,&buffer));offscreen=buffer;}
        check(device->CreateRenderTargetView(buffer.Get(),nullptr,&target));width=w;height=h;
    }
    void updateMemory(){
        uint64_t bytes=uint64_t(width)*height*(swap?16:8);
        for(auto texture:{originalTexture.Get(),sdkInput.Get(),hdrTexture.Get(),vsrTexture.Get()}){if(!texture)continue;D3D11_TEXTURE2D_DESC d{};texture->GetDesc(&d);UINT w=d.Width,h=d.Height;unsigned bpp=d.Format==DXGI_FORMAT_R32G32B32A32_FLOAT?16:d.Format==DXGI_FORMAT_R16G16B16A16_FLOAT?8:4;for(UINT i=0;i<d.MipLevels;i++){bytes+=uint64_t(w)*h*bpp;w=std::max(1u,w/2);h=std::max(1u,h/2);}}
        if(glass)bytes+=glass->bytes();status.gpuBytes=bytes;
    }
    void use(Frame& f){context->ClearState();zoom=1;panX=panY=0;rotation=0;D3D11_TEXTURE2D_DESC d{};d.Width=f.info.width;d.Height=f.info.height;d.MipLevels=0;d.ArraySize=1;d.Format=f.info.nativeHdr?DXGI_FORMAT_R32G32B32A32_FLOAT:DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;d.SampleDesc.Count=1;d.Usage=D3D11_USAGE_DEFAULT;d.BindFlags=D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_RENDER_TARGET;d.MiscFlags=D3D11_RESOURCE_MISC_GENERATE_MIPS;
        ComPtr<ID3D11Texture2D> tex;check(device->CreateTexture2D(&d,nullptr,&tex));context->UpdateSubresource(tex.Get(),0,nullptr,f.pixels.data(),f.info.width*(f.info.nativeHdr?16:4),0);ComPtr<ID3D11ShaderResourceView> view;check(device->CreateShaderResourceView(tex.Get(),nullptr,&view));context->GenerateMips(view.Get());originalTexture=tex;originalView=view;image=view;iw=f.info.width;ih=f.info.height;status.nativeHdr=f.info.nativeHdr;
        hdrCached=vsrCached=false;hdrView.Reset();vsrView.Reset();hdrTexture.Reset();vsrTexture.Reset();sdkInput.Reset();selectImage();
        if(status.sdkAvailable){status.hdrAvailable=hdrCapability;status.vsrAvailable=vsrCapability;wcscpy_s(status.hdrMessage,status.hdrAvailable?L"TrueHDR 可用，尚未开启":L"TrueHDR 不可用");wcscpy_s(status.vsrMessage,status.vsrAvailable?L"VSR 可用，默认关闭":L"VSR 不可用");}
        sourceAlpha=false;if(!f.info.nativeHdr)for(size_t i=3;i<f.pixels.size();i+=4)if(f.pixels[i]!=255){sourceAlpha=true;break;}
        if(sourceAlpha){status.hdrAvailable=status.vsrAvailable=0;wcscpy_s(status.hdrMessage,L"透明图片保留原始透明度，暂不支持增强");wcscpy_s(status.vsrMessage,L"透明图片暂不支持 VSR");}
        updateMemory();uint64_t canvasBytes=uint64_t(width)*height*16,sourceBytes=status.gpuBytes-(swap?canvasBytes:canvasBytes/2);
        double available=double(textureBudget>canvasBytes+sourceBytes?textureBudget-canvasBytes-sourceBytes:64ull*1024*1024);
        // Reserve 4 bytes/pixel for SDK input + 8 for FP16 output. Full resolution whenever budget permits.
        float processingScale=std::min(1.0f,float(std::sqrt(available/(12.0*iw*ih))));
        processWidth=std::max(1u,UINT(iw*processingScale));processHeight=std::max(1u,UINT(ih*processingScale));
        double remaining=std::max(0.0,available-4.0*processWidth*processHeight);
        float upscale=std::min({2.0f,16384.0f/std::max(iw,ih),float(std::sqrt(remaining/(12.0*iw*ih)))});vsrWidth=std::max(1u,UINT(iw*upscale));vsrHeight=std::max(1u,UINT(ih*upscale));
        if(upscale<=1.05f&&status.vsrAvailable){status.vsrAvailable=0;wcscpy_s(status.vsrMessage,L"当前显存预算不足以放大这张照片，HDR 仍可独立使用");}
    }
    void selectImage(){
        image=originalView;status.hdrEffective=status.vsrEffective=0;
        if(status.nativeHdr){wcscpy_s(status.hdrMessage,L"原生 HDR 图片，跳过 SDR 增强");return;}
        if(holdOriginal||!originalTexture)return;
        if(requestedVsr&&vsrCached){image=vsrView;status.vsrEffective=1;}
        if(requestedHdr&&hdrCached&&status.hdrActive){image=hdrView;status.hdrEffective=1;if(processWidth!=iw||processHeight!=ih)swprintf_s(status.hdrMessage,L"TrueHDR 已生效 · 按显存预算处理 %u × %u",processWidth,processHeight);else wcscpy_s(status.hdrMessage,L"TrueHDR 已生效");}
        if(requestedHdr&&!status.hdrActive)wcscpy_s(status.hdrMessage,L"当前显示器未启用 Windows HDR");
    }
    void effects(bool hdr,bool vsr,bool original,unsigned preset,bool present=true){
        currentPreset=preset;requestedHdr=hdr;requestedVsr=vsr;holdOriginal=original;status.hdrEffective=status.vsrEffective=0;
        if(status.nativeHdr||original||(!hdr&&!vsr)){selectImage();if(present)draw();return;}
        if((!hdr||!status.hdrAvailable||!status.hdrActive)&&(!vsr||!status.vsrAvailable)){selectImage();if(present)draw();return;}
#ifdef LUMA_NVIDIA
        std::lock_guard lock(ngxMutex);
        if(!status.sdkAvailable||!originalTexture){selectImage();if(present)draw();return;}
        // Ordinary GPU resampling prepares large photos for the SDK; it never invokes VSR.
        if(!sdkInput){D3D11_TEXTURE2D_DESC d{};d.Width=processWidth;d.Height=processHeight;d.ArraySize=d.MipLevels=1;d.Format=DXGI_FORMAT_R8G8B8A8_UNORM;d.SampleDesc.Count=1;d.Usage=D3D11_USAGE_DEFAULT;d.BindFlags=D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_RENDER_TARGET;check(device->CreateTexture2D(&d,nullptr,&sdkInput));
            if(processWidth==iw&&processHeight==ih)context->CopySubresourceRegion(sdkInput.Get(),0,0,0,0,originalTexture.Get(),0,nullptr);
            else{context->ClearState();ComPtr<ID3D11RenderTargetView> outputView;check(device->CreateRenderTargetView(sdkInput.Get(),nullptr,&outputView));auto t=outputView.Get();context->OMSetRenderTargets(1,&t,nullptr);D3D11_VIEWPORT viewport{0,0,float(processWidth),float(processHeight),0,1};context->RSSetViewports(1,&viewport);float values[]={1,1,0,0,1,0,0,0};context->UpdateSubresource(constants.Get(),0,nullptr,values,0,0);auto c=constants.Get();context->VSSetConstantBuffers(0,1,&c);context->IASetInputLayout(nullptr);context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);context->VSSetShader(vs.Get(),nullptr,0);context->PSSetShader(inputPs.Get(),nullptr,0);auto i=originalView.Get();context->PSSetShaderResources(0,1,&i);auto s=sampler.Get();context->PSSetSamplers(0,1,&s);context->Draw(6,0);context->ClearState();}
        }
        auto output=[&](DXGI_FORMAT format,UINT w,UINT h,ComPtr<ID3D11Texture2D>& texture,ComPtr<ID3D11ShaderResourceView>& view){D3D11_TEXTURE2D_DESC d{};d.Width=w;d.Height=h;d.ArraySize=d.MipLevels=1;d.Format=format;d.SampleDesc.Count=1;d.Usage=D3D11_USAGE_DEFAULT;d.BindFlags=D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_UNORDERED_ACCESS;check(device->CreateTexture2D(&d,nullptr,&texture));check(device->CreateShaderResourceView(texture.Get(),nullptr,&view));};
        NVSDK_NGX_Result result=NVSDK_NGX_Result_Success;
        // HDR-only reaches no VSR creation or evaluation calls.
        if(vsr&&status.vsrAvailable&&!vsrCached){if(!vsrParams){result=NVSDK_NGX_D3D11_GetCapabilityParameters(&vsrParams);}if(NVSDK_NGX_SUCCEED(result)&&!vsrFeature){NVSDK_NGX_Feature_Create_Params p{};result=NGX_D3D11_CREATE_VSR_EXT(context.Get(),&vsrFeature,vsrParams,&p);}
            if(NVSDK_NGX_SUCCEED(result)){output(DXGI_FORMAT_R8G8B8A8_UNORM,vsrWidth,vsrHeight,vsrTexture,vsrView);NVSDK_NGX_D3D11_VSR_Eval_Params p{};p.pInput=sdkInput.Get();p.pOutput=vsrTexture.Get();p.InputSubrectSize={processWidth,processHeight};p.OutputSubrectSize={vsrWidth,vsrHeight};p.QualityLevel=NVSDK_NGX_VSR_Quality_High;result=NGX_D3D11_EVALUATE_VSR_EXT(context.Get(),vsrFeature,vsrParams,&p);status.vsrEvaluations++;}
            status.ngxResult=uint32_t(result);vsrCached=NVSDK_NGX_SUCCEED(result);if(!vsrCached)swprintf_s(status.vsrMessage,L"VSR 执行失败 (0x%08X)",status.ngxResult);else wcscpy_s(status.vsrMessage,L"VSR 已生效");hdrCached=false;
        }
        unsigned peak=peakOverride?peakOverride:unsigned(std::clamp(status.maxNits>=400?status.maxNits:1000.0f,400.0f,2000.0f));
        // SDK output uses absolute scRGB luminance. Match middle gray to Windows' SDR reference white,
        // rather than comparing a fixed 50-nit HDR midtone against a 480-nit SDR white.
        unsigned middle=middleGrayOverride?middleGrayOverride:unsigned(std::clamp(status.sdrWhite*80.0f*0.21586f,50.0f,100.0f));
        unsigned key=peak+preset*10000+(vsr&&vsrCached?100000:0)+middle*1000000;
        if(hdr&&status.hdrAvailable&&status.hdrActive&&(!hdrCached||cachedPeak!=key)){
            if(!hdrFeature){NVSDK_NGX_Feature_Create_Params p{};result=NGX_D3D11_CREATE_TRUEHDR_EXT(context.Get(),&hdrFeature,hdrParams,&p);}else result=NVSDK_NGX_Result_Success;
            if(NVSDK_NGX_SUCCEED(result)){UINT w=vsr&&vsrCached?vsrWidth:processWidth,h=vsr&&vsrCached?vsrHeight:processHeight;output(DXGI_FORMAT_R16G16B16A16_FLOAT,w,h,hdrTexture,hdrView);NVSDK_NGX_D3D11_TRUEHDR_Eval_Params p{};p.pInput=vsr&&vsrCached?vsrTexture.Get():sdkInput.Get();p.pOutput=hdrTexture.Get();p.InputSubrectBR={w,h};p.OutputSubrectBR={w,h};p.Contrast=preset==0?90:preset==2?115:100;p.Saturation=preset==0?90:preset==2?115:100;p.MiddleGray=middle;p.MaxLuminance=peak;result=NGX_D3D11_EVALUATE_TRUEHDR_EXT(context.Get(),hdrFeature,hdrParams,&p);status.hdrEvaluations++;}
            status.ngxResult=uint32_t(result);hdrCached=NVSDK_NGX_SUCCEED(result);cachedPeak=key;if(!hdrCached)swprintf_s(status.hdrMessage,L"TrueHDR 执行失败 (0x%08X)",status.ngxResult);else if(processWidth!=iw||processHeight!=ih)swprintf_s(status.hdrMessage,L"TrueHDR 已生效 · 按比例处理 %u × %u",processWidth,processHeight);else wcscpy_s(status.hdrMessage,L"TrueHDR 已生效");
        }
#endif
        selectImage();updateMemory();if(present)draw();
    }
    void draw(){context->ClearState();bool frosted=glass&&glass->enabled();if(frosted)glass->ensure(width,height);const float clear[]={0,0,0,0};auto t=frosted?glass->sceneTarget.Get():target.Get();context->ClearRenderTargetView(t,clear);context->OMSetRenderTargets(1,&t,nullptr);D3D11_VIEWPORT vp{0,0,float(width),float(height),0,1};context->RSSetViewports(1,&vp);
        if(image){float w=float(rotation%2?ih:iw),h=float(rotation%2?iw:ih);float fit=std::min(width/w,height/h);
            float values[]={w*fit*zoom/width,h*fit*zoom/height,2*panX/width,-2*panY/height,status.sdrWhite,float(status.nativeHdr||status.hdrEffective),float(rotation),float(status.vsrEffective&&!status.hdrEffective)};context->UpdateSubresource(constants.Get(),0,nullptr,values,0,0);
            context->IASetInputLayout(nullptr);context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);context->VSSetShader(vs.Get(),nullptr,0);context->PSSetShader(ps.Get(),nullptr,0);auto c=constants.Get();context->VSSetConstantBuffers(0,1,&c);context->PSSetConstantBuffers(0,1,&c);auto s=sampler.Get();context->PSSetSamplers(0,1,&s);auto i=image.Get();context->PSSetShaderResources(0,1,&i);context->Draw(6,0);}
        if(frosted)glass->composite(context.Get(),target.Get(),sampler.Get(),status.sdrWhite,compositionDpi);
        if(swap)check(swap->Present(1,0));else context->Flush();
    }
};
#define API extern "C" __declspec(dllexport)
API const wchar_t* __cdecl LumaError(){return lastError.c_str();}
API HRESULT __cdecl LumaDecode(const wchar_t* path,FrameInfo* info,void** result){if(!path||!info||!result)return E_POINTER;*result=nullptr;return safe([&]{auto f=decode(path);*info=f->info;*result=f.release();});}
API HRESULT __cdecl LumaDecodeSized(const wchar_t* path,UINT maxEdge,FrameInfo* info,void** result){if(!path||!info||!result)return E_POINTER;*result=nullptr;return safe([&]{auto f=decode(path,maxEdge);*info=f->info;*result=f.release();});}
API HRESULT __cdecl LumaCreateFast(IUnknown* panel,HWND hwnd,void** result){if(!result)return E_POINTER;*result=nullptr;return safe([&]{*result=new Renderer(panel,hwnd,true);});}
API HRESULT __cdecl LumaInitEffects(void* r){if(!r)return E_POINTER;return safe([&]{static_cast<Renderer*>(r)->initEffects();});}
API void __cdecl LumaFreeFrame(void* f){delete static_cast<Frame*>(f);}
API HRESULT __cdecl LumaCreate(IUnknown* panel,HWND hwnd,void** result){if(!result)return E_POINTER;*result=nullptr;return safe([&]{*result=new Renderer(panel,hwnd);});}
API void __cdecl LumaDestroy(void* r){if(r){auto p=static_cast<Renderer*>(r);p->shutdownEffects();delete p;}}
API HRESULT __cdecl LumaAttach(void* r,IUnknown* panel){if(!r||!panel)return E_POINTER;return safe([&]{static_cast<Renderer*>(r)->attach(panel);});}
API HRESULT __cdecl LumaDetach(void* r){if(!r)return E_POINTER;return safe([&]{static_cast<Renderer*>(r)->detach();});}
API HRESULT __cdecl LumaSetGlass(void* r,const GlassRect* rects,UINT count,int dark){if(!r||count>8||(count&&!rects))return E_INVALIDARG;return safe([&]{auto& g=*static_cast<Renderer*>(r)->glass;g.rects.clear();if(count)g.rects.assign(rects,rects+count);g.dark=dark!=0;});}
API HRESULT __cdecl LumaRepaint(void* r){if(!r)return E_POINTER;return safe([&]{static_cast<Renderer*>(r)->draw();});}
API HRESULT __cdecl LumaReadGlass(void* r,GlassRect rect,int dark,UINT w,UINT h,uint8_t* bgra,UINT bytes){if(!r||!bgra||uint64_t(bytes)<uint64_t(w)*h*4)return E_INVALIDARG;return safe([&]{auto& a=*static_cast<Renderer*>(r);a.glass->readPanel(a.context.Get(),a.sampler.Get(),rect,dark!=0,a.status.sdrWhite,w,h,bgra);});}
API HRESULT __cdecl LumaResize(void* r,UINT w,UINT h,float scaleX,float scaleY){if(!r)return E_POINTER;return safe([&]{auto& x=*static_cast<Renderer*>(r);x.compositionDpi=std::max(.1f,scaleX);x.resize(w,h);if(x.swap){DXGI_MATRIX_3X2_F inverse{1/std::max(0.1f,scaleX),0,0,1/std::max(0.1f,scaleY),0,0};check(x.swap->SetMatrixTransform(&inverse));}x.draw();});}
API HRESULT __cdecl LumaUseFrame(void* r,void* f){if(!r||!f)return E_POINTER;return safe([&]{auto& x=*static_cast<Renderer*>(r);x.use(*static_cast<Frame*>(f));x.draw();});}
API HRESULT __cdecl LumaPrepareFrame(void* r,void* f){if(!r||!f)return E_POINTER;return safe([&]{static_cast<Renderer*>(r)->use(*static_cast<Frame*>(f));});}
API HRESULT __cdecl LumaView(void* r,float zoom,float x,float y,int rotation){if(!r)return E_POINTER;return safe([&]{auto& a=*static_cast<Renderer*>(r);a.zoom=std::clamp(zoom,0.05f,64.0f);a.panX=x;a.panY=y;a.rotation=(rotation%4+4)%4;a.draw();});}
API HRESULT __cdecl LumaStatus(void* r,Status* result){if(!r||!result)return E_POINTER;return safe([&]{auto& x=*static_cast<Renderer*>(r);x.probe();*result=x.status;});}
API HRESULT __cdecl LumaPeekStatus(void* r,Status* result){if(!r||!result)return E_POINTER;return safe([&]{auto& x=*static_cast<Renderer*>(r);x.probe(false);x.updateMemory();*result=x.status;});}
API HRESULT __cdecl LumaCopyStatus(void* r,Status* result){if(!r||!result)return E_POINTER;return safe([&]{auto& x=*static_cast<Renderer*>(r);x.updateMemory();*result=x.status;});}
API HRESULT __cdecl LumaWaitForGpu(void* r){if(!r)return E_POINTER;return safe([&]{auto& x=*static_cast<Renderer*>(r);D3D11_QUERY_DESC desc{D3D11_QUERY_EVENT,0};ComPtr<ID3D11Query> done;check(x.device->CreateQuery(&desc,&done));x.context->End(done.Get());x.context->Flush();HRESULT hr;while((hr=x.context->GetData(done.Get(),nullptr,0,D3D11_ASYNC_GETDATA_DONOTFLUSH))==S_FALSE){check(x.device->GetDeviceRemovedReason());Sleep(2);}check(hr);});}
API HRESULT __cdecl LumaEffects(void* r,int hdr,int vsr,int original,UINT preset){if(!r)return E_POINTER;return safe([&]{static_cast<Renderer*>(r)->effects(hdr!=0,vsr!=0,original!=0,std::min(preset,2u));});}
API HRESULT __cdecl LumaPrepareEffects(void* r,int hdr,int vsr,int original,UINT preset){if(!r)return E_POINTER;return safe([&]{static_cast<Renderer*>(r)->effects(hdr!=0,vsr!=0,original!=0,std::min(preset,2u),false);});}
// Presentation-only comparison. Keep the requested mode and all cached outputs;
// never initialize NGX, upload a frame, or evaluate an enhancement here.
API HRESULT __cdecl LumaSetOriginal(void* r,int original){if(!r)return E_POINTER;return safe([&]{auto& x=*static_cast<Renderer*>(r);x.holdOriginal=original!=0;x.selectImage();});}
API HRESULT __cdecl LumaHdrTuning(void* r,UINT middleGray,UINT peak){if(!r)return E_POINTER;return safe([&]{auto& x=*static_cast<Renderer*>(r);x.middleGrayOverride=middleGray?std::clamp(middleGray,10u,100u):0;x.peakOverride=peak?std::clamp(peak,400u,2000u):0;});}
API HRESULT __cdecl LumaReadSurface(void* r,uint16_t* rgba,UINT pixels){if(!r||!rgba)return E_POINTER;return safe([&]{auto& x=*static_cast<Renderer*>(r);if(uint64_t(pixels)<uint64_t(x.width)*x.height)throw Failure{E_INVALIDARG};ComPtr<ID3D11Texture2D> source;if(x.swap)check(x.swap->GetBuffer(0,IID_PPV_ARGS(&source)));else source=x.offscreen;D3D11_TEXTURE2D_DESC desc{};source->GetDesc(&desc);desc.Usage=D3D11_USAGE_STAGING;desc.BindFlags=0;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;ComPtr<ID3D11Texture2D> staging;check(x.device->CreateTexture2D(&desc,nullptr,&staging));x.context->CopyResource(staging.Get(),source.Get());D3D11_MAPPED_SUBRESOURCE mapped{};check(x.context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped));for(UINT y=0;y<x.height;y++)memcpy(rgba+size_t(y)*x.width*4,static_cast<uint8_t*>(mapped.pData)+size_t(y)*mapped.RowPitch,size_t(x.width)*8);x.context->Unmap(staging.Get(),0);});}
// For validation: read a real FP16 render target back, never an 8-bit screenshot.
API HRESULT __cdecl LumaReadPixel(void* r,UINT x,UINT y,uint16_t* rgba){if(!r||!rgba)return E_POINTER;return safe([&]{auto& a=*static_cast<Renderer*>(r);if(!a.offscreen||x>=a.width||y>=a.height)throw Failure{E_INVALIDARG};D3D11_TEXTURE2D_DESC d{};a.offscreen->GetDesc(&d);d.BindFlags=0;d.Usage=D3D11_USAGE_STAGING;d.CPUAccessFlags=D3D11_CPU_ACCESS_READ;ComPtr<ID3D11Texture2D> staging;check(a.device->CreateTexture2D(&d,nullptr,&staging));a.context->CopyResource(staging.Get(),a.offscreen.Get());D3D11_MAPPED_SUBRESOURCE m{};check(a.context->Map(staging.Get(),0,D3D11_MAP_READ,0,&m));memcpy(rgba,static_cast<uint8_t*>(m.pData)+size_t(y)*m.RowPitch+x*8,8);a.context->Unmap(staging.Get(),0);});}
