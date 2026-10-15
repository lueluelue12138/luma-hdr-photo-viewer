using Luma.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Luma.App;

public sealed partial class ViewerWindow
{
    private LivePhotoAsset? liveAsset;
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
            var asset=await LivePhotoReader.DetectAsync(path,token);
            if(closed||generation!=request||token.IsCancellationRequested)return;
            liveAsset=asset;LiveButton.Visibility=asset is null?Visibility.Collapsed:Visibility.Visible;
            if(asset is not null)ToolTipService.SetToolTip(LiveButton,$"{asset.Kind} · 播放 / 停止（L），空格查看原图");
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
        var token=liveCancellation.Token;LiveLabel.Text="准备中";LiveIcon.Glyph="\uE71A";LiveButton.SetValue(AutomationProperties.NameProperty,"取消实况准备");
        LivePlayback? next=null;
        try
        {
            next=await LivePlayback.CreateAsync(asset,LiveSurface,WinRT.Interop.WindowNative.GetWindowHandle(this),LiveOptions(),token);
            if(closed||generation!=request||play!=liveGeneration||token.IsCancellationRequested){next.Stop();return;}
            if(playingLiveWindow?.TryGetTarget(out var playing)==true&&playing!=this)playing.StopLive();
            livePlayback=next;playingLiveWindow=new(this);ConfigureTuning(false);next.SetOptions(LiveOptions());
            var currentLive=next;
            next.FirstFrame+=()=>{if(livePlayback!=currentLive||closed)return;Surface.Opacity=0;LiveSurface.Opacity=1;LiveLabel.Text="LIVE";UpdateGlass();RefreshStatus(false);};
            next.Ended+=message=>{if(livePlayback!=currentLive||closed)return;StopLive();if(message is not null)SetLiveWarning(message);};
            next.Renderer.Presented+=()=>{if(livePlayback==currentLive&&!closed){UpdateGlass();RefreshStatus(false);}};
            next.Renderer.Resize(Surface.ActualWidth,Surface.ActualHeight,Surface.CompositionScaleX);
            UpdateView();
            LiveMuteButton.Visibility=Visibility.Visible;LiveMuteIcon.Glyph="\uE74F";ToolTipService.SetToolTip(LiveMuteButton,"开启实况原声");
            LiveButton.SetValue(AutomationProperties.NameProperty,"停止实况播放");UpdateGlass();next.Play();
            App.Log($"Live started: {asset.Kind}; {next.Width}x{next.Height}; {next.Duration:0.###}s; embedded={asset.Embedded}");
        }
        catch(OperationCanceledException){next?.Stop();}
        catch(Exception ex){next?.Stop();if(livePlayback==next)StopLive();if(!closed&&generation==request)SetLiveWarning(ex.Message);}
        finally
        {
            if(next is not null&&next!=livePlayback)await next.Disposed;
            if(!closed&&generation==request&&play==liveGeneration){livePreparing=false;if(livePlayback is null){LiveLabel.Text="LIVE";LiveIcon.Glyph="\uE768";}RefreshStatus(false);}
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
        Surface.Opacity=1;LiveSurface.Opacity=0;LiveMuteButton.Visibility=Visibility.Collapsed;LiveLabel.Text="LIVE";LiveIcon.Glyph="\uE768";
        LiveButton.SetValue(AutomationProperties.NameProperty,"播放实况照片");
        if(old is not null)
        {
            App.Log($"Live stopped: frames={old.Frames}; coalescedNotifications={old.CoalescedNotifications}");old.Stop();liveRetirement=Task.WhenAll(liveRetirement,old.Disposed);
            if(!closed){renderer?.HoldOriginal(originalHeld);renderer?.Resize(Surface.ActualWidth,Surface.ActualHeight,Surface.CompositionScaleX);ConfigureTuning(currentFrame?.Info.NativeHdr!=0);UpdateView();UpdateGlass();RefreshStatus(false);if(applyStill)_=ApplyEffectsAsync();}
        }
    }
    private void LiveMuteClick(object sender,RoutedEventArgs e)
    {
        if(livePlayback is not {} live)return;live.Muted=!live.Muted;LiveMuteIcon.Glyph=live.Muted?"\uE74F":"\uE767";
        string label=live.Muted?"开启实况原声":"静音实况";ToolTipService.SetToolTip(LiveMuteButton,label);LiveMuteButton.SetValue(AutomationProperties.NameProperty,label);ShowControls();
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
}
