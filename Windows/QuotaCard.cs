using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PetJar;

internal static class QuotaCard
{
    private static Brush Ink=>SystemParameters.HighContrast?SystemColors.WindowTextBrush:DesktopTheme.Ink;
    private static Brush Muted=>SystemParameters.HighContrast?SystemColors.WindowTextBrush:DesktopTheme.Muted;
    private static Brush Accent=>SystemParameters.HighContrast?SystemColors.HighlightBrush:JarDrawing.Brush(.38,.49,.33);
    private static Brush Warning=>SystemParameters.HighContrast?SystemColors.WindowTextBrush:JarDrawing.Brush(.67,.36,.20);
    private static TextBlock Text(string value,double size=11,Brush? color=null)=>new() {
        Text=value,FontSize=size,FontFamily=new("Microsoft YaHei UI"),Foreground=color??Ink,TextWrapping=TextWrapping.Wrap
    };
    internal static Border Build(Client client,AccountQuota? quota,bool bound)
    {
        var rows=new StackPanel {Margin=new(16,14,16,12)};
        var header=new Grid();header.ColumnDefinitions.Add(new() {Width=new(36)});header.ColumnDefinitions.Add(new() {Width=new(1,GridUnitType.Star)});
        header.Children.Add(new Border {Width=36,Height=36,CornerRadius=new(11),Background=new SolidColorBrush(Color.FromArgb(180,255,255,255)),
            BorderBrush=new SolidColorBrush(Color.FromArgb(220,255,255,255)),BorderThickness=new(1),
            Child=new Image {Source=Art.Avatar(client),Width=29,Height=29,Stretch=Stretch.Uniform}});
        var identity=new StackPanel {Margin=new(10,0,0,0),VerticalAlignment=VerticalAlignment.Center};
        var name=new DockPanel();
        if(!bound) {
            var badge=Text("未绑定",9,Muted);badge.Margin=new(6,2,0,0);DockPanel.SetDock(badge,Dock.Right);name.Children.Add(badge);
        }
        var title=Text(client.Name(),13);title.FontWeight=FontWeights.SemiBold;name.Children.Add(title);identity.Children.Add(name);
        identity.Children.Add(Text(quota?.Source??"账号额度",10,Muted));
        Grid.SetColumn(identity,1);header.Children.Add(identity);rows.Children.Add(header);
        rows.Children.Add(new Border {Height=1,Margin=new(0,12,0,0),Background=Ink,Opacity=.09});
        if(quota==null) {
            var loading=Text("正在读取额度…",11,Muted);loading.Margin=new(0,13,0,2);rows.Children.Add(loading);
        } else if(quota.Windows.Count==0) {
            var unavailable=Text(UnavailableReason(quota.Detail),12,Muted);unavailable.Margin=new(0,14,0,2);rows.Children.Add(unavailable);
        } else {
            foreach(var window in quota.Windows.Take(3))AddWindow(rows,window);
        }
        if(quota!=null) {
            var footer=new DockPanel {Margin=new(0,12,0,0)};
            string time=quota.Stale?$"上次 {quota.FetchedAt.ToLocalTime():MM-dd HH:mm}":$"{quota.FetchedAt.ToLocalTime():HH:mm} {(quota.Windows.Count>0?"更新":"检查")}";
            var stamp=Text(time,9,Muted);DockPanel.SetDock(stamp,Dock.Right);footer.Children.Add(stamp);
            if(quota.Stale)footer.Children.Add(Text("刷新失败",9,Warning));
            else if(quota.Windows.Count>3)footer.Children.Add(Text($"另有 {quota.Windows.Count-3} 项",9,Muted));
            rows.Children.Add(footer);
        }
        var surface=new Border {Width=254,Margin=new(10),Child=rows,SnapsToDevicePixels=true,UseLayoutRounding=true};
        if(Application.Current?.TryFindResource("JarMenuSurface") is Style glass)surface.Style=glass;
        else {
            surface.CornerRadius=new(18);surface.BorderThickness=new(1);surface.BorderBrush=Brushes.White;
            surface.Background=new LinearGradientBrush(Color.FromRgb(252,252,247),Color.FromRgb(240,243,237),90);
            surface.Effect=new DropShadowEffect {Color=Color.FromRgb(56,71,60),BlurRadius=16,ShadowDepth=4,Opacity=.18};
        }
        if(SystemParameters.HighContrast) {surface.Background=SystemColors.WindowBrush;surface.BorderBrush=SystemColors.WindowTextBrush;surface.Effect=null;}
        AutomationProperties.SetName(surface,client.Name()+" 账号额度");
        if(quota!=null)AutomationProperties.SetHelpText(surface,quota.Detail);
        return surface;
    }
    private static void AddWindow(StackPanel rows,QuotaWindow window)
    {
        var block=new StackPanel {Margin=new(0,12,0,0)};rows.Children.Add(block);
        if(window.Expired(DateTimeOffset.UtcNow)) {
            block.Children.Add(Text(window.Name,10,Muted));
            block.Children.Add(Text("周期已到期 · 等待刷新",11,Muted));return;
        }
        string remaining=window.RemainingPercent.ToString("0.#",CultureInfo.InvariantCulture);
        Brush accent=window.RemainingPercent<=10?Warning:Accent;
        var metric=new Grid();metric.ColumnDefinitions.Add(new() {Width=new(1,GridUnitType.Star)});metric.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        var cycle=Text(window.Name,10,Muted);cycle.VerticalAlignment=VerticalAlignment.Center;cycle.Margin=new(0,0,8,0);metric.Children.Add(cycle);
        var value=Text($"剩余 {remaining}%",16,accent);value.FontWeight=FontWeights.SemiBold;Grid.SetColumn(value,1);metric.Children.Add(value);block.Children.Add(metric);
        var track=new Grid {Height=4,Margin=new(0,6,0,5)};
        track.Children.Add(new Border {CornerRadius=new(2),Background=Ink,Opacity=.09});
        var fill=new Border {CornerRadius=new(2),Background=accent,HorizontalAlignment=HorizontalAlignment.Left,Width=0};
        track.Children.Add(fill);track.SizeChanged+=(_,e)=>fill.Width=Math.Max(0,e.NewSize.Width*window.RemainingPercent/100);
        AutomationProperties.SetName(track,$"{window.Name} 剩余 {remaining}%");block.Children.Add(track);
        block.Children.Add(Text(window.ResetsAt is {} reset?$"{reset.ToLocalTime():MM-dd HH:mm} 重置":"重置时间未知",9,Muted));
    }
    internal static string UnavailableReason(string detail)
    {
        if(detail.Contains("登录已过期"))return "登录已过期 · 请重新登录";
        if(detail.Contains("尚未接入"))return "额度暂未接入";
        if(detail.Contains("未找到 Codex CLI"))return "未找到 Codex CLI";
        if(detail.Contains("未找到本机 Claude Code")||detail.Contains("先在 Claude Code 登录"))return "请先登录 Claude Code";
        if(detail.Contains("无权读取"))return "当前账号无额度读取权限";
        if(detail.Contains("超时"))return "读取超时 · 稍后重试";
        return "暂无法读取 · 请检查登录或网络";
    }
}
