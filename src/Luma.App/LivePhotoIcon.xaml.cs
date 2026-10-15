using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace Luma.App;

// Original vector artwork: the concentric Live Photo idiom stays legible at
// badge sizes. Only the viewer's active symbol animates; gallery icons are static.
public sealed partial class LivePhotoIcon:UserControl
{
    private int state;
    private bool animate,spinning;
    internal int VerificationState=>state;
    internal bool VerificationAnimating=>animate;
    internal bool VerificationSpinning=>spinning;
    public LivePhotoIcon()
    {
        InitializeComponent();
        // An explicit even count closes exactly and has opposite partners.
        // A dashed ellipse leaves a fractional final gap and looks asymmetric.
        var dots=new GeometryGroup();for(int n=0;n<36;n++)
        {
            double angle=n*Math.Tau/36-Math.PI/2;
            dots.Children.Add(new EllipseGeometry{Center=new(12+10.5*Math.Cos(angle),12+10.5*Math.Sin(angle)),RadiusX=.5,RadiusY=.5});
        }
        Dots.Data=dots;
        Loaded+=(_,_)=>ApplyMotion();Unloaded+=(_,_)=>StopMotion();
    }
    internal bool VerificationCentered
    {
        get
        {
            if(Dots.Data is not GeometryGroup dots||dots.Children.Count!=36)return false;
            for(int n=0;n<18;n++)
            {
                var a=(EllipseGeometry)dots.Children[n];var b=(EllipseGeometry)dots.Children[n+18];
                if(Math.Abs(a.Center.X+b.Center.X-24)>1e-6||Math.Abs(a.Center.Y+b.Center.Y-24)>1e-6)return false;
            }
            foreach(var ring in new FrameworkElement[]{OuterRing,MiddleRing,InnerRing})
            {
                var center=ring.TransformToVisual(Symbol).TransformPoint(new(ring.ActualWidth/2,ring.ActualHeight/2));
                if(Math.Abs(center.X-12)>.01||Math.Abs(center.Y-12)>.01)return false;
            }
            return true;
        }
    }
    internal void SetState(bool preparing,bool playing)
    {
        int next=preparing?1:playing?2:0;if(state==next)return;state=next;ApplyMotion();
    }
    private void StopMotion()
    {
        var visual=ElementCompositionPreview.GetElementVisual(OuterRing);
        visual.StopAnimation("Scale");visual.StopAnimation("Opacity");visual.StopAnimation("RotationAngleInDegrees");
        visual.Scale=Vector3.One;visual.Opacity=1;visual.RotationAngleInDegrees=0;animate=spinning=false;
    }
    private void ApplyMotion()
    {
        StopMotion();if(!IsLoaded||state==0||!Motion.Enabled)return;
        var visual=ElementCompositionPreview.GetElementVisual(OuterRing);visual.CenterPoint=new(12,12,0);
        var pulse=visual.Compositor.CreateScalarKeyFrameAnimation();pulse.InsertKeyFrame(0,1);pulse.InsertKeyFrame(.5f,state==1?.35f:.55f);pulse.InsertKeyFrame(1,1);
        pulse.Duration=TimeSpan.FromMilliseconds(state==1?1200:1600);pulse.IterationBehavior=AnimationIterationBehavior.Forever;visual.StartAnimation("Opacity",pulse);
        if(state==2)
        {
            var breathe=visual.Compositor.CreateVector3KeyFrameAnimation();breathe.InsertKeyFrame(0,Vector3.One);breathe.InsertKeyFrame(.5f,new(.88f,.88f,1));breathe.InsertKeyFrame(1,Vector3.One);
            breathe.Duration=pulse.Duration;breathe.IterationBehavior=AnimationIterationBehavior.Forever;visual.StartAnimation("Scale",breathe);
            var spin=visual.Compositor.CreateScalarKeyFrameAnimation();spin.InsertKeyFrame(0,0);spin.InsertKeyFrame(1,360,visual.Compositor.CreateLinearEasingFunction());
            spin.Duration=TimeSpan.FromSeconds(4);spin.IterationBehavior=AnimationIterationBehavior.Forever;visual.StartAnimation("RotationAngleInDegrees",spin);spinning=true;
        }
        animate=true;
    }
}
