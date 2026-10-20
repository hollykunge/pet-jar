using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PetJar;

internal static class SelfTests
{
    private static void Check(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
    internal static void Run()
    {
        PhysicsTests.Run();
        JarMotionTests.Run();
        MaterialSquashTests.Run();
        InteractionQuotaTests.Run();
        CollectionTests.Run();
        FillerSelectionTests.Run();
        QuotaCardTests.Run();
        foreach(var retired in new[] {"ComposerWindow","DesktopDelivery","PetModel","DeliveryRules","DeliveryResult"})
            Check(typeof(SelfTests).Assembly.GetType("PetJar."+retired)==null,"retired message feature must not ship: "+retired);
        var legacy=JsonSerializer.Deserialize<Settings>("{\"Executables\":{\"ChatGPT\":\"old-chatgpt.exe\",\"Grok\":\"old-grok.exe\",\"Muse\":\"old-muse.exe\"}}")!;
        legacy.Executables[Client.Claude]="selected-claude.exe";
        var restored=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(legacy))!;
        Check(restored.Executables[Client.ChatGPT]=="old-chatgpt.exe" && restored.Executables[Client.Muse]=="old-muse.exe" && restored.Executables[Client.Claude]=="selected-claude.exe","adding Claude preserves existing bindings and its independent selection");
        var routes=new[] {(new Point(99,63),Client.ChatGPT),(new Point(180,115),Client.Grok),(new Point(101,161),Client.Muse),(new Point(182,181),Client.Claude)};
        foreach(var (point,client) in routes) Check(JarGeometry.Hit(point)==client,"actual outline hit: "+client);
        foreach(var point in new[] {new Point(158,50),new Point(155,218),new Point(224,154),new Point(8,12),new Point(8,-55)})
            Check(JarGeometry.Hit(point)==null,"decoration must never route: "+point);
        // Check original nontransparent artwork against the transformed cavity.
        var bitmap=new FormatConvertedBitmap(Art.Image("pile-scene"),PixelFormats.Bgra32,null,0);
        var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
        for(int y=0;y<bitmap.PixelHeight;y+=4)for(int x=0;x<bitmap.PixelWidth;x+=4)
            if(pixels[(y*bitmap.PixelWidth+x)*4+3]>64)
                Check(JarGeometry.Interior.FillContains(JarGeometry.ScenePoint((x+.5)/bitmap.PixelWidth,(y+.5)/bitmap.PixelHeight)),"artwork outside glass");
        var claude=new FormatConvertedBitmap(Art.Avatar(Client.Claude),PixelFormats.Bgra32,null,0);
        var claudePixels=new byte[claude.PixelWidth*claude.PixelHeight*4];claude.CopyPixels(claudePixels,claude.PixelWidth*4,0);
        var spriteFrame=JarGeometry.ClaudeFrame;
        for(int y=0;y<claude.PixelHeight;y+=8)for(int x=0;x<claude.PixelWidth;x+=8)
            if(claudePixels[(y*claude.PixelWidth+x)*4+3]>64) {
                var point=new Point(spriteFrame.X+(x+.5)/claude.PixelWidth*spriteFrame.Width,spriteFrame.Y+(y+.5)/claude.PixelHeight*spriteFrame.Height);
                Check(JarGeometry.Interior.FillContains(point),"Claude artwork outside glass");
                Check(JarGeometry.Hit(point)==Client.Claude,"Claude opaque artwork and pointer routing must agree");
            }
        var baseline=Render(null);
        foreach(var client in Enum.GetValues<Client>())
        {
            _=Art.Image(client.Sprite());var eyes=new List<Rect>(JarGeometry.Eyes(client));
            foreach(var eye in eyes)Check(JarGeometry.Interior.FillContains(eye.TopLeft)&&JarGeometry.Interior.FillContains(eye.BottomRight),"eyes inside cavity");
            var changed=Render(client);
            int differences=0;
            for(int y=0;y<520;y++)for(int x=0;x<520;x++)
            {
                int offset=(y*520+x)*4;bool differs=false;for(int c=0;c<4;c++)if(baseline[offset+c]!=changed[offset+c])differs=true;
                if(!differs)continue;differences++;
                Check(eyes.Exists(r=>{r.Inflate(1,1);return r.Contains(new Point(x/2d,y/2d));}),"hover changed body/glass pixels: "+client);
            }
            Check(differences>20,"eye patch must visibly change: "+client);
        }
        var pet=new PetWindow(new Settings()) {FreezeExpression=true};
        int opened=0;pet.OpenRequested+=_=>opened++;
        foreach(var button in ((Canvas)pet.Content).Children.OfType<Button>())button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(opened==0,"a generic button click cannot bypass the double-click gesture");
        foreach(var client in Enum.GetValues<Client>()) {pet.SetHover(client);Check(pet.Drawing.GetEyes(client)==new EyePose(1,1) && pet.Drawing.Hovered==client,"exclusive hover");}
        pet.SetHover(null);foreach(var client in Enum.GetValues<Client>())Check(pet.Drawing.GetEyes(client)==default,"frozen idle has no animation");
        pet.Close();
        TestAnimation();
    }
    private static RenderTargetBitmap Bitmap(Client? client)
    {
        var drawing=new JarDrawing {Width=260,Height=260,ShowHint=false,Hovered=client};
        if(client is Client targetClient)drawing.SetEyes(targetClient,new(1,1));
        drawing.Measure(new(260,260));drawing.Arrange(new(0,0,260,260));drawing.UpdateLayout();
        var target=new RenderTargetBitmap(520,520,192,192,PixelFormats.Pbgra32);target.Render(drawing);return target;
    }
    private static byte[] Render(Client? client) {var target=Bitmap(client);var pixels=new byte[520*520*4];target.CopyPixels(pixels,520*4,0);return pixels;}
    internal static void RenderToFile(Client? client,string path)
    {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(Bitmap(client)));using var file=File.Create(path);encoder.Save(file);}

    internal static void RenderBlinkStrip(string path)
    {
        var strip=new DrawingVisual();
        using(var dc=strip.RenderOpen())
        {
            dc.DrawRectangle(Brushes.DimGray,null,new Rect(0,0,980,130*Enum.GetValues<Client>().Length));
            var stages=new[] {0,.18,.36,.54,.72,.86,1};
            foreach(var client in Enum.GetValues<Client>())for(int column=0;column<stages.Length;column++)
            {
                var drawing=new JarDrawing {Width=260,Height=260,ShowHint=false};
                double pose=client==Client.ChatGPT?1-stages[column]:stages[column];drawing.SetEyes(client,new(pose,pose));
                drawing.Measure(new(260,260));drawing.Arrange(new(0,0,260,260));
                var bitmap=new RenderTargetBitmap(520,520,192,192,PixelFormats.Pbgra32);bitmap.Render(drawing);
                Rect crop=Rect.Empty;foreach(var eye in JarGeometry.Eyes(client))crop.Union(eye);crop.Inflate(3,3);
                var pixels=new Int32Rect((int)(crop.X*2),(int)(crop.Y*2),(int)(crop.Width*2),(int)(crop.Height*2));
                dc.DrawImage(new CroppedBitmap(bitmap,pixels),new Rect(column*140+6,(int)client*130+24,pixels.Width,pixels.Height));
                var label=new FormattedText($"{client} {stages[column]:0.00}",System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,Brushes.White,1);
                dc.DrawText(label,new Point(column*140+6,(int)client*130+4));
            }
        }
        var output=new RenderTargetBitmap(980,130*Enum.GetValues<Client>().Length,96,96,PixelFormats.Pbgra32);output.Render(strip);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(output));using var file=File.Create(path);encoder.Save(file);
    }
    internal static void ExportClaudeEyes(string directory)
    {
        Directory.CreateDirectory(directory);var metadata=new List<object>();int index=0;
        double[] Normalize(Rect r) {var scene=JarGeometry.SceneRect;return new[] {(r.X-scene.X)/scene.Width,(r.Y-scene.Y)/scene.Height,r.Width/scene.Width,r.Height/scene.Height};}
        void Save(BitmapSource image,string name) {var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);}
        foreach(var eye in JarGeometry.Eyes(Client.Claude))
        {
            var art=EyeArtwork.For(Client.Claude,eye);string name="claude-eye-"+index++;
            Save(art.Face,name+"-face");Save(art.Open,name+"-open");Save(art.Closed,name+"-closed");
            metadata.Add(new {name,eye=Normalize(eye),openRect=Normalize(art.OpenRect),closedRect=Normalize(art.ClosedRect)});
        }
        var crop=((CroppedBitmap)Art.Avatar(Client.Claude)).SourceRect;
        using var sourceImage=Art.Resource("pet-claude.png");
        string sourceImageSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(sourceImage));
        var manifest=new {sourceImageSha256,sourceCrop=new[] {crop.X,crop.Y,crop.Width,crop.Height},eyes=metadata};
        File.WriteAllText(Path.Combine(directory,"claude-eyes.json"),JsonSerializer.Serialize(manifest,new JsonSerializerOptions {WriteIndented=true}));
    }

    private static void TestAnimation()
    {
        var stationary=new JarDrawing {Width=260,Height=260,ShowHint=false};
        stationary.Measure(new(260,260));stationary.Arrange(new(0,0,260,260));
        byte[] Capture() {
            var bitmap=new RenderTargetBitmap(520,520,192,192,PixelFormats.Pbgra32);bitmap.Render(stationary);
            var pixels=new byte[520*520*4];bitmap.CopyPixels(pixels,520*4,0);return pixels;
        }
        var baseline=Capture();
        var permitted=new List<Rect>();foreach(var client in Enum.GetValues<Client>())permitted.AddRange(JarGeometry.Eyes(client));
        // Check intermediate lid shapes, asymmetric winks, and simultaneous expressions,
        // rather than only comparing the two source images.
        foreach(var pose in new[] {new EyePose(.37,.73),new EyePose(.28,.28),new EyePose(.72,.72),new EyePose(0,1),new EyePose(1,0)})
        {
            foreach(var client in Enum.GetValues<Client>())stationary.SetEyes(client,pose);
            var frame=Capture();int changed=0;
            for(int y=0;y<520;y++)for(int x=0;x<520;x++) {
                int offset=(y*520+x)*4;
                if(baseline.AsSpan(offset,4).SequenceEqual(frame.AsSpan(offset,4)))continue;
                changed++;Check(permitted.Exists(r=>{r.Inflate(1,1);return r.Contains(new Point(x/2d,y/2d));}),"animated pose altered body or glass");
            }
            Check(changed>20,"intermediate eye pose must be visible");
        }
        foreach(var client in Enum.GetValues<Client>())
        {
            foreach(var other in Enum.GetValues<Client>())stationary.SetEyes(other,default);
            double shut=client==Client.ChatGPT?.28:.72;stationary.SetEyes(client,new(shut,shut));
            var frame=Capture();
            foreach(var eye in JarGeometry.Eyes(client))
            {
                var art=EyeArtwork.For(client,eye);var area=Rect.Union(art.OpenRect,art.ClosedRect);
                int featurePixels=0,total=0;
                for(int y=(int)Math.Ceiling(area.Top*2);y<(int)Math.Floor(area.Bottom*2);y++)
                    for(int x=(int)Math.Ceiling(area.Left*2);x<(int)Math.Floor(area.Right*2);x++) {
                        int p=(y*520+x)*4;total++;
                        if(client==Client.Grok?Math.Min(frame[p],Math.Min(frame[p+1],frame[p+2]))>115:
                            Math.Max(frame[p],Math.Max(frame[p+1],frame[p+2]))<100)featurePixels++;
                    }
                Check(total>0 && featurePixels<total*.02,"old eye must be fully covered at the shut seam: "+client);
            }
        }
        foreach(var client in Enum.GetValues<Client>())foreach(var mood in Enum.GetValues<EyeMood>())
        {
            double min=1,max=0;var previous=EyeExpression.Sample(client,0,mood);
            for(int step=1;step<=30000;step++) {
                var pose=EyeExpression.Sample(client,step/1000d,mood);
                Check(double.IsFinite(pose.Left)&&double.IsFinite(pose.Right)&&pose.Left>=0&&pose.Left<=1&&pose.Right>=0&&pose.Right<=1,"bounded animation");
                Check(Math.Abs(pose.Left-previous.Left)<.04&&Math.Abs(pose.Right-previous.Right)<.04,"continuous eye timeline including cycle boundaries");
                min=Math.Min(min,pose.Left);max=Math.Max(max,pose.Left);previous=pose;
            }
            Check(max-min>.7,"each pet animates in idle and hover");
            var animator=new EyeAnimator(client);
            foreach(var time in new[] {1.53,1.56,1.58,1.63,1.71,1.77,2.2}) {
                var before=animator.At(time);animator.SetMood(mood,time);
                Check(animator.At(time)==before,"interaction switch must preserve the displayed pose");
                animator.SetMood(EyeMood.Idle,time+.015);
            }
            Check(EyeExpression.Sample(client,2,mood,true)==(mood==EyeMood.Idle?default:new EyePose(1,1)),"reduced motion uses static eyes");
        }
        Check(EyeExpression.Sample(Client.Grok,1.15,EyeMood.Hover).Right>.9 && EyeExpression.Sample(Client.Grok,1.15,EyeMood.Hover).Left==0,"hover wink changes one eye");
    }

    // Opt-in native-window diagnostics. Callback timing describes compositor
    // scheduling on this machine, not a guarantee of displayed frames per second.
    internal static void MonitorAnimation(PetWindow pet,string path)
    {
        var clock=Stopwatch.StartNew();var intervals=new List<double>();
        var poses=new Dictionary<Client,HashSet<EyePose>>();foreach(var client in Enum.GetValues<Client>())poses[client]=new();
        double previous=-1;TimeSpan rendered=TimeSpan.MinValue;
        void Sample(object? sender,EventArgs e) {
            double now=clock.Elapsed.TotalMilliseconds;if(now<1000)return;
            if(e is RenderingEventArgs frame) {if(frame.RenderingTime==rendered)return;rendered=frame.RenderingTime;}
            if(previous>=0 && now>previous)intervals.Add(now-previous);previous=now;
            foreach(var client in Enum.GetValues<Client>())poses[client].Add(pet.Drawing.GetEyes(client));
        }
        var timer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(13)};
        void Stop() {timer.Stop();CompositionTarget.Rendering-=Sample;}
        timer.Tick+=(_,_)=> {
            Stop();intervals.Sort();double sum=0;foreach(double interval in intervals)sum+=interval;
            var variation=new Dictionary<string,int>();foreach(var pair in poses)variation[pair.Key.ToString()]=pair.Value.Count;
            var report=new {Samples=intervals.Count,MeanCallbackMs=intervals.Count==0?0:sum/intervals.Count,
                P95CallbackMs=intervals.Count==0?0:intervals[(int)((intervals.Count-1)*.95)],UniqueEyePoses=variation};
            File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
        };
        pet.Closed+=(_,_)=>Stop();CompositionTarget.Rendering+=Sample;timer.Start();
    }

}
