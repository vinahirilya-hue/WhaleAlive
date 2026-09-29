using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace WhaleAlive;

public sealed class PetQuickMenu : IDisposable
{
    readonly PetWindow pet;
    readonly Interaction engine;
    readonly CompanionFeatures features;
    readonly Action settings, exit;
    readonly ContextMenu menu = new() { HasDropShadow = true, Placement = PlacementMode.MousePoint };
    readonly StackPanel content = new() { Width = 292 };
    readonly Image cover = new() { Width = 84, Height = 84, Stretch = Stretch.UniformToFill, Clip = new RectangleGeometry(new Rect(0,0,84,84),10,10) };
    readonly TextBlock noCover = new() { Text = "♪", FontSize = 29, Foreground = GlassAppearance.Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock song = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 42, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock artist = new() { FontSize = 11, Foreground = GlassAppearance.Muted, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 5, 0, 0) };
    readonly TextBlock playback = new() { FontSize = 10, Foreground = GlassAppearance.Blue, Margin = new(0, 5, 0, 0) };
    readonly Button previous, toggle, next;
    readonly Expander todo = new() { Margin = new(0, 6, 0, 4), IsExpanded = true };
    readonly StackPanel todoRows = new();
    readonly TextBox todoInput = new() { MaxLength = 160, Tag = "添加新任务…", ToolTip = "写一件待办，按 Enter 添加", Margin = new(0, 6, 6, 6), MinWidth = 190 };
    readonly TextBlock hint = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = GlassAppearance.Muted, Margin = new(2, 6, 2, 2), Visibility = Visibility.Collapsed };
    readonly CheckBox quiet = new() { Content = "安静陪伴", Margin = new(8, 8, 0, 8) };
    string todoStamp = "";
    bool disposed, refreshing;
    public bool IsOpen => menu.IsOpen;
    internal FrameworkElement View => content;
    internal string SongText => song.Text;
    internal bool HasCover => cover.Source != null;
    internal TextBox TodoInput => todoInput;
    internal int TodoCount => features.Store.Data.Todos.Count(n => !n.Done);

    public PetQuickMenu(PetWindow pet, Interaction engine, CompanionFeatures features, Action settings, Action exit)
    {
        this.pet = pet; this.engine = engine; this.features = features; this.settings = settings; this.exit = exit;
        menu.PlacementTarget = pet; menu.FontFamily = new("Microsoft YaHei UI"); menu.Foreground = GlassAppearance.Ink;
        menu.Template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ContextMenu">
              <Border BorderBrush="#F5FFFFFF" BorderThickness="1" CornerRadius="18" Padding="16">
                <Border.Background><LinearGradientBrush StartPoint="0,0" EndPoint="1,1"><GradientStop Color="#F5F5FAFF"/><GradientStop Color="#F0E5F0FF" Offset="1"/></LinearGradientBrush></Border.Background>
                <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" CanContentScroll="False"><ItemsPresenter/></ScrollViewer>
              </Border>
            </ControlTemplate>
            """);
        // Embed controls without MenuItem hover/selection chrome. Clicks inside the card keep it open.
        var host = new MenuItem { Header = content, StaysOpenOnClick = true, Focusable = false, IsTabStop = false };
        host.Template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="MenuItem"><ContentPresenter ContentSource="Header"/></ControlTemplate>
            """);
        menu.Items.Add(host);
        todo.Style=(Style)Application.Current.FindResource("GlassExpander");todoInput.Style=(Style)Application.Current.FindResource("RoundedInput");quiet.Style=(Style)Application.Current.FindResource("Switch");
        var heading=new DockPanel{Margin=new(0,0,0,16)};
        var close=MakeButton("×",()=>{Close();return Task.CompletedTask;});close.Style=(Style)Application.Current.FindResource("QuietButton");close.FontSize=19;close.Padding=new(7,0,7,0);close.ToolTip="收起菜单";DockPanel.SetDock(close,Dock.Right);heading.Children.Add(close);
        var brand=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};brand.Children.Add(new Image{Source=GlassAppearance.Logo,Width=26,Height=23,Margin=new(0,0,9,0)});brand.Children.Add(new TextBlock{Text="Whale Alive",FontSize=17,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});heading.Children.Add(brand);content.Children.Add(heading);
        var music = new Grid { Margin = new(0, 0, 0, 10) };
        music.ColumnDefinitions.Add(new() { Width = new GridLength(99) }); music.ColumnDefinitions.Add(new());
        var artwork = new Grid(); artwork.Children.Add(noCover); artwork.Children.Add(cover);
        var album = new Border { Width = 84, Height = 84, CornerRadius = new(10), Background = GlassAppearance.Surface, Child = artwork, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment=VerticalAlignment.Top };
        music.Children.Add(album);
        var track = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; track.Children.Add(song); track.Children.Add(artist); track.Children.Add(playback); Grid.SetColumn(track, 1); music.Children.Add(track); content.Children.Add(music);
        var transport = new UniformGrid { Columns = 3 };
        previous = MakeButton("⏮", () => features.SocialMusic.Command("previous"));previous.ToolTip="上一首";
        toggle = MakeButton("▶", () => features.SocialMusic.Command("toggle"));toggle.ToolTip="播放 / 暂停";
        next = MakeButton("⏭", () => features.SocialMusic.Command("next"));next.ToolTip="下一首";
        foreach(var button in new[]{previous,toggle,next}){button.Style=(Style)Application.Current.FindResource("QuietButton");button.FontSize=17;button.FontFamily=new("Segoe UI Symbol");button.Padding=new(5,5,5,5);}
        toggle.Background=GlassAppearance.ColorBrush("#D9E9FF");
        transport.Children.Add(previous); transport.Children.Add(toggle); transport.Children.Add(next); track.Children.Add(transport);
        content.Children.Add(new Separator { Margin = new(0, 10, 0, 4) });
        var todoContent = new StackPanel();
        todoContent.Children.Add(new ScrollViewer { Content = todoRows, MaxHeight = 168, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var inputRow = new DockPanel();
        var add = MakeButton("+", () => { AddTodo(); return Task.CompletedTask; });add.Style=(Style)Application.Current.FindResource("AccentButton");add.ToolTip="添加待办";add.FontSize=19;add.Width=37;add.Padding = new(5, 3, 5, 3); DockPanel.SetDock(add, Dock.Right); inputRow.Children.Add(add); inputRow.Children.Add(todoInput); todoContent.Children.Add(inputRow);
        todoInput.KeyDown += (_, e) => { if(e.Key == Key.Enter) { AddTodo(); e.Handled = true; } };
        todo.Content = todoContent; content.Children.Add(todo);
        content.Children.Add(new Separator{Margin=new(0,10,0,8)});content.Children.Add(quiet);
        quiet.Checked += (_, _) => SetQuiet(true); quiet.Unchecked += (_, _) => SetQuiet(false);

        var actions = new StackPanel();
        actions.Children.Add(MenuRow("■","停止动作",()=>{engine.Stop();return Task.CompletedTask;}));
        actions.Children.Add(MenuRow("⚙","设置",()=>{OpenSettings();return Task.CompletedTask;}));content.Children.Add(actions);
        content.Children.Add(new Separator{Margin=new(0,10,0,8)});
        var quit=MenuRow("⏻","退出桌宠",()=>{Close();exit();return Task.CompletedTask;});quit.Foreground=GlassAppearance.Muted;content.Children.Add(quit); content.Children.Add(hint);
        menu.Opened += (_, _) => { pet.IsMenuOpen = true; Refresh(); };
        menu.Closed += (_, _) => { pet.IsMenuOpen = false; };
        menu.PreviewKeyDown += (_, e) => { if(e.Key == Key.Escape) { Close(); e.Handled = true; } };
        features.SocialMusic.Changed += Refresh; features.Changed += Refresh;
        Refresh();
    }
    Button MenuRow(string icon,string text,Func<Task> action)
    {
        var button=MakeButton(text,action);button.Style=(Style)Application.Current.FindResource("QuietButton");button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        var row=new Grid{Width=252};row.ColumnDefinitions.Add(new(){Width=new GridLength(32)});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(16)});
        row.Children.Add(new TextBlock{Text=icon,FontSize=17,VerticalAlignment=VerticalAlignment.Center});
        var label=new TextBlock{Text=text,FontSize=14,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(label,1);row.Children.Add(label);
        if(text=="设置"){var arrow=new TextBlock{Text="›",FontSize=20};Grid.SetColumn(arrow,2);row.Children.Add(arrow);}
        button.Content=row;button.ToolTip=text;return button;
    }
    Button MakeButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, FontSize = 12, Padding = new(10, 7, 10, 7), Margin = new(2, 3, 2, 3) };
        bool busy = false;
        button.Click += async (_, e) =>
        {
            e.Handled = true; if(busy) return; busy = true;
            try { await action(); }
            catch(OperationCanceledException) { }
            catch(Exception ex) { ShowHint(ex.Message); }
            finally { busy = false; }
        };
        return button;
    }
    void ShowHint(string text) { hint.Text = text; hint.Visibility = Visibility.Visible; }
    public void Open()
    {
        if(disposed) return;
        Refresh(); hint.Visibility = Visibility.Collapsed;
        menu.MaxHeight = Math.Max(220, Math.Min(620, DisplayGeometry.WorkArea(pet, pet.Anchor).Height - 30));
        menu.IsOpen = true;
    }
    public void Close() { menu.IsOpen = false; pet.IsMenuOpen = false; }
    internal void OpenSettings() { Close(); settings(); }
    internal void AddTodo()
    {
        try { features.AddTodo(todoInput.Text); todoInput.Clear(); hint.Visibility = Visibility.Collapsed; Refresh(); }
        catch(Exception ex) { ShowHint(ex.Message); }
    }
    void SetQuiet(bool value)
    {
        if(refreshing) return;
        features.Store.Data.Quiet = value; features.Store.Save(); features.Refresh();
    }
    void Refresh()
    {
        if(disposed) return;
        var media = features.SocialMusic;
        song.Text = media.Song.Length > 0 ? media.Song : "还没有正在播放的音乐";
        artist.Text = media.Artist.Length > 0 ? media.Artist : media.Connected ? "已连接播放器" : "打开 QQ 音乐，播放一首歌";
        artist.ToolTip = artist.Text;
        playback.Text = media.Connected ? media.Playing ? "正在播放" : "已暂停" : "等待连接";
        if(!media.Settings.Music) { song.Text = "音乐连接已关闭"; artist.Text = "在设置 → 消息与音乐中开启"; playback.Text = "未连接"; }
        cover.Source = media.CoverArt; noCover.Visibility = media.CoverArt == null ? Visibility.Visible : Visibility.Collapsed;
        previous.IsEnabled = media.CanPrevious; toggle.IsEnabled = media.CanToggle; next.IsEnabled = media.CanNext; toggle.Content = media.Playing ? "Ⅱ" : "▶";
        refreshing = true; quiet.IsChecked = features.Store.Data.Quiet; refreshing = false;
        var notes = features.Store.Data.Todos.Where(t => !t.Done).ToArray(); todo.Header = $"待办事项 · {notes.Length}";
        string stamp = string.Join('|', notes.Select(n => n.Id + n.Text));
        if(stamp == todoStamp && todoRows.Children.Count > 0) return;
        todoStamp = stamp; todoRows.Children.Clear();
        if(notes.Length == 0) todoRows.Children.Add(new TextBlock { Text = "今天暂时没有待办", Foreground = GlassAppearance.Muted, Margin = new(4, 6, 4, 6) });
        foreach(var note in notes)
        {
            var check = new CheckBox { Content = new TextBlock{Text=note.Text,TextWrapping=TextWrapping.Wrap,MaxWidth=234}, Margin = new(4, 5, 4, 5), ToolTip = "标记完成" };
            check.Checked += (_, _) => { try { features.CompleteTodo(note.Id); } catch(Exception ex) { ShowHint(ex.Message); } }; todoRows.Children.Add(check);
        }
    }
    public void Dispose()
    {
        disposed = true; Close(); features.SocialMusic.Changed -= Refresh; features.Changed -= Refresh;
    }
}

