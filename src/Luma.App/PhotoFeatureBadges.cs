using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Luma.Core;

namespace Luma.App;

public sealed partial class PhotoFeatureBadges:UserControl
{
    public static readonly DependencyProperty FilePathProperty=DependencyProperty.Register(nameof(FilePath),typeof(string),typeof(PhotoFeatureBadges),new PropertyMetadata(null,(d,e)=>((PhotoFeatureBadges)d).Refresh()));
    public string? FilePath{get=>(string?)GetValue(FilePathProperty);set=>SetValue(FilePathProperty,value);}
    private CancellationTokenSource? cancellation;
    private bool active;
    internal PhotoFeatures? VerificationFeatures {get;private set;}
    internal bool VerificationHdr=>hdr.Visibility==Visibility.Visible;
    internal bool VerificationLive=>live.Visibility==Visibility.Visible;
    internal Brush VerificationBrush=>hdr.Background;
    internal Brush VerificationForeground=>((TextBlock)hdr.Child).Foreground;
    public PhotoFeatureBadges()
    {
        InitializeComponent();Hide();
        Loaded+=(_,_)=>{active=true;PhotoFeaturesCache.Invalidated+=Invalidated;Refresh();};
        Unloaded+=(_,_)=>{active=false;PhotoFeaturesCache.Invalidated-=Invalidated;cancellation?.Cancel();};
    }
    private void Hide(){hdr.Visibility=live.Visibility=Visibility.Collapsed;VerificationFeatures=null;}
    private void Invalidated(string prefix){if(FilePath?.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)==true)Refresh();}
    private async void Refresh()
    {
        cancellation?.Cancel();cancellation?.Dispose();cancellation=null;Hide();string? path=FilePath;if(!active||string.IsNullOrEmpty(path))return;
        var pending=cancellation=new();var token=pending.Token;
        try
        {
            // Scrolling can recycle many cards in a few milliseconds. Wait
            // before entering the two metadata slots, and cancel unseen work.
            await Task.Delay(180,token);var traits=await PhotoFeaturesCache.ReadAsync(path,token);
            if(token.IsCancellationRequested||!active||FilePath!=path)return;
            VerificationFeatures=traits;hdr.Visibility=traits.HdrKind is null?Visibility.Collapsed:Visibility.Visible;live.Visibility=traits.Live is null?Visibility.Collapsed:Visibility.Visible;
        }
        catch(OperationCanceledException){}catch(Exception ex){App.Log("Photo feature badge: "+ex.Message);/* Optional traits never prevent a thumbnail. */}
    }
}
