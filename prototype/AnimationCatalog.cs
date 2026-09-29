using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace WhaleAlive;
public sealed class AnchorKey
{
    public int Frame { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}
public sealed class AnimationClip
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public double Fps { get; set; } = 12;
    public bool Directional { get; set; }
    public string Mode { get; set; } = "loop";
    public int LoopStart { get; set; }
    public int LoopEnd { get; set; }
    public double PivotX { get; set; }
    public double PivotY { get; set; }
    public double DisplayScale { get; set; } = 1;
    public double ReferenceHeight { get; set; }
    public AnchorKey[] Anchors { get; set; } = [];
    public Point Pivot(int frame, bool right)
    {
        var p = new Point(PivotX, PivotY);
        if (Anchors.Length > 0)
        {
            var a = Anchors.LastOrDefault(k => k.Frame <= frame) ?? Anchors[0];
            var b = Anchors.FirstOrDefault(k => k.Frame >= frame) ?? Anchors[^1];
            double t = a.Frame == b.Frame ? 0 : (double)(frame - a.Frame) / (b.Frame - a.Frame);
            p = new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }
        return new(right && Directional ? 1 - p.X : p.X, p.Y);
    }
}
// Entry once, whole gait cycles, exit once. Endpoint tests cover seam ordering.
public sealed class SequenceTimeline(AnimationClip clip, int cycles, int? exitStart = null, bool includeExit = true, double playbackRate = 1)
{
    public int LoopLength => clip.LoopEnd - clip.LoopStart + 1;
    public int ExitStart => exitStart ?? clip.LoopEnd + 1;
    public int TotalFrames => clip.LoopStart + Math.Max(1, cycles) * LoopLength + (includeExit ? clip.Count - ExitStart : 0);
    public double Duration => TotalFrames / (clip.Fps * playbackRate);
    public int FrameAt(double seconds)
    {
        int tick = Math.Clamp((int)(Math.Max(0, seconds) * clip.Fps * playbackRate), 0, TotalFrames - 1);
        if (tick < clip.LoopStart) return tick;
        tick -= clip.LoopStart;
        if (tick < Math.Max(1, cycles) * LoopLength) return clip.LoopStart + tick % LoopLength;
        return ExitStart + tick - Math.Max(1, cycles) * LoopLength;
    }
}
public sealed class AnimationCatalog
{
    readonly string root = Path.Combine(AppContext.BaseDirectory, "Assets", "Finished");
    readonly Dictionary<string, BitmapSource[]> cache = new();
    readonly LinkedList<string> recent = new();
    readonly HashSet<string> pinned = new();
    public Dictionary<string, AnimationClip> Clips { get; }
    public AnimationCatalog()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        Clips = JsonSerializer.Deserialize<Dictionary<string, AnimationClip>>(doc.RootElement.GetProperty("clips").GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        foreach (var (name, c) in Clips)
            if (c.Count < 1 || c.Fps <= 0 || c.LoopStart < 0 || c.LoopEnd >= c.Count || c.LoopEnd < c.LoopStart)
                throw new InvalidDataException("Invalid animation: " + name);
    }
    public BitmapSource[] Load(string key, bool right)
    {
        string path = key + "/" + (right && Clips[key].Directional ? "right" : "left");
        if (cache.TryGetValue(path, out var hit)) { if (!pinned.Contains(path)) { recent.Remove(path); recent.AddLast(path); } return hit; }
        var frames = Enumerable.Range(0, Clips[key].Count).Select(i =>
        {
            var b = new BitmapImage(); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad;
            b.UriSource = new Uri(Path.Combine(root, path, $"{i:0000}.png")); b.EndInit(); b.Freeze();
            return (BitmapSource)b;
        }).ToArray();
        cache[path] = frames; recent.AddLast(path);
        while (recent.Count > 4) { cache.Remove(recent.First!.Value); recent.RemoveFirst(); }
        return frames;
    }
    public void Pin(string key, bool right)
    {
        Load(key, right);
        string path = key + "/" + (right && Clips[key].Directional ? "right" : "left");
        pinned.Add(path); recent.Remove(path);
    }
}
