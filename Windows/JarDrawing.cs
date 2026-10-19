using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PetJar;

public sealed class JarDrawing : FrameworkElement
{
    private readonly VisualCollection layers;
    private readonly Dictionary<Client,EyelidVisual[]> eyes=new();
    private readonly Dictionary<Client,EyePose> poses=new();
    private readonly DrawingVisual hint=new();
    private Client? hovered,selected;
    private bool showHint=true;
    public Client? Hovered { get=>hovered;set {if(hovered==value)return;hovered=value;DrawHint();} }
    public Client? Selected { get=>selected;set {if(selected==value)return;selected=value;DrawHint();} }
    public bool ShowHint { get=>showHint;set {if(showHint==value)return;showHint=value;DrawHint();} }
    public JarDrawing()
    {
        layers=new VisualCollection(this);
        var back=new DrawingVisual();using(var dc=back.RenderOpen())DrawBack(dc);layers.Add(back);
        foreach(var client in Enum.GetValues<Client>())
        {
            var pair=new List<EyelidVisual>();
            foreach(var eye in JarGeometry.Eyes(client))
            {
                var visual=new EyelidVisual(client,eye);
                pair.Add(visual);layers.Add(visual);
            }
            eyes[client]=pair.ToArray();poses[client]=default;
        }
        var front=new DrawingVisual();using(var dc=front.RenderOpen())DrawFront(dc);layers.Add(front);
        layers.Add(hint);DrawHint();
    }
    protected override int VisualChildrenCount=>layers.Count;
    protected override Visual GetVisualChild(int index)=>layers[index];
    protected override void OnDpiChanged(DpiScale oldDpi,DpiScale newDpi) {base.OnDpiChanged(oldDpi,newDpi);DrawHint();}
    internal EyePose GetEyes(Client client)=>poses[client];
    internal void SetEyes(Client client,EyePose pose)
    {
        pose=new(Math.Clamp(pose.Left,0,1),Math.Clamp(pose.Right,0,1));
        if(poses[client]==pose)return;
        poses[client]=pose;
        // Only isolated eye transforms change; the face replacement is opaque.
        eyes[client][0].SetExpression(pose.Left);eyes[client][1].SetExpression(pose.Right);
    }
    internal static Color Color(double r,double g,double b,double a=1) => System.Windows.Media.Color.FromArgb((byte)(a*255),(byte)(r*255),(byte)(g*255),(byte)(b*255));
    internal static SolidColorBrush Brush(double r,double g,double b,double a=1) => new(Color(r,g,b,a));
    private static SolidColorBrush White(double a) => Brush(1,1,1,a);
    private static LinearGradientBrush Gradient(Color[] colors, int angle=0)
    {
        var brush = new LinearGradientBrush { StartPoint=angle==90?new(0,1):angle==180?new(1,0):new(0,0), EndPoint=angle==90?new(0,0):angle==180?new(0,0):new(1,0) };
        for (int i=0;i<colors.Length;i++) brush.GradientStops.Add(new(colors[i],(double)i/(colors.Length-1)));
        return brush;
    }
    private static Pen Pen(Brush color,double width) => new(color,width) { StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round };
    private static Geometry Ring(Geometry outer, Geometry inner) => new CombinedGeometry(GeometryCombineMode.Exclude,outer,inner);
    private static void Lip(DrawingContext dc)
    {
        var mouth=JarGeometry.Oval(17,234,226,24);
        dc.DrawGeometry(Gradient(new[] { Color(1,1,1,.87),Color(.31,.46,.48,.54),Color(1,1,1,.94),Color(.56,.72,.72,.48),Color(1,1,1,.9) },90),null,
            Ring(mouth,JarGeometry.Oval(22,238,216,16)));
        dc.DrawGeometry(null,Pen(Brush(.24,.36,.38,.5),.6),mouth);
        dc.DrawGeometry(null,Pen(White(.9),.9),JarGeometry.Oval(17.9,234.8,224.2,22.4));
        dc.DrawGeometry(null,Pen(Brush(.26,.44,.46,.47),.75),JarGeometry.Oval(22,238,216,16));
        dc.DrawGeometry(null,Pen(White(.63),.65),JarGeometry.Oval(23,238.8,214,14.4));
    }
    private static void DrawBack(DrawingContext dc)
    {
        dc.PushTransform(JarGeometry.GlassTransform);
        // Rear layers always precede the contents.
        dc.DrawGeometry(Brush(0,0,0,.12),null,JarGeometry.Oval(23,21,214,13));
        dc.DrawGeometry(Gradient(new[] { Color(.53,.72,.74,.21),Color(1,1,1,.07),Color(1,1,1,.02),Color(.48,.65,.69,.23) }),null,JarGeometry.Body);
        dc.DrawGeometry(Gradient(new[] { Color(.35,.47,.47,.17),Color(1,1,1,.08) },90),null,JarGeometry.Oval(22,238,216,16));
        dc.PushClip(new RectangleGeometry(JarGeometry.MacRect(0,246,260,14))); Lip(dc); dc.Pop(); dc.Pop();

        dc.PushClip(JarGeometry.Interior);
        dc.DrawImage(Art.Image("pile-scene"),JarGeometry.SceneRect);
        dc.DrawImage(Art.Avatar(Client.Claude),JarGeometry.ClaudeFrame);
        dc.Pop();
    }
    private static void DrawFront(DrawingContext dc)
    {
        dc.PushTransform(JarGeometry.GlassTransform);
        dc.PushClip(JarGeometry.Body);
        dc.DrawGeometry(Gradient(new[] { Color(.30,.53,.57,.35),Color(1,1,1,.28),Colors.Transparent,Colors.Transparent,Colors.Transparent,Color(1,1,1,.13),Color(.30,.50,.54,.36) }),null,JarGeometry.Body);
        var edges=new GeometryGroup(); edges.Children.Add(new RectangleGeometry(JarGeometry.MacRect(13,48,11,183))); edges.Children.Add(new RectangleGeometry(JarGeometry.MacRect(236,48,11,183)));
        dc.PushClip(edges); dc.PushOpacity(.23);
        dc.DrawImage(Art.Image("pile-scene"),JarGeometry.MacRect(13,39,234,216)); dc.Pop(); dc.Pop();
        // Only the front half of the base is allowed over the contents.
        dc.PushClip(new RectangleGeometry(JarGeometry.MacRect(0,0,260,37)));
        dc.DrawGeometry(Gradient(new[] { Color(.32,.53,.55,.4),Color(1,1,1,.72),Color(.66,.80,.79,.38),Color(1,1,1,.72) },90),null,
            Ring(JarGeometry.Oval(12,22,236,30),JarGeometry.Oval(22,32,216,20))); dc.Pop();
        dc.DrawGeometry(null,Pen(White(.92),1.4),new Shape().M(30,31).C(75,19,185,19,230,31).Open());
        foreach (var left in new[] { true,false })
        {
            double x=left?21:239, sign=left?1:-1;
            var ribbon=new Shape().M(x,220).C(x-6*sign,184,x-6*sign,111,x-2*sign,77)
                .C(x,69,x+5*sign,68,x+5*sign,72).C(x+2*sign,118,x+2*sign,184,x+8*sign,219).Close();
            dc.DrawGeometry(Gradient(new[] {Color(1,1,1,left?.58:.30),Color(1,1,1,.02)},left?0:180),null,ribbon);
            dc.DrawGeometry(null,Pen(White(left?.88:.65),left?1.3:.9),new Shape().M(x,214).C(x-5*sign,185,x-4*sign,144,x-2*sign,113).Open());
        }
        dc.Pop();
        var walls=new Shape().M(24,245).C(24,234,11,235,10,218).C(6,170,6,100,9,61).C(10,38,17,27,38,24)
            .C(86,16,174,16,222,24).C(243,27,250,38,251,61).C(254,100,254,170,250,218).C(249,235,236,234,236,245).Open();
        dc.DrawGeometry(null,Pen(Brush(.27,.40,.42,.43),1),walls);
        dc.PushTransform(new ScaleTransform(.99,.99,130,130)); dc.DrawGeometry(null,Pen(White(.9),1.1),walls); dc.Pop();
        dc.PushClip(new RectangleGeometry(JarGeometry.MacRect(0,230,260,16))); Lip(dc); dc.Pop(); dc.Pop();
    }
    private void DrawHint()
    {
        using var dc=hint.RenderOpen();
        if (ShowHint)
        {
            var text=new FormattedText(Hovered?.Name()??Selected?.Name()??"双击打开 · 悬停看额度",CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"),10,Brush(.35,.35,.35),VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text,new((260-text.Width)/2,242));
        }
    }
}
