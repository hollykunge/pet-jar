using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PetJar;

internal sealed class FillerShowcaseWindow:Window
{
    private readonly FillerCollection collection;
    private readonly Dictionary<FillerKind,ToggleButton> tiles=new();
    private readonly Dictionary<FillerKind,TextBlock> labels=new();
    private readonly TextBlock summary=new(),feedback=new();
    internal FillerShowcaseWindow(FillerCollection collection)
    {
        this.collection=collection;
        Title="填充物橱窗";Width=720;Height=610;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        MaxWidth=Math.Max(320,SystemParameters.WorkArea.Width-24);MaxHeight=Math.Max(360,SystemParameters.WorkArea.Height-24);
        WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
        ResizeMode=ResizeMode.NoResize;Topmost=true;ShowInTaskbar=true;
        FontFamily=new("Microsoft YaHei UI");Foreground=DesktopTheme.Ink;FontSize=12;
        var layout=new Grid {Margin=new(24,22,24,22)};
        layout.RowDefinitions.Add(new() {Height=GridLength.Auto});
        layout.RowDefinitions.Add(new() {Height=GridLength.Auto});
        layout.RowDefinitions.Add(new() {Height=new(1,GridUnitType.Star)});
        layout.RowDefinitions.Add(new() {Height=GridLength.Auto});
        var header=new Grid {Background=Brushes.Transparent,Margin=new(4,0,4,0)};
        header.MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource is not Button&&e.LeftButton==MouseButtonState.Pressed)DragMove();};
        var titles=new StackPanel {Margin=new(0,0,36,0)};
        titles.Children.Add(new TextBlock {Text="填充物橱窗",FontSize=24,FontWeight=FontWeights.SemiBold});
        titles.Children.Add(new TextBlock {Text="选一种，铺满一种；选多种，自由混搭。",Foreground=DesktopTheme.Muted,Margin=new(0,8,0,0)});
        header.Children.Add(titles);
        var close=new Button {Content="×",Width=30,Height=30,FontSize=22,Background=Brushes.Transparent,
            Foreground=DesktopTheme.Muted,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top};
        AutomationProperties.SetName(close,"关闭填充物橱窗");close.Click+=(_,_)=>Close();header.Children.Add(close);
        DesktopTheme.Put(layout,header,0);
        var tools=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,12,4,8)};
        tools.Children.Add(ChoiceButton("全选","fillers-all",()=>SelectAll(true)));
        tools.Children.Add(ChoiceButton("清空","fillers-clear",()=>SelectAll(false)));
        DesktopTheme.Put(layout,tools,1);
        var grid=new UniformGrid {Columns=4,Rows=2};
        foreach(var kind in Enum.GetValues<FillerKind>())grid.Children.Add(Card(kind));
        DesktopTheme.Put(layout,new ScrollViewer {Content=grid,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},2);
        var footer=new Grid {Margin=new(4,18,4,0)};
        var messages=new StackPanel {VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,16,0)};
        summary.FontSize=13;messages.Children.Add(summary);
        feedback.FontSize=11;feedback.Foreground=DesktopTheme.Muted;feedback.Margin=new(0,6,0,0);feedback.TextWrapping=TextWrapping.Wrap;
        messages.Children.Add(feedback);footer.Children.Add(messages);
        feedback.Text="点一下加入，再点取消 · 自动保存";DesktopTheme.Put(layout,footer,3);
        var surface=new Border {CornerRadius=new(22),BorderThickness=new(1),BorderBrush=Brushes.White,
            Background=new LinearGradientBrush(Color.FromRgb(252,252,247),Color.FromRgb(237,242,234),90),Child=layout,
            Effect=new DropShadowEffect {Color=Color.FromRgb(45,60,47),Opacity=.22,BlurRadius=18,ShadowDepth=4}};
        Content=new Border {Padding=new(12),Child=surface};
        if(SystemParameters.HighContrast) {
            surface.Background=SystemColors.WindowBrush;surface.BorderBrush=SystemColors.WindowTextBrush;Foreground=SystemColors.WindowTextBrush;
            feedback.Foreground=SystemColors.WindowTextBrush;close.Foreground=SystemColors.WindowTextBrush;
            foreach(var title in titles.Children.OfType<TextBlock>())title.Foreground=SystemColors.WindowTextBrush;
        }
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape) {Close();e.Handled=true;}};
        collection.Changed+=Refresh;Closed+=(_,_)=>collection.Changed-=Refresh;
        Refresh();
    }
    private Button ChoiceButton(string label,string id,Action action)
    {
        var button=new Button {Content=label,Height=28,Padding=new(10,0,10,0),Margin=new(4,0,0,0),
            Background=Brushes.Transparent,Foreground=SystemParameters.HighContrast?SystemColors.WindowTextBrush:DesktopTheme.Muted};
        AutomationProperties.SetAutomationId(button,id);button.Click+=(_,_)=>action();return button;
    }
    private FrameworkElement Card(FillerKind kind)
    {
        var content=new StackPanel {Margin=new(8,10,8,12),VerticalAlignment=VerticalAlignment.Center};
        content.Children.Add(new Image {Source=Art.Sprite(kind.Sprite()),Height=78,MaxWidth=100,Stretch=Stretch.Uniform,Margin=new(0,0,0,8)});
        content.Children.Add(new TextBlock {Text=kind.Name(),FontSize=13,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center});
        content.Children.Add(new TextBlock {Text=kind.Texture(),FontSize=10,Opacity=.65,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,4,0,0)});
        var label=new TextBlock {FontSize=10,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,8,0,0)};
        labels[kind]=label;content.Children.Add(label);
        var tile=new ToggleButton {Content=content,MinHeight=168,Cursor=Cursors.Hand,Style=PetShowcaseWindow.TileStyle,
            Background=Brushes.Transparent,BorderBrush=Brushes.Transparent,BorderThickness=new(1),
            HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        AutomationProperties.SetName(tile,$"{kind.Name()}，点击加入或取消");AutomationProperties.SetAutomationId(tile,"fillers-select-"+kind.ToString().ToLowerInvariant());
        tile.Click+=(_,_)=>Select(kind,tile.IsChecked==true);tiles[kind]=tile;
        var surface=new Border {CornerRadius=new(16),BorderThickness=new(1),BorderBrush=Brushes.White,Margin=new(4),Child=tile,
            Background=new SolidColorBrush(kind switch {
                FillerKind.Wood or FillerKind.Amber=>Color.FromRgb(247,238,220),FillerKind.Blue or FillerKind.Bubble=>Color.FromRgb(229,240,243),
                FillerKind.Knit=>Color.FromRgb(247,236,230),FillerKind.Moss=>Color.FromRgb(231,239,222),_=>Color.FromRgb(234,236,232)})};
        if(SystemParameters.HighContrast) {surface.Background=SystemColors.WindowBrush;surface.BorderBrush=SystemColors.WindowTextBrush;}
        return surface;
    }
    private void Select(FillerKind kind,bool value)
    {
        var next=Enum.GetValues<FillerKind>().Where(collection.Contains).ToHashSet();
        if(value)next.Add(kind);else next.Remove(kind);
        SaveSelection(next,kind.Name()+(value?"已加入填充":"已取消填充"));
    }
    private void SelectAll(bool value)
    {
        SaveSelection(value?Enum.GetValues<FillerKind>():Array.Empty<FillerKind>(),value?"已加入全部种类":"已清空填充物");
    }
    private void Refresh()
    {
        int count=0;
        foreach(var (kind,tile) in tiles) {bool present=collection.Contains(kind);tile.IsChecked=present;if(present)count++;labels[kind].Text=present?"✓ 已在罐中 · 点击取消":"点击加入填充";}
        summary.Text=count==0?"不放填充物，只留下宠物":$"罐中 {count} 种 · "+(count==1?"单一材质填充":"混合填充");
    }
    private void SaveSelection(IEnumerable<FillerKind> kinds,string message)
    {
        if(!collection.SetSelection(kinds)) {Refresh();feedback.Text="保存失败，罐内保持原样，请再点一次重试。";return;}
        Refresh();feedback.Text=message+" · 已保存";
    }
}
