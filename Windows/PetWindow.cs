using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace PetJar;

public sealed class PetWindow : Window
{
    private static readonly Client[] characters=Enum.GetValues<Client>();
    internal readonly JarDrawing Drawing = new() { Width=260,Height=260,IsHitTestVisible=false };
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private readonly Dictionary<Client,EyeAnimator> animators=new();
    private bool rendering,closed;
    private TimeSpan lastFrame=TimeSpan.MinValue;
    private readonly Settings settings;
    private readonly PetClickGesture gesture=new();
    private readonly AccountQuotaService? quotas;
    private readonly QuotaPopup usagePopup;
    private int hoverGeneration;
    private Client? popupFor;
    private readonly System.Windows.Threading.DispatcherTimer usageRefresh=new() {Interval=TimeSpan.FromSeconds(45)};
    private Point? dragStart;
    private Point windowStart;
    private bool dragged;
    private HwndSource? source;
    private bool freezeExpression;
    public bool FreezeExpression { get=>freezeExpression;set {freezeExpression=value;RefreshAnimation();} }
    public event Action<Client>? OpenRequested;
    internal event Action<string>? InputObserved;
    internal string LastOpenInput {get;private set;}="menu";
    public event Action? Moved;
    public event Action? ToggleRequested;
    public bool ShortcutRegistered { get; private set; }
    public PetWindow(Settings settings):this(settings,null) { }
    internal PetWindow(Settings settings,AccountQuotaService? quotas)
    {
        this.settings=settings;
        this.quotas=quotas;usagePopup=new(this);
        usageRefresh.Tick+=(_,_)=>RefreshQuota();
        foreach(var client in characters)animators[client]=new(client);
        Title="宠物罐 · 多材质智能体"; Width=260; Height=260;
        WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
        ResizeMode=ResizeMode.NoResize; Topmost=true; ShowInTaskbar=false; ShowActivated=false;
        var canvas=new Canvas { Background=Brushes.Transparent };
        canvas.Children.Add(Drawing);
        foreach (var client in characters)
        {
            var bounds=JarGeometry.Regions[client].Bounds;
            var localOutline=JarGeometry.Regions[client].Clone();localOutline.Transform=new TranslateTransform(-bounds.X,-bounds.Y);
            var button=new Button { Width=bounds.Width,Height=bounds.Height,Background=Brushes.Transparent,BorderThickness=new(0),
                Clip=localOutline };
            Canvas.SetLeft(button,bounds.X);Canvas.SetTop(button,bounds.Y);
            // No default button hover/pressed template can alter the stationary scene.
            var border=new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.BackgroundProperty,Brushes.Transparent);
            button.Template=new ControlTemplate(typeof(Button)) { VisualTree=border };
            AutomationProperties.SetName(button,$"{client.Name()}，双击打开客户端");
            AutomationProperties.SetHelpText(button,"鼠标双击打开，悬停查看账号额度；键盘空格或回车可打开客户端。");
            button.PreviewKeyDown+=(_,e)=> {if(e.Key==Key.Enter) {LastOpenInput="keyboard";OpenRequested?.Invoke(client);e.Handled=true;}};
            AutomationProperties.SetAutomationId(button,"pet-"+client.ToString().ToLowerInvariant());
            button.Click+=(_,_) => {LastOpenInput="keyboard-or-accessibility";OpenRequested?.Invoke(client);};
            canvas.Children.Add(button);
        }
        Content=canvas;
        MouseMove+=(_,e) => {
            if (dragStart is Point start && e.LeftButton==MouseButtonState.Pressed)
            {
                var current=PointToScreen(e.GetPosition(this)); var delta=current-start;
                var scale=VisualTreeHelper.GetDpi(this);
                if (Math.Abs(delta.X)+Math.Abs(delta.Y)>4) dragged=true;
                if (dragged) { SetHover(null); Left=windowStart.X+delta.X/scale.DpiScaleX; Top=windowStart.Y+delta.Y/scale.DpiScaleY; Moved?.Invoke(); }
            }
            else SetHover(JarGeometry.Hit(e.GetPosition(this)));
        };
        MouseLeave+=(_,_) => SetHover(null);
        PreviewMouseLeftButtonDown+=(_,e) => {
            InputObserved?.Invoke($"mouse down: client={JarGeometry.Hit(e.GetPosition(this))}, count={e.ClickCount}");
            hoverGeneration++;usagePopup.IsOpen=false;
            gesture.Begin(JarGeometry.Hit(e.GetPosition(this)),e.ClickCount);
            dragStart=PointToScreen(e.GetPosition(this)); windowStart=new(Left,Top); dragged=false;
            CaptureMouse(); e.Handled=true;
        };
        PreviewMouseLeftButtonUp+=(_,e) => {
            if (dragStart==null) return;
            ReleaseMouseCapture(); dragStart=null;
            if (dragged) { Clamp(); SavePosition(); Moved?.Invoke(); }
            var selected=gesture.Release(JarGeometry.Hit(e.GetPosition(this)),dragged);
            if(selected is Client client) {LastOpenInput="mouse-double-click";OpenRequested?.Invoke(client);}
            else if(!dragged)RefreshQuota();
            dragged=false; e.Handled=true;
        };
        Loaded+=(_,_)=>RefreshAnimation();
        IsVisibleChanged+=(_,_)=>RefreshAnimation();
        SystemParameters.StaticPropertyChanged+=SystemAnimationChanged;
        SourceInitialized+=(_,_) => {
            source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); source.AddHook(Hook);
            ShortcutRegistered=Native.RegisterHotKey(source.Handle,1,0x4001,0x20); // Alt+Space, no repeat.
            var work=WorkArea(); Left=settings.Left??work.Right-Width-25; Top=settings.Top??work.Bottom-Height-35;
            Clamp();
        };
        Closed+=(_,_) => { closed=true;hoverGeneration++;usageRefresh.Stop();usagePopup.IsOpen=false;SetRendering(false);SystemParameters.StaticPropertyChanged-=SystemAnimationChanged;SavePosition();if(source!=null) {Native.UnregisterHotKey(source.Handle,1);source.RemoveHook(Hook);} };
    }
    internal void SetHover(Client? client)
    {
        if (Drawing.Hovered==client) return;
        Drawing.Hovered=client;
        RefreshQuota();
        Cursor=client==null?Cursors.Arrow:Cursors.Hand;
        RefreshAnimation();
    }
    internal async void RefreshQuota()
    {
        int generation=++hoverGeneration;
        if(quotas==null||Drawing.Hovered is not Client client||closed||!IsVisible||dragStart!=null) {usageRefresh.Stop();popupFor=null;usagePopup.IsOpen=false;return;}
        usageRefresh.Start();
        if(popupFor!=client||!usagePopup.IsOpen)usagePopup.Loading(client);
        popupFor=client;
        var snapshot=await quotas.GetAsync(client);
        if(closed||generation!=hoverGeneration||Drawing.Hovered!=client)return;
        usagePopup.Update(client,snapshot,settings.Executables.ContainsKey(client));
    }
    internal void ShowStatus(string message) {Title="宠物罐 · "+message;}
    internal void SetActive(Client? client)
    {
        if(Drawing.Selected==client)return;
        Drawing.Selected=client;
        RefreshAnimation();
    }
    private void RefreshAnimation()
    {
        Animate();
        SetRendering(IsVisible&&!closed&&!FreezeExpression&&SystemParameters.ClientAreaAnimation);
    }
    private void SystemAnimationChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(SystemParameters.ClientAreaAnimation))Dispatcher.Invoke(RefreshAnimation);
    }
    private void SetRendering(bool enabled)
    {
        if(rendering==enabled)return;
        rendering=enabled;lastFrame=TimeSpan.MinValue;
        if(enabled)CompositionTarget.Rendering+=RenderFrame;
        else CompositionTarget.Rendering-=RenderFrame;
    }
    private void RenderFrame(object? sender,EventArgs e)
    {
        if(e is RenderingEventArgs frame) {
            if(frame.RenderingTime==lastFrame)return;
            lastFrame=frame.RenderingTime;
        }
        Animate();
    }
    private void Animate()
    {
        bool stationary=FreezeExpression||!SystemParameters.ClientAreaAnimation;
        double now=clock.Elapsed.TotalSeconds;
        foreach(var client in characters)
        {
            var mood=Drawing.Selected==client?EyeMood.Chatting:Drawing.Hovered==client?EyeMood.Hover:EyeMood.Idle;
            animators[client].SetMood(mood,now,stationary);
            Drawing.SetEyes(client,animators[client].At(now,stationary));
        }
    }
    internal Rect WorkArea()
    {
        var handle=new WindowInteropHelper(this).Handle;
        var physical=System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var dpi=VisualTreeHelper.GetDpi(this);
        return new(physical.Left/dpi.DpiScaleX,physical.Top/dpi.DpiScaleY,physical.Width/dpi.DpiScaleX,physical.Height/dpi.DpiScaleY);
    }
    internal void Clamp()
    {
        var work=WorkArea(); Left=Math.Clamp(Left,work.Left,Math.Max(work.Left,work.Right-Width));
        Top=Math.Clamp(Top,work.Top,Math.Max(work.Top,work.Bottom-Height));
    }
    internal void ResetPosition()
    {
        var work=WorkArea(); Left=work.Right-Width-25; Top=work.Bottom-Height-35; Clamp(); SavePosition(); Show(); Moved?.Invoke();
    }
    private void SavePosition() { if (!double.IsFinite(Left)||!double.IsFinite(Top))return;settings.Left=Left;settings.Top=Top;settings.Save(); }
    private IntPtr Hook(IntPtr handle,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if (message==0x0312 && wParam.ToInt32()==1) { ToggleRequested?.Invoke(); handled=true; }
        if (message==0x007E) Dispatcher.BeginInvoke(() => { Clamp(); Moved?.Invoke(); });
        return IntPtr.Zero;
    }
}
