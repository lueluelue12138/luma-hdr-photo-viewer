// Read-only settings query. No D3D device, swap chain, image or NGX context.
struct DisplayInfo {
    wchar_t device[128]{},name[128]{};
    int32_t hdrActive{},sdrWhiteKnown{};
    float peak{},fullFrame{},minimum{},sdrWhite{};
};
DisplayInfo queryDisplayInfo(HWND hwnd) {
    if(!hwnd||!IsWindow(hwnd))throw Failure{E_INVALIDARG};
    DisplayInfo info{};const auto monitor=MonitorFromWindow(hwnd,MONITOR_DEFAULTTONEAREST);
    ComPtr<IDXGIFactory1> factory;check(CreateDXGIFactory1(IID_PPV_ARGS(&factory)));bool found=false;
    for(UINT ai=0;!found;ai++){
        ComPtr<IDXGIAdapter1> adapter;auto hr=factory->EnumAdapters1(ai,&adapter);if(hr==DXGI_ERROR_NOT_FOUND)break;check(hr);
        for(UINT oi=0;;oi++){
            ComPtr<IDXGIOutput> output;hr=adapter->EnumOutputs(oi,&output);if(hr==DXGI_ERROR_NOT_FOUND)break;check(hr);
            ComPtr<IDXGIOutput6> modern;if(FAILED(output.As(&modern)))continue;
            DXGI_OUTPUT_DESC1 d{};if(FAILED(modern->GetDesc1(&d))||d.Monitor!=monitor)continue;
            wcsncpy_s(info.device,d.DeviceName,_TRUNCATE);info.hdrActive=d.ColorSpace==DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020;
            info.peak=d.MaxLuminance;info.fullFrame=d.MaxFullFrameLuminance;info.minimum=d.MinLuminance;found=true;break;
        }
    }
    if(!found)throw Failure{DXGI_ERROR_NOT_FOUND};
    // Topology can change between sizing and reading; retry without retaining a
    // stale factory. Optional names/white level must not hide valid DXGI data.
    for(int attempt=0;attempt<3;attempt++){
        UINT pc=0,mc=0;if(GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS,&pc,&mc)!=ERROR_SUCCESS)break;
        std::vector<DISPLAYCONFIG_PATH_INFO> paths(pc);std::vector<DISPLAYCONFIG_MODE_INFO> modes(mc);
        auto hr=QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS,&pc,paths.data(),&mc,modes.data(),nullptr);
        if(hr==ERROR_INSUFFICIENT_BUFFER)continue;if(hr!=ERROR_SUCCESS)break;
        for(UINT i=0;i<pc;i++){
            DISPLAYCONFIG_SOURCE_DEVICE_NAME source{};source.header={DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,sizeof(source),paths[i].sourceInfo.adapterId,paths[i].sourceInfo.id};
            if(DisplayConfigGetDeviceInfo(&source.header)!=ERROR_SUCCESS||wcscmp(source.viewGdiDeviceName,info.device)!=0)continue;
            DISPLAYCONFIG_TARGET_DEVICE_NAME target{};target.header={DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,sizeof(target),paths[i].targetInfo.adapterId,paths[i].targetInfo.id};
            if(DisplayConfigGetDeviceInfo(&target.header)==ERROR_SUCCESS)wcsncpy_s(info.name,target.monitorFriendlyDeviceName,_TRUNCATE);
            DISPLAYCONFIG_SDR_WHITE_LEVEL white{};white.header={DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL,sizeof(white),paths[i].targetInfo.adapterId,paths[i].targetInfo.id};
            if(info.hdrActive&&DisplayConfigGetDeviceInfo(&white.header)==ERROR_SUCCESS){info.sdrWhite=80.f*white.SDRWhiteLevel/1000.f;info.sdrWhiteKnown=1;}
            break;
        }
        break;
    }
    return info;
}
