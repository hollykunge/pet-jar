using System;
using System.IO;
using System.Text.Json;

namespace PetJar;

internal static class InteractionQuotaTests
{
    private static void Check(bool result,string message) {if(!result)throw new InvalidOperationException(message);}
    internal static void Run()
    {
        Check(ClientDiscovery.MatchesProduct(Client.ChatGPT,"ChatGPT.exe","Codex","OpenAI OpCo, LLC"),"registered Codex desktop has ChatGPT.exe name");
        Check(!ClientDiscovery.MatchesProduct(Client.ChatGPT,"codex.exe","",""),"CLI must not be bound as a desktop application");
        Check(ClientDiscovery.MatchesProduct(Client.Grok,"Grok Bot.exe","Grok Bot","SpaceXAI"),"Grok identity requires product and publisher");
        Check(!ClientDiscovery.MatchesProduct(Client.Grok,"Cursor.exe","Cursor","Anysphere"),"never bind a different editor to Grok");
        Check(!ClientDiscovery.MatchesProduct(Client.Muse,"Muse.exe","Muse","Adobe"),"unrelated Muse products must not bind");
        string manual=Path.GetTempFileName();
        try {
            var bindings=new Settings();bindings.Executables[Client.Claude]=manual;
            var installed=new[] {new InstalledClient(Client.Claude,"detected-claude.exe","test"),new InstalledClient(Client.ChatGPT,"new-version.exe","test","OpenAI.Codex_2p2nqsd0c76g0!App")};
            Check(ClientDiscovery.Apply(bindings,installed),"missing bindings are populated");
            Check(bindings.Executables[Client.Claude]==manual&&!bindings.AutoBindings.ContainsKey(Client.Claude),"existing manual selection is retained");
            Check(bindings.AppUserModelIds[Client.ChatGPT].EndsWith("!App"),"package identity is retained for cold launch");
            var updated=new[] {new InstalledClient(Client.ChatGPT,"updated-version.exe","test","OpenAI.Codex_2p2nqsd0c76g0!App")};
            bindings.WindowTitles[Client.ChatGPT]="keep conversation";bindings.QuotaSources[Client.ChatGPT]=QuotaSource.Unavailable;
            Check(ClientDiscovery.Apply(bindings,updated)&&bindings.Executables[Client.ChatGPT]=="updated-version.exe","automatic binding refreshes after package upgrade");
            Check(bindings.WindowTitles[Client.ChatGPT]=="keep conversation"&&bindings.QuotaSources[Client.ChatGPT]==QuotaSource.Unavailable,"automatic refresh preserves title and quota preferences");
            Check(!bindings.Executables.ContainsKey(Client.Muse),"undetected client stays unbound");
        }finally {File.Delete(manual);}
        var click=new PetClickGesture();
        click.Begin(Client.Claude,1);Check(click.Release(Client.Claude,false)==null,"single click must not open anything");
        click.Begin(Client.Claude,2);Check(click.Release(Client.Claude,false)==Client.Claude,"double click opens the same pet's client");
        click.Begin(Client.Muse,1);click.Release(Client.Muse,false);
        click.Begin(Client.Claude,2);Check(click.Release(Client.Claude,false)==null,"two different pets cannot form a double click");
        click.Begin(Client.Claude,2);Check(click.Release(Client.Claude,true)==null,"dragging never opens a client");
        click.Begin(null,1);click.Release(null,false);click.Begin(null,2);Check(click.Release(null,false)==null,"decorations never open a client");
        click.Begin(Client.Grok,1);click.Release(Client.Grok,false);click.Begin(Client.Grok,2);
        Check(click.Release(Client.Muse,false)==null,"release on another pet cancels the click");
        var settings=new Settings();var launcher=new ClientLauncher(settings);
        var missing=launcher.OpenAsync(Client.Claude).GetAwaiter().GetResult();Check(missing.NeedsBinding&&!missing.Opened,"unbound client must request binding");
        Check(AccountQuotaService.Resolve(settings,Client.ChatGPT)==QuotaSource.Codex,"canonical Codex source");
        settings.Executables[Client.ChatGPT]="unrelated.exe";
        Check(AccountQuotaService.Resolve(settings,Client.ChatGPT)==QuotaSource.Unavailable,"never attribute Codex quota to an unrelated bound app");
        settings.QuotaSources[Client.ChatGPT]=QuotaSource.Claude;
        Check(AccountQuotaService.Resolve(settings,Client.ChatGPT)==QuotaSource.Claude,"explicit source selection is preserved");
        using var multi=JsonDocument.Parse("""
            {"rateLimits":{"primary":{"usedPercent":99}},"rateLimitsByLimitId":{
              "codex":{"primary":{"usedPercent":0,"windowDurationMins":10080,"resetsAt":2000000000},"secondary":null},
              "other":{"limitName":"Additional","primary":{"usedPercent":31.5,"windowDurationMins":300,"resetsAt":2100000000}}}}
            """);
        var quota=QuotaParser.Codex(multi.RootElement);
        Check(quota.Windows.Count==2,"prefer all limit buckets and avoid counting legacy duplicate");
        Check(quota.Windows[0].RemainingPercent==100&&quota.Windows[0].Name.Contains("7天"),"zero used means 100 remaining, window length comes from server");
        Check(quota.Windows[1].RemainingPercent==68.5&&quota.Windows[1].Name.Contains("5小时"),"used/remaining semantics must not be reversed");
        Check(quota.Windows[0].ResetsAt==DateTimeOffset.FromUnixTimeSeconds(2000000000),"reset timestamp is seconds");
        using var unknown=JsonDocument.Parse("""{"rateLimits":{"primary":{"usedPercent":null},"secondary":{"usedPercent":101}}}""");
        Check(QuotaParser.Codex(unknown.RootElement).Windows.Count==0,"missing/invalid usage is unavailable, never zero");
        using var claude=JsonDocument.Parse("""{"five_hour":{"utilization":72.25,"resets_at":"2026-10-20T00:00:00Z"},"seven_day":null}""");
        var windows=QuotaParser.Claude(claude.RootElement).Windows;
        Check(windows.Count==1&&windows[0].RemainingPercent==27.75,"Claude quota is a percentage, not token counts");
        Check(windows[0].Expired(DateTimeOffset.Parse("2026-10-21T00:00:00Z")),"expired cached windows must be marked outdated");
    }
}
