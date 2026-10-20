using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PetJar;

internal sealed class PetShowcaseWindow : Window
{
    private readonly PetCollection collection;
    private readonly Dictionary<Client,ToggleButton> tiles=new();
    private readonly Dictionary<Client,TextBlock> labels=new();
    private readonly TextBlock summary=new(),feedback=new();
    internal PetShowcaseWindow(PetCollection collection)
    {
        this.collection=collection;
        Title="宠物橱窗";Width=660;Height=640;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        MaxWidth=Math.Max(320,SystemParameters.WorkArea.Width-24);MaxHeight=Math.Max(360,SystemParameters.WorkArea.Height-24);
        WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
        ResizeMode=ResizeMode.NoResize;Topmost=true;ShowInTaskbar=true;
        FontFamily=new("Microsoft YaHei UI");Foreground=DesktopTheme.Ink;FontSize=12;
        var layout=new Grid {Margin=new(22,14,22,18)};
        layout.RowDefinitions.Add(new() {Height=GridLength.Auto});
        layout.RowDefinitions.Add(new() {Height=new(1,GridUnitType.Star)});
        layout.RowDefinitions.Add(new() {Height=GridLength.Auto});

        var header=new Grid {Margin=new(6,0,6,14),Background=Brushes.Transparent};
        header.MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource is not Button&&e.LeftButton==MouseButtonState.Pressed)DragMove();};
        var titles=new StackPanel {Margin=new(0,16,38,0)};
        titles.Children.Add(new TextBlock {Text="宠物橱窗",FontSize=24,FontWeight=FontWeights.SemiBold});
        titles.Children.Add(new TextBlock {Text="点一下放进罐中，再点一下移回橱窗。",Foreground=DesktopTheme.Muted,Margin=new(0,8,0,0)});
        header.Children.Add(titles);
        var close=new Button {Content="×",Width=30,Height=30,FontSize=22,Background=Brushes.Transparent,
            Foreground=DesktopTheme.Muted,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top};
        AutomationProperties.SetName(close,"关闭宠物橱窗");close.Click+=(_,_)=>Close();header.Children.Add(close);
        DesktopTheme.Put(layout,header,0);

        var grid=new UniformGrid {Columns=2,Rows=2};
        foreach(var client in Enum.GetValues<Client>())grid.Children.Add(Card(client));
        var scroll=new ScrollViewer {Content=grid,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        DesktopTheme.Put(layout,scroll,1);

        var footer=new Grid {Margin=new(6,16,6,0)};
        var messages=new StackPanel {VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,0)};
        summary.FontSize=12;messages.Children.Add(summary);
        feedback.FontSize=11;feedback.Foreground=DesktopTheme.Muted;feedback.Margin=new(0,5,0,0);feedback.TextWrapping=TextWrapping.Wrap;
        feedback.Text="点击即更新 · 每种一只 · 自动保存";messages.Children.Add(feedback);footer.Children.Add(messages);
        DesktopTheme.Put(layout,footer,2);
        var surface=new Border {CornerRadius=new(22),BorderThickness=new(1),BorderBrush=Brushes.White,
            Background=new LinearGradientBrush(Color.FromRgb(252,252,247),Color.FromRgb(237,242,234),90),Child=layout,
            Effect=new DropShadowEffect {Color=Color.FromRgb(45,60,47),Opacity=.22,BlurRadius=18,ShadowDepth=4}};
        Content=new Border {Padding=new(12),Child=surface};
        if(SystemParameters.HighContrast) {
            surface.Background=SystemColors.WindowBrush;surface.BorderBrush=SystemColors.WindowTextBrush;
            Foreground=SystemColors.WindowTextBrush;summary.Foreground=SystemColors.WindowTextBrush;
            feedback.Foreground=SystemColors.WindowTextBrush;
            foreach(var title in titles.Children.OfType<TextBlock>())title.Foreground=SystemColors.WindowTextBrush;
            close.Foreground=SystemColors.WindowTextBrush;
        }
        PreviewKeyDown+=(_,e)=> {if(e.Key==Key.Escape) {Close();e.Handled=true;}};
        collection.Changed+=Refresh;
        Closed+=(_,_)=> {collection.Changed-=Refresh;};
        Refresh();
    }
    private FrameworkElement Card(Client client)
    {
        var grid=new Grid {Margin=new(6),MinHeight=196};
        grid.RowDefinitions.Add(new() {Height=new(1,GridUnitType.Star)});
        var content=new Grid {Margin=new(14,10,14,8)};
        content.RowDefinitions.Add(new() {Height=new(116)});content.RowDefinitions.Add(new() {Height=GridLength.Auto});
        content.RowDefinitions.Add(new() {Height=GridLength.Auto});
        var avatar=new Image {Source=Art.Avatar(client),Height=112,MaxWidth=172,Stretch=Stretch.Uniform};
        content.Children.Add(avatar);
        var name=new TextBlock {Text=client.Name(),FontSize=14,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,4,0,0)};
        DesktopTheme.Put(content,name,1);
        var tile=new ToggleButton {Content=content,Background=Brushes.Transparent,BorderBrush=Brushes.Transparent,BorderThickness=new(1),
            Cursor=Cursors.Hand,Style=TileStyle,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        AutomationProperties.SetName(tile,$"{client.Name()}，点击加入或移回");AutomationProperties.SetAutomationId(tile,"showcase-select-"+client.ToString().ToLowerInvariant());
        tile.Click+=(_,_)=>Select(client,tile.IsChecked==true);
        tiles[client]=tile;grid.Children.Add(tile);
        var label=new TextBlock {FontSize=11,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,12,0,0)};
        labels[client]=label;DesktopTheme.Put(content,label,2);
        var surface=new Border {CornerRadius=new(16),BorderThickness=new(1),BorderBrush=new SolidColorBrush(Color.FromArgb(220,255,255,255)),
            Background=new SolidColorBrush(client switch {
                Client.ChatGPT=>Color.FromRgb(250,242,215),Client.Grok=>Color.FromRgb(230,236,234),
                Client.Muse=>Color.FromRgb(248,239,229),_=>Color.FromRgb(250,231,216)}),Child=grid,Margin=new(3)};
        if(SystemParameters.HighContrast) {surface.Background=SystemColors.WindowBrush;surface.BorderBrush=SystemColors.WindowTextBrush;}
        return surface;
    }
    private void Select(Client client,bool value)
    {
        if(!collection.Update(value?new[] {client}:Array.Empty<Client>(),value?Array.Empty<Client>():new[] {client})) {ShowFailure();return;}
        Refresh();feedback.Text=$"{client.Name()} "+(value?"已放入罐中":"已移回橱窗")+" · 已保存";
    }
    private void ShowFailure()
    {
        feedback.Text="保存失败，罐中的宠物保持原样，请重试。";
        Refresh();
    }
    internal void Refresh()
    {
        foreach(var client in Enum.GetValues<Client>())
        {
            bool present=collection.Contains(client);
            tiles[client].IsChecked=present;
            labels[client].Text=present?"✓ 已在罐中 · 点击移回":"点击放入罐中";
            labels[client].Foreground=SystemParameters.HighContrast?SystemColors.WindowTextBrush:present?JarDrawing.Brush(.32,.42,.29):DesktopTheme.Muted;
        }
        summary.Text=$"罐中 {collection.Count} / {Enum.GetValues<Client>().Length} 只";
    }
    internal static readonly Style TileStyle=(Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ToggleButton">
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ToggleButton">
            <Border x:Name="Tile" CornerRadius="13" Margin="2" BorderThickness="{TemplateBinding BorderThickness}" BorderBrush="{TemplateBinding BorderBrush}" Background="{TemplateBinding Background}">
              <ContentPresenter HorizontalAlignment="Stretch" VerticalAlignment="Stretch"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Tile" Property="Background" Value="#65FFFFFF"/></Trigger>
              <Trigger Property="IsChecked" Value="True"><Setter TargetName="Tile" Property="BorderBrush" Value="#7C956F"/><Setter TargetName="Tile" Property="Background" Value="#B5F2F7EA"/></Trigger>
              <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Tile" Property="BorderBrush" Value="{DynamicResource {x:Static SystemColors.HighlightBrushKey}}"/><Setter TargetName="Tile" Property="BorderThickness" Value="2"/></Trigger>
              <MultiDataTrigger>
                <MultiDataTrigger.Conditions>
                  <Condition Binding="{Binding Source={x:Static SystemParameters.HighContrast}}" Value="True"/>
                  <Condition Binding="{Binding IsChecked, RelativeSource={RelativeSource Self}}" Value="True"/>
                </MultiDataTrigger.Conditions>
                <Setter TargetName="Tile" Property="Background" Value="{DynamicResource {x:Static SystemColors.HighlightBrushKey}}"/>
                <Setter TargetName="Tile" Property="BorderBrush" Value="{DynamicResource {x:Static SystemColors.HighlightBrushKey}}"/>
                <Setter Property="Foreground" Value="{DynamicResource {x:Static SystemColors.HighlightTextBrushKey}}"/>
              </MultiDataTrigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
}
