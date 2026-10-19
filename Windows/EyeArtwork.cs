using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PetJar;

// Prepare small native drawing resources once. The face is kept stationary;
// only the isolated eye feature is transformed by the compositor.
internal sealed class EyeArtwork
{
    private static readonly Dictionary<(Client,Rect),EyeArtwork> cache=new();
    internal BitmapSource Face {get;}
    internal BitmapSource Open {get;}
    internal BitmapSource Closed {get;}
    internal Rect OpenRect {get;}
    internal Rect ClosedRect {get;}
    internal static EyeArtwork For(Client client,Rect eye)
    {
        if(!cache.TryGetValue((client,eye),out var art))cache[(client,eye)]=art=new(client,eye);
        return art;
    }
    private EyeArtwork(Client client,Rect eye)
    {
        bool nativeLid=client==Client.Claude;
        var original=nativeLid?Art.Avatar(client):Art.Image("pile-scene");
        var expression=nativeLid?original:Art.Image("pile-expressions");
        var scene=nativeLid?JarGeometry.ClaudeFrame:JarGeometry.SceneRect;
        int x=(int)Math.Round((eye.X-scene.X)/scene.Width*original.PixelWidth);
        int y=(int)Math.Round((eye.Y-scene.Y)/scene.Height*original.PixelHeight);
        int width=(int)Math.Round(eye.Width/scene.Width*original.PixelWidth);
        int height=(int)Math.Round(eye.Height/scene.Height*original.PixelHeight);
        var crop=new Int32Rect(x,y,width,height);
        byte[] Read(BitmapSource image) {
            var converted=new FormatConvertedBitmap(new CroppedBitmap(image,crop),PixelFormats.Bgra32,null,0);
            var pixels=new byte[width*height*4];converted.CopyPixels(pixels,width*4,0);return pixels;
        }
        var basePixels=Read(original);var alternate=Read(expression);
        var openPixels=client==Client.ChatGPT?alternate:basePixels;
        var closedPixels=client==Client.ChatGPT?basePixels:alternate;
        var openMask=Feature(openPixels,width,height,client==Client.Grok);
        var closedMask=nativeLid?new bool[width*height]:Feature(closedPixels,width,height,client==Client.Grok);
        var occupied=new bool[width*height];
        for(int i=0;i<occupied.Length;i++)occupied[i]=openMask[i]||closedMask[i];
        var face=(byte[])basePixels.Clone();
        // Interpolate the local material only where either eye shape used to be.
        // All surrounding highlights, glasses, fur and face pixels remain fixed.
        for(int row=0;row<height;row++)
        {
            int left=width,right=-1;
            for(int col=0;col<width;col++)if(occupied[row*width+col]) {left=Math.Min(left,col);right=col;}
            if(right<left)continue;
            int a=Math.Max(0,left-2),b=Math.Min(width-1,right+2);
            for(int col=left;col<=right;col++)for(int channel=0;channel<3;channel++) {
                double t=(double)(col-a)/Math.Max(1,b-a);
                face[(row*width+col)*4+channel]=(byte)Math.Round(basePixels[(row*width+a)*4+channel]*(1-t)+basePixels[(row*width+b)*4+channel]*t);
            }
        }
        // A harmonic fill follows the surrounding face gradient in both axes;
        // row-only interpolation would leave horizontal bands in yellow fur.
        var material=new double[face.Length];for(int i=0;i<face.Length;i++)material[i]=face[i];
        for(int pass=0;pass<180;pass++)for(int row=1;row<height-1;row++)for(int col=1;col<width-1;col++)
        {
            int index=row*width+col;if(!occupied[index])continue;
            for(int channel=0;channel<3;channel++) {
                int p=index*4+channel;
                double average=(material[p-4]+material[p+4]+material[p-width*4]+material[p+width*4])*.25;
                material[p]=Math.Clamp(material[p]+1.5*(average-material[p]),0,255);
            }
        }
        for(int i=0;i<face.Length;i++)face[i]=(byte)Math.Round(material[i]);
        Face=Bitmap(face,width,height);
        (Open,OpenRect)=Sprite(openPixels,openMask,width,height,eye);
        if(nativeLid)(Closed,ClosedRect)=ClosedLid(OpenRect);
        else (Closed,ClosedRect)=Sprite(closedPixels,closedMask,width,height,eye);
    }
    private static (BitmapSource,Rect) ClosedLid(Rect open)
    {
        const int width=64,height=24;
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())
        {
            var curve=new StreamGeometry();using(var path=curve.Open()) {
                path.BeginFigure(new Point(6,7),false,false);path.BezierTo(new Point(19,20),new Point(45,20),new Point(58,7),true,false);
            }
            var pen=new Pen(new SolidColorBrush(Color.FromRgb(40,24,19)),5) {StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
            dc.DrawGeometry(null,pen,curve);
        }
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();
        return (bitmap,new Rect(open.X,open.Y+open.Height*.35,open.Width,open.Height*.38));
    }
    private static BitmapSource Bitmap(byte[] pixels,int width,int height)
    {
        var image=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);image.Freeze();return image;
    }
    private static bool[] Feature(byte[] pixels,int width,int height,bool bright)
    {
        var visited=new bool[width*height];var best=new List<int>();double bestScore=0;
        bool IsEye(int index) {
            int x=index%width,y=index/width;
            if(x<width*.10||x>width*.90||y<height*.10||y>height*.91)return false;
            int p=index*4;
            return bright?Math.Min(pixels[p],Math.Min(pixels[p+1],pixels[p+2]))>115:
                Math.Max(pixels[p],Math.Max(pixels[p+1],pixels[p+2]))<100;
        }
        for(int seed=0;seed<visited.Length;seed++)
        {
            if(visited[seed]||!IsEye(seed))continue;
            var found=new List<int>();var queue=new Queue<int>();queue.Enqueue(seed);visited[seed]=true;
            double cx=0,cy=0;
            while(queue.Count>0)
            {
                int index=queue.Dequeue();found.Add(index);int px=index%width,py=index/width;cx+=px;cy+=py;
                foreach(int next in new[] {index-1,index+1,index-width,index+width}) {
                    if(next<0||next>=visited.Length||Math.Abs(next%width-px)>1||visited[next]||!IsEye(next))continue;
                    visited[next]=true;queue.Enqueue(next);
                }
            }
            double dx=cx/found.Count/width-.5,dy=cy/found.Count/height-.5;
            double score=found.Count/(1+12*(dx*dx+dy*dy));
            if(score>bestScore) {bestScore=score;best=found;}
        }
        if(best.Count<8)throw new InvalidOperationException("Eye artwork feature could not be isolated.");
        // Fill enclosed specular highlights and dilate a few source pixels to
        // retain the original antialiased outline and its small contact shadow.
        var filled=new bool[width*height];
        var rowLeft=new int[height];Array.Fill(rowLeft,width);var rowRight=new int[height];Array.Fill(rowRight,-1);
        foreach(int index in best) {int row=index/width;rowLeft[row]=Math.Min(rowLeft[row],index%width);rowRight[row]=Math.Max(rowRight[row],index%width);}
        int padding=Math.Max(2,(int)Math.Round(Math.Min(width,height)*.04));
        for(int row=0;row<height;row++)if(rowRight[row]>=0)
            for(int yy=Math.Max(0,row-padding);yy<=Math.Min(height-1,row+padding);yy++)
                for(int xx=Math.Max(0,rowLeft[row]-padding);xx<=Math.Min(width-1,rowRight[row]+padding);xx++)filled[yy*width+xx]=true;
        return filled;
    }
    private static (BitmapSource,Rect) Sprite(byte[] source,bool[] mask,int width,int height,Rect eye)
    {
        var pixels=(byte[])source.Clone();int left=width,top=height,right=0,bottom=0;
        for(int i=0;i<mask.Length;i++)
        {
            if(!mask[i]) {pixels[i*4+3]=0;continue;}
            left=Math.Min(left,i%width);right=Math.Max(right,i%width);top=Math.Min(top,i/width);bottom=Math.Max(bottom,i/width);
        }
        var crop=new Int32Rect(left,top,right-left+1,bottom-top+1);
        var bitmap=new CroppedBitmap(Bitmap(pixels,width,height),crop);bitmap.Freeze();
        return (bitmap,new Rect(eye.X+eye.Width*left/width,eye.Y+eye.Height*top/height,eye.Width*crop.Width/width,eye.Height*crop.Height/height));
    }
}
