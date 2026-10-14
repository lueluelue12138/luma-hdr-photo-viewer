using Luma.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Windowing;
using Windows.System;
using Microsoft.Windows.Storage.Pickers;
using Windows.UI;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Luma.App;
public sealed partial class ViewerWindow : Window
{
    private readonly FrameCache cache=FrameCache.Shared;private NativeRenderer? renderer;private FrameLease? currentLease;private int displayedRequest;private DecodedFrame? currentFrame=>current is not null&&displayedRequest==request?currentLease?.Frame:null;
    private List<Photo> sequence=[];private Photo? current;private int index,request,rotation;private bool closed,selecting,fullScreen,dragging,hover,originalHeld;
    private double zoom=1,panX,panY,startX,startY;private int menuDepth;private DateTime lastActivity=DateTime.UtcNow,keyboardUntil=DateTime.MinValue;
    private AppWindowPresenter? windowedPresenter;private Windows.Graphics.RectInt32 windowedBounds;private bool windowedMaximized;
    private CancellationTokenSource? loadingCancellation;private readonly CancellationTokenSource lifetime=new();
    private readonly DispatcherTimer idleTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private readonly DispatcherTimer displayTimer=new(){Interval=TimeSpan.FromMilliseconds(120)};
    private long lastDisplayProbe;private bool displayProbeRunning;
    private readonly string initial;private readonly IReadOnlyList<Photo>? initialSequence;private readonly Task<List<Photo>>? siblingEnumeration;private bool recovering;
    private bool hdrRequested,vsrRequested,updatingEnhancements;
    private bool controlsVisible=true,manuallyHidden,panMoved;
    private ScrollViewer? filmstripScroller;
    private bool leftNavigationHover,rightNavigationHover;
    private ViewerPreferences CapturePreferences()=>new(hdrRequested,vsrRequested,Math.Clamp(HdrPreset.SelectedIndex,0,2),AutoBrightnessSwitch.IsOn,AutoPeakSwitch.IsOn,(uint)MiddleGraySlider.Value,(uint)PeakSlider.Value);
    private async Task SavePreferencesAsync(){try{await ViewerPreferences.UpdateAsync(CapturePreferences());}catch(Exception ex){App.Log("Save viewer preferences: "+ex.Message);}}
    private CancellationTokenSource? tuningCancellation,enhancementCancellation;
    private (int Hdr,float White,float Peak)? displayState;
    private readonly DispatcherTimer viewTimer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    private readonly DispatcherTimer chromeTimer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    private double chromeOpacity=1,chromeStart=1,chromeTarget=1;private DateTime chromeStarted;
    private readonly Windows.UI.ViewManagement.UISettings uiSettings=new();
    private readonly Windows.UI.ViewManagement.AccessibilitySettings accessibilitySettings=new();
    private (bool Effects,bool Contrast,ElementTheme Theme,bool Ready)? glassMaterial;
    private bool glassUpdateScheduled;
    private readonly Dictionary<FrameworkElement,PopupMaterial> popupMaterials=new();
    private int popupGlassSnapshots;
    private sealed record PopupMaterial(GlassRect Rect,bool Dark,NativeRenderer Renderer,int Photo,double Zoom,double X,double Y,int Rotation,int Hdr,int Vsr,uint HdrCalls,uint VsrCalls);
    private static void PopupBackground(FrameworkElement child,Brush brush){if(child is Control control)control.Background=brush;else if(child is Border border)border.Background=brush;else if(child is Panel panel)panel.Background=brush;}
    private async void LoadPopupGlass(FrameworkElement child,PopupMaterial material)
    {
        try
        {
            uint w=(uint)Math.Clamp(Math.Ceiling(material.Rect.Width),1,2048),h=(uint)Math.Clamp(Math.Ceiling(material.Rect.Height),1,2048);
            var pixels=await material.Renderer.ReadGlassAsync(material.Rect,material.Dark,w,h,lifetime.Token);
            if(closed||!popupMaterials.TryGetValue(child,out var pending)||pending!=material)return;
            var bitmap=new WriteableBitmap((int)w,(int)h);using(var stream=bitmap.PixelBuffer.AsStream())stream.Write(pixels,0,pixels.Length);bitmap.Invalidate();
            PopupBackground(child,new ImageBrush{ImageSource=bitmap,Stretch=Stretch.Fill});popupGlassSnapshots++;
        }
        catch(OperationCanceledException){}catch(ObjectDisposedException){}catch(COMException ex){App.Log("Popup glass: "+ex.Message);popupMaterials.Remove(child);}
    }
    internal TaskCompletionSource VerificationReady {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task rendererInitialization=Task.CompletedTask;
    private Task fullResolution=Task.CompletedTask;private int fullResolutionRequest;
    internal TaskCompletionSource FirstFrameReady {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal long FirstFrameMilliseconds {get;private set;}
    internal RenderMetrics VerificationRenderMetrics=>renderer?.Metrics??default;
    internal Task VerificationPresentationAsync()=>renderer?.PresentPendingAsync(lifetime.Token)??Task.CompletedTask;
    internal Task VerificationShowFrameAsync(DecodedFrame frame)=>renderer!.ShowAsync(frame,lifetime.Token);
    internal (double Zoom,double X,double Y) VerificationView=>(zoom,panX,panY);
    internal string? VerificationPhoto=>current?.Path;
    internal bool VerificationErrorVisible=>ErrorPanel.Visibility==Visibility.Visible;
    internal FrameInfo? VerificationFrameInfo=>currentFrame?.Info;
    internal Task VerificationDetailAsync()=>EnsureFullResolutionAsync();
    internal void VerificationPan(double x,double y){panX=x;panY=y;UpdateView();}
    private uint PreviewEdge=>(uint)Math.Clamp(Math.Ceiling(Math.Max(Surface.ActualWidth*Surface.CompositionScaleX,Surface.ActualHeight*Surface.CompositionScaleY)/256)*256,1024,4096);
    internal async Task VerificationDisposeAsync(){try{await rendererInitialization;}catch(OperationCanceledException){}if(renderer is not null)await renderer.DisposedAsync;}
    internal RenderStatus VerificationStatus=>renderer?.Status()??default;
    internal int VerificationWorkerJobs=>renderer?.WorkerJobs??0;
    internal bool VerificationDeviceReused=>renderer?.DeviceReused??false;
    internal string VerificationStatusText=>StatusText.Text;
    internal void VerificationOriginal(bool value)=>SetOriginalHeld(value);
    internal async Task VerificationEffectsAsync(bool hdr,bool vsr){hdrRequested=hdr;vsrRequested=vsr;await ApplyEffectsAsync();}
    internal Task VerificationSelectAsync(int selected)=>DisplayAsync(selected);
    internal ViewerPreferences VerificationPreferences=>CapturePreferences();
    internal async Task VerificationPersistAsync(bool hdr,bool vsr){hdrRequested=hdr;vsrRequested=vsr;await SavePreferencesAsync();await ApplyEffectsAsync();}
    internal async Task VerificationScrollAsync()
    {
        // Initial selection can still have a queued BringIntoView after decoding.
        // Wait for that initial layout before exercising a user's list scroll.
        await Task.Delay(400);
        if(filmstripScroller is null||filmstripScroller.ScrollableWidth<100||FilmstripBar.Maximum<100||!FilmstripBar.IsEnabled)throw new InvalidOperationException("Filmstrip needs a real horizontal scroll range and an enabled scrollbar");
        int selected=index;double before=filmstripScroller.HorizontalOffset;bool scheduled=filmstripScroller.ChangeView(Math.Min(before+300,filmstripScroller.ScrollableWidth),null,null,false);await Task.Delay(700);
        if(index!=selected||filmstripScroller.HorizontalOffset<=before+20)throw new InvalidOperationException($"Filmstrip scroll: index {selected}->{index}, offset {before}->{filmstripScroller.HorizontalOffset}, scheduled {scheduled}, range {filmstripScroller.ScrollableWidth}");
        if(Math.Abs(FilmstripBar.Value-filmstripScroller.HorizontalOffset)>1)throw new InvalidOperationException("Native scrollbar did not track the list offset");
    }
    internal void VerificationZoom(double value){zoom=value;UpdateView();ShowControls();}
    internal async Task VerificationChromeAndMotionAsync()
    {
        void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
        ShowControls(true);ToggleControls();await Task.Delay(300);ShowControls();
        Check(manuallyHidden&&!controlsVisible&&chromeOpacity==0&&!TopPanel.IsHitTestVisible&&!BottomPanel.IsHitTestVisible,"Manual hiding must survive pointer/activity changes");
        ToggleControls();await Task.Delay(250);Check(!manuallyHidden&&controlsVisible&&chromeOpacity==1,"Second canvas click must restore controls");
        var selected=Filmstrip.ContainerFromIndex(index) as ListViewItem;selected?.ApplyTemplate();
        var outline=FindVisual<Border>(selected!,"SelectionOutline");
        Check(outline is not null&&outline.BorderBrush is SolidColorBrush brush&&brush.Color.A>0&&outline.ActualWidth>=92&&outline.ActualHeight>=68,"Selected thumbnail needs a visible, full-size accent outline");
        ZoomAt(Surface.ActualWidth*.65,Surface.ActualHeight*.55,120);double start=renderedZoom,target=zoom;
        if(Motion.Enabled){Check(viewTimer.IsEnabled&&start<target,"Wheel zoom must animate from the rendered frame");await Task.Delay(50);Check(renderedZoom>start&&renderedZoom<=target,"Wheel animation must progress toward its target");}
        await Task.Delay(250);Check(Math.Abs(renderedZoom-zoom)<.001,"Wheel animation must finish on target");
        for(int n=0;n<8;n++)ZoomAt(Surface.ActualWidth*.65,Surface.ActualHeight*.55,n%2==0?120:-120);
        await Task.Delay(250);Check(Math.Abs(renderedZoom-zoom)<.001&&double.IsFinite(renderedPanX)&&double.IsFinite(renderedPanY),"Repeated wheel input must converge without invalid pan");Fit();await Task.Delay(250);
        var p=AppWindow.Position;var s=AppWindow.Size;ToggleFullScreen();await Task.Delay(400);
        Check(fullScreen&&FullScreenIcon.Glyph=="\uE73F"&&AutomationProperties.GetName(FullScreenButton)=="退出全屏","Fullscreen needs its matching exit action");
        ToggleFullScreen();await Task.Delay(400);Check(!fullScreen&&FullScreenIcon.Glyph=="\uE740"&&AppWindow.Position==p&&AppWindow.Size==s,"Exiting fullscreen must restore bounds and action");
    }
    private static T? FindVisual<T>(DependencyObject parent,string name) where T:FrameworkElement
    {
        if(parent is null)return null;if(parent is T item&&item.Name==name)return item;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)if(FindVisual<T>(VisualTreeHelper.GetChild(parent,i),name) is {} found)return found;return null;
    }
    internal async Task VerificationPopupAsync()
    {
        int before=popupGlassSnapshots;EnhancementButton.Flyout.ShowAt(EnhancementButton);
        var timer=Stopwatch.StartNew();while(popupGlassSnapshots==before){if(timer.Elapsed>TimeSpan.FromSeconds(5))throw new InvalidOperationException("Popup did not receive its photo glass background");await Task.Delay(50);}
        if(EnhancementScroll.Content is StackPanel content)content.Children.OfType<Expander>().First().IsExpanded=true;
        await Task.Delay(350);EnhancementButton.Flyout.Hide();await Task.Delay(50);
    }
    private double renderedZoom=1,renderedPanX,renderedPanY,viewStartZoom,viewStartX,viewStartY;private DateTime viewStarted;
    public ViewerWindow(string path,IReadOnlyList<Photo>? photos=null,Window? owner=null)
    {
        initial=SupportedFormats.Normalize(path);initialSequence=photos;InitializeComponent();ExtendsContentIntoTitleBar=true;SetTitleBar(TitleDrag);SystemBackdrop=new DesktopAcrylicBackdrop();
        var options=ViewerPreferences.Current;updatingEnhancements=true;hdrRequested=options.Hdr;vsrRequested=options.Vsr;HdrSwitch.IsOn=hdrRequested;VsrSwitch.IsOn=vsrRequested;HdrPreset.SelectedIndex=options.Preset;AutoBrightnessSwitch.IsOn=options.AutoBrightness;AutoPeakSwitch.IsOn=options.AutoPeak;MiddleGraySlider.Value=options.MiddleGray;PeakSlider.Value=options.Peak;MiddleGraySlider.IsEnabled=!options.AutoBrightness;PeakSlider.IsEnabled=!options.AutoPeak;updatingEnhancements=false;
        Filmstrip.AddHandler(UIElement.PointerWheelChangedEvent,new PointerEventHandler(FilmstripWheel),true);
        EnhancementScroll.PreviewKeyDown+=ViewerPreviewKeyDown;EnhancementScroll.PreviewKeyUp+=ViewerPreviewKeyUp;
        foreach(var button in new[]{PreviousButton,NextButton}){button.Opacity=1;ElementCompositionPreview.GetElementVisual(button).Opacity=0;}
        FileTitle.Text=System.IO.Path.GetFileName(initial);Title=$"{FileTitle.Text} — Luma";Loading.IsActive=true;Loading.Visibility=Visibility.Visible;StatusText.Text="正在加载照片…";
        // Prepare known gallery items before the first composed frame, rather
        // than growing a tiny empty filmstrip after GPU initialization.
        // Placeholder metadata keeps slow/removable/network disk access out of
        // window construction. The decode lease supplies the file's real stamp.
        sequence=initialSequence?.ToList()??[new(initial,FileTitle.Text,0,0)];
        index=Math.Max(0,sequence.FindIndex(p=>string.Equals(p.Path,initial,StringComparison.OrdinalIgnoreCase)));
        selecting=true;Filmstrip.ItemsSource=sequence;Filmstrip.SelectedIndex=index;selecting=false;UpdatePosition();
        if(initialSequence is null){BottomPanel.Visibility=Visibility.Collapsed;siblingEnumeration=Task.Run(()=>FolderIndexer.Siblings(initial),lifetime.Token);}
        WindowLayout.Configure(this,Root);
        chromeTimer.Tick+=(_,_)=>{double t=Math.Clamp((DateTime.UtcNow-chromeStarted).TotalMilliseconds/(chromeTarget>chromeStart?180:240),0,1);chromeOpacity=chromeStart+(chromeTarget-chromeStart)*(1-Math.Pow(1-t,3));ApplyChrome();if(t>=1)chromeTimer.Stop();};
        foreach(var panel in new[]{TopPanel,BottomPanel,InfoPanel,ErrorPanel})panel.SizeChanged+=(_,_)=>ScheduleGlass();Root.ActualThemeChanged+=(_,_)=>ScheduleGlass();Root.LayoutUpdated+=(_,_)=>{if(filmstripScroller is null)UpdateFilmstripBar();ScheduleGlass();};
        viewTimer.Tick+=(_,_)=>{double t=Math.Clamp((DateTime.UtcNow-viewStarted).TotalMilliseconds/180,0,1);double ease=1-Math.Pow(1-t,3);renderedZoom=viewStartZoom+(zoom-viewStartZoom)*ease;renderedPanX=viewStartX+(panX-viewStartX)*ease;renderedPanY=viewStartY+(panY-viewStartY)*ease;try{renderer?.View(renderedZoom,renderedPanX,renderedPanY,rotation);}catch(COMException ex){viewTimer.Stop();Recover(ex);}if(t>=1)viewTimer.Stop();};
        WindowLayout.SizeForDpi(this,Root,1240,850,owner);AppWindow.TitleBar.ButtonBackgroundColor=Colors.Transparent;AppWindow.TitleBar.ButtonInactiveBackgroundColor=Colors.Transparent;
        displayTimer.Tick+=async(_,_)=>{displayTimer.Stop();await ProbeDisplayAsync(true);};
        idleTimer.Tick+=IdleTick;Closed+=(_,_)=>{closed=true;request++;lifetime.Cancel();loadingCancellation?.Cancel();VerificationReady.TrySetCanceled();idleTimer.Stop();displayTimer.Stop();viewTimer.Stop();chromeTimer.Stop();renderer?.Dispose();currentLease?.Dispose();};
        AppWindow.Changed+=(_,args)=>{if(args.DidPositionChange&&!closed){displayTimer.Stop();displayTimer.Start();}};Activated+=(_,args)=>{if(closed)return;if(args.WindowActivationState==WindowActivationState.Deactivated){dragging=false;InputLayer.ReleasePointerCaptures();SetOriginalHeld(false);}else if(renderer is not null)_=ProbeDisplayAsync(true);};
    }
    public void ApplyTheme(ElementTheme theme){if(!closed)Root.RequestedTheme=theme;}
    private async void SurfaceLoaded(object sender,RoutedEventArgs e)
    {
        if(closed)return;
        Surface.Loaded-=SurfaceLoaded;App.Log("Viewer loaded");if(siblingEnumeration is not null)_=LoadSiblingsAsync();
        // Decode, build the render device and prepare the first XAML frame in
        // parallel. Previously these three waits were added serially.
        var firstDecode=PrimeInitialPhotoAsync();rendererInitialization=CreateRendererAsync();await WindowLayout.RevealAsync(this,Root);if(closed)return;
        try{await Task.WhenAll(rendererInitialization,firstDecode);}catch(OperationCanceledException){return;}catch(Exception ex){if(!closed){VerificationReady.TrySetException(ex);Fail(ex);}return;}
        if(closed)return;
        idleTimer.Start();InputLayer.Focus(FocusState.Programmatic);
        await DisplayAsync(index);
        if(!closed)InputLayer.Focus(FocusState.Programmatic);
    }
    private async Task PrimeInitialPhotoAsync()
    {
        try{using var lease=await cache.GetAsync(initial,lifetime.Token,PreviewEdge);}
        catch(Exception ex)when(ex is COMException or IOException or UnauthorizedAccessException){App.Log("Initial preview: "+ex.Message);}
    }
    private async Task LoadSiblingsAsync()
    {
        // Enumeration runs alongside initialization and decoding, never gates the
        // selected photo. Reveal the filmstrip at its prepared size in one step.
        try{var siblings=await siblingEnumeration!;if(closed)return;var path=current?.Path??initial;if(siblings.Count>0){if(!siblings.Any(p=>string.Equals(p.Path,path,StringComparison.OrdinalIgnoreCase)))siblings.Insert(0,current??sequence[index]);sequence=siblings;}index=Math.Max(0,sequence.FindIndex(p=>string.Equals(p.Path,path,StringComparison.OrdinalIgnoreCase)));selecting=true;Filmstrip.ItemsSource=sequence;Filmstrip.SelectedIndex=index;selecting=false;UpdatePosition();BottomPanel.Visibility=Visibility.Visible;if(currentFrame is not null)Prefetch();}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or OperationCanceledException){App.Log(ex.Message);if(!closed)BottomPanel.Visibility=Visibility.Visible;}
    }
    private async Task CreateRendererAsync(){renderer?.Dispose(false);renderer=null;glassMaterial=null;var next=await NativeRenderer.CreateAsync(Surface,WinRT.Interop.WindowNative.GetWindowHandle(this),lifetime.Token,hdrRequested||vsrRequested);if(closed){next.Dispose();return;}renderer=next;renderer.Faulted+=ex=>{if(renderer==next&&!closed)Recover(ex);};renderer.Presented+=()=>{if(renderer==next&&!closed){UpdateGlass();RefreshStatus(false);}};renderer.HoldOriginal(originalHeld);renderer.Resize(Surface.ActualWidth,Surface.ActualHeight,Surface.CompositionScaleX);UpdateGlass();App.Log($"Viewer renderer attached; reused={next.DeviceReused}");}
    private async Task DisplayAsync(int selected)
    {
        if(closed||sequence.Count==0||selected<0||selected>=sequence.Count)return;
        if(renderer is null){index=selected;return;}
        index=selected;current=sequence[index];int generation=++request;enhancementCancellation?.Cancel();loadingCancellation?.Cancel();loadingCancellation?.Dispose();loadingCancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=loadingCancellation.Token;
        var timing=Stopwatch.StartNew();uint previewEdge=PreviewEdge;
        viewTimer.Stop();zoom=1;panX=panY=0;rotation=0;FileTitle.Text=current.Name;Title=$"{current.Name} — Luma";ErrorPanel.Visibility=Visibility.Collapsed;Loading.IsActive=false;Loading.Visibility=Visibility.Collapsed;_ = ShowLoadingAfterDelayAsync(generation,token);ShowControls();UpdatePosition();UpdateInfo();
        try
        {
            var photo=current;var lease=await cache.GetAsync(photo.Path,token,previewEdge);if(closed||generation!=request){lease.Dispose();return;}
            // Install known panel geometry before the upload's first Present.
            // Keep a readable fallback until that composed frame is ready.
            UpdateGlass();
            try{await renderer!.ShowAsync(lease.Frame,token);if(closed||generation!=request){lease.Dispose();return;}}catch{lease.Dispose();throw;}
            photo=photo with{Size=lease.SourceLength,Modified=lease.SourceModified};current=photo;
            currentLease?.Dispose();currentLease=lease;displayedRequest=generation;UpdateView();UpdateGlass();cache.Trim(photo.Path);Loading.IsActive=false;Loading.Visibility=Visibility.Collapsed;
            FirstFrameMilliseconds=timing.ElapsedMilliseconds;FirstFrameReady.TrySetResult();App.Log($"Photo first frame: {photo.Name}; {FirstFrameMilliseconds} ms; {lease.Frame.Info.Width}x{lease.Frame.Info.Height}; original {lease.Frame.Info.OriginalWidth}x{lease.Frame.Info.OriginalHeight}");
            Prefetch();await ApplyEffectsAsync();if(closed||generation!=request)return;
            var stored=await App.Store.IsFavoriteAsync(photo.Path);if(generation!=request)return;current=photo with{Favorite=stored};UpdateFavorite();
            await App.Store.RecordOpenAsync(current);if(closed||generation!=request)return;RefreshStatus();UpdateInfo();VerificationReady.TrySetResult();
        }
        catch(OperationCanceledException){}catch(Exception ex){if(generation==request&&!closed){VerificationReady.TrySetException(ex);Fail(ex);}}
        finally{if(generation==request&&!closed){Loading.IsActive=false;Loading.Visibility=Visibility.Collapsed;}}
    }
    private async Task ShowLoadingAfterDelayAsync(int generation,CancellationToken token)
    {
        try{await Task.Delay(180,token);if(!closed&&generation==request&&displayedRequest!=generation){Loading.IsActive=true;Loading.Visibility=Visibility.Visible;}}catch(OperationCanceledException){}
    }
    private Task EnsureFullResolutionAsync()
    {
        if(currentFrame?.Info.Downsampled!=2||renderer is null||closed)return Task.CompletedTask;
        if(fullResolutionRequest==request&&!fullResolution.IsCompleted)return fullResolution;
        fullResolutionRequest=request;return fullResolution=LoadFullResolutionAsync(request,current!.Path,renderer,loadingCancellation?.Token??lifetime.Token);
    }
    private async Task LoadFullResolutionAsync(int generation,string path,NativeRenderer active,CancellationToken token)
    {
        FrameLease? lease=null;
        try
        {
            var timer=Stopwatch.StartNew();lease=await cache.GetAsync(path,token,0,true);token.ThrowIfCancellationRequested();if(closed||generation!=request||active!=renderer)return;
            UpdateGlass();await active.ShowAsync(lease.Frame,token,renderedZoom,renderedPanX,renderedPanY,rotation);if(closed||generation!=request||active!=renderer)return;
            currentLease?.Dispose();currentLease=lease;lease=null;UpdateView();UpdateGlass();cache.Trim(path);App.Log($"Photo full detail: {System.IO.Path.GetFileName(path)}; {timer.ElapsedMilliseconds} ms");
        }
        finally{lease?.Dispose();}
    }
    private async void RequestDetail()
    {
        if(currentFrame is not {} frame||frame.Info.Downsampled!=2||PixelScale()<=frame.Info.Width/(double)frame.Info.OriginalWidth*.95)return;
        if(fullResolutionRequest==request&&!fullResolution.IsCompleted)return;
        try{await EnsureFullResolutionAsync();if(!closed)await ApplyEffectsAsync();}catch(OperationCanceledException){}catch(ObjectDisposedException){}catch(Exception ex){App.Log("Photo detail: "+ex.Message);}
    }
    private async void Prefetch()
    {
        var token=loadingCancellation?.Token??lifetime.Token;var path=current?.Path;if(path is null)return;
        foreach(var adjacent in new[]{index+1,index-1})
        {
            if(adjacent<0||adjacent>=sequence.Count||token.IsCancellationRequested)continue;
            try{using var lease=await cache.GetAsync(sequence[adjacent].Path,token,PreviewEdge,true);if(!closed&&current is not null)cache.Trim(current.Path);}catch(Exception ex)when(ex is IOException or COMException or OperationCanceledException or ObjectDisposedException){ }
        }
    }
    private void UpdatePosition(){PositionText.Text=$"{index+1} / {sequence.Count}";PreviousButton.IsEnabled=index>0;NextButton.IsEnabled=index<sequence.Count-1;selecting=true;Filmstrip.SelectedIndex=index;if(current is not null)Filmstrip.ScrollIntoView(sequence.ElementAtOrDefault(index));selecting=false;}
    private void ThumbnailSelected(object sender,SelectionChangedEventArgs e){if(!selecting&&Filmstrip.SelectedIndex>=0)_=DisplayAsync(Filmstrip.SelectedIndex);}
    private void Move(int delta){if(index+delta>=0&&index+delta<sequence.Count)_=DisplayAsync(index+delta);}
    private void PreviousClick(object sender,RoutedEventArgs e)=>Move(-1);
    private void NextClick(object sender,RoutedEventArgs e)=>Move(1);
    private static T? FindChild<T>(DependencyObject parent)where T:DependencyObject
    {
        for(int n=0;n<VisualTreeHelper.GetChildrenCount(parent);n++){var child=VisualTreeHelper.GetChild(parent,n);if(child is T match)return match;if(FindChild<T>(child) is {} nested)return nested;}return null;
    }
    private void FilmstripLoaded(object sender,RoutedEventArgs e)
    {
        UpdateFilmstripBar();
    }
    private void FilmstripViewChanged(object? sender,ScrollViewerViewChangedEventArgs e)=>UpdateFilmstripBar();
    private void FilmstripSizeChanged(object sender,SizeChangedEventArgs e)=>UpdateFilmstripBar();
    private void UpdateFilmstripBar()
    {
        if(closed||Filmstrip.XamlRoot is null)return;
        // Direct file activation initially hides the list. Its Loaded event can
        // precede the ScrollViewer template, and the template can be replaced.
        // Rebind to the current visual tree after layout, without duplicate hooks.
        var active=filmstripScroller??FindChild<ScrollViewer>(Filmstrip);
        if(!ReferenceEquals(active,filmstripScroller))
        {
            if(filmstripScroller is not null){filmstripScroller.ViewChanged-=FilmstripViewChanged;filmstripScroller.SizeChanged-=FilmstripSizeChanged;}
            filmstripScroller=active;
            if(active is not null){active.ViewChanged+=FilmstripViewChanged;active.SizeChanged+=FilmstripSizeChanged;}
        }
        if(filmstripScroller is null)return;
        if(Math.Abs(FilmstripBar.Maximum-filmstripScroller.ScrollableWidth)>.01)FilmstripBar.Maximum=filmstripScroller.ScrollableWidth;
        if(Math.Abs(FilmstripBar.ViewportSize-filmstripScroller.ViewportWidth)>.01)FilmstripBar.ViewportSize=filmstripScroller.ViewportWidth;
        if(Math.Abs(FilmstripBar.Value-filmstripScroller.HorizontalOffset)>.01)FilmstripBar.Value=filmstripScroller.HorizontalOffset;
        FilmstripBar.LargeChange=Math.Max(88,filmstripScroller.ViewportWidth*.8);FilmstripBar.SmallChange=96;FilmstripBar.IsEnabled=filmstripScroller.ScrollableWidth>0;
    }
    private void FilmstripBarScroll(object sender,Microsoft.UI.Xaml.Controls.Primitives.ScrollEventArgs e)
    {
        filmstripScroller?.ChangeView(e.NewValue,null,null,e.ScrollEventType==Microsoft.UI.Xaml.Controls.Primitives.ScrollEventType.ThumbTrack);ShowControls();
    }
    private void FilmstripWheel(object sender,PointerRoutedEventArgs e)
    {
        UpdateFilmstripBar();
        if(filmstripScroller is null)return;
        var point=e.GetCurrentPoint(Filmstrip);double delta=point.Properties.MouseWheelDelta;
        if(point.Properties.IsHorizontalMouseWheel)delta=-delta;
        filmstripScroller.ChangeView(Math.Clamp(filmstripScroller.HorizontalOffset-delta*1.8,0,filmstripScroller.ScrollableWidth),null,null,false);ShowControls();e.Handled=true;
    }
    private void NavigationEntered(object sender,PointerRoutedEventArgs e)
    {
        bool left=ReferenceEquals(sender,LeftNavigationZone);if(left)leftNavigationHover=true;else rightNavigationHover=true;
        var button=left?PreviousButton:NextButton;Motion.Visibility(button,button.IsEnabled,0);ShowControls();
    }
    private void NavigationExited(object sender,PointerRoutedEventArgs e)
    {
        bool left=ReferenceEquals(sender,LeftNavigationZone);if(left)leftNavigationHover=false;else rightNavigationHover=false;
        Motion.Visibility(left?PreviousButton:NextButton,false,0);
    }
    private void UpdateView(bool animate=false)
    {
        ClampPan();
        try{viewTimer.Stop();if(animate&&Motion.Enabled){viewStartZoom=renderedZoom;viewStartX=renderedPanX;viewStartY=renderedPanY;viewStarted=DateTime.UtcNow;viewTimer.Start();}else{renderedZoom=zoom;renderedPanX=panX;renderedPanY=panY;renderer?.View(zoom,panX,panY,rotation);}var actual=PixelScale();FitButton.Content=Math.Abs(zoom-1)<0.001?"适合窗口":$"{actual*100:0}%";UpdateInfo();RequestDetail();}catch(COMException ex){Recover(ex);}
    }
    private void ClampPan()
    {
        if(currentFrame is null||zoom<=1){panX=panY=0;return;}
        var i=currentFrame.Info;double w=rotation%2==0?i.OriginalWidth:i.OriginalHeight,h=rotation%2==0?i.OriginalHeight:i.OriginalWidth,scale=FitScale()*zoom;
        double maxX=Math.Max(0,(w*scale-Surface.ActualWidth*Surface.CompositionScaleX)/2),maxY=Math.Max(0,(h*scale-Surface.ActualHeight*Surface.CompositionScaleY)/2);
        panX=Math.Clamp(panX,-maxX,maxX);panY=Math.Clamp(panY,-maxY,maxY);
    }
    private double FitScale(){if(currentFrame is null)return 1;var i=currentFrame.Info;double w=rotation%2==0?i.OriginalWidth:i.OriginalHeight,h=rotation%2==0?i.OriginalHeight:i.OriginalWidth;return Math.Min(Surface.ActualWidth*Surface.CompositionScaleX/w,Surface.ActualHeight*Surface.CompositionScaleY/h);}
    private double PixelScale()=>FitScale()*zoom;
    private void ZoomBy(double factor){zoom=Math.Clamp(zoom*factor,0.05,64);ShowControls();UpdateView(true);}
    private void Fit(){zoom=1;panX=panY=0;UpdateView(true);ShowControls();}
    private void ActualSize(){zoom=Math.Clamp(1/FitScale(),0.05,64);panX=panY=0;UpdateView(true);ShowControls();}
    private void FitClick(object sender,RoutedEventArgs e)=>Fit();private void ActualSizeClick(object sender,RoutedEventArgs e)=>ActualSize();
    private void ZoomInClick(object sender,RoutedEventArgs e)=>ZoomBy(1.25);private void ZoomOutClick(object sender,RoutedEventArgs e)=>ZoomBy(0.8);
    private void RotateClick(object sender,RoutedEventArgs e){rotation=(rotation+1)%4;panX=panY=0;UpdateView();ShowControls();}
    private void CanvasWheel(object sender,PointerRoutedEventArgs e)
    {
        var p=e.GetCurrentPoint(InputLayer);ZoomAt(p.Position.X,p.Position.Y,p.Properties.MouseWheelDelta);ShowControls();e.Handled=true;
    }
    private void ZoomAt(double pointerX,double pointerY,int delta)
    {
        double old=zoom;zoom=Math.Clamp(zoom*Math.Pow(1.0015,delta),0.05,64);double ratio=zoom/old;
        double x=(pointerX-Surface.ActualWidth/2)*Surface.CompositionScaleX,y=(pointerY-Surface.ActualHeight/2)*Surface.CompositionScaleY;
        panX=x-(x-panX)*ratio;panY=y-(y-panY)*ratio;UpdateView(true);
    }
    private void CanvasPressed(object sender,PointerRoutedEventArgs e)
    {
        var p=e.GetCurrentPoint(InputLayer);InputLayer.Focus(FocusState.Pointer);panMoved=false;
        if(!p.Properties.IsLeftButtonPressed)return;
        // Start dragging from the currently visible frame, including a wheel
        // animation in flight, instead of jumping to its unfinished target.
        viewTimer.Stop();zoom=renderedZoom;panX=renderedPanX;panY=renderedPanY;
        if(zoom<=1)return;dragging=InputLayer.CapturePointer(e.Pointer);startX=p.Position.X;startY=p.Position.Y;e.Handled=dragging;
    }
    private void CanvasMoved(object sender,PointerRoutedEventArgs e){if(!dragging)return;var p=e.GetCurrentPoint(InputLayer);double dx=p.Position.X-startX,dy=p.Position.Y-startY;if(!panMoved&&Math.Abs(dx)+Math.Abs(dy)<4)return;panMoved=true;panX+=dx*Surface.CompositionScaleX;panY+=dy*Surface.CompositionScaleY;startX=p.Position.X;startY=p.Position.Y;UpdateView();ShowControls();e.Handled=true;}
    private void CanvasReleased(object sender,PointerRoutedEventArgs e){dragging=false;InputLayer.ReleasePointerCapture(e.Pointer);e.Handled=true;}
    private void CanvasTapped(object sender,TappedRoutedEventArgs e){if(!panMoved)ToggleControls();e.Handled=true;}
    private void ToggleControls(){manuallyHidden=controlsVisible;controlsVisible=!controlsVisible;lastActivity=DateTime.UtcNow;hover=false;AnimateChrome(controlsVisible);}
    private void CanvasDoubleTapped(object sender,DoubleTappedRoutedEventArgs e){if(Math.Abs(zoom-1)<0.001)ActualSize();else Fit();e.Handled=true;}
    private async void FavoriteClick(object sender,RoutedEventArgs e){if(current is null)return;var photo=current;int generation=request;try{bool favorite=await App.Store.ToggleFavoriteAsync(photo);if(closed||generation!=request)return;current=photo with{Favorite=favorite};UpdateFavorite();ShowControls();}catch(Exception ex){if(!closed&&generation==request)Fail(ex);}}
    private void UpdateFavorite(){FavoriteIcon.Glyph=current?.Favorite==true?"\uEB52":"\uEB51";FavoriteButton.SetValue(AutomationProperties.NameProperty,current?.Favorite==true?"取消收藏":"收藏");}
    private void FullScreenClick(object sender,RoutedEventArgs e)=>ToggleFullScreen();
    private void ToggleFullScreen()
    {
        double previousWidth=Root.ActualWidth,previousHeight=Root.ActualHeight;
        if(!fullScreen)
        {
            windowedPresenter=AppWindow.Presenter;
            windowedMaximized=windowedPresenter is OverlappedPresenter {State:OverlappedPresenterState.Maximized};
            var p=AppWindow.Position;var s=AppWindow.Size;windowedBounds=new(p.X,p.Y,s.Width,s.Height);
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);fullScreen=true;
        }
        else
        {
            if(windowedPresenter is not null)AppWindow.SetPresenter(windowedPresenter);else AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            fullScreen=false;
            if(AppWindow.Presenter is OverlappedPresenter presenter)
            {
                if(windowedMaximized)presenter.Maximize();else{presenter.Restore();AppWindow.MoveAndResize(windowedBounds);}
            }
            windowedPresenter=null;
        }
        TitleRow.Height=new GridLength(fullScreen?0:44);
        FullScreenIcon.Glyph=fullScreen?"\uE73F":"\uE740";string label=fullScreen?"退出全屏":"全屏";
        ToolTipService.SetToolTip(FullScreenButton,$"{label}（F / F11）");AutomationProperties.SetName(FullScreenButton,label);
        DispatcherQueue.TryEnqueue(()=>{if(!closed)Motion.Resize(Root,previousWidth,previousHeight);});ShowControls();InputLayer.Focus(FocusState.Programmatic);
    }
    private void BackClick(object sender,RoutedEventArgs e){App.ShowGallery();Close();}
    private void SurfaceSizeChanged(object sender,SizeChangedEventArgs e){if(closed)return;EnhancementScroll.MaxHeight=Math.Max(160,Canvas.ActualHeight-116);ZoomOutButton.Visibility=ZoomInButton.Visibility=Canvas.ActualWidth<570?Visibility.Collapsed:Visibility.Visible;BottomPanel.MaxWidth=Math.Min(920,Math.Max(240,Canvas.ActualWidth-72));Filmstrip.MaxWidth=Math.Min(820,Math.Max(120,Canvas.ActualWidth-158));if(renderer is null)return;try{renderer.Resize(Surface.ActualWidth,Surface.ActualHeight,Surface.CompositionScaleX);UpdateView();}catch(COMException ex){Recover(ex);}}
    private void ScaleChanged(SwapChainPanel sender,object args){if(renderer is null||closed)return;try{renderer.Resize(Surface.ActualWidth,Surface.ActualHeight,Surface.CompositionScaleX);UpdateView();}catch(COMException ex){Recover(ex);}}
    private async void Recover(Exception reason)
    {
        if(recovering||closed)return;recovering=true;App.Log("Recovering renderer: "+reason.Message);
        try{enhancementCancellation?.Cancel();await CreateRendererAsync();if(closed)return;if(currentFrame is not null){await renderer!.ShowAsync(currentFrame,lifetime.Token);await ApplyEffectsAsync();}else if(current is not null)await DisplayAsync(index);if(!closed){renderer!.View(zoom,panX,panY,rotation);UpdateGlass();RefreshStatus();}}catch(OperationCanceledException){}catch(Exception ex){if(!closed)Fail(ex);}finally{recovering=false;}
    }
    private void RefreshStatus(bool probe=true)
    {
        if(renderer is null||closed)return;
        try
        {
            var s=probe?renderer.Status():renderer.CachedStatus;bool photoReady=currentFrame is not null;bool busy=renderer.Busy||(!photoReady&&Loading.IsActive);updatingEnhancements=true;
            HdrReasonText.Text=!photoReady?(Loading.IsActive?"正在加载照片…":"载入照片后可调整增强"):s.NativeHdr!=0||hdrRequested?s.HdrReason:s.HdrAvailable!=0?"TrueHDR 可用，未开启":s.HdrReason;
            VsrReasonText.Text=!photoReady?(Loading.IsActive?"正在加载照片…":"载入照片后可调整增强"):s.NativeHdr!=0?"原生 HDR 保留原始细节，暂不应用 VSR":vsrRequested?s.VsrMessage:s.VsrAvailable!=0?"VSR 可用，默认关闭":s.VsrMessage;
            HdrSwitch.IsOn=hdrRequested;VsrSwitch.IsOn=vsrRequested;
            HdrSwitch.IsEnabled=photoReady&&s.HdrAvailable!=0&&s.HdrActive!=0&&s.NativeHdr==0;
            VsrSwitch.IsEnabled=photoReady&&s.VsrAvailable!=0&&s.NativeHdr==0;
            HdrPreset.IsEnabled=photoReady&&s.NativeHdr==0;
            OriginalButton.Content=s.NativeHdr==2?"按住查看 SDR 底图（空格）":"按住查看原图（空格）";
            HdrSwitch.OnContent=!photoReady?(Loading.IsActive?"加载中":"未生效"):s.NativeHdr!=0?"无需增强":busy?"处理中":originalHeld?"查看原图":s.HdrEffective!=0?"已生效":"未生效";
            VsrSwitch.OnContent=!photoReady?(Loading.IsActive?"加载中":"未生效"):s.NativeHdr!=0?"不适用":busy?"处理中":originalHeld?"查看原图":s.VsrEffective!=0?"已生效":"未生效";
            HdrSwitch.OffContent=HdrSwitch.IsEnabled?"已关闭":"不可用";
            VsrSwitch.OffContent=VsrSwitch.IsEnabled?"已关闭":"不可用";
            double peak=AutoPeakSwitch.IsOn?Math.Floor(Math.Clamp(s.MaxNits>=400?s.MaxNits:1000,400,2000)):PeakSlider.Value;
            double gray=AutoBrightnessSwitch.IsOn?Math.Floor(Math.Clamp(s.SdrWhite*80*.21586,50,100)):MiddleGraySlider.Value;
            if(AutoPeakSwitch.IsOn)PeakSlider.Value=peak;if(AutoBrightnessSwitch.IsOn)MiddleGraySlider.Value=gray;
            string brightness=s.NativeHdr!=0?$"屏幕峰值：{s.MaxNits:0} nits\n{(s.NativeHdr==2?"增益图参考白：203 nits":"线性 scRGB 原图")}":$"增强峰值 / 中间灰：{peak:0} / {gray:0} nits";
            DisplayText.Text=$"{s.Gpu}\nWindows HDR：{(s.HdrActive!=0?"已开启":"未开启")}\nFP16 / scRGB\nSDR 白色：{s.SdrWhite*80:0} nits\n{brightness}\nHDR / VSR 调用：{s.HdrEvaluations} / {s.VsrEvaluations}\n图片与画布显存：{s.GpuBytes/1048576.0:0} MB";
            StatusText.Text=!photoReady?(Loading.IsActive?"正在加载照片…":"照片未加载"):s.NativeHdr==2?(originalHeld?"SDR 底图 · 对照":s.HdrActive!=0?"原生 HDR · 增益图 JPEG":"SDR 底图 · 当前屏幕 SDR"):s.NativeHdr!=0?(s.HdrActive!=0?"原生 HDR · scRGB":"HDR 原图 · 当前屏幕 SDR"):busy&&(hdrRequested||vsrRequested)?"正在处理显示增强…":originalHeld?"原图 · 未应用增强":s.HdrEffective!=0?(s.VsrEffective!=0?"RTX HDR + VSR 已生效":"RTX HDR 已生效"):s.VsrEffective!=0?"VSR 已生效 · SDR":"SDR · 原图";
            updatingEnhancements=false;
            var next=(s.HdrActive,s.SdrWhite,s.MaxNits);bool changed=displayState.HasValue&&displayState.Value!=next;displayState=next;
            if(changed&&hdrRequested&&!busy)_=ApplyEffectsAsync();
        }
        catch(COMException ex){updatingEnhancements=false;Recover(ex);}
    }
    private async Task ApplyEffectsAsync()
    {
        if(renderer is null||currentFrame is null||closed)return;
        enhancementCancellation?.Cancel();enhancementCancellation?.Dispose();enhancementCancellation=CancellationTokenSource.CreateLinkedTokenSource(loadingCancellation?.Token??lifetime.Token,lifetime.Token);var token=enhancementCancellation.Token;int generation=request;var active=renderer;
        uint gray=AutoBrightnessSwitch.IsOn?0:(uint)MiddleGraySlider.Value,peak=AutoPeakSwitch.IsOn?0:(uint)PeakSlider.Value;int preset=Math.Max(0,HdrPreset.SelectedIndex);
        try
        {
            StatusText.Text=hdrRequested||vsrRequested?"正在处理显示增强…":"SDR · 原图";
            if((hdrRequested||vsrRequested)&&currentFrame.Info.NativeHdr==0)
            {
                await active.InitializeEffectsAsync(token);var support=active.Status();
                if((hdrRequested&&support.HdrAvailable!=0&&support.HdrActive!=0)||(vsrRequested&&support.VsrAvailable!=0))await EnsureFullResolutionAsync().WaitAsync(token);
                if(closed||generation!=request||active!=renderer)return;
            }
            await active.EffectsAsync(hdrRequested,vsrRequested,preset,gray,peak,token);if(closed||generation!=request||active!=renderer||token.IsCancellationRequested)return;UpdateView();await active.PresentPendingAsync(token);RefreshStatus();
        }
        catch(OperationCanceledException){}catch(ObjectDisposedException){}catch(COMException ex){if(!closed&&generation==request)Recover(ex);}catch(Exception ex){App.Log(ex.ToString());if(!closed&&generation==request)RefreshStatus();}
    }
    private void ApplyEffects()=>_=ApplyEffectsAsync();
    private async void TuningChanged(object sender,RoutedEventArgs e)
    {
        if(renderer is null||updatingEnhancements)return;MiddleGraySlider.IsEnabled=!AutoBrightnessSwitch.IsOn;PeakSlider.IsEnabled=!AutoPeakSwitch.IsOn;tuningCancellation?.Cancel();tuningCancellation?.Dispose();tuningCancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=tuningCancellation.Token;
        try{await SavePreferencesAsync();await Task.Delay(220,token);if(!closed)ApplyEffects();}catch(OperationCanceledException){}catch(Exception ex){App.Log(ex.Message);}
    }
    private void TuningValueChanged(object sender,Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)=>TuningChanged(sender,new RoutedEventArgs());
    private async void EnhancementToggled(object sender,RoutedEventArgs e){if(updatingEnhancements||renderer is null)return;hdrRequested=HdrSwitch.IsOn;vsrRequested=VsrSwitch.IsOn;RefreshStatus(false);await SavePreferencesAsync();if(closed)return;try{ApplyEffects();}catch(Exception ex){App.Log(ex.Message);HdrReasonText.Text=ex.Message;RefreshStatus();}ShowControls();}
    private async void PresetChanged(object sender,SelectionChangedEventArgs e){if(renderer is null||updatingEnhancements)return;await SavePreferencesAsync();if(closed)return;try{ApplyEffects();}catch(Exception ex){App.Log(ex.Message);}}
    private void UpdateInfo(){if(current is null)return;var i=currentFrame?.Info;InfoText.Text=$"{current.Name}\n\n{(i is null?"":$"{i.Value.OriginalWidth} × {i.Value.OriginalHeight} 像素")}\n{current.Size/1048576.0:0.00} MB\n缩放 {PixelScale()*100:0}%\n旋转 {rotation*90}°\n{(i?.Downsampled==1?"大图已按内存预算缩小解码\n":"")}\n{current.Path}";}
    private void InfoClick(object sender,RoutedEventArgs e){InfoPanel.Visibility=InfoPanel.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;ShowControls();UpdateInfo();UpdateGlass();}
    private void CloseInfoClick(object sender,RoutedEventArgs e){InfoPanel.Visibility=Visibility.Collapsed;UpdateGlass();}
    private void RevealClick(object sender,RoutedEventArgs e){if(current is not null)Process.Start(new ProcessStartInfo("explorer.exe",$"/select,\"{current.Path}\""){UseShellExecute=true});}
    private async void OpenClick(object sender,RoutedEventArgs e)
    {
        try{var picker=new FileOpenPicker(AppWindow.Id);foreach(var ext in SupportedFormats.Extensions)picker.FileTypeFilter.Add(ext);var f=await picker.PickSingleFileAsync();if(f is not null){new ViewerWindow(f.Path).Activate();Close();}}catch(Exception ex){Fail(ex);}
    }
    private void Fail(Exception ex){App.Log(ex.ToString());ErrorText.Text=ex.Message;ErrorPanel.Visibility=Visibility.Visible;Loading.IsActive=false;Loading.Visibility=Visibility.Collapsed;ShowControls();}
    private void ShowControls(bool force=false){lastActivity=DateTime.UtcNow;if(force)manuallyHidden=false;if(manuallyHidden)return;if(!controlsVisible){controlsVisible=true;AnimateChrome(true);}}
    private void AnimateChrome(bool visible)
    {
        chromeTimer.Stop();chromeStart=chromeOpacity;chromeTarget=visible?1:0;chromeStarted=DateTime.UtcNow;TopPanel.IsHitTestVisible=BottomPanel.IsHitTestVisible=visible;
        if(Motion.Enabled)chromeTimer.Start();else{chromeOpacity=chromeTarget;ApplyChrome();}
    }
    private void ApplyChrome()
    {
        TopPanel.Opacity=BottomPanel.Opacity=StatusBadge.Opacity=chromeOpacity;
        TopPanel.RenderTransform=new TranslateTransform{Y=-8*(1-chromeOpacity)};BottomPanel.RenderTransform=new TranslateTransform{Y=10*(1-chromeOpacity)};
        UpdateGlass();
    }
    private void UpdateGlass()
    {
        if(renderer is null||closed)return;
        bool effects=uiSettings.AdvancedEffectsEnabled&&!accessibilitySettings.HighContrast;
        bool ready=renderer.Metrics.BlurBuilds>0;var material=(effects,accessibilitySettings.HighContrast,Root.ActualTheme,ready);
        if(glassMaterial!=material){glassMaterial=material;var fill=effects&&ready?Colors.Transparent:accessibilitySettings.HighContrast?uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background):Root.ActualTheme==ElementTheme.Dark?Color.FromArgb(255,40,40,40):Color.FromArgb(255,247,247,247);foreach(var panel in new[]{TopPanel,BottomPanel,InfoPanel,ErrorPanel})panel.Background=new SolidColorBrush(fill);}
        var rects=new List<GlassRect>();double dpi=Surface.CompositionScaleX;
        if(effects)foreach(var panel in new[]{TopPanel,BottomPanel,InfoPanel,ErrorPanel})
        {
            double opacity=panel==TopPanel||panel==BottomPanel?chromeOpacity:1;
            if(panel.Visibility!=Visibility.Visible||panel.ActualWidth<=0||panel.ActualHeight<=0||opacity<.001)continue;
            var p=panel.TransformToVisual(Canvas).TransformPoint(new(0,0));rects.Add(new((float)(p.X*dpi),(float)(p.Y*dpi),(float)(panel.ActualWidth*dpi),(float)(panel.ActualHeight*dpi),(float)(panel.CornerRadius.TopLeft*dpi),(float)opacity));
        }
        var popups=Root.XamlRoot is null||(menuDepth==0&&popupMaterials.Count==0)?[]:VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).ToArray();
        var children=popups.Select(p=>p.Child).OfType<FrameworkElement>().ToHashSet();foreach(var old in popupMaterials.Keys.Where(c=>!children.Contains(c)).ToArray())popupMaterials.Remove(old);
        foreach(var popup in popups)
        {
            if(popup.Child is not FrameworkElement child||child.ActualWidth<=0||child.ActualHeight<=0||rects.Count>=8)continue;
            try
            {
                if(effects)
                {
                    var p=child.TransformToVisual(Canvas).TransformPoint(new(0,0));var rect=new GlassRect((float)(p.X*dpi),(float)(p.Y*dpi),(float)(child.ActualWidth*dpi),(float)(child.ActualHeight*dpi),12*(float)dpi,1);rects.Add(rect);
                    var status=renderer.Status();var popupMaterial=new PopupMaterial(rect,Root.ActualTheme==ElementTheme.Dark,renderer,request,renderedZoom,renderedPanX,renderedPanY,rotation,status.HdrEffective,status.VsrEffective,status.HdrEvaluations,status.VsrEvaluations);
                    if(!popupMaterials.TryGetValue(child,out var previous)||previous!=popupMaterial){if(previous is null)PopupBackground(child,(Brush)Application.Current.Resources["FloatingBrush"]);popupMaterials[child]=popupMaterial;DispatcherQueue.TryEnqueue(()=>LoadPopupGlass(child,popupMaterial));}
                }
                else{popupMaterials.Remove(child);PopupBackground(child,TopPanel.Background);}
            }
            catch(ArgumentException){PopupBackground(child,(Brush)Application.Current.Resources["FloatingBrush"]);}
        }
        try{renderer.Glass(rects.ToArray(),Root.ActualTheme==ElementTheme.Dark);}catch(COMException ex){Recover(ex);}
    }
    private void ScheduleGlass()
    {
        if(closed||glassUpdateScheduled)return;glassUpdateScheduled=true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=>{glassUpdateScheduled=false;UpdateGlass();});
    }
    private async void IdleTick(object? sender,object e)
    {
        if(closed)return;
        if(menuDepth==0&&!hover&&!leftNavigationHover&&!rightNavigationHover&&!dragging&&!originalHeld&&InfoPanel.Visibility!=Visibility.Visible&&DateTime.UtcNow>=keyboardUntil&&DateTime.UtcNow-lastActivity>=TimeSpan.FromSeconds(3.5)&&controlsVisible){controlsVisible=false;AnimateChrome(false);}
        // DXGI/display configuration calls may wait on the driver. Keep this
        // polling off the shared XAML thread, including while gallery resizing.
        await ProbeDisplayAsync(false);
    }
    private async Task ProbeDisplayAsync(bool force)
    {
        if(closed||renderer is null||displayProbeRunning||(!force&&Stopwatch.GetElapsedTime(lastDisplayProbe).TotalSeconds<2))return;
        displayProbeRunning=true;lastDisplayProbe=Stopwatch.GetTimestamp();
        try{await renderer.PollDisplayAsync();if(!closed)RefreshStatus(false);}
        catch(COMException ex){if(!closed)Recover(ex);}
        finally{displayProbeRunning=false;}
    }
    private void RootPointerMoved(object sender,PointerRoutedEventArgs e)=>ShowControls();
    private void PanelEntered(object sender,PointerRoutedEventArgs e){hover=true;ShowControls();}private void PanelExited(object sender,PointerRoutedEventArgs e){hover=false;lastActivity=DateTime.UtcNow;}
    private void MenuOpened(object sender,object e){menuDepth++;ShowControls(true);DispatcherQueue.TryEnqueue(UpdateGlass);}private void MenuClosed(object sender,object e){menuDepth=Math.Max(0,menuDepth-1);ShowControls();DispatcherQueue.TryEnqueue(UpdateGlass);}
    private async void EnhancementOpened(object sender,object e){MenuOpened(sender,e);RefreshStatus();try{if(renderer is not null){await renderer.InitializeEffectsAsync(lifetime.Token);if(!closed)RefreshStatus();}}catch(OperationCanceledException){}catch(ObjectDisposedException){}catch(Exception ex){App.Log("Enhancement detection: "+ex.Message);}}
    private void SetOriginalHeld(bool value)
    {
        if(closed||originalHeld==value)return;originalHeld=value;
        try{renderer?.HoldOriginal(value);RefreshStatus(false);}catch(COMException ex){Recover(ex);}
    }
    private void OriginalPressed(object sender,PointerRoutedEventArgs e){SetOriginalHeld(true);OriginalButton.CapturePointer(e.Pointer);ShowControls();}
    private void OriginalReleased(object sender,PointerRoutedEventArgs e){SetOriginalHeld(false);OriginalButton.ReleasePointerCapture(e.Pointer);ShowControls();}
    private void ViewerKeyDown(object sender,KeyRoutedEventArgs e)
    {
        keyboardUntil=DateTime.UtcNow.AddSeconds(10);ShowControls();if(menuDepth>0)return;
        bool ctrl=Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        switch(e.Key)
        {
            case VirtualKey.Left:Move(-1);break;case VirtualKey.Right:Move(1);break;
            case VirtualKey.Number0:Fit();break;case VirtualKey.Number1:ActualSize();break;
            case VirtualKey.Add:case (VirtualKey)187:ZoomBy(1.25);break;case VirtualKey.Subtract:case (VirtualKey)189:ZoomBy(.8);break;
            case VirtualKey.R:RotateClick(sender,e);break;case VirtualKey.F:case VirtualKey.F11:ToggleFullScreen();break;
            case VirtualKey.D:if(ctrl)FavoriteClick(sender,e);else return;break;
            case VirtualKey.I:InfoClick(sender,e);break;case VirtualKey.O:if(ctrl)OpenClick(sender,e);else return;break;
            case VirtualKey.Escape:if(fullScreen)ToggleFullScreen();else if(InfoPanel.Visibility==Visibility.Visible)InfoPanel.Visibility=Visibility.Collapsed;else Close();break;
            default:return;
        }
        e.Handled=true;
    }
    private void ViewerPreviewKeyDown(object sender,KeyRoutedEventArgs e)
    {
        if(e.Key!=VirtualKey.Space)return;
        // Tunnel before Button/ListView class handling. Enter retains its normal
        // control activation semantics; Space is always image comparison.
        keyboardUntil=DateTime.UtcNow.AddSeconds(10);SetOriginalHeld(true);e.Handled=true;TraceComparisonInput("press");
    }
    private void ViewerPreviewKeyUp(object sender,KeyRoutedEventArgs e){if(e.Key==VirtualKey.Space){SetOriginalHeld(false);e.Handled=true;TraceComparisonInput("release");}}
    private void TraceComparisonInput(string action)
    {
        // Opt-in local validation only: verifies real input delivery rather than
        // calling the comparison helper directly from an integration test.
        var cli=Environment.GetCommandLineArgs();if(!cli.Contains("--test-library")||!cli.Contains("--verify-input"))return;
        var s=renderer?.CachedStatus??default;
        File.AppendAllText(Path.Combine(App.DataDirectory,"keyboard-input.jsonl"),System.Text.Json.JsonSerializer.Serialize(new{action,originalHeld,photo=current?.Name,index,zoom,favorite=current?.Favorite,hdr=s.HdrEffective,vsr=s.VsrEffective,hdrCalls=s.HdrEvaluations,vsrCalls=s.VsrEvaluations,jobs=renderer?.WorkerJobs})+Environment.NewLine);
    }
}
