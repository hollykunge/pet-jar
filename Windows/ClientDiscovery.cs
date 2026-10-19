using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PetJar;

internal sealed record InstalledClient(Client Client,string Executable,string Source,string? AppUserModelId=null);

internal static class ClientDiscovery
{
    private static readonly object gate=new();
    private static Task<IReadOnlyList<InstalledClient>>? pending;
    private static DateTimeOffset scannedAt;
    internal static Task<IReadOnlyList<InstalledClient>> ScanAsync(bool force=false)
    {
        lock(gate) {
            if(pending!=null&&(!pending.IsCompleted||!force&&DateTimeOffset.UtcNow-scannedAt<TimeSpan.FromSeconds(30)))return pending;
            scannedAt=DateTimeOffset.UtcNow;return pending=Task.Run(Scan);
        }
    }
    internal static bool IdentityMatches(Client client,string path)
    {
        try {
            if(!File.Exists(path))return false;
            var info=FileVersionInfo.GetVersionInfo(path);
            return MatchesProduct(client,Path.GetFileName(path),info.ProductName??"",info.CompanyName??"");
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception or System.Security.SecurityException) {return false;}
    }
    internal static bool MatchesProduct(Client client,string filename,string product,string publisher)
    {
        bool Name(params string[] names)=>names.Any(n=>filename.Equals(n+".exe",StringComparison.OrdinalIgnoreCase)&&product.Equals(n,StringComparison.OrdinalIgnoreCase));
        return client switch {
            Client.ChatGPT=>(filename.Equals("ChatGPT.exe",StringComparison.OrdinalIgnoreCase)||filename.Equals("Codex.exe",StringComparison.OrdinalIgnoreCase))
                &&(product.Equals("Codex",StringComparison.OrdinalIgnoreCase)||product.Equals("ChatGPT",StringComparison.OrdinalIgnoreCase))&&publisher.StartsWith("OpenAI",StringComparison.OrdinalIgnoreCase),
            Client.Claude=>Name("Claude")&&publisher.Equals("Anthropic",StringComparison.OrdinalIgnoreCase),
            Client.Grok=>Name("Grok Bot","Grok")&&(publisher.Equals("SpaceXAI",StringComparison.OrdinalIgnoreCase)||publisher.Equals("xAI",StringComparison.OrdinalIgnoreCase)),
            Client.Muse=>Name("Muse")&&(publisher.Equals("Meta",StringComparison.OrdinalIgnoreCase)||publisher.StartsWith("Meta Platforms",StringComparison.OrdinalIgnoreCase)),
            _=>false
        };
    }
    internal static bool Apply(Settings settings,IReadOnlyList<InstalledClient> installed)
    {
        bool changed=false;
        foreach(var group in installed.GroupBy(x=>x.Client)) {
            string? old=settings.Executables.GetValueOrDefault(group.Key);
            bool automatic=old!=null&&string.Equals(old,settings.AutoBindings.GetValueOrDefault(group.Key),StringComparison.OrdinalIgnoreCase);
            if(old!=null&&File.Exists(old)&&!automatic)continue;
            // Prefer a registered package identity; refuse multiple unrelated desktop installs.
            var packages=group.Where(x=>x.AppUserModelId!=null).ToArray();
            var candidates=packages.Length>0?packages:group.DistinctBy(x=>x.Executable,StringComparer.OrdinalIgnoreCase).ToArray();
            if(candidates.Length!=1)continue;
            var item=candidates[0];
            if(old!=item.Executable||settings.AppUserModelIds.GetValueOrDefault(item.Client)!=item.AppUserModelId||!automatic)changed=true;
            settings.Executables[item.Client]=item.Executable;settings.AutoBindings[item.Client]=item.Executable;
            if(item.AppUserModelId is {} id)settings.AppUserModelIds[item.Client]=id;else settings.AppUserModelIds.Remove(item.Client);
            // An automatic path refresh retains the user's explicit title and quota choices.
        }
        return changed;
    }
    internal static async Task<IReadOnlyList<InstalledClient>> BindAsync(Settings settings,bool force=false)
    {
        var installed=await ScanAsync(force);
        if(Apply(settings,installed))settings.Save();
        return installed;
    }
    private static async Task<IReadOnlyList<InstalledClient>> Scan()
    {
        var found=new List<InstalledClient>();
        void Add(string? path,string source)
        {
            if(string.IsNullOrWhiteSpace(path))return;
            foreach(var client in Enum.GetValues<Client>())if(IdentityMatches(client,path))found.Add(new(client,Path.GetFullPath(path),source));
        }
        foreach(var hive in new[] {RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[] {RegistryView.Registry64,RegistryView.Registry32}) {
            try {
                using var root=RegistryKey.OpenBaseKey(hive,view);
                using var uninstall=root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if(uninstall==null)continue;
                foreach(var name in uninstall.GetSubKeyNames()) {
                    using var app=uninstall.OpenSubKey(name);
                    if(app?.GetValue("DisplayName") is not string display||!new[] {"Codex","ChatGPT","Claude","Grok Bot","Grok","Muse"}.Any(n=>display.Equals(n,StringComparison.OrdinalIgnoreCase)||display.StartsWith(n+" ",StringComparison.OrdinalIgnoreCase)))continue;
                    if(app.GetValue("DisplayIcon") is string icon) {
                        string file=icon.Trim();
                        if(file.StartsWith('"')) {int end=file.IndexOf('"',1);file=end>1?file[1..end]:"";}
                        else {int end=file.LastIndexOf(',');if(end>0&&int.TryParse(file[(end+1)..],out _))file=file[..end];}
                        Add(file,"已安装程序："+display);
                    }
                    if(app.GetValue("InstallLocation") is string folder)foreach(string exe in new[] {"ChatGPT.exe","Codex.exe","Claude.exe","Grok Bot.exe","Grok.exe","Muse.exe"})Add(Path.Combine(folder,exe),"已安装程序："+display);
                }
            }catch(Exception e) when(e is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException) { }
        }
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach(string folder in new[] {"ChatGPT","Codex","Claude","Grok Bot","Grok","Muse"})
            Add(Path.Combine(local,"Programs",folder,folder+".exe"),"本机应用目录 + 产品 / 发布者信息");
        try {found.AddRange(await ReadPackages());}catch(Exception e) when(e is IOException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException or ArgumentException) { }
        return found.DistinctBy(x=>(x.Client,x.Executable.ToUpperInvariant(),x.AppUserModelId)).ToArray();
    }
    private static async Task<IEnumerable<InstalledClient>> ReadPackages()
    {
        // Fixed read-only inventory, without profiles, user command interpolation or elevation.
        const string script="""
            $ErrorActionPreference='Stop'
            [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
            $definitions=@(
              @{Name='OpenAI.Codex';Family='OpenAI.Codex_2p2nqsd0c76g0';App='App';Client='ChatGPT'},
              @{Name='Claude';Family='Claude_pzs8sxrjxfjjc';App='Claude';Client='Claude'}
            )
            $rows=@(foreach($definition in $definitions) {
              foreach($package in (Get-AppxPackage -Name $definition.Name | Where-Object { $_.PackageFamilyName -eq $definition.Family } | Sort-Object Version -Descending | Select-Object -First 1)) {
                $manifest=Get-AppxPackageManifest -Package $package.PackageFullName
                $app=$manifest.Package.Applications.Application | Where-Object { $_.Id -eq $definition.App }
                if($app.Executable) {
                  [pscustomobject]@{Client=$definition.Client;Executable=(Join-Path $package.InstallLocation $app.Executable);AppUserModelId=($package.PackageFamilyName+'!'+$app.Id)}
                }
              }
            })
            ConvertTo-Json -InputObject $rows -Compress
            """;
        string shell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
        var start=new ProcessStartInfo(shell) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8};
        foreach(var arg in new[] {"-NoLogo","-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(script))})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var output=process.StandardOutput.ReadToEndAsync(timeout.Token);var errors=process.StandardError.ReadToEndAsync(timeout.Token);
        try {
            await process.WaitForExitAsync(timeout.Token);string json=await output;await errors;
            if(process.ExitCode!=0||string.IsNullOrWhiteSpace(json))return Array.Empty<InstalledClient>();
            using var document=JsonDocument.Parse(json);var result=new List<InstalledClient>();
            foreach(var row in document.RootElement.EnumerateArray()) {
                if(!Enum.TryParse<Client>(row.GetProperty("Client").GetString(),out var client))continue;
                string? file=row.GetProperty("Executable").GetString(),id=row.GetProperty("AppUserModelId").GetString();
                if(file!=null&&IdentityMatches(client,file)&&id!=null)result.Add(new(client,file,"Windows 已注册应用包",id));
            }
            return result;
        }
        finally {try {if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException) {} }
    }
}
