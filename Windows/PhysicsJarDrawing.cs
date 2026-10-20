using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace PetJar;

// Only the native runtime uses the independent sprites. JarDrawing remains the
// shared static reference renderer for the macOS geometry/eye atlas tests.
internal sealed class PhysicsJarDrawing:FrameworkElement
{
    private readonly VisualCollection layers;
    private readonly ContainerVisual contents=new() {Clip=JarGeometry.Interior};
    private readonly DrawingVisual hint=new();
    private readonly Dictionary<JarObject,ObjectVisual> visuals=new();
    private readonly Dictionary<Client,EyePose> poses=new();
    private Client? hovered;
    private bool showHint=true;
    internal JarPhysicsScene Scene {get;private set;}
    internal Client? Hovered {get=>hovered;set {if(hovered==value)return;hovered=value;DrawHint();}}
    internal bool ShowHint {get=>showHint;set {showHint=value;DrawHint();}}
    internal PhysicsJarDrawing(IEnumerable<Client> clients,IEnumerable<FillerKind>? fillers=null)
    {
        Scene=new(clients,fillers);layers=new(this);
        var back=new DrawingVisual();using(var dc=back.RenderOpen())JarDrawing.DrawGlassBack(dc);layers.Add(back);
        layers.Add(contents);SyncObjects();
        var front=new DrawingVisual();using(var dc=front.RenderOpen())JarDrawing.DrawGlassFront(dc);layers.Add(front);
        layers.Add(hint);DrawHint();UpdatePoses();
    }
    protected override int VisualChildrenCount=>layers.Count;
    protected override Visual GetVisualChild(int index)=>layers[index];
    internal bool Contains(Client client)=>Scene.Objects.Any(o=>o.Client==client);
    internal void SetPets(IEnumerable<Client> clients) {Scene.SetPets(clients);SyncObjects();if(Hovered is Client c&&!Contains(c))Hovered=null;DrawHint();}
    internal void SetFillers(IEnumerable<FillerKind> kinds)
    {
        var next=kinds.Where(Enum.IsDefined).Distinct().OrderBy(kind=>kind).ToArray();
        if(Scene.SelectedFillers.SequenceEqual(next))return;
        var pets=Scene.Objects.Where(o=>o.Client!=null).Select(o=>o.Client!.Value).ToArray();
        var replacement=new JarPhysicsScene(pets,next);
        Scene.Release();Scene=replacement;SyncObjects();
    }
    private void SyncObjects()
    {
        foreach(var item in visuals.Keys.Where(o=>!Scene.Objects.Contains(o)).ToArray()) {contents.Children.Remove(visuals[item]);visuals.Remove(item);}
        foreach(var item in Scene.Objects)if(!visuals.ContainsKey(item)) {
            var visual=new ObjectVisual(item);visuals[item]=visual;contents.Children.Add(visual);
            if(item.Client is Client client&&poses.TryGetValue(client,out var pose))visual.SetEyes(pose);
        }
        UpdatePoses();
    }
    internal EyePose GetEyes(Client client)=>poses.GetValueOrDefault(client);
    internal void SetEyes(Client client,EyePose pose) {
        poses[client]=pose;
        foreach(var (item,visual) in visuals)if(item.Client==client)visual.SetEyes(pose);
    }
    internal void UpdatePoses() {foreach(var (item,visual) in visuals)visual.Matrix.Matrix=item.Pose;}
    private void DrawHint()
    {
        using var dc=hint.RenderOpen();if(!ShowHint)return;
        var text=new FormattedText(Hovered?.Name()??"轻点 · 拖动物体 · 双击打开",CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"),9.5,JarDrawing.Brush(.35,.35,.35),VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text,new((260-text.Width)/2,242));
    }
    private sealed class ObjectVisual:ContainerVisual
    {
        private static readonly Dictionary<(string,Size,int),BitmapSource> shaded=new();
        internal MatrixTransform Matrix {get;}=new();
        private readonly List<EyelidVisual> eyes=new();
        internal ObjectVisual(JarObject item)
        {
            Transform=Matrix;
            var sprite=new DrawingVisual();var bounds=item.LocalBounds;bounds.Inflate(5,5);
            using(var dc=sprite.RenderOpen())dc.DrawImage(Shaded(item),bounds);Children.Add(sprite);
            if(item.Client is not Client client)return;
            foreach(var unit in EyeRects(client)) {
                var r=item.LocalBounds;
                var eye=new Rect(r.X+unit.X*r.Width,r.Y+unit.Y*r.Height,unit.Width*r.Width,unit.Height*r.Height);
                var lid=new EyelidVisual(client,eye,r);eyes.Add(lid);Children.Add(lid);
            }
        }
        internal void SetEyes(EyePose pose) {eyes[0].SetExpression(pose.Left);eyes[1].SetExpression(pose.Right);}
        private static BitmapSource Shaded(JarObject item)
        {
            var key=(item.Sprite,item.Size,item.Depth);if(shaded.TryGetValue(key,out var cached))return cached;
            // Cache the native raster and soft contact shadow together. This
            // avoids running a per-object blur on every physics frame.
            double width=item.Size.Width+10,height=item.Size.Height+10;
            var canvas=new Canvas {Width=width,Height=height};
            var image=new Image {Width=item.Size.Width,Height=item.Size.Height,Source=Art.Sprite(item.Sprite),
                Effect=new DropShadowEffect {Color=Color.FromRgb(42,48,40),BlurRadius=2.8,ShadowDepth=1.1,Direction=305,Opacity=item.Depth<0?.22:item.Client!=null?.22:.19,RenderingBias=RenderingBias.Quality}};
            Canvas.SetLeft(image,5);Canvas.SetTop(image,5);canvas.Children.Add(image);
            if(item.Depth<0) {
                // Shade only the source alpha. Keep opaque material opaque;
                // lowering the whole sprite opacity would expose the desktop.
                var shade=new Border {Width=item.Size.Width,Height=item.Size.Height,Background=new SolidColorBrush(Color.FromArgb(42,37,48,43)),
                    OpacityMask=new ImageBrush(Art.Sprite(item.Sprite)) {Stretch=Stretch.Fill}};
                Canvas.SetLeft(shade,5);Canvas.SetTop(shade,5);canvas.Children.Add(shade);
            }
            canvas.Measure(new(width,height));canvas.Arrange(new Rect(0,0,width,height));
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(width*4),(int)Math.Ceiling(height*4),384,384,PixelFormats.Pbgra32);bitmap.Render(canvas);bitmap.Freeze();
            shaded[key]=bitmap;return bitmap;
        }
    }
    internal static Rect[] EyeRects(Client client)=>client switch {
        Client.ChatGPT=>new[] {new Rect(.202,.310,.103,.058),new Rect(.418,.324,.133,.068)},
        Client.Grok=>new[] {new Rect(.415,.353,.150,.325),new Rect(.693,.344,.141,.325)},
        Client.Muse=>new[] {new Rect(.337,.258,.079,.071),new Rect(.580,.258,.079,.071)},
        _=>new[] {new Rect(.340,.360,.074,.140),new Rect(.594,.360,.074,.140)}
    };
}
