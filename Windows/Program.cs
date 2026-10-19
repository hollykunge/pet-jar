using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace PetJar;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Trace(args,"entry");
            if (Array.IndexOf(args,"--self-test")>=0)
            {
                SelfTests.Run(); Report(args,"PASS: single/double-click and drag routing, account quota parsing/source selection/expiration, geometry, eye-only lid deformation/winks, no double-exposure at closure, continuous idle/hover/chat animation, reduced motion, drafts, recipient lock and delivery guards."); return 0;
            }
            if (args.Length>=3 && args[0]=="--render-expression")
            {
                var client=Enum.TryParse<Client>(args[1],true,out var parsed)?parsed:(Client?)null;
                SelfTests.RenderToFile(client,args[2]);return 0;
            }
            if(args.Length>=2 && args[0]=="--render-blink-strip") {SelfTests.RenderBlinkStrip(args[1]);return 0;}
            if(args.Length>=2 && args[0]=="--export-claude-eyes") {SelfTests.ExportClaudeEyes(args[1]);return 0;}
            if(args.Length>=2 && args[0]=="--quota-report")
            {
                var service=new AccountQuotaService(Settings.Load());var results=new Dictionary<string,AccountQuota>();
                foreach(var client in Enum.GetValues<Client>())results[client.Name()]=service.GetAsync(client).GetAwaiter().GetResult();
                File.WriteAllText(args[1],System.Text.Json.JsonSerializer.Serialize(results,new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));return 0;
            }
            if(args.Length>=2&&args[0]=="--discover-clients") {
                var settings=Settings.Load();var installed=ClientDiscovery.BindAsync(settings,true).GetAwaiter().GetResult();
                File.WriteAllText(args[1],System.Text.Json.JsonSerializer.Serialize(new {Installed=installed,settings.Executables,settings.AutoBindings,settings.AppUserModelIds},new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));return 0;
            }
            if (args.Length>=2 && args[0]=="--delivery-fixture")
            {
                new Application().Run(SelfTests.Fixture(args[1])); return 0;
            }
            if (Array.IndexOf(args,"--delivery-test")>=0 || Array.IndexOf(args,"--delivery-test-ui")>=0)
            {
                var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
                int result=0;
                async void RunTests()
                {
                    try {await SelfTests.TestDelivery();Report(args,"PASS: real native fixture copy/open, fill, send click, nonempty protection and ambiguous-input refusal; no external client messages.");}
                    catch(Exception e) {Report(args,e.ToString());result=1;}
                    finally {app.Shutdown();}
                }
                app.Startup+=(_,_)=> {
                    if(Array.IndexOf(args,"--delivery-test-ui")<0) {RunTests();return;}
                    var button=new Button {Content="运行本地投递验证",Margin=new(24),FontSize=16};
                    var runner=new Window {Title="宠物罐 · 本地投递验证",Width=360,Height=150,Content=button,WindowStartupLocation=WindowStartupLocation.CenterScreen,Topmost=true};
                    button.Click+=(_,_)=>{button.IsEnabled=false;RunTests();};
                    runner.Closed+=(_,_)=>app.Shutdown();runner.Show();
                };
                app.Run(); return result;
            }
            using var single=new Mutex(true,"Local\\PetJar.Windows",out var first);
            Trace(args,"instance mutex: "+first);
            if (!first) { MessageBox.Show("宠物罐已经运行，可从系统托盘打开对话框。","宠物罐");return 0; }
            var application=new PetApplication(args);
            Trace(args,"application constructed");
            application.DispatcherUnhandledException+=(_,e)=> {
                var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PetJar");
                Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory,"last-error.log"),e.Exception.ToString());
            };
            application.Run();return 0;
        }
        catch(Exception e) {Report(args,e.ToString());return 1;}
    }
    internal static void Trace(string[] args,string text)
    {
        int i=Array.IndexOf(args,"--startup-log");
        if(i>=0 && args.Length>i+1)File.AppendAllText(args[i+1],text+Environment.NewLine);
    }
    private static void Report(string[] args,string text)
    {
        var index=Array.IndexOf(args,"--test-report");
        if(index>=0 && args.Length>index+1)File.WriteAllText(args[index+1],text);
        else Console.WriteLine(text);
    }
}

internal sealed class PetApplication(string[] args) : Application
{
    internal PetWindow Pet { get; private set; }=null!;
    private ComposerWindow composer=null!;
    private readonly PetModel model=new();
    private readonly Settings settings=Settings.Load();
    private Point? conversationAnchor;
    private System.Windows.Forms.NotifyIcon? tray;
    private AccountQuotaService quotas=null!;
    private ClientLauncher launcher=null!;
    private readonly HashSet<Client> opening=new();
    private BindingWindow? clientBinding;
    private Task? discovery;
    protected override void OnStartup(StartupEventArgs e)
    {
        Program.Trace(args,"startup");
        base.OnStartup(e);ShutdownMode=ShutdownMode.OnExplicitShutdown;Resources=ComposerWindow.Styles();
        Program.Trace(args,"styles loaded");
        quotas=new(settings);launcher=new(settings);
        Pet=new(settings,quotas);MainWindow=Pet;
        Pet.InputObserved+=text=>Program.Trace(args,text);
        if(Array.IndexOf(args,"--qa-taskbar")>=0)Pet.ShowInTaskbar=true;
        Pet.Show();
        Program.Trace(args,"pet shown");
        int animationReport=Array.IndexOf(args,"--animation-report");
        if(animationReport>=0 && args.Length>animationReport+1)SelfTests.MonitorAnimation(Pet,args[animationReport+1]);
        composer=new(model,settings) {Owner=Pet};
        if(Array.IndexOf(args,"--qa-taskbar")>=0)composer.ShowInTaskbar=true;
        Program.Trace(args,"composer constructed");
        Pet.OpenRequested+=client=>OpenClient(client,Pet.LastOpenInput);Pet.Moved+=PositionComposer;Pet.ToggleRequested+=Toggle;
        composer.Dismissed+=()=>Pet.SetActive(null);
        Pet.ContextMenu=new ContextMenu();
        foreach(var client in Enum.GetValues<Client>())
        {
            var item=new MenuItem {Header=client.Name()};
            var open=new MenuItem {Header="打开客户端"};open.Click+=(_,_)=>OpenClient(client);item.Items.Add(open);
            var bind=new MenuItem {Header="绑定客户端 / 额度来源…"};bind.Click+=(_,_)=>ShowBinding(client);item.Items.Add(bind);
            var compose=new MenuItem {Header="写消息…"};compose.Click+=(_,_)=>ShowComposer(client);item.Items.Add(compose);
            Pet.ContextMenu.Items.Add(item);
        }
        Pet.ContextMenu.Items.Add(new Separator());
        var detect=new MenuItem {Header="重新识别本机客户端"};detect.Click+=async(_,_)=> {detect.IsEnabled=false;try {await DiscoverClients(true);}finally {detect.IsEnabled=true;}};Pet.ContextMenu.Items.Add(detect);
        var reset=new MenuItem {Header="找回宠物罐"};reset.Click+=(_,_)=>Pet.ResetPosition();Pet.ContextMenu.Items.Add(reset);
        var exit=new MenuItem {Header="退出宠物罐"};exit.Click+=(_,_)=>Shutdown();Pet.ContextMenu.Items.Add(exit);
        var menu=new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开 / 收起对话框",null,(_,_)=>Dispatcher.Invoke(Toggle));
        menu.Items.Add("找回宠物罐",null,(_,_)=>Dispatcher.Invoke(Pet.ResetPosition));
        menu.Items.Add("退出宠物罐",null,(_,_)=>Dispatcher.Invoke(Shutdown));
        tray=new() {Icon=System.Drawing.SystemIcons.Application,Text="宠物罐 · Alt+Space",Visible=true,ContextMenuStrip=menu};
        tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(Toggle);
        Program.Trace(args,"tray created");
        discovery=DiscoverClients();
        if(Array.IndexOf(args,"--freeze-expression")>=0)Pet.FreezeExpression=true;
        int hover=Array.IndexOf(args,"--preview-hover");
        if(hover>=0 && args.Length>hover+1 && Enum.TryParse<Client>(args[hover+1],true,out var target))Pet.SetHover(target);
        int lid=Array.IndexOf(args,"--preview-lid");
        if(lid>=0 && args.Length>lid+1 && double.TryParse(args[lid+1],System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,out double closure) && closure>=0 && closure<=1)
        {
            Pet.FreezeExpression=true;
            foreach(var client in Enum.GetValues<Client>()) {double pose=client==Client.ChatGPT?1-closure:closure;Pet.Drawing.SetEyes(client,new(pose,pose));}
        }
        if(Array.IndexOf(args,"--show-composer")>=0)ShowComposer(Client.ChatGPT);
    }
    private async void OpenClient(Client client,string input="menu")
    {
        if(!opening.Add(client))return;
        try {
            Program.Trace(args,$"open requested: {client}, input={input}");
            if(discovery!=null)await discovery;
            if(!settings.Executables.TryGetValue(client,out var bound)||!File.Exists(bound))await DiscoverClients();
            var result=await launcher.OpenAsync(client);Pet.ShowStatus(result.Message);
            Program.Trace(args,$"open result: {client}, opened={result.Opened}, needsBinding={result.NeedsBinding}, foreground={Native.GetForegroundWindow()}, path={settings.Executables.GetValueOrDefault(client,"")}");
            if(result.NeedsBinding)ShowBinding(client);
            tray?.ShowBalloonTip(2500,"宠物罐",result.Message,System.Windows.Forms.ToolTipIcon.Info);
        }
        finally {opening.Remove(client);}
    }
    private async Task DiscoverClients(bool force=false)
    {
        var installed=await ClientDiscovery.BindAsync(settings,force);
        Program.Trace(args,"detected clients: "+string.Join(", ",installed.Select(x=>x.Client+"="+x.Executable)));
        quotas.Invalidate();Pet.RefreshQuota();composer.Refresh();
    }
    private void ShowBinding(Client client)
    {
        if(clientBinding?.IsVisible==true) {clientBinding.Activate();return;}
        clientBinding=new BindingWindow(settings,client) {Owner=Pet,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        clientBinding.Closed+=(_,_)=>{clientBinding=null;quotas.Invalidate();Pet.RefreshQuota();composer.Refresh();};clientBinding.Show();
    }
    private void Toggle() {if(composer.IsVisible)composer.Dismiss();else ShowComposer(model.Client);}
    private void ShowComposer(Client client,Point? click=null)
    {
        if(!model.Select(client))return;
        var bounds=JarGeometry.Regions[client].Bounds;
        conversationAnchor=click??new Point(bounds.X+bounds.Width/2,bounds.Y+bounds.Height/2);
        Pet.SetActive(client);composer.Refresh();PositionComposer();
        composer.Show();composer.Activate();composer.Editor.Focus();
    }
    private void PositionComposer()
    {
        if(conversationAnchor is not Point anchor)return;
        var position=ComposerPlacement.At(new Rect(Pet.Left,Pet.Top,Pet.Width,Pet.Height),anchor,new Size(composer.Width,composer.Height),Pet.WorkArea());
        composer.Left=position.X;composer.Top=position.Y;
    }
    protected override void OnExit(ExitEventArgs e) {tray?.Dispose();base.OnExit(e);}
}
