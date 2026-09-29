using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Media.Playback;
using Windows.Media.Core;

namespace WhaleAlive;
public static class SocialMusicVerification
{
    static void Check(bool value, string reason) { if(!value) throw new Exception(reason); }
    public static async Task Run()
    {
        var evidence = new List<object>();
        Check(NotificationPreview.Format("微信", 0, "私密姓名", "私密内容") == "微信 收到一条新消息", "Private mode leaks text");
        Check(NotificationPreview.Format("微信", 1, "甲：私密内容", "私密内容") == "微信 · 甲 发来新消息", "Sender-only mode leaks body in title");
        Check(NotificationPreview.Format("QQ", 2, "甲", "你好") == "QQ · 甲：你好", "Full preview incorrect");
        Check(!NotificationPreview.Format("QQ", 2, "甲", "你好", 4).Contains("甲"), "Burst summary leaks content");
        Check(NotificationPreview.AppKind("Tencent.QQNT", "QQ") == "QQ" && NotificationPreview.AppKind("Weixin", "微信") == "微信" && NotificationPreview.AppKind("QQMusic.exe", "QQ音乐") == null, "App filtering");
        evidence.Add(new { test = "privacy-sender-body-burst-and-app-filter", pass = true });
        var now = DateTimeOffset.UtcNow;
        var cursor = new NotificationCursor(); cursor.Reset(now);
        Check(cursor.TakeNew(new[]{("old", now.AddSeconds(-10))}).Count == 0, "Startup replays old notices");
        Check(cursor.TakeNew(new[]{("old", now.AddSeconds(-10)),("new",now.AddSeconds(1))}).SetEquals(new[]{"new"}), "New notification missing");
        Check(cursor.TakeNew(new[]{("new",now.AddSeconds(1))}).Count == 0, "Notification replayed");
        cursor.Reset(now.AddSeconds(3));
        Check(cursor.TakeNew(new[]{("new",now.AddSeconds(1))}).Count == 0, "Reauthorization replays history");
        evidence.Add(new { test = "notification-baseline-deduplication-and-reset", pass = true });
        var pet = new PetWindow(); pet.Show();
        var file = Path.Combine(AppContext.BaseDirectory,"social-music-test-settings.json");
        var store = new CompanionStore(file); store.Data.SocialMusic = new();
        var engine = new Interaction(pet, Path.Combine(AppContext.BaseDirectory, "unused-social-history.json"));
        using var features = new CompanionFeatures(pet,engine,store);
        var service = features.SocialMusic;
        int settingsOpened = 0;
        using var menu = new PetQuickMenu(pet, engine, features, () => settingsOpened++, () => {});
        try
        {
            store.Data.Todos.Clear(); features.Refresh();
            menu.Open(); await Task.Delay(100);
            Check(menu.IsOpen && pet.IsMenuOpen && settingsOpened == 0, "Menu opened settings");
            menu.TodoInput.Text = "菜单测试待办"; menu.AddTodo();
            Check(menu.TodoCount == 1 && new CompanionStore(file).Data.Todos.Any(t => t.Text == "菜单测试待办"), "Menu todo not saved");
            features.CompleteTodo(store.Data.Todos.Single().Id);
            Check(menu.TodoCount == 0 && menu.IsOpen, "Todo completion closed menu or failed");
            menu.OpenSettings();
            Check(settingsOpened == 1 && !menu.IsOpen && !pet.IsMenuOpen, "Settings did not dismiss menu");
            evidence.Add(new {test="quick-menu-open-todo-persistence-completion-and-settings",pass=true});
            var finished=store.Data.Todos.Single();
            Check(finished.Completed is DateTime completed&&completed>=finished.Created&&completed<=DateTime.UtcNow,"Completion timestamp missing or invalid");
            Check(!store.Complete(finished.Id)&&store.Data.Todos.Single().Completed==finished.Completed,"Repeated completion changed timestamp");
            Check(new CompanionStore(file).Data.Todos.Single().Completed==finished.Completed,"Completion timestamp not persisted");
            var old=JsonSerializer.Deserialize<PetNote>("""{"Id":"old","Text":"旧记录","Created":"2026-01-01T00:00:00Z","Done":true}""")!;
            Check(old.Completed==null&&old.CompletedLabel=="完成：未记录","Legacy record invented a completion time");
            evidence.Add(new{test="todo-completion-timestamp-restart-idempotency-and-legacy-unknown",pass=true});
            foreach(var bounds in new[]{new Rect(0,0,1280,800),new Rect(-1600,0,1600,900)})
            foreach(var position in new[]{bounds.TopLeft+new Vector(30,30),bounds.BottomRight-new Vector(30,30),bounds.TopLeft+new Vector(600,400)})
            for(int count=1;count<=4;count++)
            {
                var layout=PetWindow.TodoArcLayout(position,bounds,count);
                Check(layout.Length==count&&layout.All(r=>bounds.Contains(r)),"Todo arc escaped work area");
                for(int i=0;i<count;i++)for(int j=i+1;j<count;j++)Check(!layout[i].IntersectsWith(layout[j]),"Todo arc overlaps");
            }
            var arc=PetWindow.TodoArcLayout(new(600,400),new(0,0,1280,800),4);
            Check(arc[1].X<arc[0].X&&arc[2].X<arc[3].X&&arc.All(r=>r.Right<600),"Four todos are not a left-side arc");
            var sampleTexts=new[]{"整理今天的设计笔记","完成桌宠交互细节","记得休息，喝一杯水","检查明天的工作安排","整理本周待办记录"};
            for(int i=0;i<sampleTexts.Length;i++)store.Data.Todos.Add(new("arc-"+i,sampleTexts[i],DateTime.UtcNow.AddHours(-i-1)));
            features.Refresh();pet.SetAnchor(new(700,500));pet.SetState("idle");pet.Say("");await Task.Delay(80);pet.UpdateLayout();
            var noteButtons=Descendants(pet).OfType<System.Windows.Controls.Button>().Where(b=>b.Tag is string id&&id.StartsWith("arc-")).ToArray();
            Check(noteButtons.Length==4,"Expected four visible todo bubbles");
            var arcRender=DragSurfaceVerification.Crop(pet,pet.PointFromScreen(pet.ToPixels(pet.Anchor))+new Vector(-120,-35),400,350);
            var arcPng=new PngBitmapEncoder();arcPng.Frames.Add(BitmapFrame.Create(arcRender));using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"todo-arc.png")))arcPng.Save(output);
            noteButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await engine.WaitForIdle();
            Check(store.Data.Todos.Single(n=>n.Id=="arc-0").Completed!=null,"Bubble click failed to record completion");
            noteButtons=Descendants(pet).OfType<System.Windows.Controls.Button>().Where(b=>b.Tag is string id&&id.StartsWith("arc-")).ToArray();
            Check(noteButtons.Length==4&&noteButtons.Any(b=>Equals(b.Tag,"arc-4"))&&!noteButtons.Any(b=>Equals(b.Tag,"arc-0")),"Completion did not refill the arc");
            evidence.Add(new{test="todo-left-arc-edge-bounds-no-overlap-click-and-refill",pass=true});
            pet.Say(""); pet.SetMusicBubble("♪ 测试歌曲",true);
            Check(pet.DisplayedSpeech=="♪ 测试歌曲", "Music bubble absent");
            pet.SayNotification("测试新消息"); pet.SetMusicBubble("♪ 下一首",true);pet.Say("普通待机话语");
            Check(pet.DisplayedSpeech=="测试新消息", "Ambient speech or music overwrote a notification");
            await Task.Delay(6300);
            Check(pet.DisplayedSpeech=="♪ 下一首", "Music failed to resume after notification");
            pet.SayNotification("私密测试");service.Settings.Privacy=0;service.SaveSettings();
            Check(!pet.DisplayedSpeech.Contains("私密"), "Privacy setting leaves old notification on screen");
            pet.SetMusicBubble("",false);pet.Say("");Check(!pet.SpeechVisible,"Disabled music leaves bubble");
            evidence.Add(new { test = "notification-priority-music-resume-and-immediate-privacy-clear", pass = true });
            await service.Command("next");Check(!service.Connected&&!service.CanNext,"Disconnected player is controllable");
            service.Settings.Music=false;service.Settings.Notifications=false;service.SaveSettings();
            var persisted=new CompanionStore(file);
            Check(persisted.Data.SocialMusic.Privacy==0&&!persisted.Data.SocialMusic.Notifications,"Settings persistence");
            Check(!File.ReadAllText(file).Contains("私密"),"Message content persisted");
            evidence.Add(new { test = "disconnected-control-and-settings-without-message-storage", pass = true });
            try
            {
                var manager=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                evidence.Add(new { test="native-media-session-read-only-probe",pass=true,players=manager.GetSessions().Select(s=>new {id=s.SourceAppUserModelId,next=s.GetPlaybackInfo().Controls.IsNextEnabled,repeat=s.GetPlaybackInfo().Controls.IsRepeatEnabled}).ToArray() });
                await VerifyTransport(service, manager, evidence, menu);
            }
            catch(Exception ex) { evidence.Add(new {test="native-media-session-read-only-probe",pass=false,error=$"0x{ex.HResult:X8}"}); throw; }
            bool identity;
            try { identity=Windows.ApplicationModel.Package.Current.Id.Name=="WhaleAlive.Desktop"; } catch { identity=false; }
            string notificationAccess="NotRegistered";
            if(identity) notificationAccess=Windows.UI.Notifications.Management.UserNotificationListener.Current.GetAccessStatus().ToString();
            evidence.Add(new {test="notification-package-identity-probe",registered=identity,permissionRequested=false,access=notificationAccess});
            var panel=new CompanionPanel(features,engine,()=>[]);
            var window=new Window {Title="Whale Alive · 消息与音乐核对",Content=panel,Width=760,Height=800,Background=GlassAppearance.Surface};
            try
            {
                window.Show();panel.ShowSocialMusic();await Task.Delay(200);window.UpdateLayout();
                var render=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);render.Render(window);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(render));using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"social-music-panel.png")))png.Save(output);
                evidence.Add(new{test="social-music-page-render",pass=true});
                panel.ShowTodos();window.UpdateLayout();
                render=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);render.Render(window);
                png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(render));using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"todo-details-panel.png")))png.Save(output);
                var filters=Descendants(panel).OfType<System.Windows.Controls.ComboBox>().Single(b=>b.Items.Contains("全部记录"));
                filters.SelectedIndex=2;window.UpdateLayout();
                var records=Descendants(panel).OfType<System.Windows.Controls.ListBox>().Single();
                Check(records.Items.Cast<PetNote>().All(n=>n.Done)&&records.Items.Count==2,"Completed filter hid history or included pending tasks");
                filters.SelectedIndex=1;Check(records.Items.Count==4&&records.Items.Cast<PetNote>().All(n=>!n.Done),"Pending filter incorrect");
                evidence.Add(new{test="todo-details-and-completed-pending-filters",pass=true});
                var defaultRest=new AutonomousSettings();var legacyRest=JsonSerializer.Deserialize<AutonomousSettings>("{\"Sleep\":false}")!;
                Check(defaultRest.RestSeconds(false)==120&&defaultRest.RestSeconds(true)==300&&legacyRest.RestSeconds(false)==120&&legacyRest.RestSeconds(true)==300,"Rest defaults or legacy settings incorrect");
                defaultRest.SitSeconds=-1;defaultRest.SleepSeconds=int.MaxValue;
                Check(defaultRest.RestSeconds(false)==30&&defaultRest.RestSeconds(true)==3600,"Rest duration out of bounds");
                panel.ShowAutonomy();window.UpdateLayout();
                var durationBoxes=Descendants(panel).OfType<System.Windows.Controls.ComboBox>().Where(b=>b.Tag is string tag&&tag.EndsWith("-duration")).ToArray();
                Check(durationBoxes.Length==2,"Rest duration controls missing");
                foreach(var box in durationBoxes)box.SelectedItem=box.Items.Cast<System.Windows.Controls.ComboBoxItem>().Single(i=>Equals(i.Tag,Equals(box.Tag,"sit-duration")?180:600));
                var restSaved=new CompanionStore(file).Data.Autonomy;
                Check(restSaved.SitSeconds==180&&restSaved.SleepSeconds==600&&engine.RestSeconds(false)==180&&engine.RestSeconds(true)==600,"Rest selections did not persist or reach the animation engine");
                window.UpdateLayout();render=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);render.Render(window);
                png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(render));using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"rest-duration-panel.png")))png.Save(output);
                evidence.Add(new{test="rest-duration-defaults-legacy-bounds-ui-persistence-and-live-settings",pass=true});
            }
            finally {panel.Dispose();window.Close();}
        }
        finally { pet.Close(); }
        var settings = new MainWindow(desktopMode:true);
        Check(!settings.IsVisible,"Settings visible at desktop startup");
        settings.ShowSettings(); await Task.Delay(150);
        var settingsTabs=Descendants(settings).OfType<System.Windows.Controls.TabControl>().Single();
        settingsTabs.SelectedItem=settingsTabs.Items.OfType<System.Windows.Controls.TabItem>().Single(t=>Equals(t.Header,"待办"));
        settings.UpdateLayout();
        var alignedInputs=Descendants(settings).OfType<System.Windows.Controls.TextBox>().Where(t=>t.Tag is string tag&&(tag.StartsWith("输入待办")||tag.StartsWith("搜索图标"))).ToArray();
        Check(alignedInputs.Length==2,"Search or todo input missing");
        foreach(var input in alignedInputs)
        {
            input.Focus();input.UpdateLayout();
            var placeholder=(System.Windows.Controls.TextBlock)input.Template.FindName("Placeholder",input);
            var origin=placeholder.TransformToAncestor(input).Transform(new Point());
            var caret=input.GetRectFromCharacterIndex(0);
            Check(!caret.IsEmpty&&Math.Abs(caret.X-origin.X)<=3&&Math.Abs(caret.Y-origin.Y)<=3,$"Input alignment mismatch: {input.Tag}, caret={caret}, placeholder={origin}");
        }
        var settingsRender=new RenderTargetBitmap((int)settings.ActualWidth,(int)settings.ActualHeight,96,96,PixelFormats.Pbgra32);settingsRender.Render(settings);
        var settingsPng=new PngBitmapEncoder();settingsPng.Frames.Add(BitmapFrame.Create(settingsRender));
        using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"settings-todo-panel.png")))settingsPng.Save(output);
        evidence.Add(new{test="search-and-todo-caret-placeholder-alignment",pass=true});
        Check(settings.IsVisible,"Settings could not open"); settings.Close();
        Check(!settings.IsVisible,"Closing settings did not hide it");
        settings.ShowSettings();Check(settings.IsVisible,"Hidden settings could not reopen");settings.ExitApplication();
        evidence.Add(new{test="desktop-start-hidden-settings-close-reopen-and-explicit-exit",pass=true});
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"social-music-verification.json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions {WriteIndented=true}));
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);yield return child;
            foreach(var descendant in Descendants(child))yield return descendant;
        }
    }
    static async Task Until(Func<bool> condition, string message)
    {
        var deadline=DateTime.UtcNow.AddSeconds(8);
        while(!condition()) { if(DateTime.UtcNow>deadline)throw new Exception(message); await Task.Delay(50); }
    }
    static async Task VerifyTransport(SocialMusicService service, GlobalSystemMediaTransportControlsSessionManager manager, List<object> evidence, PetQuickMenu menu)
    {
        string wav=Path.Combine(AppContext.BaseDirectory,"silent-media-fixture.wav");
        using(var w=new BinaryWriter(File.Create(wav)))
        {
            w.Write("RIFF"u8);w.Write(960000+36);w.Write("WAVEfmt "u8);w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(8000);w.Write(16000);w.Write((short)2);w.Write((short)16);w.Write("data"u8);w.Write(960000);w.Write(new byte[960000]);
        }
        using var player=new Windows.Media.Playback.MediaPlayer {Volume=0};
        using var source=MediaSource.CreateFromUri(new Uri(wav));
        using var secondSource=MediaSource.CreateFromUri(new Uri(wav));
        var playlist=new MediaPlaybackList {AutoRepeatEnabled=true};
        var artworkFile = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Assets","Finished"), "*.png", SearchOption.AllDirectories).First();
        var thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(await Windows.Storage.StorageFile.GetFileFromPathAsync(artworkFile));
        foreach(var (media,title) in new[]{(source,"Whale Alive integration fixture"),(secondSource,"Whale Alive second fixture")})
        {
            var item=new MediaPlaybackItem(media);var properties=item.GetDisplayProperties();properties.Type=Windows.Media.MediaPlaybackType.Music;
            properties.MusicProperties.Title=title;properties.MusicProperties.Artist="Local test";
            if(media == source) properties.Thumbnail = thumbnail;
            item.ApplyDisplayProperties(properties);playlist.Items.Add(item);
        }
        player.Source=playlist;player.Play();
        try
        {
            await Until(()=>player.PlaybackSession.PlaybackState==MediaPlaybackState.Playing,"Silent fixture did not start");
            var smtc=player.SystemMediaTransportControls;
            smtc.DisplayUpdater.Type=Windows.Media.MediaPlaybackType.Music;
            smtc.DisplayUpdater.MusicProperties.Title="Whale Alive integration fixture";
            smtc.DisplayUpdater.MusicProperties.Artist="Local test";smtc.DisplayUpdater.Update();
            GlobalSystemMediaTransportControlsSession? fixture=null;
            var deadline=DateTime.UtcNow.AddSeconds(8);
            while(fixture==null&&DateTime.UtcNow<deadline)
            {
                foreach(var candidate in manager.GetSessions())
                    if((await candidate.TryGetMediaPropertiesAsync()).Title=="Whale Alive integration fixture") {fixture=candidate;break;}
                if(fixture==null)await Task.Delay(100);
            }
            Check(fixture!=null,"Silent fixture did not publish media session");
            service.Settings.Music=true;service.Settings.PlayerId=fixture!.SourceAppUserModelId;service.SaveSettings(false);service.Start();
            await Until(()=>service.Connected&&service.Song=="Whale Alive integration fixture"&&service.Playing,"Service did not connect to fixture");
            await Until(()=>service.CoverArt != null && menu.HasCover,"Album art did not reach menu");
            menu.Open(); await Task.Delay(100); menu.View.UpdateLayout();
            var render = new RenderTargetBitmap((int)Math.Ceiling(menu.View.ActualWidth),(int)Math.Ceiling(menu.View.ActualHeight),96,96,PixelFormats.Pbgra32);render.Render(menu.View);
            var png = new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(render));using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"quick-menu.png")))png.Save(output);
            Check(menu.SongText == service.Song,"Menu title did not follow session"); menu.Close();
            await service.Command("toggle");await Until(()=>player.PlaybackSession.PlaybackState==MediaPlaybackState.Paused,"System pause did not reach fixture");
            await Until(()=>!service.Playing,"Service did not observe pause");
            await service.Command("toggle");await Until(()=>player.PlaybackSession.PlaybackState==MediaPlaybackState.Playing,"System play did not reach fixture");
            await Until(()=>service.CanNext,"Fixture did not offer Next");await service.Command("next");
            await Until(()=>service.Song=="Whale Alive second fixture","Next did not reach selected native session");
            Check(service.CoverArt == null && !menu.HasCover,"Previous track cover leaked onto track without artwork");
            await Until(()=>service.CanPrevious,"Fixture did not offer Previous");await service.Command("previous");
            await Until(()=>service.Song=="Whale Alive integration fixture","Previous did not reach selected native session");
            bool repeatSupported=service.CanRepeat,shuffleSupported=service.CanShuffle;
            if(repeatSupported) { await service.Command("repeat-all");await Until(()=>service.Repeat=="列表循环","Repeat not applied"); }
            if(shuffleSupported) { await service.Command("shuffle");await Until(()=>service.Repeat=="随机播放","Shuffle not applied"); }
            evidence.Add(new{test="native-silent-fixture-controls-and-track-change",pass=true,repeatSupported,shuffleSupported});
        }
        finally { player.Pause(); service.Settings.Music=false;service.Settings.PlayerId="";service.SaveSettings(false); }
        Check(!menu.HasCover,"Disabled music kept album art");
        evidence.Add(new{test="native-artwork-menu-track-change-and-disable",pass=true});
    }
}
