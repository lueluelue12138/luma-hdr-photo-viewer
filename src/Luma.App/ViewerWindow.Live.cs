using Luma.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Luma.App;

public sealed partial class ViewerWindow
{
    private LivePhotoAsset? liveAsset;
    private PhotoFeatures? photoFeatures;
    private LivePlayback? livePlayback;
    private CancellationTokenSource? liveCancellation;
    private Task liveDetection=Task.CompletedTask;
    private Task liveRetirement=Task.CompletedTask;
    private Task liveStarting=Task.CompletedTask;
    private int liveGeneration;
    private bool livePreparing;
    private string? liveWarning;
    private static WeakReference<ViewerWindow>? playingLiveWindow;
    private NativeRenderer? DisplayRenderer=>livePlayback?.Renderer??renderer;
    private LiveRenderOptions LiveOptions()=>new(hdrRequested,originalHeld,Math.Max(0,HdrPreset.SelectedIndex),AutoBrightnessSwitch.IsOn?0:(uint)MiddleGraySlider.Value,AutoPeakSwitch.IsOn?0:(uint)PeakSlider.Value);
    private async Task DetectLiveAsync(string path,int generation,CancellationToken token)
    {
        try
        {
            var features=await PhotoFeaturesCache.ReadAsync(path,token);var asset=features.Live;
            if(closed||generation!=request||token.IsCancellationRequested)return;
            photoFeatures=features;liveAsset=asset;UpdateLiveControls();UpdateInfo();
            UpdateGlass();
        }
        catch(OperationCanceledException){}catch(Exception ex){App.Log("Live detection: "+ex.Message);}
    }
    private async void LiveClick(object sender,RoutedEventArgs e)
    {
        if(livePlayback is not null||livePreparing)StopLive();else await StartLiveAsync();
        ShowControls();InputLayer.Focus(FocusState.Programmatic);
    }
    private Task StartLiveAsync()=>liveStarting=Task.WhenAll(liveStarting,StartLiveCoreAsync());
    private async Task StartLiveCoreAsync()
    {
        if(closed||liveAsset is not {} asset||currentFrame is null||livePlayback is not null||livePreparing)return;
        if(playingLiveWindow?.TryGetTarget(out var other)==true&&other!=this)other.StopLive();
        livePreparing=true;liveWarning=null;int generation=request,play=++liveGeneration;
        enhancementCancellation?.Cancel();
        liveCancellation?.Dispose();liveCancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,loadingCancellation?.Token??lifetime.Token);
        var token=liveCancellation.Token;UpdateLiveControls();
        LivePlayback? next=null;
        try
        {
            next=await LivePlayback.CreateAsync(asset,LiveSurface,WinRT.Interop.WindowNative.GetWindowHandle(this),LiveOptions(),token);
            if(closed||generation!=request||play!=liveGeneration||token.IsCancellationRequested){next.Stop();return;}
            if(playingLiveWindow?.TryGetTarget(out var playing)==true&&playing!=this)playing.StopLive();
            livePlayback=next;playingLiveWindow=new(this);ConfigureTuning(false);next.SetOptions(LiveOptions());
            next.Muted=ViewerPreferences.Current.LiveMuted;
            var currentLive=next;
            next.FirstFrame+=()=>{if(livePlayback!=currentLive||closed)return;Surface.Opacity=0;LiveSurface.Opacity=1;UpdateLiveControls();UpdateGlass();RefreshStatus(false);};
            next.Ended+=message=>{if(livePlayback!=currentLive||closed)return;StopLive();if(message is not null)SetLiveWarning(message);};
            next.Renderer.Presented+=()=>{if(livePlayback==currentLive&&!closed){UpdateGlass();RefreshStatus(false);}};
            next.Renderer.Resize(Surface.ActualWidth,Surface.ActualHeight,Surface.CompositionScaleX);
            UpdateView();
            UpdateLiveControls();UpdateGlass();next.Play();
            App.Log($"Live started: {asset.Kind}; {next.Width}x{next.Height}; {next.Duration:0.###}s; embedded={asset.Embedded}");
        }
        catch(OperationCanceledException){next?.Stop();}
        catch(Exception ex){next?.Stop();if(livePlayback==next)StopLive();if(!closed&&generation==request)SetLiveWarning(ex.Message);}
        finally
        {
            if(next is not null&&next!=livePlayback)await next.Disposed;
            if(!closed&&generation==request&&play==liveGeneration){livePreparing=false;UpdateLiveControls();RefreshStatus(false);}
        }
    }
    private void SetLiveWarning(string message)
    {
        liveWarning=message;ToolTipService.SetToolTip(LiveButton,message);App.Log("Live warning: "+message);RefreshStatus(false);
    }
    private void StopLive(bool applyStill=true)
    {
        liveGeneration++;liveCancellation?.Cancel();livePreparing=false;
        var old=livePlayback;livePlayback=null;
        Surface.Opacity=1;LiveSurface.Opacity=0;UpdateLiveControls();
        if(old is not null)
        {
            App.Log($"Live stopped: frames={old.Frames}; coalescedNotifications={old.CoalescedNotifications}");old.Stop();liveRetirement=Task.WhenAll(liveRetirement,old.Disposed);
            if(!closed){renderer?.HoldOriginal(originalHeld);renderer?.Resize(Surface.ActualWidth,Surface.ActualHeight,Surface.CompositionScaleX);ConfigureTuning(currentFrame?.Info.NativeHdr!=0);UpdateView();UpdateGlass();RefreshStatus(false);if(applyStill)_=ApplyEffectsAsync();}
        }
    }
    private Action<string>? verificationLiveControls;
    private void UpdateLiveControls()
    {
        LiveButton.Visibility=LiveMuteButton.Visibility=liveAsset is null?Visibility.Collapsed:Visibility.Visible;
        bool playing=livePlayback?.Frames>0;
        bool preparing=!playing&&(livePreparing||livePlayback is not null);
        LiveIcon.SetState(preparing,playing);
        string label=preparing?"正在准备实况，点击取消（L）":playing?"停止实况播放（L）":liveWarning??"播放实况照片（L），空格查看原图";
        LiveButton.SetValue(AutomationProperties.NameProperty,label);ToolTipService.SetToolTip(LiveButton,label);
        UpdateLiveMute();verificationLiveControls?.Invoke(preparing?"preparing":playing?"playing":"idle");
    }
    private void UpdateLiveMute()
    {
        bool muted=livePlayback?.Muted??ViewerPreferences.Current.LiveMuted;
        LiveMuteIcon.Glyph=muted?"\uE74F":"\uE767";LiveMuteButton.IsEnabled=liveAsset is not null&&livePlayback?.AudioTracks!=0;
        string label=livePlayback?.AudioTracks==0?"这张实况照片没有音轨":muted?"开启实况原声":"静音实况";ToolTipService.SetToolTip(LiveMuteButton,label);LiveMuteButton.SetValue(AutomationProperties.NameProperty,label);
    }
    private async void LiveMuteClick(object sender,RoutedEventArgs e)
    {
        try{await SetLiveMutedAsync(!(livePlayback?.Muted??ViewerPreferences.Current.LiveMuted));ShowControls();}
        catch(Exception ex){App.Log("Save Live sound preference: "+ex.Message);}
    }
    private async Task SetLiveMutedAsync(bool muted)
    {
        if(livePlayback is {} live){live.Muted=muted;UpdateLiveMute();}
        await ViewerPreferences.UpdateAsync(ViewerPreferences.Current with{LiveMuted=muted});if(!closed)UpdateLiveMute();
    }
    internal async Task<object> VerificationLiveAsync()
    {
        await liveDetection.WaitAsync(TimeSpan.FromSeconds(10));
        if(liveAsset is null)throw new InvalidOperationException("Expected a detected Live Photo");
        hdrRequested=true;vsrRequested=false;await StartLiveAsync();
        var live=livePlayback??throw new InvalidOperationException(liveWarning??"Live player not created");live.VerificationLoop=true;
        async Task Until(Func<bool> ready){var watch=System.Diagnostics.Stopwatch.StartNew();while(!ready()){if(liveWarning is not null)throw new InvalidOperationException(liveWarning);if(watch.Elapsed.TotalSeconds>15)throw new TimeoutException("Live frames timed out");await Task.Delay(20);}}
        await Until(()=>live.Frames>=12&&live.Renderer.Status().HdrEffective!=0);
        await renderer!.PresentPendingAsync(lifetime.Token);ulong stillPresents=renderer.Metrics.Presents;
        ZoomBy(1.2);await Task.Delay(220);Fit();await Task.Delay(220);
        if(renderer.Metrics.Presents!=stillPresents)throw new InvalidOperationException("Hidden still renderer must not redraw during Live interaction");
        var original=currentLease;var before=live.Renderer.Status();
        if(before.Fp16==0||before.SdkAvailable==0||before.HdrEvaluations<3||before.VsrEvaluations!=0)throw new InvalidOperationException("Live HDR must evaluate the actual SDK in FP16 without VSR");
        uint width=(uint)Math.Ceiling(Surface.ActualWidth*Surface.CompositionScaleX)+2,height=(uint)Math.Ceiling(Surface.ActualHeight*Surface.CompositionScaleY)+2;
        var a=await live.Renderer.VerificationSurfaceAsync(width,height,lifetime.Token);await Task.Delay(180);
        var b=await live.Renderer.VerificationSurfaceAsync(width,height,lifetime.Token);
        if(a.Peak<=1||b.Peak<=1||a.Fingerprint==b.Fingerprint)throw new InvalidOperationException("Actual FP16 presentation must contain changing high-range video pixels");
        SetOriginalHeld(true);await Task.Delay(160);uint calls=live.Renderer.Status().HdrEvaluations;int frames=live.Frames;
        await Task.Delay(250);var comparison=live.Renderer.Status();
        if(live.Frames<=frames||comparison.HdrEvaluations!=calls||comparison.HdrEffective!=0)throw new InvalidOperationException("Live original comparison must advance without new HDR evaluations");
        SetOriginalHeld(false);await Until(()=>live.Renderer.Status().HdrEvaluations>calls&&live.Renderer.Status().HdrEffective!=0);
        ulong memory=live.Renderer.Status().GpuBytes;int stressFrames=live.Frames;var jitter=new List<double>();
        // Exercise repeated loops and keep sampling XAML responsiveness. This
        // measures task continuation delay, not physical input/display latency.
        for(int n=0;n<100;n++){var delay=System.Diagnostics.Stopwatch.StartNew();await Task.Delay(25);jitter.Add(Math.Max(0,delay.Elapsed.TotalMilliseconds-25));}
        ulong finalMemory=live.Renderer.Status().GpuBytes;
        if(live.Frames<=stressFrames+10||finalMemory>memory+8UL*1024*1024)throw new InvalidOperationException("Looping Live must advance with bounded owned GPU textures");
        bool muted=live.Muted;live.Muted=false;if(live.Muted)throw new InvalidOperationException("Live audio toggle failed");live.Muted=true;
        UpdateView();UpdateGlass();var final=live.Renderer.Status();
        int count=live.Frames,coalescedNotifications=live.CoalescedNotifications;double[] times=live.FrameMilliseconds.OrderBy(v=>v).ToArray();int tracks=live.AudioTracks;
        StopLive();await liveRetirement;
        if(currentLease!=original||Surface.Opacity!=1||LiveSurface.Opacity!=0||livePlayback is not null)throw new InvalidOperationException("Live stop must restore the same cached static photo");
        jitter.Sort();return new{kind=liveAsset.Kind,embedded=liveAsset.Embedded,video=new{live.Width,live.Height},frames=count,coalescedNotifications,live.FirstFrameMilliseconds,medianSubmitMilliseconds=times[times.Length/2],p95SubmitMilliseconds=times[(int)((times.Length-1)*.95)],p95DispatcherDelayMilliseconds=jitter[94],ownedGpuBytesBefore=memory,ownedGpuBytesAfter=finalMemory,hdrEvaluations=final.HdrEvaluations,vsrEvaluations=final.VsrEvaluations,peak=Math.Max(a.Peak,b.Peak),changingFp16Pixels=true,comparisonAdvancesWithoutSdk=true,defaultMuted=muted,audioTracks=tracks,staticLeasePreserved=true,hiddenStillDoesNotRedraw=true};
    }
    internal async Task VerificationLiveCloseAsync()
    {
        // Cancel while MediaPlayer is opening, then replay after its resources
        // retire. The picture and current lease must survive both operations.
        var opening=StartLiveAsync();await Task.Delay(25);StopLive();await opening;await liveRetirement;
        await StartLiveAsync();if(livePlayback is not {} natural)throw new InvalidOperationException(liveWarning??"Natural completion replay failed");
        var completion=System.Diagnostics.Stopwatch.StartNew();while(livePlayback is not null){if(completion.Elapsed.TotalSeconds>natural.Duration+10)throw new TimeoutException("Live natural completion timed out");await Task.Delay(25);}
        await liveRetirement;
        await StartLiveAsync();if(livePlayback is not {} live)throw new InvalidOperationException(liveWarning??"Replay failed");live.VerificationLoop=true;
        var watch=System.Diagnostics.Stopwatch.StartNew();while(live.Frames<2){if(watch.Elapsed.TotalSeconds>10)throw new TimeoutException("Live replay timed out");await Task.Delay(25);}
        Close();await liveRetirement;await VerificationDisposeAsync();
    }
    internal async Task<object> VerificationLiveAudioAsync(bool write)
    {
        await liveDetection;hdrRequested=false;vsrRequested=false;
        var geometry=new List<(string State,double Width,double X,double SoundX)>();var controlErrors=new List<string>();
        verificationLiveControls=state=>
        {
            Root.UpdateLayout();var p=LiveButton.TransformToVisual(TopPanel).TransformPoint(new(0,0));var sound=LiveMuteButton.TransformToVisual(TopPanel).TransformPoint(new(0,0));
            if(LiveButton.ActualWidth!=40||LiveButton.ActualHeight!=38||LiveMuteButton.Visibility!=Visibility.Visible)controlErrors.Add("Live controls must keep fixed geometry");
            geometry.Add((state,TopPanel.ActualWidth,p.X,sound.X));
            if(geometry.Count>1&&geometry.Any(g=>Math.Abs(g.Width-geometry[0].Width)>.1||Math.Abs(g.X-geometry[0].X)>.1||Math.Abs(g.SoundX-geometry[0].SoundX)>.1))controlErrors.Add("Live state moved the toolbar");
            int expected=state=="preparing"?1:state=="playing"?2:0;
            if(LiveIcon.VerificationState!=expected)controlErrors.Add("Live icon state mismatch");
        };
        UpdateLiveControls();await StartLiveAsync();
        var live=livePlayback??throw new InvalidOperationException(liveWarning??"No Live audio player");live.VerificationLoop=true;
        var watch=System.Diagnostics.Stopwatch.StartNew();while(!geometry.Any(g=>g.State=="playing")){if(watch.Elapsed.TotalSeconds>15)throw new TimeoutException("No playing icon state");await Task.Delay(25);}
        if(!LiveIcon.VerificationCentered)throw new InvalidOperationException("Live rings / opposite dot pairs must share the same center");
        if(LiveIcon.VerificationSpinning!=Motion.Enabled)throw new InvalidOperationException("Playing Live icon must spin when system animations are enabled");
        if(live.Muted!=write||ViewerPreferences.Current.LiveMuted!=write)throw new InvalidOperationException("Live audio initial preference differs from expected persisted state");
        if(write)
        {
            await SetLiveMutedAsync(false);await SavePreferencesAsync();
            if(live.Muted||ViewerPreferences.Current.LiveMuted)throw new InvalidOperationException("HDR preference saving must preserve the sound setting");
            StopLive();await liveRetirement;await StartLiveAsync();
            if(livePlayback?.Muted!=false)throw new InvalidOperationException("Replay reset the sound preference");
        }
        // Layout checks below resize the window deliberately; finish the fixed
        // toolbar assertion before those unrelated layout changes.
        bool animated=LiveIcon.VerificationAnimating;
        StopLive();await liveRetirement;verificationLiveControls=null;
        if(controlErrors.Count>0)throw new InvalidOperationException(string.Join("; ",controlErrors.Distinct()));
        if(!geometry.Any(g=>g.State=="preparing")||!geometry.Any(g=>g.State=="playing")||LiveIcon.VerificationAnimating||animated!=Motion.Enabled)throw new InvalidOperationException($"Preparing / playing / stop icon transitions missing; playing animated={animated}, system={Motion.Enabled}");
        await LivePhotoVerification.RenderSymbolAsync(LiveIcon);
        await StartLiveAsync();await liveDetection;await VerificationOpenInfoAsync();
        if(liveAsset is null||InfoLiveFeature.Visibility!=Visibility.Visible||!InfoLiveSummary.Text.Contains(liveAsset.Kind))throw new InvalidOperationException("Details must identify the Live file and container");
        await VerificationRenderInfoAsync(write?"live-details-write.png":"live-details-read.png");
        var result=new{soundEnabled=livePlayback?.Muted==false,storedSoundEnabled=!ViewerPreferences.Current.LiveMuted,liveDetails=true,fixedToolbarGeometry=geometry.Select(g=>new{g.State,g.Width,g.X,g.SoundX}),animationRespectsSystem=true};
        StopLive();await liveRetirement;return result;
    }
    internal async Task<object> VerificationLivePerformanceAsync()
    {
        await liveDetection;hdrRequested=false;vsrRequested=false;var runs=new List<object>();
        for(int n=0;n<3;n++)
        {
            await StartLiveAsync();var live=livePlayback??throw new InvalidOperationException(liveWarning??"No Live player");live.VerificationLoop=true;
            var watch=System.Diagnostics.Stopwatch.StartNew();while(live.Frames<12){if(watch.Elapsed.TotalSeconds>15)throw new TimeoutException("No Live frames");await Task.Delay(15);}
            var times=live.FrameMilliseconds.OrderBy(x=>x).ToArray();
            var state=new{iteration=n,live.RendererReadyMilliseconds,live.MediaOpenMilliseconds,live.SurfaceReadyMilliseconds,live.FirstFrameMilliseconds,live.Renderer.DeviceReused,frames=live.Frames,submitP95=times[(int)((times.Length-1)*.95)],gpuBytes=live.Renderer.Status().GpuBytes};
            StopLive();await liveRetirement;
            if(!live.PreparedOffUiThread||!live.ReleasedOffUiThread)throw new InvalidOperationException("Media preparation and retirement must stay off XAML");
            runs.Add(new{timing=state,live.PreparedOffUiThread,live.ReleasedOffUiThread});
        }
        return new{mode="SDR, no SDK success claim",runs};
    }
}
