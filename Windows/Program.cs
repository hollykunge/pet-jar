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
                SelfTests.Run(); Report(args,"PASS: directional plush/knit pressure, lateral bulge, sleeping-contact load retention, gentle grip/rebound, hard-material rigidity, strain limits and material cleanup; jar motion inertia, start/reverse/stop, constant-speed energy, fast drag, clamp/reset/stall exclusion, mass-independent acceleration, horizontal/vertical response and dense-pile shake containment; independent physics bodies, mouse joint, gravity, collisions, decoration poke, glass boundary, moving eyes/alpha hit and collection joint cleanup; compact quota card states; showcase persistence/rollback; single/double-click/drag routing, generic-click refusal; account quota parsing/source/expiration; shared reference geometry, eye-only deformation/winks, no double-exposure, continuous animation, reduced motion; message feature removal."); return 0;
            }
            if (args.Length>=3 && args[0]=="--render-expression")
            {
                var client=Enum.TryParse<Client>(args[1],true,out var parsed)?parsed:(Client?)null;
                SelfTests.RenderToFile(client,args[2]);return 0;
            }
            if(args.Length>=2 && args[0]=="--render-blink-strip") {SelfTests.RenderBlinkStrip(args[1]);return 0;}
            if(args.Length>=2 && args[0]=="--render-physics") {PhysicsTests.Render(args[1]);return 0;}
            if(args.Length>=2 && args[0]=="--render-current-physics") {PhysicsTests.Render(args[1],Settings.Load());return 0;}
            if(args.Length>=2 && args[0]=="--render-materials") {MaterialSquashTests.Render(args[1]);return 0;}
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
            using var single=new Mutex(true,"Local\\PetJar.Windows",out var first);
            Trace(args,"instance mutex: "+first);
            if (!first) { MessageBox.Show("宠物罐已经运行，可从系统托盘打开宠物橱窗。","宠物罐");return 0; }
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
    private readonly Settings settings=Settings.Load();
    private System.Windows.Forms.NotifyIcon? tray;
    private AccountQuotaService quotas=null!;
    private ClientLauncher launcher=null!;
    private readonly HashSet<Client> opening=new();
    private BindingWindow? clientBinding;
    private Task? discovery;
    private PetCollection collection=null!;
    private PetShowcaseWindow? showcase;
    private FillerCollection fillers=null!;
    private FillerShowcaseWindow? fillerShowcase;
    private bool fillerRefreshPending;
    private readonly Dictionary<Client,MenuItem> petMenus=new();
    protected override void OnStartup(StartupEventArgs e)
    {
        Program.Trace(args,"startup");
        base.OnStartup(e);ShutdownMode=ShutdownMode.OnExplicitShutdown;Resources=DesktopTheme.Styles();
        Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("/PetJar;component/JarMenu.xaml",UriKind.Relative)});
        Program.Trace(args,"styles loaded");
        quotas=new(settings);launcher=new(settings);collection=new(settings);fillers=new(settings);
        Pet=new(settings,quotas);MainWindow=Pet;
        Pet.InputObserved+=text=>Program.Trace(args,text);
        if(Array.IndexOf(args,"--qa-taskbar")>=0)Pet.ShowInTaskbar=true;
        Pet.Show();
        Program.Trace(args,"pet shown");
        int animationReport=Array.IndexOf(args,"--animation-report");
        if(animationReport>=0 && args.Length>animationReport+1)SelfTests.MonitorAnimation(Pet,args[animationReport+1]);
        Pet.OpenRequested+=client=>OpenClient(client,Pet.LastOpenInput);
        TextBlock MenuGlyph(string glyph)=>new() {Text=glyph,FontFamily=new("Segoe MDL2 Assets"),FontSize=15,
            HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Opacity=0.7};
        Pet.ContextMenu=new ContextMenu();
        var shop=new MenuItem {Header="宠物橱窗",Icon=MenuGlyph("\uE719")};shop.Click+=(_,_)=>ShowShowcase();
        Pet.ContextMenu.Items.Add(shop);
        var fillerShop=new MenuItem {Header="填充物橱窗",Icon=MenuGlyph("\uE7B8")};fillerShop.Click+=(_,_)=>ShowFillers();
        Pet.ContextMenu.Items.Add(fillerShop);Pet.ContextMenu.Items.Add(new Separator());
        foreach(var client in Enum.GetValues<Client>())
        {
            var item=new MenuItem {Header=client.Name(),Icon=new Image {Source=Art.Avatar(client),Width=24,Height=24}};
            var open=new MenuItem {Header="打开客户端",Icon=MenuGlyph("\uE8A7")};open.Click+=(_,_)=>OpenClient(client);item.Items.Add(open);
            var bind=new MenuItem {Header="绑定客户端 / 额度来源…",Icon=MenuGlyph("\uE71B")};bind.Click+=(_,_)=>ShowBinding(client);item.Items.Add(bind);
            Pet.ContextMenu.Items.Add(item);
            petMenus[client]=item;
        }
        Pet.ContextMenu.Items.Add(new Separator());
        var detect=new MenuItem {Header="重新识别本机客户端",Icon=MenuGlyph("\uE72C")};detect.Click+=async(_,_)=> {detect.IsEnabled=false;try {await DiscoverClients(true);}finally {detect.IsEnabled=true;}};Pet.ContextMenu.Items.Add(detect);
        var reset=new MenuItem {Header="找回宠物罐",Icon=MenuGlyph("\uE80F")};reset.Click+=(_,_)=>Pet.ResetPosition();Pet.ContextMenu.Items.Add(reset);
        var exit=new MenuItem {Header="退出宠物罐",Icon=MenuGlyph("\uE7E8")};exit.Click+=(_,_)=>Shutdown();Pet.ContextMenu.Items.Add(exit);
        Pet.ContextMenuOpening+=(_,_)=>Pet.SetHover(null);
        collection.Changed+=()=> {
            Pet.RefreshPets();
            foreach(var (client,item) in petMenus)item.Visibility=collection.Contains(client)?Visibility.Visible:Visibility.Collapsed;
        };
        foreach(var (client,item) in petMenus)item.Visibility=collection.Contains(client)?Visibility.Visible:Visibility.Collapsed;
        fillers.Changed+=()=> {
            if(fillerRefreshPending)return;
            fillerRefreshPending=true;
            // Let the clicked card paint first and merge queued selections.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=> {
                fillerRefreshPending=false;Pet.RefreshFillers();
                Program.Trace(args,"fillers applied: "+string.Join(",",Pet.Drawing.Scene.SelectedFillers)+", props="+Pet.Drawing.Scene.Objects.Count(o=>o.Client==null));
            }));
        };
        var menu=new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("宠物橱窗",null,(_,_)=>Dispatcher.Invoke(ShowShowcase));
        menu.Items.Add("填充物橱窗",null,(_,_)=>Dispatcher.Invoke(ShowFillers));
        menu.Items.Add("找回宠物罐",null,(_,_)=>Dispatcher.Invoke(Pet.ResetPosition));
        menu.Items.Add("退出宠物罐",null,(_,_)=>Dispatcher.Invoke(Shutdown));
        tray=new() {Icon=System.Drawing.SystemIcons.Application,Text="宠物罐",Visible=true,ContextMenuStrip=menu};
        tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(ShowShowcase);
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
        if(Array.IndexOf(args,"--show-showcase")>=0)ShowShowcase();
        if(Array.IndexOf(args,"--show-fillers")>=0)ShowFillers();
    }
    private void ShowShowcase()
    {
        if(showcase?.IsVisible==true) {if(showcase.WindowState==WindowState.Minimized)showcase.WindowState=WindowState.Normal;showcase.Activate();return;}
        Pet.SetHover(null);
        showcase=new(collection) {Owner=Pet};showcase.Closed+=(_,_)=>showcase=null;showcase.Show();showcase.Activate();
    }
    private void ShowFillers()
    {
        if(fillerShowcase?.IsVisible==true) {if(fillerShowcase.WindowState==WindowState.Minimized)fillerShowcase.WindowState=WindowState.Normal;fillerShowcase.Activate();return;}
        Pet.SetHover(null);
        fillerShowcase=new(fillers) {Owner=Pet};fillerShowcase.Closed+=(_,_)=>fillerShowcase=null;fillerShowcase.Show();fillerShowcase.Activate();
    }
    private async void OpenClient(Client client,string input="menu")
    {
        if(!collection.Contains(client))return;
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
        quotas.Invalidate();Pet.RefreshQuota();
    }
    private void ShowBinding(Client client)
    {
        if(clientBinding?.IsVisible==true) {clientBinding.Activate();return;}
        clientBinding=new BindingWindow(settings,client) {Owner=Pet,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        clientBinding.Closed+=(_,_)=>{clientBinding=null;quotas.Invalidate();Pet.RefreshQuota();};clientBinding.Show();
    }
    protected override void OnExit(ExitEventArgs e) {tray?.Dispose();base.OnExit(e);}
}
