#include "Converter.h"
#include "Version.h"
#include <commctrl.h>
#include <shobjidl.h>
#include <shellapi.h>
#include <dwmapi.h>
#include <gdiplus.h>
#include <thread>
#include <mutex>
#pragma comment(linker,"\"/manifestdependency:type='win32' name='Microsoft.Windows.Common-Controls' version='6.0.0.0' processorArchitecture='*' publicKeyToken='6595b64144ccf1df' language='*'\"")
using namespace hdrjpeg;
namespace {
constexpr UINT Progress=WM_APP+1,Finished=WM_APP+2;
enum { Brand=100,Title,Subtitle,DropTitle,DropHint,Choose,QueueTitle,Clear,List,DestinationLabel,Destination,Change,Start,Cancel,Open,Status,Hint,Project };
struct Item { fs::path source,output; std::wstring status=L"等待转换"; bool done=false; };
struct Update { size_t index;std::wstring status;fs::path output;bool done=false; };
HWND window{},controls[130]{};HFONT normal{},fontSmall{},heading{},bold{};
std::vector<Item> items;fs::path destination;std::thread worker;std::atomic_bool stop=false;bool busy=false;
int dpi=96;bool dark=false,highContrast=false;HBRUSH background{},surface{};
int theme=-1;
COLORREF bg,card,ink,muted,line,accent;
int px(int value){return MulDiv(value,dpi,96);}
void textAt(int id,const std::wstring& text){SetWindowTextW(controls[id],text.c_str());}
void message(const std::wstring& text){MessageBoxW(window,text.c_str(),L"Luma HDR 转换器",MB_OK|MB_ICONINFORMATION);}
void colors(){
    HIGHCONTRASTW hc{sizeof(hc)};SystemParametersInfoW(SPI_GETHIGHCONTRAST,sizeof(hc),&hc,0);highContrast=(hc.dwFlags&HCF_HIGHCONTRASTON)!=0;
    DWORD light=1,size=sizeof(light);RegGetValueW(HKEY_CURRENT_USER,L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",L"AppsUseLightTheme",RRF_RT_REG_DWORD,nullptr,&light,&size);dark=theme<0?!light:theme==1;
    bg=highContrast?GetSysColor(COLOR_WINDOW):(dark?RGB(19,22,29):RGB(245,247,251));
    card=highContrast?bg:(dark?RGB(28,33,43):RGB(255,255,255));ink=highContrast?GetSysColor(COLOR_WINDOWTEXT):(dark?RGB(239,242,248):RGB(26,35,52));
    muted=highContrast?ink:(dark?RGB(160,172,191):RGB(105,119,140));line=highContrast?ink:(dark?RGB(53,63,80):RGB(223,229,239));
    accent=highContrast?GetSysColor(COLOR_HIGHLIGHT):(dark?RGB(117,163,255):RGB(46,102,225));
    if(background)DeleteObject(background);if(surface)DeleteObject(surface);background=CreateSolidBrush(bg);surface=CreateSolidBrush(card);
    BOOL value=dark;DwmSetWindowAttribute(window,20,&value,sizeof(value));
    if(controls[List]){ListView_SetBkColor(controls[List],card);ListView_SetTextBkColor(controls[List],card);ListView_SetTextColor(controls[List],ink);}
}
HFONT font(int size,int weight=FW_NORMAL){return CreateFontW(-px(size),0,0,0,weight,FALSE,FALSE,FALSE,DEFAULT_CHARSET,OUT_DEFAULT_PRECIS,CLIP_DEFAULT_PRECIS,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Segoe UI");}
void fonts(){
    for(auto f:{normal,fontSmall,heading,bold})if(f)DeleteObject(f);
    normal=font(14);fontSmall=font(12);heading=font(30,FW_SEMIBOLD);bold=font(16,FW_SEMIBOLD);
    for(int id=Brand;id<=Project;id++)if(controls[id])SendMessageW(controls[id],WM_SETFONT,WPARAM(id==Title?heading:(id==DropTitle||id==QueueTitle?bold:(id==Brand||id==Hint||id==Project?fontSmall:normal))),TRUE);
}
void place(int id,int x,int y,int w,int h){MoveWindow(controls[id],px(x),px(y),px(w),px(h),TRUE);}
void layout(){
    RECT rc;GetClientRect(window,&rc);int w=MulDiv(rc.right,96,dpi),h=MulDiv(rc.bottom,96,dpi);
    place(Brand,34,25,w-230,22);place(Project,w-164,18,132,34);place(Title,32,55,w-64,48);place(Subtitle,34,112,w-68,26);
    place(DropTitle,112,184,w-326,30);place(DropHint,112,222,w-326,44);place(Choose,w-190,201,126,42);
    place(QueueTitle,34,326,200,28);place(Clear,w-132,320,100,32);
    place(List,34,366,w-68,h-556);
    ListView_SetColumnWidth(controls[List],0,px((w-84)*52/100));ListView_SetColumnWidth(controls[List],1,px((w-84)*48/100));
    place(DestinationLabel,34,h-164,76,25);place(Destination,115,h-164,w-265,28);place(Change,w-132,h-170,100,34);
    place(Status,34,h-109,w-440,48);place(Open,w-395,h-112,116,44);place(Cancel,w-266,h-112,94,44);place(Start,w-158,h-112,126,44);
    place(Hint,34,h-45,w-68,26);InvalidateRect(window,nullptr,TRUE);
}
void addControl(int id,const wchar_t* cls,const wchar_t* text,DWORD style){
    controls[id]=CreateWindowExW(0,cls,text,WS_CHILD|WS_VISIBLE|style,0,0,1,1,window,reinterpret_cast<HMENU>(INT_PTR(id)),GetModuleHandleW(nullptr),nullptr);
}
void refresh(){
    ListView_DeleteAllItems(controls[List]);
    for(size_t i=0;i<items.size();i++){
        auto name=items[i].source.filename().wstring();LVITEMW item{};item.mask=LVIF_TEXT;item.iItem=int(i);item.pszText=name.data();ListView_InsertItem(controls[List],&item);
        ListView_SetItemText(controls[List],int(i),1,items[i].status.data());
    }
    textAt(QueueTitle,L"转换列表 · "+std::to_wstring(items.size())+L" 张");
    ShowWindow(controls[List],items.empty()?SW_HIDE:SW_SHOW);
    textAt(Destination,destination.empty()?L"原图旁的 Ultra HDR 文件夹":destination.wstring());
    bool pending=std::any_of(items.begin(),items.end(),[](const auto& i){return !i.done;});
    EnableWindow(controls[Start],!busy&&pending);EnableWindow(controls[Choose],!busy);EnableWindow(controls[Clear],!busy&&!items.empty());EnableWindow(controls[Change],!busy);
    EnableWindow(controls[Cancel],busy&&!stop.load());EnableWindow(controls[Open],!busy&&std::any_of(items.begin(),items.end(),[](const auto& i){return i.done;}));
    InvalidateRect(window,nullptr,FALSE);
}
void addFiles(const std::vector<fs::path>& paths){
    if(busy)return;unsigned skipped=0;
    for(auto path:paths){
        auto ext=path.extension().wstring();std::transform(ext.begin(),ext.end(),ext.begin(),towlower);
        if(ext!=L".jxr"&&ext!=L".wdp"&&ext!=L".hdp"){skipped++;continue;}
        std::error_code ec;path=fs::absolute(path,ec);if(ec||!fs::is_regular_file(path,ec)||ec){skipped++;continue;}
        if(std::any_of(items.begin(),items.end(),[&](const auto& i){return _wcsicmp(i.source.c_str(),path.c_str())==0;}))continue;
        if(items.size()>=1000){skipped++;continue;}items.push_back({path});
    }
    textAt(Status,skipped?L"部分文件未添加：仅支持 JXR / WDP / HDP，最多 1000 张。":L"准备好后，点击开始转换。");refresh();
}
void choose(bool folder){
    ComPtr<IFileOpenDialog> dialog;if(FAILED(CoCreateInstance(CLSID_FileOpenDialog,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&dialog)))){message(L"无法打开文件选择器");return;}
    DWORD flags=0;dialog->GetOptions(&flags);dialog->SetOptions(flags|FOS_FORCEFILESYSTEM|(folder?FOS_PICKFOLDERS:FOS_ALLOWMULTISELECT|FOS_FILEMUSTEXIST));
    if(folder)dialog->SetTitle(L"选择 Ultra HDR 图片的保存文件夹");
    else{COMDLG_FILTERSPEC filter{L"Windows HDR 图片（JXR / WDP / HDP）",L"*.jxr;*.wdp;*.hdp"};dialog->SetFileTypes(1,&filter);dialog->SetTitle(L"添加 Windows HDR 图片");}
    if(FAILED(dialog->Show(window)))return;
    ComPtr<IShellItemArray> selection;if(FAILED(dialog->GetResults(&selection)))return;DWORD count=0;selection->GetCount(&count);std::vector<fs::path> paths;
    for(DWORD i=0;i<count;i++){ComPtr<IShellItem> item;PWSTR name=nullptr;if(SUCCEEDED(selection->GetItemAt(i,&item))&&SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH,&name))){paths.emplace_back(name);CoTaskMemFree(name);}}
    if(folder){if(!paths.empty())destination=paths.front();refresh();}else addFiles(paths);
}
void start(){
    if(busy)return;if(worker.joinable())worker.join();busy=true;stop=false;textAt(Status,L"正在转换；大图可能需要一些时间。");refresh();
    const auto work=items;const auto folder=destination;
    worker=std::thread([work,folder]{
        size_t success=0,failed=0;
        for(size_t i=0;i<work.size()&&!stop.load();i++){
            if(work[i].done)continue;
            PostMessageW(window,Progress,0,reinterpret_cast<LPARAM>(new Update{i,L"正在转换…"}));
            auto update=std::make_unique<Update>();update->index=i;
            try{
                auto target=folder.empty()?work[i].source.parent_path()/L"Ultra HDR":folder;fs::create_directories(target);
                update->output=availableName(target,work[i].source);auto result=convert(work[i].source,update->output,&stop);
                update->done=true;update->status=L"已完成 · "+std::to_wstring(result.width)+L" × "+std::to_wstring(result.height)+(result.clippedPixels?L" · 少量色值超出范围":L"");success++;
            }catch(const Error& e){update->status=e.message;if(!stop.load())failed++;}
            catch(const std::bad_alloc&){update->status=L"内存不足，请关闭其他大图后重试";failed++;}
            catch(...){update->status=L"无法完成，请检查文件和保存位置后重试";failed++;}
            PostMessageW(window,Progress,0,reinterpret_cast<LPARAM>(update.release()));
        }
        PostMessageW(window,Finished,WPARAM(success),LPARAM(failed));
    });
}
Gdiplus::Color color(COLORREF c){return Gdiplus::Color(255,GetRValue(c),GetGValue(c),GetBValue(c));}
void rounded(Gdiplus::Graphics& g,float x,float y,float w,float h,float radius,COLORREF fill,COLORREF border){
    Gdiplus::GraphicsPath p;float d=radius*2;p.AddArc(x,y,d,d,180,90);p.AddArc(x+w-d,y,d,d,270,90);p.AddArc(x+w-d,y+h-d,d,d,0,90);p.AddArc(x,y+h-d,d,d,90,90);p.CloseFigure();
    Gdiplus::SolidBrush b(color(fill));g.FillPath(&b,&p);Gdiplus::Pen pen(color(border));g.DrawPath(&pen,&p);
}
void paint(){
    PAINTSTRUCT ps;HDC dc=BeginPaint(window,&ps);RECT rc;GetClientRect(window,&rc);HDC mem=CreateCompatibleDC(dc);HBITMAP bmp=CreateCompatibleBitmap(dc,rc.right,rc.bottom);auto old=SelectObject(mem,bmp);
    FillRect(mem,&rc,background);
    {Gdiplus::Graphics g(mem);g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);g.ScaleTransform(float(dpi)/96,float(dpi)/96);float w=float(rc.right)*96/dpi;
        rounded(g,32,156,w-64,144,18,card,line);rounded(g,52,193,44,44,12,highContrast?bg:(dark?RGB(41,58,91):RGB(234,241,255)),highContrast?ink:(dark?RGB(41,58,91):RGB(234,241,255)));
        Gdiplus::Pen pen(color(accent),2.1f);g.DrawRectangle(&pen,63,205,22,18);g.DrawLine(&pen,64,220,71,212);g.DrawLine(&pen,71,212,77,218);g.DrawLine(&pen,77,218,84,210);
        Gdiplus::SolidBrush dot(color(accent));g.FillEllipse(&dot,77,208,3,3);
        if(items.empty()){
            SetBkMode(mem,TRANSPARENT);SetTextColor(mem,muted);SelectObject(mem,normal);RECT empty{px(50),px(407),rc.right-px(50),rc.bottom-px(205)};
            DrawTextW(mem,L"还没有图片\n拖入 HDR 截图，或者点击上方“添加图片”",-1,&empty,DT_CENTER|DT_WORDBREAK);
        }
    }
    BitBlt(dc,0,0,rc.right,rc.bottom,mem,0,0,SRCCOPY);SelectObject(mem,old);DeleteObject(bmp);DeleteDC(mem);EndPaint(window,&ps);
}
LRESULT CALLBACK proc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp){
    switch(msg){
    case WM_CREATE:{window=hwnd;dpi=GetDpiForWindow(hwnd);colors();
        addControl(Brand,L"STATIC",L"L U M A   /   HDR CONVERTER   ·   v" HDRJPEG_WIDEN(HDRJPEG_VERSION) L"   ·   便携 · 离线",SS_LEFT);
        addControl(Project,L"BUTTON",L"项目主页 ↗ (&H)",WS_TABSTOP|BS_OWNERDRAW);
        addControl(Title,L"STATIC",L"把 HDR，带到手机。",SS_LEFT);addControl(Subtitle,L"STATIC",L"Windows HDR 截图 → Ultra HDR JPG，保留高光，也方便分享。",SS_LEFT);
        addControl(DropTitle,L"STATIC",L"将 HDR 图片拖到这里",SS_LEFT);addControl(DropHint,L"STATIC",L"支持 JXR · WDP · HDP\n可一次添加多张图片",SS_LEFT);
        for(auto id:{Choose,Clear,Change,Start,Cancel,Open})addControl(id,L"BUTTON",id==Choose?L"添加图片 (&A)":id==Clear?L"清空列表 (&L)":id==Change?L"更改 (&D)":id==Start?L"开始转换 (&S)":id==Cancel?L"取消 (&C)":L"打开文件夹 (&O)",WS_TABSTOP|BS_OWNERDRAW);
        addControl(QueueTitle,L"STATIC",L"转换列表 · 0 张",SS_LEFT);
        addControl(List,WC_LISTVIEWW,L"图片转换列表",WS_TABSTOP|LVS_REPORT|LVS_SHOWSELALWAYS|LVS_NOSORTHEADER|LVS_SINGLESEL);
        ListView_SetExtendedListViewStyle(controls[List],LVS_EX_FULLROWSELECT|LVS_EX_DOUBLEBUFFER|LVS_EX_LABELTIP);
        LVCOLUMNW col{};col.mask=LVCF_TEXT|LVCF_WIDTH;col.cx=400;col.pszText=const_cast<wchar_t*>(L"图片");ListView_InsertColumn(controls[List],0,&col);col.pszText=const_cast<wchar_t*>(L"状态 / 错误原因");ListView_InsertColumn(controls[List],1,&col);
        addControl(DestinationLabel,L"STATIC",L"保存位置",SS_LEFT);addControl(Destination,L"STATIC",L"",SS_LEFT|SS_PATHELLIPSIS);
        addControl(Status,L"STATIC",L"原图会保留，同名文件自动编号。",SS_LEFT);
        addControl(Hint,L"STATIC",L"手机显示 HDR 需要兼容的屏幕与相册；传输时请选择原文件，避免压缩。",SS_LEFT);
        auto menu=GetSystemMenu(hwnd,FALSE);AppendMenuW(menu,MF_SEPARATOR,0,nullptr);AppendMenuW(menu,MF_STRING,0x110,L"外观：跟随系统 (&Y)");AppendMenuW(menu,MF_STRING,0x120,L"外观：浅色 (&W)");AppendMenuW(menu,MF_STRING,0x130,L"外观：深色 (&B)");
        fonts();colors();refresh();DragAcceptFiles(hwnd,TRUE);return 0;}
    case WM_SYSCOMMAND:if((wp&0xfff0)>=0x110&&(wp&0xfff0)<=0x130){theme=int((wp&0xfff0)-0x110)/16-1;colors();RedrawWindow(hwnd,nullptr,nullptr,RDW_INVALIDATE|RDW_ERASE|RDW_ALLCHILDREN);return 0;}break;
    case WM_SIZE:layout();return 0;
    case WM_GETMINMAXINFO:{auto m=reinterpret_cast<MINMAXINFO*>(lp);m->ptMinTrackSize={px(740),px(680)};return 0;}
    case WM_DPICHANGED:{dpi=HIWORD(wp);fonts();auto r=reinterpret_cast<RECT*>(lp);SetWindowPos(hwnd,nullptr,r->left,r->top,r->right-r->left,r->bottom-r->top,SWP_NOZORDER|SWP_NOACTIVATE);layout();return 0;}
    case WM_SETTINGCHANGE:case WM_THEMECHANGED:colors();InvalidateRect(hwnd,nullptr,TRUE);return 0;
    case WM_NOTIFY:{auto header=reinterpret_cast<NMHDR*>(lp);
        if(header->idFrom==List&&(header->code==NM_DBLCLK||header->code==NM_RETURN)){
            int selected=ListView_GetNextItem(controls[List],-1,LVNI_SELECTED);if(selected>=0&&size_t(selected)<items.size())message(items[selected].source.filename().wstring()+L"\n\n"+items[selected].status+(items[selected].done?L"\n\n"+items[selected].output.wstring():L""));return 0;}
        if(header->hwndFrom==ListView_GetHeader(controls[List])&&header->code==NM_CUSTOMDRAW){auto draw=reinterpret_cast<NMCUSTOMDRAW*>(lp);if(draw->dwDrawStage==CDDS_PREPAINT)return CDRF_NOTIFYITEMDRAW;
            if(draw->dwDrawStage==CDDS_ITEMPREPAINT){FillRect(draw->hdc,&draw->rc,surface);SetBkMode(draw->hdc,TRANSPARENT);SetTextColor(draw->hdc,muted);SelectObject(draw->hdc,fontSmall);RECT r=draw->rc;r.left+=px(8);DrawTextW(draw->hdc,draw->dwItemSpec==0?L"图片":L"状态 / 错误原因（双击查看）",-1,&r,DT_SINGLELINE|DT_VCENTER|DT_END_ELLIPSIS);return CDRF_SKIPDEFAULT;}}
        break;}
    case WM_ERASEBKGND:return 1;
    case WM_PAINT:paint();return 0;
    case WM_CTLCOLORSTATIC:{auto dc=reinterpret_cast<HDC>(wp);int id=GetDlgCtrlID(reinterpret_cast<HWND>(lp));bool inCard=id==DropTitle||id==DropHint;SetBkColor(dc,inCard?card:bg);SetTextColor(dc,id==Title||id==DropTitle||id==QueueTitle?ink:muted);return LRESULT(inCard?surface:background);}
    case WM_DRAWITEM:{auto di=reinterpret_cast<DRAWITEMSTRUCT*>(lp);if(di->CtlType!=ODT_BUTTON)break;
        bool enabled=!(di->itemState&ODS_DISABLED),primary=di->CtlID==Start,pressed=di->itemState&ODS_SELECTED;
        COLORREF fill=primary&&enabled?accent:card,border=primary&&enabled?accent:line;
        if(pressed)fill=dark?RGB(47,68,103):RGB(219,230,249);
        // The add button sits on the card. Its antialiased corners must expose
        // the card material, rather than a rectangle of the window background.
        FillRect(di->hDC,&di->rcItem,di->CtlID==Choose?surface:background);if(di->CtlID!=Project){Gdiplus::Graphics g(di->hDC);g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
            rounded(g,1,1,float(di->rcItem.right-2),float(di->rcItem.bottom-2),float(px(8)),fill,border);}
        SetBkMode(di->hDC,TRANSPARENT);SetTextColor(di->hDC,di->CtlID==Project?accent:(!enabled?muted:(primary&&!pressed?(dark?RGB(16,26,44):RGB(255,255,255)):ink)));SelectObject(di->hDC,di->CtlID==Project?fontSmall:normal);
        wchar_t label[100];GetWindowTextW(di->hwndItem,label,100);RECT r=di->rcItem;DrawTextW(di->hDC,label,-1,&r,DT_CENTER|DT_VCENTER|DT_SINGLELINE|((di->itemState&ODS_NOACCEL)?DT_HIDEPREFIX:0));
        if(di->itemState&ODS_FOCUS){InflateRect(&r,-px(5),-px(5));DrawFocusRect(di->hDC,&r);}return TRUE;}
    case WM_DROPFILES:{HDROP drop=reinterpret_cast<HDROP>(wp);UINT count=DragQueryFileW(drop,0xffffffff,nullptr,0);std::vector<fs::path> paths;
        for(UINT i=0;i<count;i++){UINT n=DragQueryFileW(drop,i,nullptr,0);std::wstring path(n+1,L'\0');DragQueryFileW(drop,i,path.data(),n+1);path.resize(n);paths.emplace_back(path);}DragFinish(drop);addFiles(paths);return 0;}
    case WM_COMMAND:switch(LOWORD(wp)){
        case Project:ShellExecuteW(hwnd,L"open",L"https://github.com/lueluelue12138/luma-hdr-photo-viewer",nullptr,nullptr,SW_SHOWNORMAL);break;
        case Choose:if(!busy)choose(false);break;case Change:if(!busy)choose(true);break;case Clear:if(!busy){items.clear();textAt(Status,L"原图会保留，同名文件自动编号。");refresh();}break;
        case Start:start();break;case Cancel:stop=true;EnableWindow(controls[Cancel],FALSE);textAt(Status,L"正在停止，当前编码结束后取消保存…");break;
        case Open:for(auto it=items.rbegin();it!=items.rend();++it)if(it->done){ShellExecuteW(hwnd,L"open",it->output.parent_path().c_str(),nullptr,nullptr,SW_SHOWNORMAL);break;}break;
        }return 0;
    case Progress:{std::unique_ptr<Update> update(reinterpret_cast<Update*>(lp));auto& item=items.at(update->index);item.status=update->status;item.done=update->done;item.output=update->output;
        ListView_SetItemText(controls[List],int(update->index),1,item.status.data());ListView_EnsureVisible(controls[List],int(update->index),FALSE);return 0;}
    case Finished:if(worker.joinable())worker.join();busy=false;textAt(Status,(stop.load()?L"已停止 · ":L"转换结束 · ")+std::to_wstring(wp)+L" 张完成"+(lp?L"，"+std::to_wstring(lp)+L" 张失败（可重试）":L""));refresh();return 0;
    case WM_CLOSE:if(busy){stop=true;textAt(Status,L"正在停止，请稍后再关闭窗口。");EnableWindow(controls[Cancel],FALSE);return 0;}DestroyWindow(hwnd);return 0;
    case WM_DESTROY:if(worker.joinable())worker.join();PostQuitMessage(0);return 0;
    }return DefWindowProcW(hwnd,msg,wp,lp);
}
}
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,PWSTR,int show){
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);OleInitialize(nullptr);
    Gdiplus::GdiplusStartupInput gdiplus;ULONG_PTR token{};Gdiplus::GdiplusStartup(&token,&gdiplus,nullptr);
    INITCOMMONCONTROLSEX cc{sizeof(cc),ICC_LISTVIEW_CLASSES};InitCommonControlsEx(&cc);
    WNDCLASSW cls{};cls.lpfnWndProc=proc;cls.hInstance=instance;cls.lpszClassName=L"LumaHdrJpeg";cls.hCursor=LoadCursorW(nullptr,IDC_ARROW);cls.hIcon=LoadIconW(instance,MAKEINTRESOURCEW(1));RegisterClassW(&cls);
    UINT screenDpi=GetDpiForSystem();RECT size{0,0,MulDiv(900,screenDpi,96),MulDiv(700,screenDpi,96)};AdjustWindowRectExForDpi(&size,WS_OVERLAPPEDWINDOW,FALSE,0,screenDpi);
    HWND app=CreateWindowExW(WS_EX_ACCEPTFILES,cls.lpszClassName,L"Luma HDR 转换器 " HDRJPEG_WIDEN(HDRJPEG_VERSION),WS_OVERLAPPEDWINDOW,CW_USEDEFAULT,CW_USEDEFAULT,size.right-size.left,size.bottom-size.top,nullptr,nullptr,instance,nullptr);
    if(!app)return 1;int argc=0;auto argv=CommandLineToArgvW(GetCommandLineW(),&argc);std::vector<fs::path> paths;for(int i=1;i<argc;i++)paths.emplace_back(argv[i]);LocalFree(argv);addFiles(paths);
    ShowWindow(app,show);UpdateWindow(app);MSG msg;while(GetMessageW(&msg,nullptr,0,0)>0){if(!IsDialogMessageW(app,&msg)){TranslateMessage(&msg);DispatchMessageW(&msg);}}
    for(auto f:{normal,fontSmall,heading,bold})if(f)DeleteObject(f);DeleteObject(background);DeleteObject(surface);Gdiplus::GdiplusShutdown(token);OleUninitialize();return 0;
}
