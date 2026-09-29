using System.IO;
using System.Text.Json;
namespace WhaleAlive;
public record PetNote(string Id, string Text, DateTime Created, bool Done = false, DateTime? Completed = null)
{
    [System.Text.Json.Serialization.JsonIgnore] public string StatusLabel=>Done?"已完成":"未完成";
    [System.Text.Json.Serialization.JsonIgnore] public string CreatedLabel=>"创建："+Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    [System.Text.Json.Serialization.JsonIgnore] public string CompletedLabel=>"完成："+(Done?(Completed?.ToLocalTime().ToString("yyyy-MM-dd HH:mm")??"未记录"):"尚未完成");
    [System.Text.Json.Serialization.JsonIgnore] public string Details=>$"{Text}\n{StatusLabel}\n{CreatedLabel}\n{CompletedLabel}";
}
public record SavedItem(string Id, string Path, string Kind, DateTime Created)
{
    public string Label => $"{(Kind=="delivery"?"产物":Kind=="favorite"?"收藏":"稍后")} · {Path}";
}
public sealed class CompanionData
{
    // Keep retired fields on disk without restoring their behavior.
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string,JsonElement>? LegacyData {get;set;}
    public List<PetNote> Todos { get; set; } = [];
    public List<SavedItem> Files { get; set; } = [];
    public bool Quiet { get; set; }
    public AutonomousSettings Autonomy {get;set;} = new();
    public SocialMusicSettings SocialMusic {get;set;} = new();
    public PoseCorrection SeatCorrection {get;set;} = PoseCorrection.DefaultSeat;
    public PoseCorrection SleepCorrection {get;set;} = PoseCorrection.DefaultSleep;
    public PoseCorrection PickupCorrection {get;set;} = new(0,0);
    public PoseCorrection PutdownCorrection {get;set;} = new(0,0);
    // Optional independent right-facing values, in the same canonical coordinates.
    // Older settings without them retain the original mirrored behavior.
    public PoseCorrection? PickupRightCorrection {get;set;}
    public PoseCorrection? PutdownRightCorrection {get;set;}
    // Window-edge poses use the same approved vertical contact height as the
    // corresponding icon poses. Their small horizontal values keep the window
    // artwork visually centred without affecting the random point on the edge.
    public PoseCorrection WindowSeatCorrection {get;set;} = new(-1,15);
    public PoseCorrection WindowSleepCorrection {get;set;} = new(3,16);
    public List<WindowMoveRecord> WindowMoves { get; set; } = [];
}
public sealed class CompanionStore
{
    readonly string path;
    public CompanionData Data { get; }
    public CompanionStore(string? file = null)
    {
        path = file ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhaleAlive", "companion.json");
        Data = File.Exists(path) ? JsonSerializer.Deserialize<CompanionData>(File.ReadAllText(path)) ?? throw new InvalidDataException("陪伴记录为空，原文件已保留。") : new();
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
    public PetNote AddNote(string text)
    {
        text = text.Trim(); if (text.Length is < 1 or > 160) throw new ArgumentException("请输入 1–160 字。");
        var list = Data.Todos;
        if (list.Count >= 500) throw new InvalidOperationException("记录已满，请先移除不需要的条目。");
        var note = new PetNote(Guid.NewGuid().ToString("N"), text, DateTime.UtcNow); list.Add(note); Save(); return note;
    }
    public bool Complete(string id)
    {
        int n = Data.Todos.FindIndex(t => t.Id == id && !t.Done); if (n < 0) return false;
        var previous=Data.Todos[n];
        Data.Todos[n] = previous with { Done = true, Completed = DateTime.UtcNow };
        try{Save();}catch{Data.Todos[n]=previous;throw;}
        return true;
    }
    public SavedItem KeepFile(string path, string kind = "later")
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("文件路径必须是完整路径。");
        path = Path.GetFullPath(path);
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("文件不存在。", path);
        var found = Data.Files.FirstOrDefault(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && f.Kind == kind);
        if (found != null) return found;
        if (Data.Files.Count >= 500) throw new InvalidOperationException("暂存架已满。");
        var item = new SavedItem(Guid.NewGuid().ToString("N"), path, kind, DateTime.UtcNow); Data.Files.Add(item); Save(); return item;
    }
}
