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
    internal readonly PhysicsJarDrawing Drawing;
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private readonly Dictionary<Client,EyeAnimator> animators=new();
    private readonly Dictionary<Client,Button> petButtons=new();
    private bool rendering,closed;
    private TimeSpan lastFrame=TimeSpan.MinValue;
    private readonly Settings settings;
    private readonly PetClickGesture gesture=new();
    private readonly JarMotion jarMotion=new();
    private Dictionary<JarObject,Point>? jarMoveStart;
    private int jarImpulseStart;
    private readonly AccountQuotaService? quotas;
    private readonly QuotaPopup usagePopup;
    private int hoverGeneration;
    private Client? popupFor;
    private readonly System.Windows.Threading.DispatcherTimer usageRefresh=new() {Interval=TimeSpan.FromSeconds(45)};
    private Point? dragStart;
    private Point windowStart;
    private Point pointerStart;
    private JarObject? pressedObject;
    private bool primaryPress;
    private Point lastDragPoint;
    private double lastDragTime;
    private Vector throwVelocity;
    private bool dragged;
    private HwndSource? source;
    private bool freezeExpression;
    public bool FreezeExpression { get=>freezeExpression;set {freezeExpression=value;RefreshAnimation();} }
    public event Action<Client>? OpenRequested;
    internal event Action<string>? InputObserved;
    internal string LastOpenInput {get;private set;}="menu";
    public PetWindow(Settings settings):this(settings,null) { }
    internal PetWindow(Settings settings,AccountQuotaService? quotas)
    {
        this.settings=settings;
        Drawing=new(settings.JarPets,settings.JarFillers) {Width=260,Height=260,IsHitTestVisible=false};
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
            var button=new Button {Background=Brushes.Transparent,BorderThickness=new(0)};
            // Body physics and material response live in the drawing, never in
            // the accessibility button's default hover/pressed template.
            var border=new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.BackgroundProperty,Brushes.Transparent);
            button.Template=new ControlTemplate(typeof(Button)) { VisualTree=border };
            AutomationProperties.SetName(button,$"{client.Name()}，双击打开客户端");
            AutomationProperties.SetHelpText(button,"轻点推动，按住拖动，双击打开；悬停查看额度，空格或回车打开客户端。");
            button.PreviewKeyDown+=(_,e)=> {
                if(e.Key is Key.Enter or Key.Space) {
                    if(!e.IsRepeat) {LastOpenInput="keyboard";OpenRequested?.Invoke(client);}
                    e.Handled=true;
                }
            };
            AutomationProperties.SetAutomationId(button,"pet-"+client.ToString().ToLowerInvariant());
            // Mouse activation goes exclusively through the jar's double-click
            // gesture. A Button.Click must never become a single-click shortcut.
            canvas.Children.Add(button);
            petButtons[client]=button;
        }
        RefreshPets();
        Content=canvas;
        MouseMove+=(_,e) => {
            if (dragStart is Point start && (primaryPress?e.LeftButton:e.RightButton)==MouseButtonState.Pressed)
            {
                var current=PointToScreen(e.GetPosition(this)); var delta=current-start;
                var scale=VisualTreeHelper.GetDpi(this);
                if (!dragged && Math.Abs(delta.X)+Math.Abs(delta.Y)>4) {
                    dragged=true;SetHover(null);
                    if(pressedObject!=null)Drawing.Scene.Grab(pressedObject,pointerStart);
                }
                if (dragged) {
                    if(pressedObject!=null) {
                        var point=e.GetPosition(this);double now=clock.Elapsed.TotalSeconds;
                        double elapsed=now-lastDragTime;
                        if(elapsed>.003) {throwVelocity=(point-lastDragPoint)/elapsed;lastDragPoint=point;lastDragTime=now;}
                        Drawing.Scene.Drag(point);
                    }
                    else {Left=windowStart.X+delta.X/scale.DpiScaleX;Top=windowStart.Y+delta.Y/scale.DpiScaleY;}
                }
            }
            else {var hit=Drawing.Scene.Hit(e.GetPosition(this));SetHover(hit?.Client);Cursor=hit==null?Cursors.Arrow:Cursors.Hand;}
        };
        MouseLeave+=(_,_) => SetHover(null);
        void PointerDown(MouseButtonEventArgs e,bool primary) {
            if(dragStart!=null) {e.Handled=true;return;}
            primaryPress=primary;
            pointerStart=e.GetPosition(this);pressedObject=Drawing.Scene.Hit(pointerStart);
            if(primary&&pressedObject!=null)Drawing.Scene.Press(pressedObject,pointerStart);
            lastDragPoint=pointerStart;lastDragTime=clock.Elapsed.TotalSeconds;throwVelocity=default;
            InputObserved?.Invoke($"mouse down: object={pressedObject?.Id}, client={pressedObject?.Client}, count={e.ClickCount}");
            hoverGeneration++;usagePopup.IsOpen=false;
            gesture.Begin(primary?pressedObject?.Client:null,primary?e.ClickCount:0);
            dragStart=PointToScreen(e.GetPosition(this)); windowStart=new(Left,Top); dragged=false;
            if(pressedObject==null) {
                jarMotion.Begin(windowStart,clock.Elapsed.TotalSeconds);
                jarMoveStart=new();foreach(var item in Drawing.Scene.Objects)jarMoveStart[item]=item.Center;
                jarImpulseStart=Drawing.Scene.JarMotionImpulseCount;
            }
            CaptureMouse(); e.Handled=primary;
        }
        void PointerUp(MouseButtonEventArgs e,bool primary) {
            if (dragStart==null) return;
            if(primary!=primaryPress) {e.Handled=true;return;}
            dragStart=null;ReleaseMouseCapture();
            if(pressedObject==null)jarMotion.End();
            Drawing.Scene.Release(dragged&&clock.Elapsed.TotalSeconds-lastDragTime<.12?throwVelocity:null);
            if (dragged&&pressedObject==null) { Clamp(); SavePosition(); }
            var released=Drawing.Scene.Hit(e.GetPosition(this));
            var selected=gesture.Release(primary?released?.Client:null,dragged);
            InputObserved?.Invoke($"mouse up: object={pressedObject?.Id}, drag={dragged}, center={pressedObject?.Center}, squeeze={pressedObject?.Compression:P1}, contacts={Drawing.Scene.ContactCount}, collisions={Drawing.Scene.CollisionCount}");
            if(selected is Client client) {LastOpenInput="mouse-double-click";OpenRequested?.Invoke(client);}
            else if(primary&&!dragged&&pressedObject!=null&&released==pressedObject) {Drawing.Scene.Poke(pressedObject,e.GetPosition(this));RefreshQuota();}
            pressedObject=null;
            e.Handled=primary||dragged;dragged=false;
        }
        PreviewMouseLeftButtonDown+=(_,e)=>PointerDown(e,true);
        PreviewMouseLeftButtonUp+=(_,e)=>PointerUp(e,true);
        // A secondary-button drag is also supported (including swapped mouse
        // configurations); a stationary secondary click still opens the menu.
        PreviewMouseRightButtonDown+=(_,e)=>PointerDown(e,false);
        PreviewMouseRightButtonUp+=(_,e)=>PointerUp(e,false);
        LostMouseCapture+=(_,_)=>CancelPointer();
        Deactivated+=(_,_)=>CancelPointer();
        Loaded+=(_,_)=>RefreshAnimation();
        IsVisibleChanged+=(_,_)=>RefreshAnimation();
        SystemParameters.StaticPropertyChanged+=SystemAnimationChanged;
        SourceInitialized+=(_,_) => {
            source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); source.AddHook(Hook);
            var work=WorkArea(); Left=settings.Left??work.Right-Width-25; Top=settings.Top??work.Bottom-Height-35;
            Clamp();
        };
        Closed+=(_,_) => {CancelPointer();closed=true;hoverGeneration++;usageRefresh.Stop();usagePopup.IsOpen=false;SetRendering(false);SystemParameters.StaticPropertyChanged-=SystemAnimationChanged;SavePosition();if(source!=null) {source.RemoveHook(Hook);} };
    }
    private void CancelPointer()
    {
        if(dragStart==null)return;
        dragStart=null;pressedObject=null;dragged=false;Drawing.Scene.Release();gesture.Begin(null,0);jarMotion.End();
        if(IsMouseCaptured)ReleaseMouseCapture();
    }
    internal void SetHover(Client? client)
    {
        if(client is Client candidate&&!Drawing.Contains(candidate))client=null;
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
    internal Client? HitPet(Point point)
    {
        return Drawing.Scene.Hit(point)?.Client;
    }
    internal void RefreshPets()
    {
        Drawing.SetPets(settings.JarPets);
        CancelPointer();
        foreach(var (client,button) in petButtons)button.Visibility=Drawing.Contains(client)?Visibility.Visible:Visibility.Collapsed;
        // Cancel any in-flight quota request and click gesture for a removed pet.
        gesture.Begin(null,0);RefreshQuota();RefreshAnimation();
        UpdateButtons();
    }
    internal void RefreshFillers()
    {
        CancelPointer();Drawing.Scene.Release();gesture.Begin(null,0);
        jarMotion.Reset();jarMoveStart=null;lastPhysicsTime=-1;SetHover(null);
        Drawing.SetFillers(settings.JarFillers);
        UpdateButtons();RefreshQuota();RefreshAnimation();
    }
    private void RefreshAnimation()
    {
        Animate();
        // Reduced motion controls spontaneous eye animation; direct manipulation
        // and gravity must still complete rather than suspend a grabbed body.
        SetRendering(IsVisible&&!closed);
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
        else {CompositionTarget.Rendering-=RenderFrame;jarMotion.Reset();jarMoveStart=null;lastPhysicsTime=-1;}
    }
    private void RenderFrame(object? sender,EventArgs e)
    {
        if(e is RenderingEventArgs frame) {
            if(frame.RenderingTime==lastFrame)return;
        }
        double now=clock.Elapsed.TotalSeconds;
        bool tracking=jarMotion.IsTracking;
        if(!FreezeExpression) {
            Drawing.Scene.ApplyJarVelocityChange(jarMotion.Sample(new(Left,Top),now));
            if(lastPhysicsTime>=0)Drawing.Scene.Advance(now-lastPhysicsTime);
        }
        else {jarMotion.Reset();jarMoveStart=null;}
        lastPhysicsTime=now;Drawing.UpdatePoses();UpdateButtons();
        if(tracking&&!jarMotion.IsTracking&&jarMoveStart!=null) {
            int impulses=Drawing.Scene.JarMotionImpulseCount-jarImpulseStart,moved=0;double distance=0;
            foreach(var item in Drawing.Scene.Objects)if(jarMoveStart.TryGetValue(item,out var start)) {
                double travel=(item.Center-start).Length;if(travel>.5)moved++;distance=Math.Max(distance,travel);
            }
            if(impulses>0)InputObserved?.Invoke($"jar motion stopped: impulses={impulses}, moved={moved}, maxShift={distance:F2}, collisions={Drawing.Scene.CollisionCount}");
            jarMoveStart=null;
        }
        if(dragStart==null&&IsMouseOver)SetHover(HitPet(Mouse.GetPosition(this)));
        if(e is RenderingEventArgs rendered)lastFrame=rendered.RenderingTime;
        Animate();
    }
    private double lastPhysicsTime=-1;
    private void UpdateButtons()
    {
        foreach(var item in Drawing.Scene.Objects)if(item.Client is Client client&&petButtons.TryGetValue(client,out var button)) {
            var bounds=item.Bounds;button.Width=bounds.Width;button.Height=bounds.Height;Canvas.SetLeft(button,bounds.X);Canvas.SetTop(button,bounds.Y);
        }
    }
    private void Animate()
    {
        bool stationary=FreezeExpression||!SystemParameters.ClientAreaAnimation;
        double now=clock.Elapsed.TotalSeconds;
        foreach(var client in characters)
        {
            if(!Drawing.Contains(client))continue;
            var mood=Drawing.Hovered==client?EyeMood.Hover:EyeMood.Idle;
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
        var previous=new Point(Left,Top);
        var work=WorkArea(); Left=Math.Clamp(Left,work.Left,Math.Max(work.Left,work.Right-Width));
        Top=Math.Clamp(Top,work.Top,Math.Max(work.Top,work.Bottom-Height));
        jarMotion.Rebase(new Point(Left,Top)-previous);
    }
    internal void ResetPosition()
    {
        jarMotion.Reset();jarMoveStart=null;
        var work=WorkArea(); Left=work.Right-Width-25; Top=work.Bottom-Height-35; Clamp(); SavePosition(); Show();
    }
    private void SavePosition() { if (!double.IsFinite(Left)||!double.IsFinite(Top))return;settings.Left=Left;settings.Top=Top;settings.Save(); }
    private IntPtr Hook(IntPtr handle,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if (message==0x007E) Dispatcher.BeginInvoke(() => { Clamp(); });
        return IntPtr.Zero;
    }
}
