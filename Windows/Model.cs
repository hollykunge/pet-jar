using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PetJar;

public enum Client { ChatGPT, Grok, Muse, Claude }
public enum DeliveryMode { Copy, Fill, Send, NewChat }
public enum QuotaSource { Auto, Codex, Claude, Unavailable }

public static class Clients
{
    public static string Name(this Client client) => client == Client.Grok ? "Grok Bot" : client.ToString();
    public static string Character(this Client client) => client switch { Client.ChatGPT => "dot", Client.Grok => "Bot", Client.Claude => "Claude", _ => "Muse" };
    public static string Sprite(this Client client) => client switch { Client.ChatGPT => "pet-chatgpt", Client.Grok => "pet-grok-glossy", Client.Claude => "pet-claude", _ => "pet-muse" };
}

// Each stage is independent: an opened window is not proof of insertion or receipt.
public sealed record DeliveryResult(string Message, bool Copied = false, bool Opened = false,
    bool Inserted = false, bool SendClicked = false, bool ServerReceived = false);

public static class DeliveryRules
{
    public static bool Empty(string text) => string.IsNullOrWhiteSpace(text);
    public static bool CanInsert(string current, string descriptor, bool readOnly) =>
        !readOnly && Empty(current) && IsComposer(descriptor) && !Excluded(descriptor);
    public static bool IsComposer(string descriptor) => Contains(descriptor,
        "message", "prompt", "ask anything", "ask grok", "ask chatgpt", "ask muse", "ask claude", "chat input", "composer",
        "发消息", "发送消息", "输入消息", "消息输入", "询问", "提问", "输入指令", "任务描述");
    public static bool Excluded(string descriptor) => Contains(descriptor,
        "search", "搜索", "password", "密码", "address bar", "地址栏", "filter", "筛选", "rename", "重命名");
    private static bool Contains(string text, params string[] words) => Array.Exists(words,
        word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    public static bool IsSend(string name) => Array.Exists(new[] { "send", "send message", "send prompt", "submit", "发送", "发送消息", "发送提示", "提交" },
        value => string.Equals(value, name.Trim(), StringComparison.OrdinalIgnoreCase));
    public static string NewChatUrl(string text) => "codex://new?prompt=" + Uri.EscapeDataString(text);
    public static bool Valid(string text) => !Empty(text) && Encoding.UTF8.GetByteCount(text) <= 100_000;
}

public sealed class PetModel
{
    public Client Client { get; private set; }
    private readonly Dictionary<Client, string> drafts = new();
    private readonly Dictionary<Client, DeliveryMode> modes = new();
    private readonly Dictionary<Client, DeliveryResult> results = new();
    public bool Busy { get; private set; }
    public string Draft { get => drafts.GetValueOrDefault(Client, ""); set { if (!Busy) drafts[Client] = value; } }
    public DeliveryMode Mode { get => modes.GetValueOrDefault(Client, DeliveryMode.Copy); set { if (!Busy) modes[Client] = value; } }
    public DeliveryResult? Result => results.GetValueOrDefault(Client);
    public string Status => Result?.Message ?? $"先在 {Client.Name()} 中打开要通信的智能体或会话。";
    public event Action? Changed;
    public bool Select(Client client)
    {
        if (Busy) return false;
        Client = client;
        Changed?.Invoke();
        return true;
    }
    public async Task SendAsync(Func<Client, string, DeliveryMode, Task<DeliveryResult>> deliver)
    {
        if (Busy || !DeliveryRules.Valid(Draft)) return;
        var target = Client; var text = Draft; var mode = Mode;
        Busy = true; Changed?.Invoke();
        try { results[target] = await deliver(target, text, mode); }
        catch (Exception e) { results[target] = new("投递已停止，源草稿仍保留。" + e.Message); }
        finally { Busy = false; Changed?.Invoke(); }
    }
}

public sealed class Settings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    // Manual bindings are preserved; automatic bindings require installed product identity.
    public Dictionary<Client, string> Executables { get; set; } = new();
    public Dictionary<Client, string> WindowTitles { get; set; } = new();
    public Dictionary<Client, QuotaSource> QuotaSources { get; set; } = new();
    public Dictionary<Client,string> AutoBindings { get; set; }=new();
    public Dictionary<Client,string> AppUserModelIds { get; set; }=new();
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PetJar", "settings.json");
    public static Settings Load()
    {
        try {
            var settings=JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath))??new();
            settings.Executables??=new();settings.WindowTitles??=new();settings.QuotaSources??=new();settings.AutoBindings??=new();settings.AppUserModelIds??=new();return settings;
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
