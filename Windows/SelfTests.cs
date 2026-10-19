using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PetJar;

internal static class SelfTests
{
    private static void Check(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
    internal static void Run()
    {
        InteractionQuotaTests.Run();
        var text="请总结 A&B + C? #宠物罐\n你好 👋 https://example.com?q=a&b=2";
        Check(Uri.UnescapeDataString(DeliveryRules.NewChatUrl(text).Split("?prompt=")[1])==text,"Unicode/URL round trip");
        Check(!DeliveryRules.Valid(" \n\t") && !DeliveryRules.Valid(new string('中',34000)) && DeliveryRules.Valid(text),"input size guards");
        Check(DeliveryRules.IsSend("发送消息") && !DeliveryRules.IsSend("Send feedback"),"send button semantics");
        Check(!DeliveryRules.CanInsert("已有草稿","Message your dot",false),"nonempty target draft must be preserved");
        Check(!DeliveryRules.CanInsert("","Search messages",false),"search field cannot receive messages");
        Check(!DeliveryRules.CanInsert("","Message",true) && DeliveryRules.CanInsert("","Message your dot",false),"editable semantics");
        var model=new PetModel();
        foreach(var client in Enum.GetValues<Client>()) {Check(model.Select(client),"selection");Check(model.Draft=="","isolated initial draft");model.Draft=client.Name()+text;model.Mode=client==Client.Grok?DeliveryMode.Fill:DeliveryMode.Copy;}
        foreach(var client in Enum.GetValues<Client>()) {model.Select(client);Check(model.Draft==client.Name()+text,"independent draft restore");Check(model.Mode==(client==Client.Grok?DeliveryMode.Fill:DeliveryMode.Copy),"independent modes");}
        var completion=new TaskCompletionSource<DeliveryResult>();model.Select(Client.Grok);
        Client? recipient=null;string? payload=null;
        var sending=model.SendAsync((client,draft,mode)=>{recipient=client;payload=draft;return completion.Task;});
        Check(model.Busy && !model.Select(Client.Muse),"recipient lock during delivery");model.Draft="overwrite";model.Mode=DeliveryMode.Send;
        Check(model.Draft==Client.Grok.Name()+text && model.Mode==DeliveryMode.Fill,"editing and mode locked during delivery");
        completion.SetResult(new("filled",Inserted:true));sending.GetAwaiter().GetResult();
        Check(!model.Busy && recipient==Client.Grok && payload==Client.Grok.Name()+text && model.Result?.Inserted==true,"captured target and completion");
        Check(!model.Result!.ServerReceived && model.Draft==payload,"source draft retained, receipt never invented");
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
            var changed=Render(client);var chattingFrame=Render(client,true);
            Check(changed.AsSpan().SequenceEqual(chattingFrame),"active chat uses the same eye-only scene geometry: "+client);
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
        foreach(var client in Enum.GetValues<Client>()) {pet.SetHover(client);Check(pet.Drawing.GetEyes(client)==new EyePose(1,1) && pet.Drawing.Hovered==client,"exclusive hover");}
        pet.SetHover(null);foreach(var client in Enum.GetValues<Client>())Check(pet.Drawing.GetEyes(client)==default,"frozen idle has no animation");
        pet.SetActive(Client.Grok);pet.SetHover(Client.Muse);pet.SetHover(null);
        Check(pet.Drawing.Selected==Client.Grok && pet.Drawing.GetEyes(Client.Grok)==new EyePose(1,1) && pet.Drawing.GetEyes(Client.Muse)==default,"chatting expression survives pointer leaving the jar");
        pet.SetActive(Client.Muse);Check(pet.Drawing.Selected==Client.Muse,"chatting target switches exclusively");
        pet.SetActive(null);Check(pet.Drawing.Selected==null && pet.Drawing.GetEyes(Client.Muse)==default,"dismissal clears chatting expression");pet.Close();
        TestAnimation();
        var work=new Rect(0,0,1920,1040);var jar=new Rect(900,400,260,260);var composerSize=new Size(352,176);
        var first=ComposerPlacement.At(jar,new Point(99,63),composerSize,work);
        var second=ComposerPlacement.At(jar,new Point(99,88),composerSize,work);
        Check(Math.Abs(second.Y-first.Y-25)<.01 && first.X+composerSize.Width<jar.Left,"composer follows actual clicks without obscuring the jar");
        var flipped=ComposerPlacement.At(new Rect(0,0,260,260),new Point(99,5),composerSize,work);
        Check(flipped.X>=272 && flipped.Y>=ComposerWindow.AvatarOverlap,"left edge flips placement and top edge preserves the exposed avatar");
        var bottom=ComposerPlacement.At(new Rect(1660,780,260,260),new Point(101,220),composerSize,work);
        Check(bottom.Y+composerSize.Height<=work.Bottom,"bottom edge keeps the full composer visible");
    }
    private static RenderTargetBitmap Bitmap(Client? client,bool chatting=false)
    {
        var drawing=new JarDrawing {Width=260,Height=260,ShowHint=false,Hovered=chatting?null:client,Selected=chatting?client:null};
        if(client is Client targetClient)drawing.SetEyes(targetClient,new(1,1));
        drawing.Measure(new(260,260));drawing.Arrange(new(0,0,260,260));drawing.UpdateLayout();
        var target=new RenderTargetBitmap(520,520,192,192,PixelFormats.Pbgra32);target.Render(drawing);return target;
    }
    private static byte[] Render(Client? client,bool chatting=false) {var target=Bitmap(client,chatting);var pixels=new byte[520*520*4];target.CopyPixels(pixels,520*4,0);return pixels;}
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
            Check(max-min>.7,"each pet animates in idle, hover and chat");
            var animator=new EyeAnimator(client);
            foreach(var time in new[] {1.53,1.56,1.58,1.63,1.71,1.77,2.2}) {
                var before=animator.At(time);animator.SetMood(mood,time);
                Check(animator.At(time)==before,"interaction switch must preserve the displayed pose");
                animator.SetMood(EyeMood.Idle,time+.015);
            }
            Check(EyeExpression.Sample(client,2,mood,true)==(mood==EyeMood.Idle?default:new EyePose(1,1)),"reduced motion uses static eyes");
        }
        Check(EyeExpression.Sample(Client.Grok,1.15,EyeMood.Hover).Right>.9 && EyeExpression.Sample(Client.Grok,1.15,EyeMood.Hover).Left==0,"hover wink changes one eye");
        Check(EyeExpression.Sample(Client.Muse,.58,EyeMood.Chatting).Left>.9,"chat entry includes a second blink");
        Check(EyeExpression.Sample(Client.ChatGPT,1,EyeMood.Chatting)==new EyePose(1,1),"yellow pet stays attentive while chatting");
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

    internal static Window Fixture(string mode)
    {
        var window=new Window {Title="PetJar local delivery fixture",Width=480,Height=260,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        var panel=new StackPanel {Margin=new(20)};var box=new TextBox {Text=mode=="nonempty"?"原有目标草稿，请勿覆盖":"",AcceptsReturn=true,Height=90};
        System.Windows.Automation.AutomationProperties.SetName(box,"Message your dot");panel.Children.Add(box);
        if(mode=="ambiguous") {var second=new TextBox();System.Windows.Automation.AutomationProperties.SetName(second,"Message another conversation");panel.Children.Add(second);}
        var send=new Button {Content="Send message",Height=30,Focusable=false};
        send.Click+=(_,_)=>{box.Text="";send.Content="Fixture send invoked";};
        if(mode!="no-button")panel.Children.Add(send);
        var group=new GroupBox {Header="Conversation",Content=panel,Focusable=true};window.Content=group;
        // Ambiguous case must not resolve via a focused composer.
        if(mode=="ambiguous")window.Loaded+=(_,_)=>window.Dispatcher.BeginInvoke(()=>group.Focus());else window.Loaded+=(_,_)=>box.Focus();
        return window;
    }
    internal static async Task TestDelivery()
    {
        var exe=Environment.ProcessPath!;
        foreach(var scenario in new[] {"empty","nonempty","ambiguous","no-button"})
        {
            var start=new ProcessStartInfo(exe) {UseShellExecute=false};start.ArgumentList.Add("--delivery-fixture");start.ArgumentList.Add(scenario);
            using var fixture=Process.Start(start)!;
            try
            {
                for(int i=0;i<30;i++) {fixture.Refresh();if(fixture.MainWindowHandle!=IntPtr.Zero)break;await Task.Delay(100);}
                var settings=new Settings();settings.Executables[Client.ChatGPT]=exe;settings.WindowTitles[Client.ChatGPT]="PetJar local delivery fixture";
                var delivery=new DesktopDelivery(settings);var text="仅本地测试 A&B 👋\n第二行";
                if(scenario=="empty")
                {
                    var copy=await delivery.Deliver(Client.ChatGPT,text,DeliveryMode.Copy);
                    Check(copy.Copied&&copy.Opened&&!copy.Inserted&&!copy.SendClicked&&!copy.ServerReceived,"copy/open fixture: "+copy.Message);
                    var filled=await delivery.Deliver(Client.ChatGPT,text,DeliveryMode.Fill);
                    Check(filled.Inserted&&!filled.SendClicked&&!filled.ServerReceived,"fill fixture: "+filled.Message);
                    var refusal=await delivery.Deliver(Client.ChatGPT,"不能覆盖",DeliveryMode.Send);
                    Check(!refusal.Inserted&&!refusal.SendClicked,"second nonempty send refused");
                }
                else if(scenario=="no-button")
                {
                    var result=await delivery.Deliver(Client.ChatGPT,text,DeliveryMode.Send);
                    Check(result.Inserted&&!result.SendClicked,"missing send button must stop after insertion: "+result.Message);
                }
                else
                {
                    var result=await delivery.Deliver(Client.ChatGPT,text,DeliveryMode.Send);
                    Check(!result.Inserted&&!result.SendClicked,"must refuse "+scenario+": "+result.Message);
                    if(scenario=="nonempty")
                    {
                        var root=AutomationElement.FromHandle(fixture.MainWindowHandle);
                        var editor=root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit));
                        Check(((ValuePattern)editor.GetCurrentPattern(ValuePattern.Pattern)).Current.Value=="原有目标草稿，请勿覆盖","original target draft was changed");
                    }
                }
            }
            finally {if(!fixture.HasExited) {fixture.Kill();await fixture.WaitForExitAsync();}}
        }
        var sendStart=new ProcessStartInfo(exe) {UseShellExecute=false};sendStart.ArgumentList.Add("--delivery-fixture");sendStart.ArgumentList.Add("empty");
        using var sendFixture=Process.Start(sendStart)!;
        try
        {
            for(int i=0;i<30;i++){sendFixture.Refresh();if(sendFixture.MainWindowHandle!=IntPtr.Zero)break;await Task.Delay(100);}
            var settings=new Settings();settings.Executables[Client.ChatGPT]=exe;settings.WindowTitles[Client.ChatGPT]="PetJar local delivery fixture";
            var result=await new DesktopDelivery(settings).Deliver(Client.ChatGPT,"只在本地 fixture 点击发送",DeliveryMode.Send);
            Check(result.Inserted&&result.SendClicked&&!result.ServerReceived,"native send invoke: "+result.Message);
            await Task.Delay(100);
            var root=AutomationElement.FromHandle(sendFixture.MainWindowHandle);
            Check(root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,"Fixture send invoked"))!=null,"send click did not reach native fixture");
        }
        finally {if(!sendFixture.HasExited){sendFixture.Kill();await sendFixture.WaitForExitAsync();}}
    }
}
