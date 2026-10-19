using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PetJar;

internal sealed record QuotaWindow(string Name,double UsedPercent,DateTimeOffset? ResetsAt)
{
    internal double RemainingPercent=>100-UsedPercent;
    internal bool Expired(DateTimeOffset now)=>ResetsAt is {} reset&&reset<=now;
}
internal sealed record AccountQuota(string Source,DateTimeOffset FetchedAt,IReadOnlyList<QuotaWindow> Windows,string Detail,bool Stale=false)
{
    internal static AccountQuota Missing(string source,string detail)=>new(source,DateTimeOffset.UtcNow,Array.Empty<QuotaWindow>(),detail);
}

internal static class QuotaParser
{
    private static bool Field(JsonElement root,string name,out JsonElement value) {value=default;return root.ValueKind==JsonValueKind.Object&&root.TryGetProperty(name,out value);}
    private static string? Text(JsonElement root,string name)=>Field(root,name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
    private static double? Number(JsonElement root,string name)=>Field(root,name,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out var n)&&double.IsFinite(n)?n:null;
    private static DateTimeOffset? Epoch(double? seconds)
    {
        if(seconds is not {} value)return null;
        try {return DateTimeOffset.FromUnixTimeSeconds(checked((long)value));}catch(ArgumentOutOfRangeException) {return null;}catch(OverflowException) {return null;}
    }
    internal static string Duration(double? minutes)=>minutes switch {
        >=1440=>minutes.Value%1440==0?$"{minutes.Value/1440:0}天":$"{minutes.Value/60:0.#}小时",
        >=60=>$"{minutes.Value/60:0.#}小时",>0=>$"{minutes.Value:0.#}分钟",_=>"周期额度"
    };
    internal static AccountQuota Codex(JsonElement root,DateTimeOffset? now=null)
    {
        if(Field(root,"result",out var result))root=result;
        var windows=new List<QuotaWindow>();
        void AddBucket(JsonElement bucket,string fallback)
        {
            string name=Text(bucket,"limitName")??Text(bucket,"limitId")??fallback;
            if(name=="codex")name="Codex";
            foreach(string field in new[] {"primary","secondary"})if(Field(bucket,field,out var window)) {
                var used=Number(window,"usedPercent");if(used is not (>=0 and <=100))continue;
                windows.Add(new(name+" · "+Duration(Number(window,"windowDurationMins")),used.Value,Epoch(Number(window,"resetsAt"))));
            }
        }
        if(Field(root,"rateLimitsByLimitId",out var buckets)&&buckets.ValueKind==JsonValueKind.Object&&buckets.EnumerateObject().Any())
            foreach(var bucket in buckets.EnumerateObject())AddBucket(bucket.Value,bucket.Name);
        else if(Field(root,"rateLimits",out var single))AddBucket(single,"Codex");
        return new("本机 Codex 登录账号",now??DateTimeOffset.UtcNow,windows,windows.Count==0?"暂不可获取账号额度；缺失数据不会记为 0。":"账号额度比例，非 Token 数量");
    }
    internal static AccountQuota Claude(JsonElement root,DateTimeOffset? now=null)
    {
        var windows=new List<QuotaWindow>();
        foreach(var pair in new[] {("five_hour","5小时"),("seven_day","7天"),("seven_day_sonnet","Sonnet · 7天"),("seven_day_opus","Opus · 7天")})
        {
            if(!Field(root,pair.Item1,out var window))continue;
            var used=Number(window,"utilization");if(used is not (>=0 and <=100))continue;
            DateTimeOffset? reset=DateTimeOffset.TryParse(Text(window,"resets_at"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var parsed)?parsed:null;
            windows.Add(new(pair.Item2,used.Value,reset));
        }
        return new("本机 Claude Code 登录账号",now??DateTimeOffset.UtcNow,windows,windows.Count==0?"暂不可获取账号额度；缺失数据不会记为 0。":"请确认与客户端使用同一账号");
    }
}

internal sealed class AccountQuotaService(Settings settings)
{
    private readonly object gate=new();
    private readonly Dictionary<string,(AccountQuota Snapshot,DateTimeOffset Expires)> cache=new();
    private readonly Dictionary<string,Task<AccountQuota>> running=new();
    private static readonly HttpClient http=new(new HttpClientHandler {AllowAutoRedirect=false}) {Timeout=TimeSpan.FromSeconds(10)};
    internal static QuotaSource Resolve(Settings settings,Client client)
    {
        var source=settings.QuotaSources.GetValueOrDefault(client,QuotaSource.Auto);
        if(source!=QuotaSource.Auto)return source;
        if(settings.Executables.TryGetValue(client,out var bound)) {
            string name=Path.GetFileNameWithoutExtension(bound);
            if(ClientDiscovery.IdentityMatches(Client.ChatGPT,bound))return QuotaSource.Codex;
            if(name.Equals("codex",StringComparison.OrdinalIgnoreCase))return QuotaSource.Codex;
            if(name.Equals("claude",StringComparison.OrdinalIgnoreCase))return QuotaSource.Claude;
            return QuotaSource.Unavailable;
        }
        return client==Client.ChatGPT?QuotaSource.Codex:client==Client.Claude?QuotaSource.Claude:QuotaSource.Unavailable;
    }
    internal void Invalidate() {lock(gate)cache.Clear();}
    internal Task<AccountQuota> GetAsync(Client client)
    {
        var source=Resolve(settings,client);string key=client+":"+source+":"+settings.Executables.GetValueOrDefault(client,"");
        lock(gate)
        {
            if(cache.TryGetValue(key,out var cached)&&cached.Expires>DateTimeOffset.UtcNow)return Task.FromResult(cached.Snapshot);
            if(running.TryGetValue(key,out var task))return task;
            var previous=cache.GetValueOrDefault(key).Snapshot;
            task=Task.Run(()=>Fetch(source,previous));running[key]=task;
            _=task.ContinueWith(completed=> {lock(gate) {cache[key]=(completed.Result,DateTimeOffset.UtcNow.AddSeconds(45));running.Remove(key);}},TaskScheduler.Default);
            return task;
        }
    }
    private static async Task<AccountQuota> Fetch(QuotaSource source,AccountQuota? previous)
    {
        AccountQuota result;
        try {
            result=source switch {
                QuotaSource.Codex=>await ReadCodex(),QuotaSource.Claude=>await ReadClaude(),
                _=>AccountQuota.Missing("当前绑定客户端","暂不可获取：该客户端尚未接入账号额度来源。")
            };
        }
        catch(OperationCanceledException) {result=AccountQuota.Missing(SourceName(source),"额度读取超时，请稍后重试。");}
        catch(Exception)
        {result=AccountQuota.Missing(SourceName(source),"暂不可获取账号额度，请检查客户端登录状态和网络。");}
        if(result.Windows.Count==0&&previous is {Windows.Count:>0})
            return previous with {Stale=true,Detail="刷新失败，显示上次结果。"+result.Detail};
        return result;
    }
    private static string SourceName(QuotaSource source)=>source==QuotaSource.Codex?"本机 Codex 登录账号":source==QuotaSource.Claude?"本机 Claude Code 登录账号":"当前绑定客户端";
    internal static string? FindCodexCli()
    {
        foreach(string directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)) {
            if(directory.Length==0)continue;
            try {string file=Path.Combine(directory.Trim('"'),"codex.exe");if(File.Exists(file))return file;}catch(ArgumentException) { }
        }
        string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
        if(Directory.Exists(root))foreach(var directory in Directory.EnumerateDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc)) {
            string file=Path.Combine(directory,"codex.exe");if(File.Exists(file))return file;
        }
        return null;
    }
    private static async Task<AccountQuota> ReadCodex()
    {
        string? cli=FindCodexCli();
        if(cli==null)return AccountQuota.Missing(SourceName(QuotaSource.Codex),"未找到 Codex CLI，暂不可读取账号额度。");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));var cancel=timeout.Token;
        var start=new ProcessStartInfo(cli) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
            StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=Path.GetTempPath()};
        start.ArgumentList.Add("app-server");start.ArgumentList.Add("--listen");start.ArgumentList.Add("stdio://");
        using var process=Process.Start(start)??throw new InvalidOperationException();
        process.ErrorDataReceived+=(_,_)=>{};process.BeginErrorReadLine();
        try
        {
            async Task<JsonElement> Read(int id)
            {
                for(int i=0;i<100;i++) {
                    var line=await process.StandardOutput.ReadLineAsync(cancel);
                    if(line==null||line.Length>1024*1024)throw new IOException("Invalid app-server response.");
                    using var json=JsonDocument.Parse(line);var root=json.RootElement;
                    if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("id",out var responseId)||responseId.ValueKind!=JsonValueKind.Number||!responseId.TryGetInt32(out var value)||value!=id)continue;
                    if(root.TryGetProperty("error",out _))throw new InvalidOperationException("Account quota unavailable.");
                    if(!root.TryGetProperty("result",out var result)||result.ValueKind!=JsonValueKind.Object)throw new IOException("Invalid account quota result.");
                    return result.Clone();
                }
                throw new IOException("No account quota response.");
            }
            // Read-only account RPCs only: no thread, turn, login or reset calls.
            await process.StandardInput.WriteLineAsync("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"pet_jar_quota\",\"title\":\"PetJar Account Quota\",\"version\":\"0.5.3\"}}}");
            await Read(1);
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\",\"params\":{}}");
            await process.StandardInput.WriteLineAsync("{\"id\":2,\"method\":\"account/rateLimits/read\"}");
            return QuotaParser.Codex(await Read(2));
        }
        finally {try {if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException) {}await process.WaitForExitAsync();}
    }
    private static async Task<AccountQuota> ReadClaude()
    {
        string root=Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".claude");
        string file=Path.Combine(root,".credentials.json");
        if(!File.Exists(file))return AccountQuota.Missing(SourceName(QuotaSource.Claude),"未找到本机 Claude Code 登录信息，暂不可获取。");
        using var credentials=JsonDocument.Parse(await File.ReadAllTextAsync(file));
        if(credentials.RootElement.ValueKind!=JsonValueKind.Object||!credentials.RootElement.TryGetProperty("claudeAiOauth",out var oauth)||oauth.ValueKind!=JsonValueKind.Object||!oauth.TryGetProperty("accessToken",out var access)||access.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(access.GetString()))
            return AccountQuota.Missing(SourceName(QuotaSource.Claude),"请先在 Claude Code 登录订阅账号。");
        if(oauth.TryGetProperty("expiresAt",out var expires)&&expires.ValueKind==JsonValueKind.Number&&expires.TryGetInt64(out long milliseconds)&&milliseconds<=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            return AccountQuota.Missing(SourceName(QuotaSource.Claude),"本机 Claude Code 登录已过期，请在客户端重新登录。");
        // Compatibility endpoint used by Claude Code. Never copy credentials to
        // settings, logs or cache, never refresh them, and never follow redirects.
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.anthropic.com/api/oauth/usage");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access.GetString());
        request.Headers.TryAddWithoutValidation("anthropic-beta","oauth-2025-04-20");
        using var response=await http.SendAsync(request);
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return AccountQuota.Missing(SourceName(QuotaSource.Claude),"当前登录无权读取额度，请在 Claude Code 检查账号。");
        response.EnsureSuccessStatusCode();
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return QuotaParser.Claude(json.RootElement);
    }
}
