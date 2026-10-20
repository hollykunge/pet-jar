using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PetJar;

internal static class MaterialSquashTests
{
    private static void Check(bool condition,string reason) {if(!condition)throw new InvalidOperationException(reason);}
    private const double dt=1d/120;
    private static MaterialSquash Plush()=>new(SquashMaterial.For(Client.Muse,Client.Muse.Sprite()));
    private static void Load(MaterialSquash material,Vector axis,double weight,int steps) {
        for(int i=0;i<steps;i++) {material.ClearLoad();material.AddLoad(axis,weight);material.Advance(dt);}
    }
    internal static void Run()
    {
        var vertical=Plush();Load(vertical,new(0,1),3,240);var pose=vertical.Transform;
        Check(pose.M22<.97&&pose.M11>1&&Math.Abs(pose.M12)<.0001,"vertical plush pressure shortens height and bulges sideways");
        Load(vertical,new(0,1),3,240);
        Check(Math.Abs(vertical.Transform.M22-pose.M22)<.0005,"sustained pressure remains stable without pumping a repeated impact");
        var horizontal=Plush();Load(horizontal,new(1,0),3,240);
        Check(horizontal.Transform.M11<.97&&horizontal.Transform.M22>1,"horizontal pressure follows its own axis");
        var diagonal=Plush();Load(diagonal,new(1,1),3,240);
        var normal=new Vector(1,1);normal.Normalize();var tangent=new Vector(-normal.Y,normal.X);
        Check(diagonal.Transform.Transform(normal).Length<.97&&diagonal.Transform.Transform(tangent).Length>1,"diagonal pressure uses a shared directional transform");
        double pressed=vertical.Compression;Load(vertical,new(0,1),0,6);
        Check(vertical.Compression>pressed*.4,"pressure release rebounds gradually");
        Load(vertical,new(0,1),0,300);Check(vertical.Compression<.0005,"unloaded plush returns to its original shape");
        var grip=Plush();grip.Hold(new(1,0));Load(grip,new(0,0),0,120);
        Check(grip.Transform.M11<.95&&grip.Transform.M22>1,"holding a plush body gives a gentle pinch");
        grip.Release();Load(grip,new(0,0),0,300);Check(grip.Compression<.0005,"releasing a grip clears the squeeze");
        var hard=new MaterialSquash(SquashMaterial.For(Client.Grok,Client.Grok.Sprite()));
        hard.Hold(new(1,1));hard.Impact(new(1,1),100);Load(hard,new(1,1),1000,120);
        Check(hard.Transform.IsIdentity,"mirror metal stays rigid under the same inputs");
        foreach(string sprite in new[] {"prop-chrome","prop-stone","prop-wood","prop-blue","prop-amber"})Check(SquashMaterial.For(null,sprite).Limit==0,"hard material does not flatten: "+sprite);
        foreach(var client in new[] {Client.ChatGPT,Client.Muse,Client.Claude})Check(SquashMaterial.For(client,client.Sprite()).Limit>0,"furry pet has soft material: "+client);
        var impact=Plush();impact.Impact(new(0,1),3);Load(impact,new(0,0),0,5);
        Check(impact.Compression>.006,"a collision visibly compresses soft material");
        for(int i=0;i<360;i++) {
            impact.Impact(new(Math.Cos(i),Math.Sin(i)),100);Load(impact,new(1,1),1000,1);
            Check(impact.Compression<=impact.Limit+.00001&&impact.Transform.Determinant>.8,"repeated impacts cannot invert or over-compress plush");
        }
        impact.Release();Load(impact,new(0,0),0,600);Check(impact.Compression<.0005,"strong impacts still recover");
    }
    internal static void Render(string path)
    {
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen()) {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(241,244,238)),null,new Rect(0,0,800,390));
            string[] labels={"自然状态","轻握","堆叠受压","解除后恢复"};
            for(int i=0;i<4;i++) {
                var soft=Plush();if(i==1)soft.Hold(new(1,0));if(i==2)Load(soft,new(0,1),5,240);
                if(i==1)Load(soft,new(0,0),0,120);
                if(i==3) {soft.Hold(new(1,0));Load(soft,new(0,0),0,120);soft.Release();Load(soft,new(0,0),0,300);}
                double x=i*200+100;var transform=soft.Transform;transform.Translate(x,86);
                dc.PushTransform(new MatrixTransform(transform));dc.DrawImage(Art.Avatar(Client.Muse),new Rect(-52,-58,104,116));dc.Pop();
                var label=new FormattedText(labels[i],CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),13,Brushes.DimGray,1);
                dc.DrawText(label,new(x-label.Width/2,155));
                dc.DrawImage(Art.Avatar(Client.Grok),new Rect(x-48,215,96,96));
            }
            var note=new FormattedText("离屏材质对比 · 毛绒轻微变形，硬质保持原形",CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),13,Brushes.DimGray,1);
            dc.DrawText(note,new((800-note.Width)/2,350));
        }
        var bitmap=new RenderTargetBitmap(1600,780,192,192,PixelFormats.Pbgra32);bitmap.Render(visual);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);png.Save(file);
    }
}
