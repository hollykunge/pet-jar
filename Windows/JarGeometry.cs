using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PetJar;

public static class Art
{
    private static readonly Dictionary<string, BitmapSource> cache = new();
    private static readonly Dictionary<string, BitmapSource> sprites = new();
    private static readonly Dictionary<string, (byte[] pixels,int width,int height)> spritePixels = new();
    private static readonly Dictionary<Client, BitmapSource> hitImages = new();
    public static Stream Resource(string suffix)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = Array.Find(assembly.GetManifestResourceNames(), x => x.EndsWith(suffix, StringComparison.Ordinal));
        return assembly.GetManifestResourceStream(name ?? throw new FileNotFoundException(suffix))!;
    }
    public static BitmapSource Image(string name)
    {
        if (cache.TryGetValue(name, out var image)) return image;
        using var stream = Resource(name + ".png");
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); cache[name] = bitmap; return bitmap;
    }
    public static BitmapSource Avatar(Client client)
        => Sprite(client.Sprite());
    internal static BitmapSource Sprite(string name)
    {
        if(sprites.TryGetValue(name,out var cached))return cached;
        var source=Image(name);
        var bitmap=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);
        int width=bitmap.PixelWidth,height=bitmap.PixelHeight;
        var pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);
        int left=width,top=height,right=-1,bottom=-1;
        // Ignore the asset's almost transparent canvas so all three visible
        // characters straddle the input edge at their visual midpoint.
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            if(pixels[(y*width+x)*4+3]>32) {
                left=Math.Min(left,x);right=Math.Max(right,x);
                top=Math.Min(top,y);bottom=Math.Max(bottom,y);
            }
        if(right<left)return source;
        left=Math.Max(0,left-4);top=Math.Max(0,top-4);
        right=Math.Min(width-1,right+4);bottom=Math.Min(height-1,bottom+4);
        var cropped=new CroppedBitmap(source,new Int32Rect(left,top,right-left+1,bottom-top+1));
        cropped.Freeze();sprites[name]=cropped;return cropped;
    }
    internal static bool SpriteContains(string name,Point unit)
    {
        if(unit.X<0||unit.X>=1||unit.Y<0||unit.Y>=1)return false;
        if(!spritePixels.TryGetValue(name,out var data)) {
            var source=Sprite(name);double scale=Math.Min(1,256d/Math.Max(source.PixelWidth,source.PixelHeight));
            var bitmap=new FormatConvertedBitmap(new TransformedBitmap(source,new ScaleTransform(scale,scale)),PixelFormats.Bgra32,null,0);
            var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
            var alpha=new byte[bitmap.PixelWidth*bitmap.PixelHeight];for(int i=0;i<alpha.Length;i++)alpha[i]=pixels[i*4+3];
            spritePixels[name]=data=(alpha,bitmap.PixelWidth,bitmap.PixelHeight);
        }
        return data.pixels[(int)(unit.Y*data.height)*data.width+(int)(unit.X*data.width)]>64;
    }
    public static bool AvatarContains(Client client,Rect frame,Point point)
    {
        if(!frame.Contains(point))return false;
        if(!hitImages.TryGetValue(client,out var bitmap)) {
            bitmap=new FormatConvertedBitmap(Avatar(client),PixelFormats.Bgra32,null,0);bitmap.Freeze();hitImages[client]=bitmap;
        }
        int x=Math.Min(bitmap.PixelWidth-1,(int)((point.X-frame.X)/frame.Width*bitmap.PixelWidth));
        int y=Math.Min(bitmap.PixelHeight-1,(int)((point.Y-frame.Y)/frame.Height*bitmap.PixelHeight));
        var pixel=new byte[4];bitmap.CopyPixels(new Int32Rect(x,y,1,1),pixel,4,0);return pixel[3]>64;
    }
}

// AppKit uses a lower-left origin. The Windows port reflects Y once, here,
// and uses the SAME sceneRect for the stationary art, eye patches and hit regions.
public static class JarGeometry
{
    public const double Size = 260;
    private static readonly JsonDocument layout = JsonDocument.Parse(Art.Resource("SceneLayout.json"));
    public static readonly Rect SceneRect = RectFrom(layout.RootElement.GetProperty("sceneRect"));
    public static readonly MatrixTransform GlassTransform = new(new Matrix(.90, 0, 0, .967, 13, .066));
    public static readonly Geometry Body = new Shape().M(24,246).L(236,246)
        .C(235,236,249,235,250,219).C(254,170,254,100,251,61).C(250,38,243,27,222,24)
        .C(174,16,86,16,38,24).C(17,27,10,38,9,61).C(6,100,6,170,10,219).C(11,235,25,236,24,246).Close();
    public static readonly Geometry Interior = MakeInterior();
    public static readonly IReadOnlyDictionary<Client, Geometry> Regions = MakeRegions();
    private static string Key(Client client) => client.ToString().ToLowerInvariant();
    private static Rect RectFrom(JsonElement element)
    {
        var a = new List<double>(); foreach (var value in element.EnumerateArray()) a.Add(value.GetDouble());
        return new(a[0], a[1], a[2], a[3]);
    }
    private static Geometry MakeInterior()
    {
        var shape = new Shape().M(24,246).C(26,259,234,259,236,246).C(234,236,241,231,242,218)
            .C(247,165,247,100,243,65).C(243,46,243,40,228,38).C(170,30,90,30,32,38)
            .C(17,40,17,46,17,65).C(13,100,13,165,18,218).C(19,231,26,236,24,246).Close();
        shape.Transform = GlassTransform; shape.Freeze(); return shape;
    }
    public static Point ScenePoint(double x, double y) => new(SceneRect.X + x * SceneRect.Width, SceneRect.Y + y * SceneRect.Height);
    // Extra sprite placement is expressed in the same normalized scene as the
    // pile, eyes and hit regions; no independent window-relative coordinates.
    public static readonly Rect ClaudeFrame=MakeClaudeFrame();
    private static Rect MakeClaudeFrame()
    {
        var r=RectFrom(layout.RootElement.GetProperty("sprites").GetProperty("claude"));
        var p=ScenePoint(r.X,r.Y);return new(p.X,p.Y,r.Width*SceneRect.Width,r.Height*SceneRect.Height);
    }
    private static IReadOnlyDictionary<Client, Geometry> MakeRegions()
    {
        var result = new Dictionary<Client, Geometry>();
        foreach (var client in Enum.GetValues<Client>())
        {
            var points = new List<Point>();
            foreach (var raw in layout.RootElement.GetProperty("regions").GetProperty(Key(client)).EnumerateArray())
                points.Add(ScenePoint(raw[0].GetDouble(), raw[1].GetDouble()));
            var path = new PathGeometry(); var figure = new PathFigure { StartPoint = points[0], IsClosed = true };
            for (int i=0; i<points.Count; i++)
            {
                var previous=points[(i+points.Count-1)%points.Count]; var current=points[i];
                var next=points[(i+1)%points.Count]; var following=points[(i+2)%points.Count];
                figure.Segments.Add(new BezierSegment(current+(next-previous)/6, next-(following-current)/6, next, true));
            }
            path.Figures.Add(figure); path.Freeze(); result[client]=path;
        }
        return result;
    }
    public static IEnumerable<Rect> Eyes(Client client)
    {
        foreach (var raw in layout.RootElement.GetProperty("eyes").GetProperty(Key(client)).EnumerateArray())
        {
            var r = RectFrom(raw); var p = ScenePoint(r.X, r.Y);
            yield return new(p.X,p.Y,r.Width*SceneRect.Width,r.Height*SceneRect.Height);
        }
    }
    public static Client? Hit(Point point)
    {
        if (!Interior.FillContains(point)) return null;
        if(Art.AvatarContains(Client.Claude,ClaudeFrame,point))return Client.Claude;
        foreach (var client in new[] { Client.Muse, Client.Grok, Client.ChatGPT })
            if (Regions[client].FillContains(point)) return client;
        return null;
    }
    public static Rect MacRect(double x, double y, double width, double height) => new(x,Size-y-height,width,height);
    public static Geometry Oval(double x,double y,double width,double height) => new EllipseGeometry(MacRect(x,y,width,height));
}

internal sealed class Shape
{
    private readonly PathGeometry path = new();
    private PathFigure figure = new();
    public Shape M(double x,double y) { figure = new() { StartPoint=new(x,260-y) }; path.Figures.Add(figure); return this; }
    public Shape L(double x,double y) { figure.Segments.Add(new LineSegment(new(x,260-y),true)); return this; }
    public Shape C(double x1,double y1,double x2,double y2,double x,double y)
    { figure.Segments.Add(new BezierSegment(new(x1,260-y1),new(x2,260-y2),new(x,260-y),true)); return this; }
    public PathGeometry Open() => path;
    public PathGeometry Close() { figure.IsClosed=true; return path; }
}
