using Luma.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Luma.App;
public sealed partial class ViewerWindow
{
    private PhotoMetadata? detailsMetadata;
    private int detailsPhotoRequest=-1,detailsLoadRequest=-1;
    private CancellationTokenSource? detailsCancellation;
    private Task detailsLoad=Task.CompletedTask;
    private IReadOnlyList<PhotoProperty> detailsRows=[];
    private void InfoClick(object sender,RoutedEventArgs e)=>ShowInfo(InfoPanel.Visibility!=Visibility.Visible);
    private void CloseInfoClick(object sender,RoutedEventArgs e){ShowInfo(false);InputLayer.Focus(FocusState.Programmatic);}
    private void ShowInfo(bool visible)
    {
        InfoPanel.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        InformationButton.Background=new SolidColorBrush(visible?(Root.ActualTheme==ElementTheme.Dark?Windows.UI.Color.FromArgb(36,255,255,255):Windows.UI.Color.FromArgb(22,0,0,0)):Microsoft.UI.Colors.Transparent);
        AutomationProperties.SetHelpText(InformationButton,visible?"照片信息已展开":"照片信息已收起");
        if(visible){ShowControls(true);UpdateInfo();Motion.Enter(InfoPanel,8);}
        else{detailsCancellation?.Cancel();detailsCancellation?.Dispose();detailsCancellation=null;detailsLoadRequest=-1;hover=false;ShowControls();}
        UpdateGlass();
    }
    private static void SetInfoText(TextBlock target,string text){if(target.Text!=text)target.Text=text;}
    private void UpdateInfo()
    {
        if(closed||current is null||InfoPanel.Visibility!=Visibility.Visible)return;
        if(detailsPhotoRequest!=request){
            detailsPhotoRequest=request;detailsCancellation?.Cancel();detailsCancellation?.Dispose();detailsCancellation=null;detailsLoadRequest=-1;detailsMetadata=null;detailsRows=[];
            InfoMetadataGroups.ItemsSource=null;InfoCameraSection.Visibility=InfoGpsSection.Visibility=Visibility.Collapsed;InfoLens.Text=InfoCamera.Text=InfoExposure.Text=InfoColor.Text=InfoResolution.Text="";
            InfoDateText.Text="日期读取中…";InfoReadStatus.Text="正在读取拍摄信息…";InfoReadStatus.Visibility=Visibility.Visible;InfoScroll.ChangeView(null,0,null,true);
        }
        SetInfoText(InfoFileName,current.Name);SetInfoText(InfoFilePath,current.Path);if(!Equals(ToolTipService.GetToolTip(InfoFilePath),current.Path))ToolTipService.SetToolTip(InfoFilePath,current.Path);
        long length=detailsMetadata?.Length??current.Size;SetInfoText(InfoSize,length>0?PhotoMetadataFormatting.Size(length):"文件大小读取中…");
        var frame=currentFrame?.Info;uint width=frame?.OriginalWidth??detailsMetadata?.Width??0,height=frame?.OriginalHeight??detailsMetadata?.Height??0;
        SetInfoText(InfoDimensions,width>0?$"{width:N0} × {height:N0} 像素 · {width*(double)height/1000000:0.#} MP":"尺寸读取中…");
        SetInfoText(InfoHdr,frame is null?"正在识别图片…":currentFrame!.HdrCompatibility?"兼容预览 · HDR 数据未读取":frame.Value.NativeHdr switch{2=>"原生 HDR · JPEG 增益图",3=>"原生 HDR · Apple HEIC 增益图",4=>"原生 HDR · ISO HEIC 增益图",1=>Path.GetExtension(current.Path).Equals(".heic",StringComparison.OrdinalIgnoreCase)||Path.GetExtension(current.Path).Equals(".heif",StringComparison.OrdinalIgnoreCase)?"原生 HDR · 浮点 HEIC":"原生 HDR · 线性 scRGB",_=>"SDR 图片"});
        SetInfoText(InfoViewing,$"当前显示：{StatusText.Text}\n缩放 {PixelScale()*100:0}% · 查看旋转 {rotation*90}°");
        UpdateInfoFeatures();
        if(detailsMetadata is null&&detailsLoadRequest!=request){
            detailsCancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);detailsLoadRequest=request;
            detailsLoad=LoadInfoAsync(current,request,detailsCancellation.Token);
        }
    }
    private async Task LoadInfoAsync(Photo photo,int generation,CancellationToken token)
    {
        try{
            var data=await PhotoMetadataReader.ReadAsync(photo.Path,token);
            if(closed||token.IsCancellationRequested||generation!=request||InfoPanel.Visibility!=Visibility.Visible)return;
            detailsMetadata=data;detailsRows=data.Rows;
            var dates=new List<string>();if(data.Taken is {} taken)dates.Add("拍摄："+PhotoMetadataFormatting.Date(taken));
            dates.Add("修改："+PhotoMetadataFormatting.Date(data.Modified));dates.Add("创建："+PhotoMetadataFormatting.Date(data.Created));InfoDateText.Text=string.Join("\n",dates);
            string Join(params string[] keys)=>string.Join(" · ",keys.Select(data.Value).Where(s=>!string.IsNullOrWhiteSpace(s)));
            InfoCamera.Text=Join("System.Photo.CameraManufacturer","System.Photo.CameraModel");InfoLens.Text=Join("System.Photo.LensManufacturer","System.Photo.LensModel");
            InfoExposure.Text=Join("System.Photo.FocalLength","System.Photo.FNumber","System.Photo.ExposureTime","System.Photo.ISOSpeed","System.Photo.ExposureBias");
            InfoCameraSection.Visibility=InfoCamera.Text.Length+InfoLens.Text.Length+InfoExposure.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
            InfoLens.Visibility=string.IsNullOrEmpty(InfoLens.Text)?Visibility.Collapsed:Visibility.Visible;
            string? horizontal=data.Value("System.Image.HorizontalResolution"),vertical=data.Value("System.Image.VerticalResolution");
            string? resolution=horizontal==vertical?horizontal:horizontal is null?vertical:vertical is null?horizontal:horizontal.Replace(" dpi","")+" × "+vertical;
            InfoResolution.Text=string.Join(" · ",new[]{resolution,data.Value("System.Image.BitDepth")}.Where(s=>!string.IsNullOrWhiteSpace(s)));
            InfoColor.Text=data.Value("System.Image.ColorSpace") is {} color?"EXIF 色彩空间："+color:"未记录 EXIF 色彩空间";
            if(data.Latitude is {} lat&&data.Longitude is {} lon){InfoGps.Text=$"纬度 {lat.ToString("0.######",CultureInfo.InvariantCulture)}°\n经度 {lon.ToString("0.######",CultureInfo.InvariantCulture)}°";InfoGpsSection.Visibility=Visibility.Visible;}
            InfoReadStatus.Text=InfoCameraSection.Visibility==Visibility.Collapsed?"这张图片未记录相机或曝光参数":"";InfoReadStatus.Visibility=string.IsNullOrEmpty(InfoReadStatus.Text)?Visibility.Collapsed:Visibility.Visible;
            FilterInfoRows();UpdateInfo();UpdateGlass();
        }catch(OperationCanceledException){}catch(Exception ex){
            if(!closed&&generation==request&&!token.IsCancellationRequested){InfoReadStatus.Text="暂时无法读取更多拍摄信息，仍可查看基本信息";InfoReadStatus.Visibility=Visibility.Visible;InfoDateText.Text="未能读取文件日期";App.Log("Photo metadata: "+ex.Message);}
        }
    }
    private void InfoSearchChanged(object sender,TextChangedEventArgs e)=>FilterInfoRows();
    private void InfoSearchLoaded(object sender,RoutedEventArgs e)=>FilterInfoRows();
    private void FilterInfoRows()
    {
        var rows=PhotoMetadataFormatting.Filter(detailsRows,InfoSearch.Text??"");InfoMetadataGroups.ItemsSource=rows.GroupBy(r=>r.Group).Select(g=>new PhotoPropertyGroup(g.Key,g.ToArray())).ToArray();
        InfoEmptySearch.Text=detailsMetadata is null?"信息读取中…":string.IsNullOrWhiteSpace(InfoSearch.Text)?"没有更多已记录的参数":"没有匹配的信息";
        InfoEmptySearch.Visibility=rows.Count==0?Visibility.Visible:Visibility.Collapsed;
    }
    private string BuildDetailsText()
    {
        if(current is null)return "";
        var summary=$"文件名：{current.Name}\n路径：{current.Path}\n尺寸：{InfoDimensions.Text}\n大小：{InfoSize.Text}\n{InfoDateText.Text}\nHDR 类型：{InfoHdr.Text}\n特性：{InfoHdrFeature.Text}{(liveAsset is null?"":" · LIVE")}\n{InfoLiveSummary.Text}";
        if(detailsMetadata?.Latitude is {} lat&&detailsMetadata.Longitude is {} lon)summary+=$"\n位置：{lat.ToString("0.######",CultureInfo.InvariantCulture)}, {lon.ToString("0.######",CultureInfo.InvariantCulture)}";
        return summary+"\n\n"+string.Join("\n",detailsRows.Select(r=>$"{r.Group} / {r.Label}：{r.Value}"));
    }
    private void CopyPathClick(object sender,RoutedEventArgs e){if(current is not null)CopyInfo(current.Path,"文件路径已复制");}
    private void UpdateInfoFeatures()
    {
        if(current is null)return;
        InfoFormatFeature.Text=Path.GetExtension(current.Path).TrimStart('.').ToUpperInvariant();
        InfoHdrFeature.Text=currentFrame?.Info.NativeHdr>0?"HDR":photoFeatures?.HdrKind is not null?"HDR 信息":"SDR";
        ToolTipService.SetToolTip(InfoHdrFeature,currentFrame?.Info.NativeHdr>0?InfoHdr.Text:photoFeatures?.HdrKind??"原始照片为 SDR，RTX HDR 是可选的显示增强");
        InfoLiveFeature.Visibility=InfoLiveSection.Visibility=liveAsset is null?Visibility.Collapsed:Visibility.Visible;
        if(liveAsset is not {} asset){InfoLiveSummary.Text="";return;}
        string storage=asset.Embedded?"内嵌视频":$"配套视频：{Path.GetFileName(asset.VideoPath)}";
        string playback=asset.NativeVideoHdr?"视频带原生 HDR，暂保留静态照片":livePlayback is {} live?$"播放中 · {live.Width} × {live.Height} · {live.Duration:0.##} 秒 · {(live.AudioTracks>0?"有音轨":"无音轨")}":"点击顶部实况图标或按 L 播放；空格查看未增强画面";
        SetInfoText(InfoLiveSummary,$"{asset.Kind}\n{storage} · {PhotoMetadataFormatting.Size(asset.Length)}\n{playback}");
    }
    private void CopyDetailsClick(object sender,RoutedEventArgs e){UpdateInfo();CopyInfo(BuildDetailsText(),"照片详情已复制");}
    private async void CopyInfo(string text,string feedback)
    {
        try{var package=new DataPackage();package.SetText(text);Clipboard.SetContent(package);InfoReadStatus.Text=feedback;InfoReadStatus.Visibility=Visibility.Visible;int generation=request;await Task.Delay(1600,lifetime.Token);if(!closed&&generation==request){InfoReadStatus.Text="";InfoReadStatus.Visibility=Visibility.Collapsed;}}
        catch(OperationCanceledException){}catch(Exception ex){App.Log("Copy photo info: "+ex.Message);if(!closed){InfoReadStatus.Text="暂时无法访问剪贴板";InfoReadStatus.Visibility=Visibility.Visible;}}
    }
    private static bool IsInfoTextEditing(object? source)
    {
        for(var node=source as DependencyObject;node is not null;node=VisualTreeHelper.GetParent(node))if(node is TextBox)return true;
        return false;
    }
    internal async Task<PhotoMetadata?> VerificationOpenInfoAsync(){ShowInfo(true);await detailsLoad;return detailsMetadata;}
    internal void VerificationCloseInfo()=>ShowInfo(false);
    internal bool VerificationInfoVisible=>InfoPanel.Visibility==Visibility.Visible;
    internal string VerificationInfoFile=>InfoFileName.Text;
    internal IReadOnlyList<PhotoProperty> VerificationInfoRows=>detailsRows;
    internal string VerificationInfoCopyText=>BuildDetailsText();
    internal object VerificationInfoLayout=>new{width=InfoPanel.ActualWidth,height=InfoPanel.ActualHeight,viewport=InfoScroll.ViewportHeight,scrollable=InfoScroll.ScrollableHeight,canvas=Canvas.ActualHeight,top=InfoPanel.Margin.Top};
    internal async Task VerificationInfoFilterAsync(string query){InfoMore.IsExpanded=true;for(int n=0;n<50&&!InfoSearch.IsLoaded;n++)await Task.Delay(20);InfoSearch.Text=query;await Task.Delay(60);}
    internal string VerificationInfoFilterText=>string.Join("\n",(InfoMetadataGroups.ItemsSource as IEnumerable<PhotoPropertyGroup>??[]).SelectMany(g=>g.Rows).Select(r=>$"{r.Label}: {r.Value}"));
    internal async Task<object> VerificationInfoResponsiveAsync()
    {
        var saved=AppWindow.Size;var layouts=new List<object>();
        foreach(var size in new[]{(Width:900,Height:680),(Width:740,Height:570)}){
            AppWindow.Resize(new((int)(size.Width*Surface.CompositionScaleX),(int)(size.Height*Surface.CompositionScaleY)));await Task.Delay(250);await VerificationPresentationAsync();
            var p=InfoPanel.TransformToVisual(Canvas).TransformPoint(new(0,0));var b=InformationButton.TransformToVisual(Canvas).TransformPoint(new(0,0));
            if(p.X<0||p.Y<0||p.X+InfoPanel.ActualWidth>Canvas.ActualWidth+1||p.Y+InfoPanel.ActualHeight>Canvas.ActualHeight+1||b.X<0||b.X+InformationButton.ActualWidth>Canvas.ActualWidth+1||InfoScroll.ViewportHeight<60)throw new InvalidOperationException("Details and its toolbar entry must remain within the resized window");
            layouts.Add(VerificationInfoLayout);
        }
        AppWindow.Resize(saved);await Task.Delay(250);return layouts;
    }
    internal async Task<object> VerificationFilmstripDetailsAsync()
    {
        var saved=AppWindow.Size;var originalSequence=sequence;var samples=new List<object>();
        sequence=Enumerable.Repeat(current!,24).ToList();selecting=true;Filmstrip.ItemsSource=sequence;Filmstrip.SelectedIndex=0;selecting=false;
        foreach(int width in new[]{1240,900,740,600})foreach(bool visible in new[]{true,false})
        {
            AppWindow.Resize(new((int)(width*Surface.CompositionScaleX),(int)(800*Surface.CompositionScaleY)));ShowInfo(visible);ShowControls(true);await Task.Delay(280);await VerificationPresentationAsync();UpdateFilmstripBar();
            var strip=BottomPanel.TransformToVisual(Canvas).TransformPoint(new(0,0));
            double expected=(BottomPanel.Margin.Left+Canvas.ActualWidth-BottomPanel.Margin.Right)/2;
            if(Math.Abs(strip.X+BottomPanel.ActualWidth/2-expected)>2||strip.X<0||strip.X+BottomPanel.ActualWidth>Canvas.ActualWidth+1||Filmstrip.ActualWidth>BottomPanel.ActualWidth-76||FilmstripBar.Maximum<=0)
                throw new InvalidOperationException($"Filmstrip not centered / bounded at {width}, details={visible}: {strip.X}, {BottomPanel.ActualWidth}, list {Filmstrip.ActualWidth}");
            if(visible)
            {
                var info=InfoPanel.TransformToVisual(Canvas).TransformPoint(new(0,0));
                if(strip.X<info.X+InfoPanel.ActualWidth&&strip.X+BottomPanel.ActualWidth>info.X&&strip.Y<info.Y+InfoPanel.ActualHeight&&strip.Y+BottomPanel.ActualHeight>info.Y)
                    throw new InvalidOperationException("Details and filmstrip must not overlap");
            }
            samples.Add(new{width,details=visible,left=strip.X,stripWidth=BottomPanel.ActualWidth,listWidth=Filmstrip.ActualWidth,expectedCenter=expected,range=FilmstripBar.Maximum});
        }
        sequence=originalSequence;selecting=true;Filmstrip.ItemsSource=sequence;Filmstrip.SelectedIndex=index;selecting=false;
        AppWindow.Resize(saved);ShowInfo(true);await Task.Delay(250);return new{scenario="filmstrip-details-layout",samples};
    }
    internal async Task<object> VerificationFillWindowAsync()
    {
        var saved=AppWindow.Size;int oldRotation=rotation;var samples=new List<object>();
        try
        {
            FillClick(FitButton,new RoutedEventArgs());
            foreach(int width in new[]{1240,600})foreach(int turn in new[]{0,1,2,3})
            {
                AppWindow.Resize(new((int)(width*Surface.CompositionScaleX),(int)(800*Surface.CompositionScaleY)));rotation=turn;panX=panY=0;UpdateView();await Task.Delay(200);
                var (w,h)=VisibleDimensions();double vw=Surface.ActualWidth*Surface.CompositionScaleX,vh=Surface.ActualHeight*Surface.CompositionScaleY;
                double shownWidth=w*PixelScale(),shownHeight=h*PixelScale();
                if(!fillWindow||!Equals(FitButton.Content,"填满窗口")||shownWidth<vw-.1||shownHeight<vh-.1||Math.Min(Math.Abs(shownWidth-vw),Math.Abs(shownHeight-vh))>.1||panX!=0||panY!=0)
                    throw new InvalidOperationException("Fill must preserve aspect, cover both edges and fit one dimension after resize / rotation");
                samples.Add(new{width,rotation,viewportWidth=vw,viewportHeight=vh,shownWidth,shownHeight});
            }
            ZoomAt(Surface.ActualWidth/2,Surface.ActualHeight/2,-120);if(fillWindow)throw new InvalidOperationException("Manual zoom must leave automatic fill mode");
            Fill();FitClick(FitToWindowButton,new RoutedEventArgs());await Task.Delay(220);
            if(fillWindow||zoom!=1||panX!=0||panY!=0||!Equals(FitButton.Content,"适合窗口"))throw new InvalidOperationException("Dedicated fit button must leave fill mode");
            return new{scenario="fill-window",samples,manualZoomExits=true,fitRestores=true};
        }
        finally{rotation=oldRotation;AppWindow.Resize(saved);Fit();}
    }
    internal async Task VerificationRenderInfoAsync(string file)
    {
        // QA-only XAML render: a neutral solid base makes text/layout inspectable
        // without Windows monitor capture. This does not capture the FP16 photo
        // or validate the live Gaussian material / physical HDR brightness.
        var original=InfoPanel.Background;InfoPanel.Background=new SolidColorBrush(Root.ActualTheme==ElementTheme.Dark?Windows.UI.Color.FromArgb(255,40,40,40):Windows.UI.Color.FromArgb(255,247,247,247));
        try{
            await Task.Delay(280);var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(InfoPanel);
            var pixels=await bitmap.GetPixelsAsync();using var stream=File.OpenWrite(Path.Combine(App.DataDirectory,file));using var random=stream.AsRandomAccessStream();
            var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,random);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels.ToArray());await encoder.FlushAsync();
        }finally{InfoPanel.Background=original;}
    }
    internal async Task VerificationInfoAdvancedRenderAsync(){InfoMore.IsExpanded=true;await Task.Delay(200);InfoScroll.ChangeView(null,InfoScroll.ScrollableHeight,null,true);await Task.Delay(150);await VerificationRenderInfoAsync("details-more.png");InfoScroll.ChangeView(null,0,null,true);}
}
