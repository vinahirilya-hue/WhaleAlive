using System.Globalization;
using System.Text.RegularExpressions;

namespace WhaleAlive;

public sealed class SocialMusicSettings
{
    public bool Notifications { get; set; }
    public bool WeChat { get; set; } = true;
    public bool QQ { get; set; } = true;
    public bool OtherApps { get; set; }
    public int Privacy { get; set; } // 0: no sender/content; 1: sender; 2: full preview
    public bool Music { get; set; } = true;
    public bool MusicBubble { get; set; }
    public string PlayerId { get; set; } = ""; // Empty follows QQ Music only, never an unrelated browser.
    // Retain retired settings as inert data when loading older files.
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? LegacyData { get; set; }
}

public static class NotificationPreview
{
    public static string? AppKind(string id, string name)
    {
        string value = (id + " " + name).ToLowerInvariant();
        if(value.Contains("qqmusic") || value.Contains("qq音乐")) return null;
        if(value.Contains("wechat") || value.Contains("weixin") || value.Contains("微信")) return "微信";
        if(Regex.IsMatch(value, @"(^|[^a-z])qq([^a-z]|$)") || value.Contains("qqnt") || value.Contains("腾讯qq")) return "QQ";
        return null;
    }
    public static string Format(string app, int privacy, string title, string body, int count = 1)
    {
        if(count > 1) return $"{app} 收到 {count} 条新消息";
        if(privacy <= 0 || privacy > 2) return app + " 收到一条新消息";
        // Titles can also contain a body preview. Never expose that suffix in sender-only mode.
        string sender = new Regex(@"[：:\r\n]").Split(title, 2)[0].Trim();
        if(sender == app || sender.Length == 0) return privacy == 2 && body.Length > 0 ? app + "：" + Trim(body) : app + " 收到一条新消息";
        sender = Trim(sender, 32);
        return privacy == 1 ? $"{app} · {sender} 发来新消息" : $"{app} · {sender}：{Trim(body.Length > 0 ? body : "新消息")}";
    }
    static string Trim(string value, int max = 110) => value.Length > max ? value[..max] + "…" : value;
}

public sealed class NotificationCursor
{
    readonly HashSet<string> seen = new();
    bool initialized;
    DateTimeOffset since;
    public void Reset(DateTimeOffset now) { initialized = false; seen.Clear(); since = now; }
    public HashSet<string> TakeNew(IEnumerable<(string Key, DateTimeOffset Created)> snapshot)
    {
        var current = snapshot.ToArray();
        var fresh = current.Where(n => initialized && n.Created >= since && !seen.Contains(n.Key)).Select(n => n.Key).ToHashSet();
        seen.Clear(); seen.UnionWith(current.Select(n => n.Key)); initialized = true;
        return fresh;
    }
}

