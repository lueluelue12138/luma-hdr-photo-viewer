// MediaPlayer frame-server writes directly to this owned GPU surface. It never
// uses the still-frame upload/reset path, CPU pixel copies or a per-frame fence.
API HRESULT __cdecl LumaCreateVideoSurface(void* value,UINT width,UINT height,IInspectable** surface){
    if(!value||!surface)return E_POINTER;*surface=nullptr;
    return safe([&]{
        if(!width||!height||width>8192||height>8192||uint64_t(width)*height>16777216)throw Failure{E_INVALIDARG};
        auto& x=*static_cast<Renderer*>(value);
        ComPtr<ID3D10Multithread> protection;check(x.device.As(&protection));protection->SetMultithreadProtected(TRUE);
        D3D11_TEXTURE2D_DESC desc{};desc.Width=width;desc.Height=height;desc.ArraySize=desc.MipLevels=1;
        desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.Usage=D3D11_USAGE_DEFAULT;
        desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
        ComPtr<ID3D11Texture2D> texture;ComPtr<ID3D11ShaderResourceView> view;
        check(x.device->CreateTexture2D(&desc,nullptr,&texture));check(x.device->CreateShaderResourceView(texture.Get(),nullptr,&view));
        ComPtr<IDXGISurface> dxgi;check(texture.As(&dxgi));check(CreateDirect3D11SurfaceFromDXGISurface(dxgi.Get(),surface));
        x.originalTexture=texture;x.originalView=view;x.image=view;x.videoSource=true;x.sourceAlpha=false;
        x.iw=x.processWidth=x.vsrWidth=width;x.ih=x.processHeight=x.vsrHeight=height;
        x.status.nativeHdr=0;x.gainDisplay={};x.sdrOriginalView.Reset();x.sdrOriginalTexture.Reset();x.gainBaseView.Reset();x.gainBaseTexture.Reset();
        x.sdkInputReady=x.hdrCached=x.vsrCached=false;x.sceneStamp={};x.textureMetrics.originalAllocations++;x.updateMemory();
    });
}
API HRESULT __cdecl LumaPresentVideoFrame(void* value,int hdr,int original,unsigned preset,unsigned middleGray,unsigned peak){
    if(!value)return E_POINTER;
    return safe([&]{
        auto& x=*static_cast<Renderer*>(value);if(!x.videoSource||!x.originalTexture)throw Failure{E_UNEXPECTED};
        if(hdr)x.initEffects();
        x.middleGrayOverride=middleGray?std::clamp(middleGray,10u,100u):0;
        x.peakOverride=peak?std::clamp(peak,400u,2000u):0;
        // New pixels require a new evaluation, even though the input resource
        // pointer and allocations stay constant throughout this clip.
        x.sdkInputReady=x.hdrCached=x.vsrCached=false;x.sceneStamp={};
        x.effects(hdr!=0,false,original!=0,preset,false);x.draw();x.updateMemory();
    });
}
