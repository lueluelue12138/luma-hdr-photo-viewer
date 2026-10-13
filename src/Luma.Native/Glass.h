#pragma once

struct GlassRect {float left,top,width,height,radius,opacity;};

// Sample the actual linear FP16 photo scene, since WinUI's in-process Acrylic
// cannot read a SwapChainPanel. Only chrome is blurred; the photograph is untouched.
const char* glassShader=R"(
cbuffer Glass : register(b0) {float4 canvas; float4 rect; float4 style;};
Texture2D scene : register(t0);SamplerState sampling : register(s0);
struct Vertex {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;};
Vertex VS(uint id:SV_VertexID) {
 float2 p[3]={float2(-1,1),float2(3,1),float2(-1,-3)};
 Vertex v;v.pos=float4(p[id],0,1);v.uv=float2((p[id].x+1)*.5,(1-p[id].y)*.5);return v;
}
float4 Copy(Vertex v):SV_TARGET{return scene.Sample(sampling,v.uv);}
float4 Panel(Vertex v):SV_TARGET{
 float2 pos=v.pos.xy+canvas.zw;
 float2 center=rect.xy+rect.zw*.5;float2 q=abs(pos-center)-rect.zw*.5+style.x;
 float distance=length(max(q,0))+min(max(q.x,q.y),0)-style.x;
 float coverage=saturate(.5-distance)*style.y;if(coverage<=0)discard;
 float4 blurred=scene.Sample(sampling,pos/canvas.xy);
 float3 tint=lerp(float3(.93,.93,.93),float3(.026,.026,.026),style.z)*style.w;
 // Keep the blurred premultiplied alpha at photo/letterbox boundaries. Dividing
 // by alpha erased the Gaussian edge and produced the user's hard vertical seam.
 float3 color=blurred.rgb+tint*(1-saturate(blurred.a));
 // Glass controls remain readable above HDR highlights, independently of the photo.
 color=min(color,style.w*1.3);
 color=lerp(color,tint,lerp(.38,.62,style.z));
 // Dark controls need contrast even above a white sky. Limit only the chrome's
 // luminance relative to Windows SDR white, preserving its sampled hue.
 float luminance=dot(color,float3(.2126,.7152,.0722));
 if(style.z>.5)color*=min(1,style.w*.035/max(luminance,.0001));
 float noise=(frac(sin(dot(v.pos.xy,float2(12.9898,78.233)))*43758.5453)-.5)*.002*style.w;
 // The sampled blur already contains the photograph. Do not mix unblurred HDR
 // highlights back through it, which washes out dark chrome and its white text.
 float alpha=coverage;return float4((color+noise)*alpha,alpha);
}
)";

struct GlassRenderer {
    ComPtr<ID3D11Device> device;ComPtr<ID3D11Texture2D> sceneTexture,blurTextures[2];
    ComPtr<ID3D11RenderTargetView> sceneTarget,blurTargets[2];ComPtr<ID3D11ShaderResourceView> sceneView,blurViews[2];
    ComPtr<ID3D11VertexShader> vs;ComPtr<ID3D11PixelShader> copy,panel;ComPtr<ID3D11Buffer> constants;ComPtr<ID3D11BlendState> blend;ComPtr<ID3D11RasterizerState> scissor;
    ComPtr<ID2D1Factory1> d2dFactory;ComPtr<ID2D1Device> d2dDevice;ComPtr<ID2D1DeviceContext> d2d;
    ComPtr<ID2D1Bitmap1> sceneBitmap,blurBitmap;ComPtr<ID2D1Effect> gaussian;
    std::vector<GlassRect> rects;bool dark=false,blurReady=false;UINT width=0,height=0,bw=0,bh=0;uint64_t blurBuilds=0;
    GlassRenderer(ID3D11Device* d):device(d){
        auto code=compiledShader(glassShader,"VS","vs_5_0");check(device->CreateVertexShader(code->GetBufferPointer(),code->GetBufferSize(),nullptr,&vs));
        for(auto item:{std::pair("Copy",std::addressof(copy)),std::pair("Panel",std::addressof(panel))}){code=compiledShader(glassShader,item.first,"ps_5_0");check(device->CreatePixelShader(code->GetBufferPointer(),code->GetBufferSize(),nullptr,item.second->ReleaseAndGetAddressOf()));}
        check(D2D1CreateFactory(D2D1_FACTORY_TYPE_MULTI_THREADED,IID_PPV_ARGS(&d2dFactory)));
        ComPtr<IDXGIDevice> dxgi;check(device.As(&dxgi));check(d2dFactory->CreateDevice(dxgi.Get(),&d2dDevice));check(d2dDevice->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE,&d2d));
        check(d2d->CreateEffect(CLSID_D2D1GaussianBlur,&gaussian));check(gaussian->SetValue(D2D1_PROPERTY_PRECISION,D2D1_BUFFER_PRECISION_16BPC_FLOAT));
        check(gaussian->SetValue(D2D1_GAUSSIANBLUR_PROP_OPTIMIZATION,D2D1_GAUSSIANBLUR_OPTIMIZATION_BALANCED));check(gaussian->SetValue(D2D1_GAUSSIANBLUR_PROP_BORDER_MODE,D2D1_BORDER_MODE_HARD));
        D3D11_BUFFER_DESC cb{};cb.ByteWidth=48;cb.Usage=D3D11_USAGE_DEFAULT;cb.BindFlags=D3D11_BIND_CONSTANT_BUFFER;check(device->CreateBuffer(&cb,nullptr,&constants));
        D3D11_BLEND_DESC b{};auto& t=b.RenderTarget[0];t.BlendEnable=TRUE;t.SrcBlend=t.SrcBlendAlpha=D3D11_BLEND_ONE;t.DestBlend=t.DestBlendAlpha=D3D11_BLEND_INV_SRC_ALPHA;t.BlendOp=t.BlendOpAlpha=D3D11_BLEND_OP_ADD;t.RenderTargetWriteMask=D3D11_COLOR_WRITE_ENABLE_ALL;check(device->CreateBlendState(&b,&blend));
        D3D11_RASTERIZER_DESC rs{};rs.FillMode=D3D11_FILL_SOLID;rs.CullMode=D3D11_CULL_NONE;rs.DepthClipEnable=TRUE;rs.ScissorEnable=TRUE;check(device->CreateRasterizerState(&rs,&scissor));
    }
    bool enabled()const{return !rects.empty();}
    void releaseSurfaces(){
        d2d->SetTarget(nullptr);gaussian->SetInput(0,nullptr);sceneBitmap.Reset();blurBitmap.Reset();
        sceneTexture.Reset();sceneTarget.Reset();sceneView.Reset();
        for(int i=0;i<2;i++){blurTextures[i].Reset();blurTargets[i].Reset();blurViews[i].Reset();}
        rects.clear();dark=blurReady=false;width=height=bw=bh=0;blurBuilds=0;
    }
    void ensure(UINT w,UINT h){
        if(width==w&&height==h)return;d2d->SetTarget(nullptr);gaussian->SetInput(0,nullptr);sceneBitmap.Reset();blurBitmap.Reset();sceneTexture.Reset();sceneTarget.Reset();sceneView.Reset();for(int i=0;i<2;i++){blurTextures[i].Reset();blurTargets[i].Reset();blurViews[i].Reset();}
        auto create=[&](UINT tw,UINT th,ComPtr<ID3D11Texture2D>& tex,ComPtr<ID3D11RenderTargetView>& target,ComPtr<ID3D11ShaderResourceView>& view){D3D11_TEXTURE2D_DESC d{};d.Width=tw;d.Height=th;d.ArraySize=d.MipLevels=1;d.Format=DXGI_FORMAT_R16G16B16A16_FLOAT;d.SampleDesc.Count=1;d.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;check(device->CreateTexture2D(&d,nullptr,&tex));check(device->CreateRenderTargetView(tex.Get(),nullptr,&target));check(device->CreateShaderResourceView(tex.Get(),nullptr,&view));};
        width=w;height=h;blurReady=false;bw=std::max(1u,w/4);bh=std::max(1u,h/4);create(w,h,sceneTexture,sceneTarget,sceneView);for(int i=0;i<2;i++)create(bw,bh,blurTextures[i],blurTargets[i],blurViews[i]);
        // Reduce the input before the native Gaussian rather than blurring a
        // full 4K/8K canvas and shrinking its output afterwards.
        ComPtr<IDXGISurface> input,output;check(blurTextures[0].As(&input));check(blurTextures[1].As(&output));
        auto properties=D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_NONE,D2D1::PixelFormat(DXGI_FORMAT_R16G16B16A16_FLOAT,D2D1_ALPHA_MODE_PREMULTIPLIED));
        check(d2d->CreateBitmapFromDxgiSurface(input.Get(),&properties,&sceneBitmap));properties.bitmapOptions=D2D1_BITMAP_OPTIONS_TARGET;check(d2d->CreateBitmapFromDxgiSurface(output.Get(),&properties,&blurBitmap));gaussian->SetInput(0,sceneBitmap.Get());
    }
    uint64_t bytes()const{return uint64_t(width)*height*8+uint64_t(bw)*bh*16;}
    void readPanel(ID3D11DeviceContext* context,ID3D11SamplerState* sampler,GlassRect r,bool theme,float white,UINT w,UINT h,uint8_t* bgra){
        if(!sceneTexture||!w||!h||w>2048||h>2048)throw Failure{E_INVALIDARG};
        context->ClearState();ComPtr<ID3D11Texture2D> texture,staging;ComPtr<ID3D11RenderTargetView> target;
        D3D11_TEXTURE2D_DESC d{};d.Width=w;d.Height=h;d.MipLevels=d.ArraySize=1;d.Format=DXGI_FORMAT_R16G16B16A16_FLOAT;d.SampleDesc.Count=1;d.BindFlags=D3D11_BIND_RENDER_TARGET;
        check(device->CreateTexture2D(&d,nullptr,&texture));check(device->CreateRenderTargetView(texture.Get(),nullptr,&target));
        auto t=target.Get();float clear[4]{};context->ClearRenderTargetView(t,clear);context->OMSetRenderTargets(1,&t,nullptr);D3D11_VIEWPORT vp{0,0,float(w),float(h),0,1};context->RSSetViewports(1,&vp);
        // Render the same shader in a popup-local surface. The compositor's
        // brush contains the blurred photograph, so underlying XAML controls
        // cannot bleed through a transparent popup over the filmstrip.
        float values[]={float(width),float(height),r.left,r.top,r.left,r.top,float(w),float(h),0,1,theme?1.0f:0.0f,white};
        context->UpdateSubresource(constants.Get(),0,nullptr,values,0,0);auto cb=constants.Get();context->PSSetConstantBuffers(0,1,&cb);context->IASetInputLayout(nullptr);context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);context->VSSetShader(vs.Get(),nullptr,0);context->PSSetShader(panel.Get(),nullptr,0);auto src=blurViews[1].Get();context->PSSetShaderResources(0,1,&src);context->PSSetSamplers(0,1,&sampler);context->Draw(3,0);context->ClearState();
        d.BindFlags=0;d.Usage=D3D11_USAGE_STAGING;d.CPUAccessFlags=D3D11_CPU_ACCESS_READ;check(device->CreateTexture2D(&d,nullptr,&staging));context->CopyResource(staging.Get(),texture.Get());D3D11_MAPPED_SUBRESOURCE m{};check(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&m));
        auto half=[](uint16_t v){uint32_t sign=uint32_t(v&0x8000)<<16,exp=(v>>10)&31,mant=v&1023;float result;if(exp==0)result=std::ldexp(float(mant),-24);else if(exp==31)result=mant?0.0f:65504.0f;else{uint32_t bits=sign|((exp+112)<<23)|(mant<<13);memcpy(&result,&bits,4);return result;}return sign?-result:result;};
        auto encode=[](float linear){linear=std::clamp(linear,0.0f,1.0f);return uint8_t(std::lround(255*(linear<=.0031308f?12.92f*linear:1.055f*std::pow(linear,1/2.4f)-.055f)));};
        for(UINT y=0;y<h;y++){auto row=reinterpret_cast<uint16_t*>(static_cast<uint8_t*>(m.pData)+size_t(y)*m.RowPitch);for(UINT x=0;x<w;x++){auto pixel=row+x*4;float alpha=half(pixel[3]);auto out=bgra+(size_t(y)*w+x)*4;for(int channel=0;channel<3;channel++)out[2-channel]=encode(half(pixel[channel])/std::max(.001f,alpha*white));out[3]=255;}}
        context->Unmap(staging.Get(),0);
    }
    void composite(ID3D11DeviceContext* context,ID3D11RenderTargetView* output,ID3D11SamplerState* sampler,float white,float dpi,bool refresh){
        context->IASetInputLayout(nullptr);context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        auto pass=[&](ID3D11RenderTargetView* target,ID3D11ShaderResourceView* source,ID3D11PixelShader* shader,UINT w,UINT h,const float* values){context->OMSetRenderTargets(0,nullptr,nullptr);ID3D11ShaderResourceView* empty=nullptr;context->PSSetShaderResources(0,1,&empty);context->OMSetRenderTargets(1,&target,nullptr);D3D11_VIEWPORT vp{0,0,float(w),float(h),0,1};context->RSSetViewports(1,&vp);context->UpdateSubresource(constants.Get(),0,nullptr,values,0,0);auto cb=constants.Get();context->PSSetConstantBuffers(0,1,&cb);context->VSSetShader(vs.Get(),nullptr,0);context->PSSetShader(shader,nullptr,0);context->PSSetShaderResources(0,1,&source);context->PSSetSamplers(0,1,&sampler);context->Draw(3,0);};
        float values[12]={float(width),float(height)};
        // Windows Direct2D performs its native Gaussian in linear FP16. Its
        // internal optimized effect graph replaces the handwritten blur kernel.
        if(refresh||!blurReady){
            pass(blurTargets[0].Get(),sceneView.Get(),copy.Get(),bw,bh,values);context->ClearState();
            check(gaussian->SetValue(D2D1_GAUSSIANBLUR_PROP_STANDARD_DEVIATION,24.0f*dpi*float(bw)/width));
            d2d->SetTarget(blurBitmap.Get());d2d->SetTransform(D2D1::Matrix3x2F::Identity());d2d->BeginDraw();d2d->Clear(D2D1::ColorF(0,0));d2d->DrawImage(gaussian.Get());check(d2d->EndDraw());d2d->SetTarget(nullptr);blurReady=true;blurBuilds++;
        }
        context->ClearState();context->IASetInputLayout(nullptr);context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        pass(output,sceneView.Get(),copy.Get(),width,height,values);
        context->OMSetBlendState(blend.Get(),nullptr,0xffffffff);
        context->RSSetState(scissor.Get());
        for(auto& r:rects){D3D11_RECT clip{LONG(std::max(0.0f,std::floor(r.left))),LONG(std::max(0.0f,std::floor(r.top))),LONG(std::min(float(width),std::ceil(r.left+r.width))),LONG(std::min(float(height),std::ceil(r.top+r.height)))};context->RSSetScissorRects(1,&clip);values[4]=r.left;values[5]=r.top;values[6]=r.width;values[7]=r.height;values[8]=r.radius;values[9]=r.opacity;values[10]=dark?1.0f:0.0f;values[11]=white;pass(output,blurViews[1].Get(),panel.Get(),width,height,values);}
        context->OMSetBlendState(nullptr,nullptr,0xffffffff);context->ClearState();
    }
};
