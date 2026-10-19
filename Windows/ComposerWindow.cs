using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Windows.Media;

namespace PetJar;

public sealed class ComposerWindow : Window
{
    private readonly PetModel model;
    private readonly Settings settings;
    private readonly DesktopDelivery delivery;
    internal readonly TextBox Editor=new();
    private readonly Image avatar=new();
    private readonly Button send=new();
    private readonly Window avatarWindow=new();
    private bool avatarReady;
    internal const double AvatarOverlap=32;
    private readonly ContextMenu options=new();
    private readonly MenuItem binding=new(),modeMenu=new() {Header="投递方式"},receipt=new() {Header="最近投递结果"};
    private readonly Dictionary<DeliveryMode,MenuItem> modeItems=new();
    private readonly ToolTip feedback=new() {Placement=PlacementMode.Bottom,StaysOpen=true,MaxWidth=340};
    private readonly DispatcherTimer feedbackTimer=new() {Interval=TimeSpan.FromSeconds(7)};
    private readonly TextBlock placeholder=new();
    private bool refreshing;
    private Client? displayedClient;
    private BindingWindow? bindingWindow;
    public event Action? Dismissed;
    internal static readonly Brush Ink=JarDrawing.Brush(.19,.18,.16);
    internal static readonly Brush Muted=JarDrawing.Brush(.48,.46,.42);
    internal static readonly Brush Orange=JarDrawing.Brush(.86,.32,.13);
    internal static readonly Brush Cream=JarDrawing.Brush(.98,.97,.94);
    public ComposerWindow(PetModel model,Settings settings)
    {
        this.model=model; this.settings=settings; delivery=new(settings);
        Width=352; Height=176; WindowStyle=WindowStyle.None;
        Background=Brushes.Transparent; Topmost=true; ShowInTaskbar=false; ResizeMode=ResizeMode.NoResize;
        FontFamily=new("Microsoft YaHei UI"); Foreground=Ink; Resources=Styles();
        var panel=new Grid {Margin=new(16,34,16,12)};
        panel.RowDefinitions.Add(new() {Height=new GridLength(1,GridUnitType.Star)});panel.RowDefinitions.Add(new() {Height=new(40)});
        avatar.Width=64;avatar.Height=64;avatar.Stretch=Stretch.Uniform;avatar.ContextMenu=options;
        avatarWindow.Width=64;avatarWindow.Height=64;avatarWindow.WindowStyle=WindowStyle.None;
        avatarWindow.AllowsTransparency=true;avatarWindow.Background=Brushes.Transparent;
        avatarWindow.ResizeMode=ResizeMode.NoResize;avatarWindow.Topmost=true;
        avatarWindow.ShowInTaskbar=Array.IndexOf(Environment.GetCommandLineArgs(),"--qa-taskbar")>=0;
        avatarWindow.ShowActivated=false;avatarWindow.Content=avatar;
        avatarWindow.PreviewMouseLeftButtonDown+=(_,e)=>{Activate();Editor.Focus();e.Handled=true;};
        SourceInitialized+=(_,_)=>{avatarWindow.Owner=this;avatarReady=true;UpdateAvatarWindow();};
        LocationChanged+=(_,_)=>UpdateAvatarWindow();
        IsVisibleChanged+=(_,_)=>UpdateAvatarWindow();
        Closed+=(_,_)=>avatarWindow.Close();
        Editor.FontSize=14;Editor.AcceptsReturn=true;Editor.TextWrapping=TextWrapping.Wrap;Editor.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
        Editor.Background=Brushes.Transparent;Editor.BorderThickness=new(0);Editor.Padding=new(4,4,4,4);Editor.Foreground=Ink;
        Editor.SelectionBrush=JarDrawing.Brush(.86,.52,.24,.35);
        AutomationProperties.SetAutomationId(Editor,"message-draft");
        placeholder.Text="说点什么…";placeholder.FontSize=14;placeholder.Foreground=JarDrawing.Brush(.19,.18,.16,.42);
        placeholder.Margin=new(6,6,0,0);placeholder.IsHitTestVisible=false;
        Editor.TextChanged+=(_,_)=> {
            placeholder.Visibility=Editor.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
            if(!refreshing) {model.Draft=Editor.Text;feedback.IsOpen=false;UpdateSend();}
        };
        var editorArea=new Grid();editorArea.Children.Add(Editor);editorArea.Children.Add(placeholder);
        editorArea.Clip=new RectangleGeometry(new Rect(0,0,320,88),8,8);
        editorArea.SizeChanged+=(_,_)=>editorArea.Clip=new RectangleGeometry(new Rect(0,0,editorArea.ActualWidth,editorArea.ActualHeight),8,8);
        Put(panel,editorArea,0);
        send.Width=76;send.Height=34;send.HorizontalAlignment=HorizontalAlignment.Right;send.VerticalAlignment=VerticalAlignment.Bottom;
        send.Background=new LinearGradientBrush(Color.FromRgb(218,127,70),Color.FromRgb(186,86,42),90);
        send.Foreground=Brushes.White;send.FontSize=12;send.FontWeight=FontWeights.SemiBold;
        AutomationProperties.SetAutomationId(send,"deliver-message");
        send.Click+=async (_,_)=>await Deliver();Put(panel,send,1);
        feedback.PlacementTarget=send;feedbackTimer.Tick+=(_,_)=>{feedback.IsOpen=false;feedbackTimer.Stop();};
        BuildOptions();
        var layout=new Grid();layout.Children.Add(panel);
        var close=new Button {Content="×",Width=28,Height=28,FontSize=19,
            HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,
            Margin=new(0,6,7,0),Background=Brushes.Transparent,Foreground=Ink,ToolTip="关闭对话框 · Esc"};
        AutomationProperties.SetAutomationId(close,"dismiss-composer");
        AutomationProperties.SetName(close,"关闭对话框");
        close.Click+=(_,_)=>Dismiss();layout.Children.Add(close);
        var surface=new Border {CornerRadius=new(8),BorderBrush=new LinearGradientBrush(Color.FromArgb(220,255,255,255),Color.FromArgb(70,255,255,255),90),BorderThickness=new(1),Child=layout,ContextMenu=options};
        Content=surface;GlassBackdrop.Attach(this,surface);
        PreviewKeyDown+=async (_,e)=> {
            if(e.Key==Key.Escape) {Dismiss();e.Handled=true;}
            else if(e.Key==Key.Enter && Keyboard.Modifiers==ModifierKeys.Control) {e.Handled=true;await Deliver();}
        };
        model.Changed+=Refresh;
        Closing+=(_,e)=>{if(!Application.Current.Dispatcher.HasShutdownStarted) {e.Cancel=true;Dismiss();}};
        Refresh();
    }
    private void UpdateAvatarWindow()
    {
        if(!avatarReady)return;
        avatarWindow.Left=Left+24;avatarWindow.Top=Top-AvatarOverlap;
        if(IsVisible) {if(!avatarWindow.IsVisible)avatarWindow.Show();}
        else avatarWindow.Hide();
    }
    private void BuildOptions()
    {
        binding.Click+=(_,_)=>OpenBinding();AutomationProperties.SetAutomationId(binding,"bind-client");options.Items.Add(binding);
        foreach(var mode in Enum.GetValues<DeliveryMode>())
        {
            var item=new MenuItem {Header=ModeTitle(mode),IsCheckable=true};
            item.Click+=(_,_)=>{model.Mode=mode;Refresh();};modeItems[mode]=item;modeMenu.Items.Add(item);
        }
        options.Items.Add(modeMenu);options.Items.Add(receipt);options.Items.Add(new Separator());
        var close=new MenuItem {Header="收起",InputGestureText="Esc"};close.Click+=(_,_)=>Dismiss();options.Items.Add(close);
        options.Opened+=(_,_)=>Refresh();
    }
    private static string ModeTitle(DeliveryMode mode)=>mode switch {
        DeliveryMode.Copy=>"复制并打开",DeliveryMode.Fill=>"填入当前会话",DeliveryMode.Send=>"填入并发送",_=>"新建 ChatGPT 草稿"
    };
    private async System.Threading.Tasks.Task Deliver()
    {
        if(model.Busy||!send.IsEnabled)return;
        feedback.IsOpen=false;feedbackTimer.Stop();
        await model.SendAsync(delivery.Deliver);
        if(IsVisible && model.Result is {} result)
        {
            feedback.Content=new TextBlock {Text=ResultText(result),TextWrapping=TextWrapping.Wrap,MaxWidth=310};
            feedback.IsOpen=true;feedbackTimer.Start();
        }
    }
    private static string ResultText(DeliveryResult result)=>$"{result.Message}\n复制{Mark(result.Copied)}  打开{Mark(result.Opened)}  填入{Mark(result.Inserted)}\n点击发送{Mark(result.SendClicked)}  服务端接收{(result.ServerReceived?"✓":"未确认")}";
    internal static void Put(Grid grid,UIElement child,int row,int column=0) {Grid.SetRow(child,row);Grid.SetColumn(child,column);grid.Children.Add(child);}
    internal static ResourceDictionary Styles() => (ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="Button">
            <Setter Property="Cursor" Value="Hand"/><Setter Property="Padding" Value="8,2"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Border x:Name="Surface" Background="{TemplateBinding Background}" CornerRadius="12" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border><ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Opacity" Value="0.86"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Surface" Property="Opacity" Value="0.42"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="#B86F36"/><Setter TargetName="Surface" Property="BorderThickness" Value="1"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="FlatButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}"><Setter Property="Background" Value="Transparent"/><Setter Property="Foreground" Value="#7A756B"/><Setter Property="Padding" Value="0"/></Style>
        </ResourceDictionary>
        """);
    public void Refresh()
    {
        if(displayedClient!=model.Client) {feedback.IsOpen=false;feedbackTimer.Stop();displayedClient=model.Client;}
        refreshing=true;Title=$"{model.Client.Name()} · 对话";
        avatar.Source=Art.Avatar(model.Client);avatarWindow.Title=$"{model.Client.Name()} · 头像";AutomationProperties.SetName(avatar,$"{model.Client.Name()} 宠物头像");
        avatar.ToolTip=model.Client.Name();
        if(Editor.Text!=model.Draft)Editor.Text=model.Draft;
        AutomationProperties.SetName(Editor,$"发送给 {model.Client.Name()} 的消息");
        Editor.IsReadOnly=model.Busy;binding.IsEnabled=modeMenu.IsEnabled=!model.Busy;
        foreach(var (mode,item) in modeItems) {item.IsChecked=model.Mode==mode;item.Visibility=mode==DeliveryMode.NewChat && model.Client!=Client.ChatGPT?Visibility.Collapsed:Visibility.Visible;}
        binding.Header=settings.Executables.TryGetValue(model.Client,out var path)?$"客户端：{Path.GetFileNameWithoutExtension(path)} · 更改":"绑定 Windows 客户端…";
        receipt.IsEnabled=model.Result!=null;receipt.Items.Clear();
        if(model.Result is {} result)receipt.Items.Add(new MenuItem {Header=ResultText(result),IsEnabled=false});
        UpdateSend();refreshing=false;
    }
    private static string Mark(bool value)=>value?"✓":"—";
    private void UpdateSend()
    {
        send.Content=model.Busy?"交接中…":"发送";
        send.ToolTip=$"{ModeTitle(model.Mode)} · {model.Client.Name()}";
        AutomationProperties.SetHelpText(send,$"{ModeTitle(model.Mode)}；点击发送不代表服务端接收");
        send.IsEnabled=!model.Busy && DeliveryRules.Valid(Editor.Text) && (model.Client==Client.ChatGPT || model.Mode!=DeliveryMode.NewChat);
        Editor.ToolTip=!DeliveryRules.Valid(Editor.Text) && !DeliveryRules.Empty(Editor.Text)?"内容超过 100 KB，请缩短后投递。":null;
    }
    public void Dismiss() {feedback.IsOpen=false;feedbackTimer.Stop();bindingWindow?.Close();Hide();Dismissed?.Invoke();}
    private void OpenBinding()
    {
        if(model.Busy)return;
        if(bindingWindow?.IsVisible==true) {bindingWindow.Activate();return;}
        var client=model.Client;
        bindingWindow=new BindingWindow(settings,client) {Owner=this};bindingWindow.Closed+=(_,_)=>{bindingWindow=null;Refresh();};bindingWindow.Show();
    }
}

internal sealed class BindingWindow : Window
{
    internal BindingWindow(Settings settings,Client client)
    {
        Title=$"绑定 {client.Name()} 客户端";Width=460;Height=410;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=ComposerWindow.Cream;FontFamily=new("Microsoft YaHei UI");Topmost=true;
        var stack=new StackPanel {Margin=new(20)};
        stack.Children.Add(new TextBlock {Text="选择这个角色对应的本地程序或已打开的窗口。",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)});
        var path=new TextBox {Text=settings.Executables.GetValueOrDefault(client,""),IsReadOnly=true,Margin=new(0,0,0,8)};stack.Children.Add(path);
        var choose=new Button {Content="选择 .exe 文件…",HorizontalAlignment=HorizontalAlignment.Left};choose.Click+=(_,_)=> {
            var dialog=new Microsoft.Win32.OpenFileDialog {Filter="Windows 应用程序 (*.exe)|*.exe",CheckFileExists=true};if(dialog.ShowDialog(this)==true) path.Text=dialog.FileName;
        };stack.Children.Add(choose);
        var windows=Native.Windows().Where(w=>!w.Executable.EndsWith("\\PetJar.exe",StringComparison.OrdinalIgnoreCase)).ToList();
        var picker=new ComboBox {ItemsSource=windows,DisplayMemberPath="Title",Margin=new(0,10,0,8)};stack.Children.Add(picker);
        stack.Children.Add(new TextBlock {Text="或选上方已打开窗口。窗口标题包含（可选，用于区分会话）：",FontSize=11});
        var title=new TextBox {Text=settings.WindowTitles.GetValueOrDefault(client,""),Margin=new(0,6,0,10)};stack.Children.Add(title);
        picker.SelectionChanged+=(_,_)=>{if(picker.SelectedItem is Native.Target target){path.Text=target.Executable;title.Text=target.Title;}};
        stack.Children.Add(new TextBlock {Text="账号额度来源（请与客户端使用同一登录账号）：",FontSize=11});
        var quotaChoices=new[] {new {Source=QuotaSource.Auto,Name="自动识别客户端"},new {Source=QuotaSource.Codex,Name="本机 Codex 登录账号"},new {Source=QuotaSource.Claude,Name="本机 Claude Code 登录账号"},new {Source=QuotaSource.Unavailable,Name="暂不接入额度"}};
        var quota=new ComboBox {ItemsSource=quotaChoices,DisplayMemberPath="Name",SelectedValuePath="Source",SelectedValue=settings.QuotaSources.GetValueOrDefault(client,QuotaSource.Auto),Margin=new(0,6,0,12)};stack.Children.Add(quota);
        var save=new Button {Content="保存绑定",HorizontalAlignment=HorizontalAlignment.Right};save.Click+=(_,_)=> {
            if(!File.Exists(path.Text)||!path.Text.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)) {MessageBox.Show(this,"请选择有效的 .exe 客户端。");return;}
            settings.Executables[client]=path.Text;settings.WindowTitles[client]=title.Text.Trim();
            settings.AutoBindings.Remove(client);settings.AppUserModelIds.Remove(client);
            settings.QuotaSources[client]=quota.SelectedValue is QuotaSource source?source:QuotaSource.Auto;
            if(settings.Save())Close();else MessageBox.Show(this,"绑定已在本次运行生效，但保存失败，请检查本地目录权限。");
        };stack.Children.Add(save);Content=stack;
    }
}
