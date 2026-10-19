using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PetJar;

internal sealed class QuotaPopup : Popup
{
    private readonly StackPanel rows=new() {Margin=new(14,12,14,12)};
    private Client client;
    internal QuotaPopup(FrameworkElement owner)
    {
        PlacementTarget=owner;Placement=PlacementMode.Custom;AllowsTransparency=true;StaysOpen=true;
        IsHitTestVisible=false;Focusable=false;PopupAnimation=PopupAnimation.None;
        CustomPopupPlacementCallback=(size,target,offset)=>new[] {
            new CustomPopupPlacement(new Point(-size.Width-8,Math.Min(120,Math.Max(0,260-size.Height)/2)),PopupPrimaryAxis.Vertical),
            new CustomPopupPlacement(new Point(268,Math.Min(120,Math.Max(0,260-size.Height)/2)),PopupPrimaryAxis.Vertical)
        };
        var scroll=new ScrollViewer {Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=450};
        Child=new Border {Width=284,Margin=new(8),CornerRadius=new(12),BorderThickness=new(1),BorderBrush=new SolidColorBrush(Color.FromArgb(220,255,255,255)),
            Background=new SolidColorBrush(Color.FromRgb(249,247,241)),Child=scroll,
            Effect=new DropShadowEffect {BlurRadius=14,ShadowDepth=2,Opacity=.20,Color=Colors.Black}};
    }
    private static TextBlock Text(string value,double size=11,Brush? color=null)=>new() {
        Text=value,FontSize=size,FontFamily=new("Microsoft YaHei UI"),Foreground=color??new SolidColorBrush(Color.FromRgb(65,61,55)),TextWrapping=TextWrapping.Wrap
    };
    internal void Loading(Client target)
    {
        client=target;rows.Children.Clear();Header();rows.Children.Add(Text("正在读取账号额度…"));IsOpen=true;
    }
    private void Header()
    {
        var title=Text(client.Name()+" · 账号额度",13);title.FontWeight=FontWeights.SemiBold;title.Margin=new(0,0,0,8);rows.Children.Add(title);
        AutomationProperties.SetName(rows,client.Name()+" 账号额度");
    }
    internal void Update(Client target,AccountQuota quota,bool bound)
    {
        client=target;rows.Children.Clear();Header();var now=DateTimeOffset.UtcNow;
        var source=Text(quota.Source,10,Brushes.DimGray);source.Margin=new(0,0,0,8);rows.Children.Add(source);
        foreach(var window in quota.Windows.Take(3))
        {
            var label=Text(window.Name,11);label.FontWeight=FontWeights.SemiBold;label.Margin=new(0,6,0,3);rows.Children.Add(label);
            if(window.Expired(now)) {rows.Children.Add(Text("该周期已到期，等待刷新",10,Brushes.DimGray));continue;}
            string used=window.UsedPercent.ToString("0.#",CultureInfo.InvariantCulture),remaining=window.RemainingPercent.ToString("0.#",CultureInfo.InvariantCulture);
            rows.Children.Add(Text($"已用 {used}%    剩余 {remaining}%"));
            rows.Children.Add(new ProgressBar {Minimum=0,Maximum=100,Value=window.UsedPercent,Height=5,Margin=new(0,5,0,4),
                Foreground=new SolidColorBrush(window.UsedPercent>=90?Color.FromRgb(205,104,54):Color.FromRgb(80,139,117)),Background=new SolidColorBrush(Color.FromRgb(230,229,220)),BorderThickness=new(0)});
            rows.Children.Add(Text(window.ResetsAt is {} reset?$"重置：{reset.ToLocalTime():MM-dd HH:mm}":"重置时间暂不可获取",10,Brushes.DimGray));
        }
        if(quota.Windows.Count>3)rows.Children.Add(Text($"另有 {quota.Windows.Count-3} 项额度，请在客户端查看。",10,Brushes.DimGray));
        var detail=Text(quota.Detail,10,Brushes.DimGray);detail.Margin=new(0,8,0,0);rows.Children.Add(detail);
        if(quota.Windows.Count>0)rows.Children.Add(Text($"{(quota.Stale?"上次读取":"更新于")} {quota.FetchedAt.ToLocalTime():HH:mm:ss}",10,Brushes.DimGray));
        var foot=Text(bound?"双击打开客户端":"尚未绑定 · 双击选择客户端",10,Brushes.DimGray);foot.Margin=new(0,8,0,0);rows.Children.Add(foot);
        IsOpen=true;
    }
}
