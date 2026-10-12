using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace Luma.App;
internal static class Motion
{
    private static readonly UISettings settings=new();
    public static bool Enabled=>settings.AnimationsEnabled;
    public static void Enter(UIElement element,float distance=12)
    {
        if(!Enabled)return;
        ElementCompositionPreview.SetIsTranslationEnabled(element,true);
        var visual=ElementCompositionPreview.GetElementVisual(element);
        var fade=visual.Compositor.CreateScalarKeyFrameAnimation();fade.InsertKeyFrame(0,0);fade.InsertKeyFrame(1,1);fade.Duration=TimeSpan.FromMilliseconds(260);
        var move=visual.Compositor.CreateVector3KeyFrameAnimation();move.InsertKeyFrame(0,new(0,distance,0));move.InsertKeyFrame(1,Vector3.Zero,visual.Compositor.CreateCubicBezierEasingFunction(new(.16f,1),new(.3f,1)));move.Duration=fade.Duration;
        visual.StartAnimation("Opacity",fade);visual.StartAnimation("Translation",move);
    }
    public static void Visibility(UIElement element,bool show,float hiddenOffset)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element,true);
        var visual=ElementCompositionPreview.GetElementVisual(element);float opacity=show?1:0;
        element.IsHitTestVisible=show;
        var old=visual.Opacity;visual.Opacity=opacity;
        var previous=visual.Properties.TryGetVector3("Translation",out var translation)==Microsoft.UI.Composition.CompositionGetValueStatus.Succeeded?translation:Vector3.Zero;
        var destination=new Vector3(0,show?0:hiddenOffset,previous.Z);visual.Properties.InsertVector3("Translation",destination);
        if(!Enabled){visual.StopAnimation("Opacity");visual.StopAnimation("Translation");return;}
        var fade=visual.Compositor.CreateScalarKeyFrameAnimation();fade.InsertKeyFrame(0,old);fade.InsertKeyFrame(1,opacity);fade.Duration=TimeSpan.FromMilliseconds(show?180:240);
        var move=visual.Compositor.CreateVector3KeyFrameAnimation();move.InsertKeyFrame(0,previous);move.InsertKeyFrame(1,destination);move.Duration=fade.Duration;
        visual.StartAnimation("Opacity",fade);visual.StartAnimation("Translation",move);
    }
    public static void Hover(FrameworkElement element,bool over)
    {
        if(!Enabled)return;
        var visual=ElementCompositionPreview.GetElementVisual(element);visual.CenterPoint=new((float)element.ActualWidth/2,(float)element.ActualHeight/2,0);
        var scale=visual.Compositor.CreateVector3KeyFrameAnimation();scale.InsertKeyFrame(1,new(over?1.018f:1));scale.Duration=TimeSpan.FromMilliseconds(150);visual.StartAnimation("Scale",scale);
    }
}
