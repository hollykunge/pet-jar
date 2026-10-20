using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PetJar;

internal static class QuotaCardTests
{
    private static void Check(bool result,string message) {if(!result)throw new InvalidOperationException(message);}
    private static IEnumerable<string> Texts(DependencyObject node)
    {
        if(node is TextBlock text)yield return text.Text;
        foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach(var line in Texts(child))yield return line;
    }
    private static string Content(Client client,AccountQuota quota,bool bound=true)=>string.Join("\n",Texts(QuotaCard.Build(client,quota,bound)));
    internal static void Run()
    {
        var now=DateTimeOffset.UtcNow;
        var quota=new AccountQuota("本机 Codex 登录账号",now,new[] {new QuotaWindow("Codex · 7天",2,now.AddDays(2))},"账号额度比例，非 Token 数量");
        var normal=Content(Client.ChatGPT,quota);
        Check(normal.Contains("本机 Codex 登录账号")&&normal.Contains("Codex · 7天")&&normal.Contains("剩余 98%")&&normal.Contains("更新"),"compact card preserves login source, period, remaining quota and update time");
        Check(!normal.Contains("双击")&&!normal.Contains("已用")&&!normal.Contains("Token"),"compact card omits repeated interaction hints and explanations");
        var unknown=Content(Client.Muse,AccountQuota.Missing("当前绑定客户端","该客户端尚未接入账号额度来源。"),false);
        Check(unknown.Contains("未绑定")&&unknown.Contains("额度暂未接入")&&!unknown.Contains('%'),"unknown quota never displays a made-up percentage");
        var expired=quota with {Windows=new[] {new QuotaWindow("7天",2,now.AddSeconds(-1))},Stale=true};
        var old=Content(Client.ChatGPT,expired);
        Check(old.Contains("周期已到期")&&old.Contains("刷新失败")&&old.Contains("上次")&&!old.Contains('%'),"expired cached quota hides its old percentage and keeps stale timestamp");
        var stale=Content(Client.ChatGPT,quota with {Stale=true});
        Check(stale.Contains("剩余 98%")&&stale.Contains("刷新失败")&&stale.Contains("上次"),"unexpired cached data remains visibly marked stale");
        var full=Content(Client.ChatGPT,quota with {Windows=new[] {new QuotaWindow("7天",0,null)}});
        Check(full.Contains("剩余 100%")&&full.Contains("重置时间未知"),"true zero use remains full remaining quota without inventing a reset time");
        Check(QuotaCard.UnavailableReason("本机 Claude Code 登录已过期，请在客户端重新登录。")=="登录已过期 · 请重新登录","expired login has a concise actionable state");
    }
}
