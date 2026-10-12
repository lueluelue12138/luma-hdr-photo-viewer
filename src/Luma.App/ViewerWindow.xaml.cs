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
using Windows.Storage.Pickers;
using Windows.UI;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Luma.App;
public sealed partial class ViewerWindow : Window
{
    private readonly FrameCache cache=new();private NativeRenderer? renderer;private FrameLease? currentLease;private int displayedRequest;private DecodedFrame? currentFrame=>current is not null&&displayedRequest==request?currentLease?.Frame:null;
    private List<Photo> sequence=[];private Photo? current;private int index,request,rotation;private bool closed,selecting,fullScreen,dragging,hover,originalHeld;
    private double zoom=1,panX,panY,startX,startY;private int menuDepth;private DateTime lastActivity=DateTime.UtcNow,keyboardUntil=DateTime.MinValue;
    private AppWindowPresenter? windowedPresenter;private Windows.Graphics.RectInt32 windowedBounds;private bool windowedMaximized;
    private CancellationTokenSource? loadingCancellation;private readonly CancellationTokenSource lifetime=new();
    private readonly DispatcherTimer idleTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private readonly string initial;private readonly IReadOnlyList<Photo>? initialSequence;private readonly Task<List<Photo>>? siblingEnumeration;private bool recovering;
    private bool hdrRequested,vsrRequested,updatingEnhancements;
    private bool controlsVisible=true;
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
    private (bool Effects,bool Contrast,ElementTheme Theme)? glassMaterial;
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
    internal async Task VerificationDisposeAsync(){try{await rendererInitialization;}catch(OperationCanceledException){}if(renderer is not null)await renderer.DisposedAsync;}
    internal RenderStatus VerificationStatus=>renderer?.Status()??default;
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
    internal async Task VerificationPopupAsync()
    {
        int before=popupGlassSnapshots;EnhancementButton.Flyout.ShowAt(EnhancementButton);
        var timer=Stopwatch.StartNew();while(popupGlassSnapshots==before){if(timer.Elapsed>TimeSpan.FromSeconds(5))throw new InvalidOperationException("Popup did not receive its photo glass background");await Task.Delay(50);}
        if(EnhancementScroll.Content is StackPanel content)content.Children.OfType<Expander>().First().IsExpanded=true;
        await Task.Delay(350);EnhancementButton.Flyout.Hide();await Task.Delay(50);
    }
    private double renderedZoom=1,renderedPanX,renderedPanY,viewStartZoom,viewStartX,viewStartY;private DateTime viewStarted;
    public ViewerWindow(string path,IReadOnlyList<Photo>? photos=null)
    {
        initial=SupportedFormats.Normalize(path);initialSequence=photos;InitializeComponent();ExtendsContentIntoTitleBar=true;SetTitleBar(TitleDrag);SystemBackdrop=new DesktopAcrylicBackdrop();
        var options=ViewerPreferences.Current;updatingEnhancements=true;hdrRequested=options.Hdr;vsrRequested=options.Vsr;HdrSwitch.IsOn=hdrRequested;VsrSwitch.IsOn=vsrRequested;HdrPreset.SelectedIndex=options.Preset;AutoBrightnessSwitch.IsOn=options.AutoBrightness;AutoPeakSwitch.IsOn=options.AutoPeak;MiddleGraySlider.Value=options.MiddleGray;PeakSlider.Value=options.Peak;MiddleGraySlider.IsEnabled=!options.AutoBrightness;PeakSlider.IsEnabled=!options.AutoPeak;updatingEnhancements=false;
        Filmstrip.AddHandler(UIElement.PointerWheelChangedEvent,new PointerEventHandler(FilmstripWheel),true);
        foreach(var button in new[]{PreviousButton,NextButton}){button.Opacity=1;ElementCompositionPreview.GetElementVisual(button).Opacity=0;}
        FileTitle.Text=System.IO.Path.GetFileName(initial);Title=$"{FileTitle.Text} — Luma";Loading.IsActive=true;Loading.Visibility=Visibility.Visible;StatusText.Text="正在加载照片…";
        // Prepare known gallery items before the first composed frame, rather
        // than growing a tiny empty filmstrip after GPU initialization.
        var f=new FileInfo(initial);sequence=initialSequence?.ToList()??[new(initial,f.Name,f.Exists?f.Length:0,f.Exists?f.LastWriteTimeUtc.Ticks:0)];
        index=Math.Max(0,sequence.FindIndex(p=>string.Equals(p.Path,initial,StringComparison.OrdinalIgnoreCase)));
        selecting=true;Filmstrip.ItemsSource=sequence;Filmstrip.SelectedIndex=index;selecting=false;UpdatePosition();
        if(initialSequence is null){BottomPanel.Visibility=Visibility.Collapsed;siblingEnumeration=Task.Run(()=>FolderIndexer.Siblings(initial),lifetime.Token);}
        WindowLayout.Configure(this,Root);
        chromeTimer.Tick+=(_,_)=>{double t=Math.Clamp((DateTime.UtcNow-chromeStarted).TotalMilliseconds/(chromeTarget>chromeStart?180:240),0,1);chromeOpacity=chromeStart+(chromeTarget-chromeStart)*(1-Math.Pow(1-t,3));ApplyChrome();if(t>=1)chromeTimer.Stop();};
        foreach(var panel in new[]{TopPanel,BottomPanel,InfoPanel,ErrorPanel})panel.SizeChanged+=(_,_)=>UpdateGlass();Root.ActualThemeChanged+=(_,_)=>UpdateGlass();Root.LayoutUpdated+=(_,_)=>{UpdateFilmstripBar();UpdateGlass();};
        viewTimer.Tick+=(_,_)=>{double t=Math.Clamp((DateTime.UtcNow-viewStarted).TotalMilliseconds/180,0,1);double ease=1-Math.Pow(1-t,3);renderedZoom=viewStartZoom+(zoom-viewStartZoom)*ease;renderedPanX=viewStartX+(panX-viewStartX)*ease;renderedPanY=viewStartY+(panY-viewStartY)*ease;try{renderer?.View(renderedZoom,renderedPanX,renderedPanY,rotation);}catch(COMException ex){viewTimer.Stop();Recover(ex);}if(t>=1)viewTimer.Stop();};
        WindowLayout.SizeForDpi(this,Root,1240,850);AppWindow.TitleBar.ButtonBackgroundColor=Colors.Transparent;AppWindow.TitleBar.ButtonInactiveBackgroundColor=Colors.Transparent;
        idleTimer.Tick+=IdleTick;Closed+=(_,_)=>{closed=true;request++;lifetime.Cancel();loadingCancellation?.Cancel();VerificationReady.TrySetCanceled();idleTimer.Stop();viewTimer.Stop();chromeTimer.Stop();renderer?.Dispose();currentLease?.Dispose();cache.Dispose();};
        AppWindow.Changed+=(_,args)=>{if(args.DidPositionChange)RefreshStatus();};Activated+=(_,args)=>{if(closed)return;if(args.WindowActivationState==WindowActivationState.Deactivated){dragging=false;InputLayer.ReleasePointerCaptures();if(originalHeld){originalHeld=false;ApplyEffects();}}if(renderer is not null)RefreshStatus();};
    }
    public void ApplyTheme(ElementTheme theme){if(!closed)Root.RequestedTheme=theme;}
    private async void SurfaceLoaded(object sender,RoutedEventArgs e)
    {
        if(closed)return;
        Surface.Loaded-=SurfaceLoaded;App.Log("Viewer loaded");if(siblingEnumeration is not null)_=LoadSiblingsAsync();await WindowLayout.RevealAsync(this,Root);if(closed)return;
        try{rendererInitialization=CreateRendererAsync();await rendererInitialization;}catch(OperationCanceledException){return;}catch(Exception ex){if(!closed){VerificationReady.TrySetException(ex);Fail(ex);}return;}
        if(closed)return;
        idleTimer.Start();InputLayer.Focus(FocusState.Programmatic);
        await DisplayAsync(index);
        if(!closed)InputLayer.Focus(FocusState.Programmatic);
    }
    private async Task LoadSiblingsAsync()
    {
        // Enumeration runs alongside initialization and decoding, never gates the
        // selected photo. Reveal the filmstrip at its prepared size in one step.
        try{var siblings=await siblingEnumeration!;if(closed)return;var path=current?.Path??initial;if(siblings.Count>0)sequence=siblings;index=Math.Max(0,sequence.FindIndex(p=>string.Equals(p.Path,path,StringComparison.OrdinalIgnoreCase)));selecting=true;Filmstrip.ItemsSource=sequence;Filmstrip.SelectedIndex=index;selecting=false;UpdatePosition();BottomPanel.Visibility=Visibility.Visible;if(currentFrame is not null)Prefetch();}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or OperationCanceledException){App.Log(ex.Message);if(!closed)BottomPanel.Visibility=Visibility.Visible;}
    }
    private async Task CreateRendererAsync(){renderer?.Dispose();renderer=null;var next=await NativeRenderer.CreateAsync(Surface,WinRT.Interop.WindowNative.GetWindowHandle(this),lifetime.Token);if(closed){next.Dispose();return;}renderer=next;renderer.Resize(Surface.ActualWidth,Surface.ActualHeight,Surface.CompositionScaleX);App.Log("Viewer renderer attached");}
    private async Task DisplayAsync(int selected)
    {
        if(closed||sequence.Count==0||selected<0||selected>=sequence.Count)return;
        index=selected;current=sequence[index];int generation=++request;enhancementCancellation?.Cancel();loadingCancellation?.Cancel();loadingCancellation?.Dispose();loadingCancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=loadingCancellation.Token;
        viewTimer.Stop();zoom=1;panX=panY=0;rotation=0;FileTitle.Text=current.Name;Title=$"{current.Name} — Luma";ErrorPanel.Visibility=Visibility.Collapsed;Loading.IsActive=true;Loading.Visibility=Visibility.Visible;ShowControls();UpdatePosition();UpdateInfo();
        try
        {
            var photo=current;var lease=await cache.GetAsync(photo.Path,token);if(closed||generation!=request){lease.Dispose();return;}
            try{await renderer!.ShowAsync(lease.Frame,token);if(closed||generation!=request){lease.Dispose();return;}}catch{lease.Dispose();throw;}
            currentLease?.Dispose();currentLease=lease;displayedRequest=generation;UpdateView();UpdateGlass();cache.Trim(photo.Path);await ApplyEffectsAsync();if(closed||generation!=request)return;
            var stored=await App.Store.IsFavoriteAsync(photo.Path);if(generation!=request)return;current=photo with{Favorite=stored};UpdateFavorite();
            await App.Store.RecordOpenAsync(current);if(closed||generation!=request)return;RefreshStatus();UpdateInfo();Prefetch();VerificationReady.TrySetResult();
        }
        catch(OperationCanceledException){}catch(Exception ex){if(generation==request&&!closed){VerificationReady.TrySetException(ex);Fail(ex);}}
        finally{if(generation==request&&!closed){Loading.IsActive=false;Loading.Visibility=Visibility.Collapsed;}}
    }
    private async void Prefetch()
    {
        var token=loadingCancellation?.Token??lifetime.Token;var path=current?.Path;if(path is null)return;
        foreach(var adjacent in new[]{index+1,index-1})
        {
            if(adjacent<0||adjacent>=sequence.Count||token.IsCancellationRequested)continue;
            try{using var lease=await cache.GetAsync(sequence[adjacent].Path,token);if(!closed&&current is not null)cache.Trim(current.Path);}catch(Exception ex)when(ex is IOException or COMException or OperationCanceledException or ObjectDisposedException){ }
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
        var active=FindChild<ScrollViewer>(Filmstrip);
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
        try{viewTimer.Stop();if(animate&&Motion.Enabled){viewStartZoom=renderedZoom;viewStartX=renderedPanX;viewStartY=renderedPanY;viewStarted=DateTime.UtcNow;viewTimer.Start();}else{renderedZoom=zoom;renderedPanX=panX;renderedPanY=panY;renderer?.View(zoom,panX,panY,rotation);}var actual=PixelScale();FitButton.Content=Math.Abs(zoom-1)<0.001?"适合窗口":$"{actual*100:0}%";UpdateInfo();}catch(COMException ex){Recover(ex);}
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
        var p=e.GetCurrentPoint(InputLayer);double old=zoom;zoom=Math.Clamp(zoom*Math.Pow(1.0015,p.Properties.MouseWheelDelta),0.05,64);double ratio=zoom/old;double x=(p.Position.X-Surface.ActualWidth/2)*Surface.CompositionScaleX,y=(p.Position.Y-Surface.ActualHeight/2)*Surface.CompositionScaleY;panX=x-(x-panX)*ratio;panY=y-(y-panY)*ratio;UpdateView();ShowControls();e.Handled=true;
    }
    private void CanvasPressed(object sender,PointerRoutedEventArgs e){var p=e.GetCurrentPoint(InputLayer);InputLayer.Focus(FocusState.Pointer);if(!p.Properties.IsLeftButtonPressed||zoom<=1)return;dragging=true;startX=p.Position.X;startY=p.Position.Y;InputLayer.CapturePointer(e.Pointer);}
    private void CanvasMoved(object sender,PointerRoutedEventArgs e){if(!dragging)return;var p=e.GetCurrentPoint(InputLayer);panX+=(p.Position.X-startX)*Surface.CompositionScaleX;panY+=(p.Position.Y-startY)*Surface.CompositionScaleY;startX=p.Position.X;startY=p.Position.Y;UpdateView();}
    private void CanvasReleased(object sender,PointerRoutedEventArgs e){dragging=false;InputLayer.ReleasePointerCapture(e.Pointer);}
    private void CanvasDoubleTapped(object sender,DoubleTappedRoutedEventArgs e){if(Math.Abs(zoom-1)<0.001)ActualSize();else Fit();e.Handled=true;}
    private async void FavoriteClick(object sender,RoutedEventArgs e){if(current is null)return;var photo=current;int generation=request;try{bool favorite=await App.Store.ToggleFavoriteAsync(photo);if(closed||generation!=request)return;current=photo with{Favorite=favorite};UpdateFavorite();ShowControls();}catch(Exception ex){if(!closed&&generation==request)Fail(ex);}}
    private void UpdateFavorite(){FavoriteIcon.Glyph=current?.Favorite==true?"\uEB52":"\uEB51";FavoriteButton.SetValue(AutomationProperties.NameProperty,current?.Favorite==true?"取消收藏":"收藏");}
    private void FullScreenClick(object sender,RoutedEventArgs e)=>ToggleFullScreen();
    private void ToggleFullScreen()
    {
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
        TitleRow.Height=new GridLength(fullScreen?0:44);ShowControls();InputLayer.Focus(FocusState.Programmatic);
    }
    private void BackClick(object sender,RoutedEventArgs e){App.ShowGallery();Close();}
    private void SurfaceSizeChanged(object sender,SizeChangedEventArgs e){if(closed)return;EnhancementScroll.MaxHeight=Math.Max(160,Canvas.ActualHeight-116);ZoomOutButton.Visibility=ZoomInButton.Visibility=Canvas.ActualWidth<570?Visibility.Collapsed:Visibility.Visible;BottomPanel.MaxWidth=Math.Min(920,Math.Max(240,Canvas.ActualWidth-72));Filmstrip.MaxWidth=Math.Min(820,Math.Max(120,Canvas.ActualWidth-158));if(renderer is null)return;try{renderer.Resize(Surface.ActualWidth,Surface.ActualHeight,Surface.CompositionScaleX);UpdateView();}catch(COMException ex){Recover(ex);}}
    private void ScaleChanged(SwapChainPanel sender,object args){if(renderer is null||closed)return;try{renderer.Resize(Surface.ActualWidth,Surface.ActualHeight,Surface.CompositionScaleX);UpdateView();}catch(COMException ex){Recover(ex);}}
    private async void Recover(Exception reason)
    {
        if(recovering||closed)return;recovering=true;App.Log("Recovering renderer: "+reason.Message);
        try{enhancementCancellation?.Cancel();await CreateRendererAsync();if(closed)return;if(currentFrame is not null){await renderer!.ShowAsync(currentFrame,lifetime.Token);await ApplyEffectsAsync();}else if(current is not null)await DisplayAsync(index);if(!closed){renderer!.View(zoom,panX,panY,rotation);UpdateGlass();RefreshStatus();}}catch(OperationCanceledException){}catch(Exception ex){if(!closed)Fail(ex);}finally{recovering=false;}
    }
    private void RefreshStatus()
    {
        if(renderer is null||closed)return;
        try
        {
            var s=renderer.Status();bool photoReady=currentFrame is not null;bool busy=renderer.Busy||(!photoReady&&Loading.IsActive);updatingEnhancements=true;
            HdrReasonText.Text=!photoReady?(Loading.IsActive?"正在加载照片…":"载入照片后可调整增强"):hdrRequested?s.HdrReason:s.HdrAvailable!=0?"TrueHDR 可用，未开启":s.HdrReason;
            VsrReasonText.Text=!photoReady?(Loading.IsActive?"正在加载照片…":"载入照片后可调整增强"):vsrRequested?s.VsrMessage:s.VsrAvailable!=0?"VSR 可用，默认关闭":s.VsrMessage;
            HdrSwitch.IsOn=hdrRequested;VsrSwitch.IsOn=vsrRequested;
            HdrSwitch.IsEnabled=photoReady&&s.HdrAvailable!=0&&s.HdrActive!=0&&s.NativeHdr==0;
            VsrSwitch.IsEnabled=photoReady&&s.VsrAvailable!=0&&s.NativeHdr==0;
            HdrSwitch.OnContent=!photoReady?(Loading.IsActive?"加载中":"未生效"):busy?"处理中":originalHeld?"查看原图":s.HdrEffective!=0?"已生效":"未生效";
            VsrSwitch.OnContent=!photoReady?(Loading.IsActive?"加载中":"未生效"):busy?"处理中":originalHeld?"查看原图":s.VsrEffective!=0?"已生效":"未生效";
            HdrSwitch.OffContent=HdrSwitch.IsEnabled?"已关闭":"不可用";
            VsrSwitch.OffContent=VsrSwitch.IsEnabled?"已关闭":"不可用";
            double peak=AutoPeakSwitch.IsOn?Math.Floor(Math.Clamp(s.MaxNits>=400?s.MaxNits:1000,400,2000)):PeakSlider.Value;
            double gray=AutoBrightnessSwitch.IsOn?Math.Floor(Math.Clamp(s.SdrWhite*80*.21586,50,100)):MiddleGraySlider.Value;
            if(AutoPeakSwitch.IsOn)PeakSlider.Value=peak;if(AutoBrightnessSwitch.IsOn)MiddleGraySlider.Value=gray;
            DisplayText.Text=$"{s.Gpu}\nWindows HDR：{(s.HdrActive!=0?"已开启":"未开启")}\nFP16 / scRGB\nSDR 白色：{s.SdrWhite*80:0} nits\n增强峰值 / 中间灰：{peak:0} / {gray:0} nits\nHDR / VSR 调用：{s.HdrEvaluations} / {s.VsrEvaluations}\n图片与画布显存：{s.GpuBytes/1048576.0:0} MB";
            StatusText.Text=!photoReady?(Loading.IsActive?"正在加载照片…":"照片未加载"):busy&&(hdrRequested||vsrRequested)?"正在处理显示增强…":s.NativeHdr!=0?(s.HdrActive!=0?"原生 HDR · scRGB":"HDR 原图 · 当前屏幕 SDR"):originalHeld?"原图 · 未应用增强":s.HdrEffective!=0?(s.VsrEffective!=0?"RTX HDR + VSR 已生效":"RTX HDR 已生效"):s.VsrEffective!=0?"VSR 已生效 · SDR":"SDR · 原图";
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
        try{StatusText.Text=hdrRequested||vsrRequested?"正在处理显示增强…":"SDR · 原图";await active.EffectsAsync(hdrRequested,vsrRequested,originalHeld,preset,gray,peak,token);if(closed||generation!=request||active!=renderer||token.IsCancellationRequested)return;UpdateView();RefreshStatus();}
        catch(OperationCanceledException){}catch(ObjectDisposedException){}catch(COMException ex){if(!closed&&generation==request)Recover(ex);}catch(Exception ex){App.Log(ex.ToString());if(!closed&&generation==request)RefreshStatus();}
    }
    private void ApplyEffects()=>_=ApplyEffectsAsync();
    private async void TuningChanged(object sender,RoutedEventArgs e)
    {
        if(renderer is null||updatingEnhancements)return;MiddleGraySlider.IsEnabled=!AutoBrightnessSwitch.IsOn;PeakSlider.IsEnabled=!AutoPeakSwitch.IsOn;tuningCancellation?.Cancel();tuningCancellation?.Dispose();tuningCancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=tuningCancellation.Token;
        try{await SavePreferencesAsync();await Task.Delay(220,token);if(!closed)ApplyEffects();}catch(OperationCanceledException){}catch(Exception ex){App.Log(ex.Message);}
    }
    private void TuningValueChanged(object sender,Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)=>TuningChanged(sender,new RoutedEventArgs());
    private async void EnhancementToggled(object sender,RoutedEventArgs e){if(updatingEnhancements||renderer is null)return;hdrRequested=HdrSwitch.IsOn;vsrRequested=VsrSwitch.IsOn;await SavePreferencesAsync();if(closed)return;try{ApplyEffects();}catch(Exception ex){App.Log(ex.Message);HdrReasonText.Text=ex.Message;RefreshStatus();}ShowControls();}
    private async void PresetChanged(object sender,SelectionChangedEventArgs e){if(renderer is null||updatingEnhancements)return;await SavePreferencesAsync();if(closed)return;try{ApplyEffects();}catch(Exception ex){App.Log(ex.Message);}}
    private void UpdateInfo(){if(current is null)return;var i=currentFrame?.Info;InfoText.Text=$"{current.Name}\n\n{(i is null?"":$"{i.Value.OriginalWidth} × {i.Value.OriginalHeight} 像素")}\n{current.Size/1048576.0:0.00} MB\n缩放 {PixelScale()*100:0}%\n旋转 {rotation*90}°\n{(i?.Downsampled==1?"大图已按内存预算缩小解码\n":"")}\n{current.Path}";}
    private void InfoClick(object sender,RoutedEventArgs e){InfoPanel.Visibility=InfoPanel.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;ShowControls();UpdateInfo();UpdateGlass();}
    private void CloseInfoClick(object sender,RoutedEventArgs e){InfoPanel.Visibility=Visibility.Collapsed;UpdateGlass();}
    private void RevealClick(object sender,RoutedEventArgs e){if(current is not null)Process.Start(new ProcessStartInfo("explorer.exe",$"/select,\"{current.Path}\""){UseShellExecute=true});}
    private async void OpenClick(object sender,RoutedEventArgs e)
    {
        try{var picker=new FileOpenPicker();foreach(var ext in SupportedFormats.Extensions)picker.FileTypeFilter.Add(ext);WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var f=await picker.PickSingleFileAsync();if(f is not null){new ViewerWindow(f.Path).Activate();Close();}}catch(Exception ex){Fail(ex);}
    }
    private void Fail(Exception ex){App.Log(ex.ToString());ErrorText.Text=ex.Message;ErrorPanel.Visibility=Visibility.Visible;Loading.IsActive=false;Loading.Visibility=Visibility.Collapsed;ShowControls();}
    private void ShowControls(){lastActivity=DateTime.UtcNow;if(!controlsVisible){controlsVisible=true;AnimateChrome(true);}}
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
        if(renderer is null||closed||currentFrame is null)return;
        bool effects=uiSettings.AdvancedEffectsEnabled&&!accessibilitySettings.HighContrast;
        var material=(effects,accessibilitySettings.HighContrast,Root.ActualTheme);
        if(glassMaterial!=material){glassMaterial=material;var fill=effects?Colors.Transparent:accessibilitySettings.HighContrast?uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background):Root.ActualTheme==ElementTheme.Dark?Color.FromArgb(255,35,39,47):Color.FromArgb(255,246,248,252);foreach(var panel in new[]{TopPanel,BottomPanel,InfoPanel,ErrorPanel})panel.Background=new SolidColorBrush(fill);}
        var rects=new List<GlassRect>();double dpi=Surface.CompositionScaleX;
        if(effects)foreach(var panel in new[]{TopPanel,BottomPanel,InfoPanel,ErrorPanel})
        {
            double opacity=panel==TopPanel||panel==BottomPanel?chromeOpacity:1;
            if(panel.Visibility!=Visibility.Visible||panel.ActualWidth<=0||panel.ActualHeight<=0||opacity<.001)continue;
            var p=panel.TransformToVisual(Canvas).TransformPoint(new(0,0));rects.Add(new((float)(p.X*dpi),(float)(p.Y*dpi),(float)(panel.ActualWidth*dpi),(float)(panel.ActualHeight*dpi),(float)(panel.CornerRadius.TopLeft*dpi),(float)opacity));
        }
        var popups=Root.XamlRoot is null?[]:VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).ToArray();
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
    private void IdleTick(object? sender,object e)
    {
        RefreshStatus();UpdateGlass();
        if(menuDepth>0||hover||leftNavigationHover||rightNavigationHover||dragging||originalHeld||InfoPanel.Visibility==Visibility.Visible||DateTime.UtcNow<keyboardUntil||DateTime.UtcNow-lastActivity<TimeSpan.FromSeconds(3.5))return;
        if(controlsVisible){controlsVisible=false;AnimateChrome(false);}
        // Poll display state without continuously processing the photo.
    }
    private void RootPointerMoved(object sender,PointerRoutedEventArgs e)=>ShowControls();
    private void PanelEntered(object sender,PointerRoutedEventArgs e){hover=true;ShowControls();}private void PanelExited(object sender,PointerRoutedEventArgs e){hover=false;lastActivity=DateTime.UtcNow;}
    private void MenuOpened(object sender,object e){menuDepth++;ShowControls();DispatcherQueue.TryEnqueue(UpdateGlass);}private void MenuClosed(object sender,object e){menuDepth=Math.Max(0,menuDepth-1);ShowControls();DispatcherQueue.TryEnqueue(UpdateGlass);}
    private void EnhancementOpened(object sender,object e){MenuOpened(sender,e);RefreshStatus();}
    private void OriginalPressed(object sender,PointerRoutedEventArgs e){originalHeld=true;OriginalButton.CapturePointer(e.Pointer);ApplyEffects();ShowControls();}
    private void OriginalReleased(object sender,PointerRoutedEventArgs e){originalHeld=false;OriginalButton.ReleasePointerCapture(e.Pointer);ApplyEffects();ShowControls();}
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
            case VirtualKey.Space:originalHeld=true;ApplyEffects();break;
            case VirtualKey.Escape:if(fullScreen)ToggleFullScreen();else if(InfoPanel.Visibility==Visibility.Visible)InfoPanel.Visibility=Visibility.Collapsed;else Close();break;
            default:return;
        }
        e.Handled=true;
    }
    private void ViewerKeyUp(object sender,KeyRoutedEventArgs e){if(e.Key==VirtualKey.Space){originalHeld=false;ApplyEffects();e.Handled=true;}}
}
