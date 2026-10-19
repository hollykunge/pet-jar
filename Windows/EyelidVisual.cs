using System;
using System.Windows;
using System.Windows.Media;

namespace PetJar;

// A blink changes the height of one opaque eye, never the alpha of two eyes.
// A fixed local face plate covers the original eye baked into pile-scene.
// At the fully shut seam the isolated feature has zero height,
// so the source can change to the closed-eye artwork without a double exposure.
internal sealed class EyelidVisual : ContainerVisual
{
    private const double ShutSeam=.72;
    private readonly EyeSurface open,closed;
    private readonly bool originallyClosed;
    internal EyelidVisual(Client client,Rect eye)
    {
        originallyClosed=client==Client.ChatGPT;
        Clip=new CombinedGeometry(GeometryCombineMode.Intersect,JarGeometry.Interior,JarGeometry.Regions[client]);
        OpacityMask=new RadialGradientBrush {
            GradientStops=new GradientStopCollection {new(Colors.White,0),new(Colors.White,.92),new(Colors.Transparent,1)} };
        var art=EyeArtwork.For(client,eye);
        var face=new DrawingVisual();using(var dc=face.RenderOpen())dc.DrawImage(art.Face,eye);Children.Add(face);
        open=new EyeSurface(art.Open,art.OpenRect);
        closed=new EyeSurface(art.Closed,art.ClosedRect);
        Children.Add(open);Children.Add(closed);SetExpression(0);
    }
    internal void SetExpression(double expression)
    {
        double closure=originallyClosed?1-expression:expression;
        bool useOpen=closure<ShutSeam;
        open.Opacity=useOpen?1:0;closed.Opacity=useOpen?0:1;
        if(useOpen)open.SetHeight(1-closure/ShutSeam);
        else closed.SetHeight((closure-ShutSeam)/(1-ShutSeam));
    }
    private sealed class EyeSurface : ContainerVisual
    {
        private readonly double center;
        private readonly ScaleTransform stretch=new();
        internal EyeSurface(System.Windows.Media.Imaging.BitmapSource sprite,Rect bounds)
        {
            center=bounds.Top+bounds.Height/2;
            stretch.CenterY=center;Transform=stretch;
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawImage(sprite,bounds);Children.Add(visual);
        }
        internal void SetHeight(double amount)
        {
            stretch.ScaleY=Math.Clamp(amount,0,1);
        }
    }
}
