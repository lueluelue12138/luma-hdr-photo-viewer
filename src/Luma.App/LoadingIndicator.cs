using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Composition;
using System.Numerics;

namespace Luma.App;

// A small XAML path animated by the existing compositor, without activating an
// AnimatedVisualPlayer or loading its template during the window's first layout.
public sealed class LoadingIndicator : Microsoft.UI.Xaml.Controls.Grid
{
    public static readonly DependencyProperty IsActiveProperty=DependencyProperty.Register(nameof(IsActive),typeof(bool),typeof(LoadingIndicator),new PropertyMetadata(false,(d,e)=>((LoadingIndicator)d).Animate()));
    public bool IsActive {get=>(bool)GetValue(IsActiveProperty);set=>SetValue(IsActiveProperty,value);}
    private readonly Microsoft.UI.Xaml.Shapes.Path arc=new(){StrokeThickness=3,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Data=new PathGeometry{Figures={new PathFigure{StartPoint=new(20,4),Segments={new ArcSegment{Point=new(36,20),Size=new(16,16),SweepDirection=SweepDirection.Clockwise}}}}}};
    public LoadingIndicator()
    {
        Width=Height=40;AutomationProperties.SetName(this,"正在加载");
        var content=new Microsoft.UI.Xaml.Controls.Grid{Width=40,Height=40};
        content.Children.Add(new Ellipse{Margin=new(4),StrokeThickness=3,Stroke=new SolidColorBrush(Microsoft.UI.Colors.Gray),Opacity=.18});
        arc.Stroke=new SolidColorBrush(new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent));content.Children.Add(arc);Children.Add(new Microsoft.UI.Xaml.Controls.Viewbox{Child=content,Stretch=Stretch.Uniform});
        Loaded+=(_,_)=>Animate();Unloaded+=(_,_)=>Stop();
    }
    private void Stop()=>ElementCompositionPreview.GetElementVisual(arc).StopAnimation("RotationAngleInDegrees");
    private void Animate()
    {
        if(!IsLoaded)return;Stop();if(!IsActive||!Motion.Enabled)return;
        var visual=ElementCompositionPreview.GetElementVisual(arc);visual.CenterPoint=new Vector3(20,20,0);
        var spin=visual.Compositor.CreateScalarKeyFrameAnimation();spin.InsertKeyFrame(0,0);spin.InsertKeyFrame(1,360,visual.Compositor.CreateLinearEasingFunction());spin.Duration=TimeSpan.FromMilliseconds(900);spin.IterationBehavior=AnimationIterationBehavior.Forever;visual.StartAnimation("RotationAngleInDegrees",spin);
    }
}
