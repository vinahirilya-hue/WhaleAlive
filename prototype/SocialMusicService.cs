using System.IO;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;
using Windows.Media;
using Windows.Media.Control;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace WhaleAlive;

public sealed record MusicPlayer(string Id, string Name)
{
    public override string ToString()=>Name;
}
public sealed class SocialMusicService : IDisposable
{
    readonly PetWindow pet;
    readonly CompanionStore store;
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(400) };
    GlobalSystemMediaTransportControlsSessionManager? manager;
    GlobalSystemMediaTransportControlsSession? session;
    GlobalSystemMediaTransportControlsSessionPlaybackControls? controls;
    readonly NotificationCursor notificationCursor = new();
    readonly Dictionary<string, (int Count, string Preview)> pending = new();
    UserNotificationListener? listener;
    bool polling, disposed, commanding;
    int notificationEpoch;
    DateTimeOffset lastNotifications, lastMetadata, lastNotice;
    string trackKey = "";
    string coverKey = "";
    public SocialMusicSettings Settings => store.Data.SocialMusic;
    public event Action? Changed;
    public string NotificationStatus { get; private set; } = "消息提示未开启";
    public string MusicStatus { get; private set; } = "等待 QQ 音乐播放歌曲";
    public string Song { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public BitmapSource? CoverArt { get; private set; }
    public double Position { get; private set; }
    public bool Playing { get; private set; }
    public bool Connected => session != null;
    public bool CanNext => !commanding && controls?.IsNextEnabled == true;
    public bool CanPrevious => !commanding && controls?.IsPreviousEnabled == true;
    public bool CanToggle => !commanding && (Playing ? controls?.IsPauseEnabled == true : controls?.IsPlayEnabled == true);
    public bool CanRepeat => !commanding && controls?.IsRepeatEnabled == true;
    public bool CanShuffle => !commanding && controls?.IsShuffleEnabled == true;
    public string Repeat { get; private set; } = "未知";
    public IReadOnlyList<MusicPlayer> Players { get; private set; } = [];
    public SocialMusicService(PetWindow pet, CompanionStore store)
    {
        this.pet = pet; this.store = store;
        clock.Tick += async (_, _) => await Poll();
        pet.MusicCommand += OnMusicCommand;
    }
    public void Start() { if(!disposed) { clock.Start(); _ = Poll(); } }
    async void OnMusicCommand(string command) => await Command(command);
    public void SaveSettings(bool resetNotifications = true)
    {
        store.Save();
        if(resetNotifications) { notificationEpoch++; notificationCursor.Reset(DateTimeOffset.UtcNow); pending.Clear(); pet.ClearNotification(); }
        if(!Settings.Notifications) NotificationStatus = "消息提示未开启";
        if(!Settings.Music) ClearMusic();
        if(!Settings.MusicBubble) pet.SetMusicBubble("", false);
        lastMetadata = DateTimeOffset.MinValue;
        Changed?.Invoke();
    }
    public async Task EnableNotifications()
    {
        try
        {
            listener = UserNotificationListener.Current;
            var access = await listener.RequestAccessAsync();
            if(disposed) return;
            Settings.Notifications = access == UserNotificationListenerAccessStatus.Allowed;
            SaveSettings();
            NotificationStatus = Settings.Notifications ? "已授权 · 只提示开启后收到的新通知" : "未获得通知访问权限，请在 Windows 设置中允许后重试";
            if(Settings.Notifications) await PollNotifications();
        }
        catch(Exception ex)
        {
            Settings.Notifications = false; store.Save();
            NotificationStatus = $"通知组件尚未注册或不可用（0x{ex.HResult:X8}）。完成本机通知组件安装后，请重启桌宠再开启。";
        }
        Changed?.Invoke();
    }
    public void PreviewNotification()
    {
        pet.SayNotification("微信·示例联系人：示例信息。");
    }
    async Task Poll()
    {
        if(polling || disposed) return;
        polling = true;
        try
        {
            if(Settings.Music)
            {
                try { await PollMusic(); }
                catch(Exception ex) { ClearMusic(); MusicStatus = $"音乐连接暂不可用（0x{ex.HResult:X8}），会自动重试"; }
            }
            if(disposed) return;
            if(Settings.Notifications && (DateTimeOffset.UtcNow-lastNotifications).TotalSeconds >= 2)
            {
                lastNotifications = DateTimeOffset.UtcNow;
                try { await PollNotifications(); }
                catch(Exception ex) { NotificationStatus = $"通知读取不可用（0x{ex.HResult:X8}），请检查授权"; }
            }
            if(disposed) return;
            if(pending.Count > 0 && (DateTimeOffset.UtcNow-lastNotice).TotalSeconds >= 7)
            {
                var next = pending.First(); pending.Remove(next.Key);
                pet.SayNotification(next.Value.Count > 1 ? NotificationPreview.Format(next.Key, 0, "", "", next.Value.Count) : next.Value.Preview);
                lastNotice = DateTimeOffset.UtcNow;
            }
            Changed?.Invoke();
        }
        finally { polling = false; }
    }
    async Task PollNotifications()
    {
        int epoch = notificationEpoch;
        listener ??= UserNotificationListener.Current;
        if(listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
        {
            pending.Clear(); notificationCursor.Reset(DateTimeOffset.UtcNow); pet.ClearNotification();
            NotificationStatus = "通知访问未授权或已撤销，请点击「开启通知访问」"; return;
        }
        var notices = await listener.GetNotificationsAsync(NotificationKinds.Toast);
        if(disposed || !Settings.Notifications || epoch != notificationEpoch) return;
        static string Key(UserNotification notice) => notice.Id + ":" + notice.CreationTime.UtcTicks;
        var fresh = notificationCursor.TakeNew(notices.Select(n => (Key(n), n.CreationTime)));
        foreach(var notice in notices.OrderBy(n => n.CreationTime))
        {
            try
            {
                string id = notice.AppInfo.AppUserModelId;
                if(!fresh.Contains(Key(notice))) continue;
                string appName = notice.AppInfo.DisplayInfo.DisplayName;
                string? kind = NotificationPreview.AppKind(id, appName);
                if(kind == "微信" && !Settings.WeChat || kind == "QQ" && !Settings.QQ) continue;
                if(kind == null && (!Settings.OtherApps || id.Contains("WhaleAlive", StringComparison.OrdinalIgnoreCase))) continue;
                string label = kind ?? appName;
                string title = "", body = "";
                // Privacy-only mode never extracts title or message text from the notification.
                if(Settings.Privacy is 1 or 2)
                {
                    var text = notice.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements();
                    title = text?.FirstOrDefault()?.Text ?? "";
                    if(Settings.Privacy == 2) body = string.Join(" ", text?.Skip(1).Select(t => t.Text) ?? []);
                }
                var preview = NotificationPreview.Format(label, Settings.Privacy, title, body);
                int count = pending.TryGetValue(label, out var existing) ? existing.Count + 1 : 1;
                pending[label] = (count, preview);
            }
            catch { /* A malformed or removed toast must not stop other notifications. */ }
        }
        // Keep only ids, and only for notifications still present; never persist message text.
        NotificationStatus = "正在监听 · 微信 / QQ 按下方开关过滤；自绘弹窗不在系统通知中";
    }
    async Task PollMusic()
    {
        manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        if(disposed || !Settings.Music) return;
        var sessions = manager.GetSessions();
        Players = sessions.Select(s => new MusicPlayer(s.SourceAppUserModelId, s.SourceAppUserModelId.Contains("qqmusic", StringComparison.OrdinalIgnoreCase) ? "QQ 音乐" : s.SourceAppUserModelId)).DistinctBy(p => p.Id).ToArray();
        var chosen = sessions.FirstOrDefault(s => Settings.PlayerId.Length > 0 ? s.SourceAppUserModelId == Settings.PlayerId : s.SourceAppUserModelId.Contains("qqmusic", StringComparison.OrdinalIgnoreCase));
        if(chosen == null)
        {
            ClearMusic(); MusicStatus = Settings.PlayerId.Length > 0 ? "所选播放器未提供媒体会话，请打开播放器播放一首歌" : "未发现 QQ 音乐媒体会话，请打开 QQ 音乐播放一首歌；也可选择其他播放器"; return;
        }
        if(session?.SourceAppUserModelId != chosen.SourceAppUserModelId) { lastMetadata = DateTimeOffset.MinValue; trackKey = ""; }
        session = chosen;
        var playback = chosen.GetPlaybackInfo(); controls = playback.Controls;
        Playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        Repeat = playback.IsShuffleActive == true ? "随机播放" : playback.AutoRepeatMode switch { MediaPlaybackAutoRepeatMode.Track => "单曲循环", MediaPlaybackAutoRepeatMode.List => "列表循环", MediaPlaybackAutoRepeatMode.None => "顺序播放", _ => "播放器未提供模式" };
        var timeline = chosen.GetTimelineProperties();
        double elapsed = Playing ? Math.Max(0, (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalSeconds) * (playback.PlaybackRate ?? 1) : 0;
        double end = Math.Max(timeline.EndTime.TotalSeconds, timeline.MaxSeekTime.TotalSeconds);
        Position = Math.Clamp(timeline.Position.TotalSeconds + elapsed, timeline.StartTime.TotalSeconds, Math.Max(timeline.StartTime.TotalSeconds, end));
        if((DateTimeOffset.UtcNow-lastMetadata).TotalSeconds >= 2)
        {
            var properties = await chosen.TryGetMediaPropertiesAsync();
            if(disposed || !Settings.Music || session != chosen) return;
            Song = properties.Title ?? ""; Artist = properties.Artist ?? "";
            string key = chosen.SourceAppUserModelId + "\n" + Song + "\n" + Artist;
            trackKey = key;
            string artworkKey = key + "\n" + properties.AlbumTitle;
            if(artworkKey != coverKey || CoverArt == null)
            {
                coverKey = artworkKey; CoverArt = null;
                var image = await ReadCover(properties.Thumbnail);
                if(disposed || !Settings.Music || session != chosen || trackKey != key) return;
                CoverArt = image;
            }
            lastMetadata = DateTimeOffset.UtcNow;
        }
        MusicStatus = Song.Length == 0 ? "已连接，播放器尚未提供歌名" : (Playing ? "播放中 · " : "已暂停 · ") + Repeat;
        string bubbleText = !Settings.MusicBubble || Song.Length == 0 ? "" : "♪ " + Song + (Artist.Length > 0 ? " · " + Artist : "");
        pet.SetMusicBubble(bubbleText, Playing, CanPrevious, CanToggle, CanNext);
    }
    void ClearMusic()
    {
        session = null; controls = null; trackKey = "";
        Song = Artist = ""; coverKey = ""; CoverArt = null; Playing = false; Position = 0;
        MusicStatus = "音乐连接已关闭"; pet.SetMusicBubble("", false);
    }
    internal static async Task<BitmapSource?> ReadCover(IRandomAccessStreamReference? thumbnail)
    {
        if(thumbnail == null) return null;
        try
        {
            using var input = await thumbnail.OpenReadAsync();
            if(input.Size == 0 || input.Size > 8 * 1024 * 1024) return null;
            using var stream = input.AsStreamForRead();
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 256; image.StreamSource = stream; image.EndInit(); image.Freeze();
            return image;
        }
        catch { return null; } // Missing/broken artwork must not disable music controls.
    }
    public async Task Command(string command)
    {
        if(commanding) return;
        var target = session;
        if(disposed || target == null || !Settings.Music) { MusicStatus = "请先连接正在播放的音乐软件"; Changed?.Invoke(); return; }
        commanding = true; Changed?.Invoke();
        try
        {
            var info = target.GetPlaybackInfo(); var c = info.Controls;
            bool ok = command switch
            {
                "previous" when c.IsPreviousEnabled => await target.TrySkipPreviousAsync(),
                "next" when c.IsNextEnabled => await target.TrySkipNextAsync(),
                "toggle" when info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && c.IsPauseEnabled => await target.TryPauseAsync(),
                "toggle" when info.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && c.IsPlayEnabled => await target.TryPlayAsync(),
                "shuffle" when c.IsShuffleEnabled => await target.TryChangeShuffleActiveAsync(true),
                "repeat-one" or "repeat-all" or "repeat-off" when c.IsRepeatEnabled => await SetRepeat(target, c, command),
                _ => false
            };
            if(disposed) return;
            if(!ok) { MusicStatus = "播放器未支持或未接受这项操作，请在播放器内切换"; pet.Say(MusicStatus); }
            else { lastMetadata = DateTimeOffset.MinValue; await Poll(); }
        }
        catch(Exception ex) { if(!disposed) MusicStatus = $"操作未完成（0x{ex.HResult:X8}），请确认播放器仍在运行"; }
        finally { commanding = false; }
        if(!disposed) Changed?.Invoke();
    }
    static async Task<bool> SetRepeat(GlobalSystemMediaTransportControlsSession target, GlobalSystemMediaTransportControlsSessionPlaybackControls controls, string command)
    {
        if(target.GetPlaybackInfo().IsShuffleActive == true && (!controls.IsShuffleEnabled || !await target.TryChangeShuffleActiveAsync(false))) return false;
        return await target.TryChangeAutoRepeatModeAsync(command == "repeat-one" ? MediaPlaybackAutoRepeatMode.Track : command == "repeat-all" ? MediaPlaybackAutoRepeatMode.List : MediaPlaybackAutoRepeatMode.None);
    }
    public void Dispose()
    {
        disposed = true; clock.Stop(); notificationEpoch++; pending.Clear(); notificationCursor.Reset(DateTimeOffset.UtcNow);
        pet.MusicCommand -= OnMusicCommand; pet.SetMusicBubble("", false); pet.ClearNotification();
        session = null; manager = null; listener = null;
    }
}
