using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PetJar;

public enum Client { ChatGPT, Grok, Muse, Claude }
public enum QuotaSource { Auto, Codex, Claude, Unavailable }
public enum FillerKind { Wood, Blue, Knit, Chrome, Amber, Moss, Stone, Bubble }

internal static class Fillers
{
    internal static string Sprite(this FillerKind kind)=>"prop-"+kind.ToString().ToLowerInvariant();
    internal static string Name(this FillerKind kind)=>kind switch {
        FillerKind.Wood=>"木纹方块",FillerKind.Blue=>"蓝色晶块",FillerKind.Knit=>"针织方块",FillerKind.Chrome=>"镜面星星",
        FillerKind.Amber=>"琥珀方块",FillerKind.Moss=>"苔藓球",FillerKind.Stone=>"石纹方块",_=>"幻彩泡泡"};
    internal static string Texture(this FillerKind kind)=>kind switch {
        FillerKind.Wood=>"温润木纹",FillerKind.Blue=>"清透晶面",FillerKind.Knit=>"柔软针织",FillerKind.Chrome=>"闪亮金属",
        FillerKind.Amber=>"暖色透光",FillerKind.Moss=>"蓬松绒感",FillerKind.Stone=>"自然石纹",_=>"轻盈幻彩"};
}

public static class Clients
{
    public static string Name(this Client client) => client == Client.Grok ? "Grok Bot" : client.ToString();
    public static string Character(this Client client) => client switch { Client.ChatGPT => "dot", Client.Grok => "Bot", Client.Claude => "Claude", _ => "Muse" };
    public static string Sprite(this Client client) => client switch { Client.ChatGPT => "pet-chatgpt", Client.Grok => "pet-grok-glossy", Client.Claude => "pet-claude", _ => "pet-muse" };
}

public sealed class Settings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    // Missing in older settings means the original, full four-pet jar.
    public HashSet<Client> JarPets { get; set; } = new(Enum.GetValues<Client>());
    // Missing means the original mix; an explicit empty set means no fillers.
    public HashSet<FillerKind> JarFillers { get; set; } = new(Enum.GetValues<FillerKind>());
    // Manual bindings are preserved; automatic bindings require installed product identity.
    public Dictionary<Client, string> Executables { get; set; } = new();
    public Dictionary<Client, QuotaSource> QuotaSources { get; set; } = new();
    public Dictionary<Client,string> AutoBindings { get; set; }=new();
    public Dictionary<Client,string> AppUserModelIds { get; set; }=new();
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PetJar", "settings.json");
    public static Settings Load()
    {
        try {
            var settings=JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath))??new();
            settings.Executables??=new();settings.QuotaSources??=new();settings.AutoBindings??=new();settings.AppUserModelIds??=new();
            settings.JarPets??=new(Enum.GetValues<Client>());settings.JarPets.RemoveWhere(client=>!Enum.IsDefined(client));
            settings.JarFillers??=new(Enum.GetValues<FillerKind>());settings.JarFillers.RemoveWhere(kind=>!Enum.IsDefined(kind));return settings;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(FilePath + ".tmp", FilePath, true); return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}

internal sealed class FillerCollection(Settings settings,Func<bool>? save=null)
{
    internal event Action? Changed;
    internal bool Contains(FillerKind kind)=>settings.JarFillers.Contains(kind);
    internal bool Matches(IEnumerable<FillerKind> kinds)=>settings.JarFillers.SetEquals(kinds);
    internal bool SetSelection(IEnumerable<FillerKind> kinds)
    {
        var next=new HashSet<FillerKind>(kinds);
        foreach(var kind in next)if(!Enum.IsDefined(kind))return false;
        if(settings.JarFillers.SetEquals(next))return true;
        var previous=settings.JarFillers;settings.JarFillers=next;
        if(!(save??settings.Save)()) {settings.JarFillers=previous;return false;}
        Changed?.Invoke();return true;
    }
}

// Membership changes are saved as one transaction and preserve client bindings and account sources.
internal sealed class PetCollection(Settings settings,Func<bool>? save=null)
{
    internal event Action? Changed;
    internal bool Contains(Client client)=>settings.JarPets.Contains(client);
    internal int Count=>settings.JarPets.Count;
    internal bool Update(IEnumerable<Client> added,IEnumerable<Client> removed)
    {
        var next=new HashSet<Client>(settings.JarPets);
        foreach(var client in added) {if(!Enum.IsDefined(client))return false;next.Add(client);}
        foreach(var client in removed) {if(!Enum.IsDefined(client))return false;next.Remove(client);}
        if(next.SetEquals(settings.JarPets))return true;
        var previous=settings.JarPets;settings.JarPets=next;
        if(!(save??settings.Save)()) {settings.JarPets=previous;return false;}
        Changed?.Invoke();return true;
    }
}
