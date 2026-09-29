using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WhaleAlive;
public sealed partial class CompanionPanel
{
    readonly TextBlock notificationStatus = new() { TextWrapping = TextWrapping.Wrap, Foreground = GlassAppearance.Muted };
    readonly TextBlock musicStatus = new() { TextWrapping = TextWrapping.Wrap, Foreground = GlassAppearance.Muted };
    readonly TextBlock nowPlaying = new() { TextWrapping = TextWrapping.Wrap, FontSize = 16, Margin = new(0, 10, 0, 4) };
    readonly ComboBox musicPlayer = new() { DisplayMemberPath = "Name", SelectedValuePath = "Id", Margin = new(0, 8, 0, 8) };
    Button? musicPrevious, musicToggle, musicNext, repeatOne, repeatAll, repeatOff, shuffle;
    string playersStamp = "";
    bool refreshingMusic;
    StackPanel SocialMusicTab()
    {
        var p = Column(); var service = features.SocialMusic; var settings = service.Settings;
        CheckBox Toggle(string label, bool value, Action<bool> apply)
        {
            var box = new CheckBox { Content = label, IsChecked = value, Margin = new(0, 5, 0, 5), Style=(Style)FindResource("Switch") };
            box.Checked += (_, _) => { apply(true); service.SaveSettings(false); };
            box.Unchecked += (_, _) => { apply(false); service.SaveSettings(false); };
            p.Children.Add(box); return box;
        }
        Text(p, "消息提醒"); p.Children.Add(notificationStatus);
        Buttons(p, ("开启通知访问", service.EnableNotifications), ("关闭提示", () => Sync(() => { settings.Notifications = false; service.SaveSettings(); })), ("预览气泡", () => Sync(service.PreviewNotification)));
        var privacy = new ComboBox { ItemsSource = new[] { "仅提示 · 隐藏发信人和内容", "显示发信人 · 隐藏内容", "显示发信人和内容" }, SelectedIndex = Math.Clamp(settings.Privacy, 0, 2), Margin = new(0, 8, 0, 8) };
        privacy.SelectionChanged += (_, _) => { settings.Privacy = privacy.SelectedIndex; service.SaveSettings(); }; p.Children.Add(privacy);
        var apps = new WrapPanel();
        foreach(var kind in new[]{"微信", "QQ", "其他系统通知"})
        {
            var box = new CheckBox { Content = kind, IsChecked = kind == "微信" ? settings.WeChat : kind == "QQ" ? settings.QQ : settings.OtherApps, Margin = new(0, 6, 18, 6) };
            void Change(bool value) { if(kind == "微信") settings.WeChat = value; else if(kind == "QQ") settings.QQ = value; else settings.OtherApps = value; service.SaveSettings(); }
            box.Checked += (_, _) => Change(true); box.Unchecked += (_, _) => Change(false); apps.Children.Add(box);
        }
        p.Children.Add(apps);
        Text(p, "只提示新收到的 Windows 通知，连续消息合并。软件自绘弹窗或隐藏的消息详情无法读取。消息不会保存或发给DSH。");
        p.Children.Add(new Separator { Margin = new(0, 8, 0, 8) });
        Toggle("连接音乐播放器", settings.Music, v => settings.Music = v);
        p.Children.Add(musicPlayer);
        musicPlayer.SelectionChanged += (_, _) => { if(refreshingMusic || musicPlayer.SelectedValue is not string id) return; settings.PlayerId = id; service.SaveSettings(false); };
        p.Children.Add(nowPlaying); p.Children.Add(musicStatus);
        var commands = new WrapPanel();
        musicPrevious = Button("上一首", () => service.Command("previous")); musicToggle = Button("播放 / 暂停", () => service.Command("toggle")); musicNext = Button("下一首", () => service.Command("next"));
        foreach(var b in new[]{musicPrevious,musicToggle,musicNext}) commands.Children.Add(b); p.Children.Add(commands);
        var modes = new WrapPanel();
        repeatOff = Button("顺序", () => service.Command("repeat-off")); repeatAll = Button("列表循环", () => service.Command("repeat-all")); repeatOne = Button("单曲循环", () => service.Command("repeat-one")); shuffle = Button("随机", () => service.Command("shuffle"));
        foreach(var b in new[]{repeatOff,repeatAll,repeatOne,shuffle}) modes.Children.Add(b); p.Children.Add(modes);
        Toggle("桌宠气泡显示歌名", settings.MusicBubble, v => settings.MusicBubble = v);
        Text(p, "悬停音乐气泡可切歌和暂停。消息提示结束后恢复歌名。不支持的控制会置灰。");
        service.Changed += RefreshSocialMusic; RefreshSocialMusic(); return p;
    }
    void RefreshSocialMusic()
    {
        var service = features.SocialMusic;
        notificationStatus.Text = service.NotificationStatus; musicStatus.Text = service.MusicStatus;
        nowPlaying.Text = service.Song.Length == 0 ? "还没有正在播放的歌曲" : service.Song + (service.Artist.Length > 0 ? " · " + service.Artist : "");
        if(musicPrevious != null) musicPrevious.IsEnabled = service.CanPrevious;
        if(musicNext != null) musicNext.IsEnabled = service.CanNext;
        if(musicToggle != null) { musicToggle.IsEnabled = service.CanToggle; musicToggle.Content = service.Playing ? "暂停" : "播放"; }
        foreach(var b in new[]{repeatOne,repeatAll,repeatOff}) if(b != null) b.IsEnabled = service.CanRepeat;
        if(shuffle != null) shuffle.IsEnabled = service.CanShuffle;
        string stamp = string.Join('|', service.Players.Select(p => p.Id)) + ":" + service.Settings.PlayerId;
        if(stamp != playersStamp)
        {
            playersStamp = stamp; refreshingMusic = true;
            var players = new List<MusicPlayer> { new("", "自动连接 QQ 音乐") }; players.AddRange(service.Players);
            if(service.Settings.PlayerId.Length > 0 && players.All(p => p.Id != service.Settings.PlayerId)) players.Add(new(service.Settings.PlayerId, "未连接 · " + service.Settings.PlayerId));
            musicPlayer.ItemsSource = players; musicPlayer.SelectedValue = service.Settings.PlayerId; refreshingMusic = false;
        }
    }
}

