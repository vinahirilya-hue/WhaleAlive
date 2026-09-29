using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace WhaleAlive;
public sealed partial class CompanionPanel : UserControl, IDisposable
{
    readonly CompanionFeatures features;readonly Interaction engine;
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new(12),Foreground=GlassAppearance.Blue};
    readonly Func<IReadOnlyList<DesktopIcon>> selectedIcons;
    readonly ListBox todos=new(){Height=280};
    readonly ComboBox todoFilter=new(){ItemsSource=new[]{"全部记录","未完成","已完成"},SelectedIndex=0,Width=150,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,8,0,8)};
    readonly TextBlock todoSummary=new(){Foreground=GlassAppearance.Muted,Margin=new(0,4,0,8)};
    readonly ListBox files=new(){Height=230,DisplayMemberPath="Label"};
    readonly ListBox plan=new(){Height=120};
    string dataStamp="";
    readonly TabControl tabs=new();
    CheckBox? autonomousEnabled;
    public CompanionPanel(CompanionFeatures features,Interaction engine,Func<IReadOnlyList<DesktopIcon>> selectedIcons)
    {
        this.features=features;this.engine=engine;this.selectedIcons=selectedIcons;Foreground=GlassAppearance.Ink;FontFamily=new("Microsoft YaHei UI");FontSize=14;
        var layout=new DockPanel();Content=layout;
        var bottom=new DockPanel();DockPanel.SetDock(bottom,Dock.Bottom);layout.Children.Add(bottom);
        var stop=Button("■ 停止动作",()=>{engine.Stop();return Task.CompletedTask;});stop.Style=(Style)FindResource("DangerButton");DockPanel.SetDock(stop,Dock.Right);bottom.Children.Add(stop);bottom.Children.Add(status);
        layout.Children.Add(tabs);
        AddTab(tabs,"待办",TodoTab());AddTab(tabs,"文件",FilesTab());AddTab(tabs,"整理",IconOrganizeTab());
        AddTab(tabs,"自主设置",AutonomyTab());
        AddTab(tabs,"消息与音乐",SocialMusicTab());
        features.Changed+=Refresh;Refresh();
    }
    public void Dispose(){features.Changed-=Refresh;features.SocialMusic.Changed-=RefreshSocialMusic;}
    public void ShowAutonomy()=>tabs.SelectedIndex=3;
    public void ShowSocialMusic()=>tabs.SelectedIndex=4;
    public void ShowTodos()=>tabs.SelectedIndex=0;
    StackPanel AutonomyTab()
    {
        var p=Column();var auto=features.Autonomy;var settings=auto.Settings;
        CheckBox Toggle(string text,bool value,Action<bool> change){var c=new CheckBox{Content=text,IsChecked=value,Style=(Style)FindResource("Switch")};c.Checked+=(_,_)=>change(true);c.Unchecked+=(_,_)=>change(false);p.Children.Add(c);return c;}
        autonomousEnabled=Toggle("允许桌宠自由搬运图标",auto.Enabled,auto.Enable);
        Text(p,"开启后会移动真实图标，本次运行有效。退出后再打开默认关闭，恢复记录会保存。");
        Toggle("允许图案彩蛋",settings.Patterns,v=>{settings.Patterns=v;auto.Save();});
        Toggle("玩完自己逐个送回原位",settings.ReturnAfterPlay,v=>{settings.ReturnAfterPlay=v;auto.Save();});
        Toggle("允许打盹",settings.Sleep,v=>{settings.Sleep=v;auto.Save();if(!v)engine.StopAmbient();});
        void Duration(string label,string tag,bool sleep,Action<int> apply)
        {
            var row=new Grid{Margin=new(0,4,0,4)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(145)});
            row.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center});
            var select=new ComboBox{Tag=tag,HorizontalAlignment=HorizontalAlignment.Stretch};
            foreach(int seconds in new[]{30,60,120,180,300,600,900,1800,3600}.Append(settings.RestSeconds(sleep)).Distinct().Order())
            {
                var option=new ComboBoxItem{Content=seconds<60?$"{seconds} 秒":$"{seconds/60.0:0.#} 分钟",Tag=seconds};select.Items.Add(option);
                if(seconds==settings.RestSeconds(sleep))select.SelectedItem=option;
            }
            select.SelectionChanged+=(_,_)=>{if(select.SelectedItem is ComboBoxItem item&&item.Tag is int seconds){apply(seconds);auto.Save();}};
            Grid.SetColumn(select,1);row.Children.Add(select);p.Children.Add(row);
        }
        Duration("坐下时长","sit-duration",false,seconds=>settings.SitSeconds=seconds);
        Duration("睡觉时长","sleep-duration",true,seconds=>settings.SleepSeconds=seconds);
        Text(p,"图标和窗口边沿共用，时间从坐稳或趴好后开始。");
        Text(p,"出现频率");
        var frequency = new System.Windows.Controls.Primitives.UniformGrid{Columns=3};
        int selectedFrequency=settings.IntervalSeconds>=300?0:settings.IntervalSeconds<=60?2:1;
        foreach(var (label,seconds,index) in new[]{("偶尔",300,0),("适中",180,1),("经常",60,2)})
        {
            var segment=new RadioButton{Content=label,IsChecked=selectedFrequency==index,Style=(Style)FindResource("Segment"),ToolTip=$"约 {seconds/60} 分钟一次"};
            segment.Checked+=(_,_)=>{settings.IntervalSeconds=seconds;auto.Save();};frequency.Children.Add(segment);
        }
        p.Children.Add(new Border{Child=frequency,Background=GlassAppearance.Surface,BorderBrush=GlassAppearance.Line,BorderThickness=new(1),CornerRadius=new(10),Padding=new(3),Margin=new(0,0,0,18)});
        var restore=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};
        restore.Children.Add(Button("↶  恢复最近一次",()=>auto.Restore(false)));restore.Children.Add(Button("↶  恢复全部",()=>auto.Restore(true)));p.Children.Add(restore);
        p.Children.Add(new Separator{Margin=new(0,16,0,8)});
        var primary=p; p=new StackPanel();
        Toggle("只在主屏幕玩（关闭则在桌宠所在屏幕）",settings.PrimaryScreenOnly,v=>{settings.PrimaryScreenOnly=v;auto.Save();});
        var all=Toggle("所有图标都可以玩",settings.AllIcons,v=>{settings.AllIcons=v;auto.Save();});
        var scope=new TextBlock{Text=$"已保存 {settings.AllowedIcons.Count} 个指定图标",Foreground=GlassAppearance.Muted};p.Children.Add(scope);
        Buttons(p,("用左侧所选作为可玩范围",()=>Sync(()=>{var ids=SelectedIcons();if(ids.Length==0)throw new InvalidOperationException("先在左侧选择图标，Ctrl 可多选。");settings.AllowedIcons=ids.ToList();all.IsChecked=false;auto.Save();scope.Text=$"已保存 {ids.Length} 个指定图标";})));
        Text(p,"仅使用露出的图标和空位。空间或图标不够时只搬一个；安静陪伴时暂停。");
        Buttons(p,("现在玩一次",async()=>{if(!await auto.TryPlay(true))features.Tell("先开启自主搬运，并等待当前动作结束。");}),("看看睡觉链条",()=>engine.IdleAction("sleep")));
        Toggle("允许随机小动作、散步和坐图标",auto.IdleEnabled,v=>{auto.IdleEnabled=v;if(!v)engine.Stop();});
        var permissions=new StackPanel();
        var real=new CheckBox{Content="允许手动及 DSH 移动图标",IsChecked=engine.AllowRealMove};real.Checked+=(_,_)=>engine.AllowRealMove=true;real.Unchecked+=(_,_)=>{engine.AllowRealMove=false;engine.Stop();};permissions.Children.Add(real);
        var cursor=new CheckBox{Content="允许鼠标互动",IsChecked=engine.AllowCursor};cursor.Checked+=(_,_)=>engine.AllowCursor=true;cursor.Unchecked+=(_,_)=>{engine.AllowCursor=false;engine.Stop();};permissions.Children.Add(cursor);
        var windowMove=new CheckBox{Content="允许 DSH 移动窗口",IsChecked=features.AllowWindowMove};windowMove.Checked+=(_,_)=>features.AllowWindowMove=true;windowMove.Unchecked+=(_,_)=>{features.AllowWindowMove=false;engine.Stop();};permissions.Children.Add(windowMove);
        p.Children.Add(new Expander{Header="手动操作权限 · 本次运行有效",Content=permissions,Style=(Style)FindResource("GlassExpander")});
        Text(p,"恢复会暂停自主搬运。你后来手动移动的图标会跳过，不覆盖新位置。");
        primary.Children.Add(new Expander{Header="更多行为与权限",Content=p,Style=(Style)FindResource("GlassExpander")});return primary;
    }
    static Brush Brush(string hex)=>(Brush)new BrushConverter().ConvertFromString(hex)!;
    static StackPanel Column()=>new(){Margin=new(24)};
    static void Text(Panel p,string text)=>p.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,10),Foreground=GlassAppearance.Muted});
    static TextBox Input(Panel p,string hint){var box=new TextBox{Margin=new(0,5,0,8),Padding=new(10,8,10,8),ToolTip=hint,Tag=hint,Style=(Style)Application.Current.FindResource("RoundedInput")};p.Children.Add(box);return box;}
    Button Button(string text,Func<Task> action)
    {
        var b=new Button{Content=text,Padding=new(12,8,12,8),Margin=new(0,4,8,4)};
        b.Click+=async(_,_)=>{if(!b.IsEnabled)return;b.IsEnabled=false;try{var task=action();if(!task.IsCompleted)b.Content="进行中…";await task;Refresh();}catch(OperationCanceledException){features.Tell("动作已停止。");}catch(Exception ex){features.Tell(ex.Message);}finally{b.Content=text;b.IsEnabled=true;}};return b;
    }
    void Buttons(Panel p,params (string,Func<Task>)[] actions){var row=new WrapPanel();foreach(var a in actions)row.Children.Add(Button(a.Item1,a.Item2));p.Children.Add(row);}
    static Task Sync(Action action){action();return Task.CompletedTask;}
    static void AddTab(TabControl tabs,string name,UIElement content)
    {
        if(content is Panel column)column.Children.Insert(0,new TextBlock{Text=name,FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,16)});
        tabs.Items.Add(new TabItem{Header=name,Content=new Border{CornerRadius=new(12),BorderBrush=GlassAppearance.Line,BorderThickness=new(1),Background=Brush("#AFFFFFFF"),Child=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Foreground=GlassAppearance.Ink}}});
    }
    string[] SelectedIcons()=>selectedIcons().Select(i=>i.Id).ToArray();
    StackPanel TodoTab()
    {
        var p=Column();Text(p,"最多四条待办沿桌宠左侧排列，点泡泡即可完成。完成后可在这里查看记录。");var input=Input(p,"输入待办，最多 160 字");input.MaxLength=160;
        Buttons(p,("加一个泡泡",()=>Sync(()=>{features.AddTodo(input.Text);input.Clear();})));
        todos.ItemTemplate=(DataTemplate)System.Windows.Markup.XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <StackPanel Margin="0,3">
                <TextBlock Text="{Binding Text}" TextWrapping="Wrap" FontSize="14" Margin="0,0,0,5"/>
                <TextBlock Text="{Binding StatusLabel}" FontSize="11" Foreground="#3979EF"/>
                <TextBlock Text="{Binding CreatedLabel}" FontSize="11" Foreground="#607798" Margin="0,3,0,0"/>
                <TextBlock Text="{Binding CompletedLabel}" FontSize="11" Foreground="#607798" Margin="0,2,0,0"/>
              </StackPanel>
            </DataTemplate>
            """);
        ScrollViewer.SetHorizontalScrollBarVisibility(todos,ScrollBarVisibility.Disabled);
        todoFilter.SelectionChanged+=(_,_)=>RefreshTodos();p.Children.Add(todoFilter);p.Children.Add(todoSummary);p.Children.Add(todos);
        Buttons(p,("完成所选",()=>Sync(()=>{if(todos.SelectedItem is PetNote n)features.CompleteTodo(n.Id);})),("移除所选记录",()=>Sync(()=>{if(todos.SelectedItem is PetNote n){features.Store.Data.Todos.Remove(n);features.Store.Save();features.Refresh();}})));
        return p;
    }
    StackPanel FilesTab()
    {
        var p=Column();Text(p,"把文件拖到桌宠身上，选择收藏或稍后处理；也可直接拖到下面。这里只保存位置，不移动文件。");
        var drop=new Border{BorderBrush=GlassAppearance.Blue,BorderThickness=new(1),Padding=new(18),Margin=new(0,5,0,12),AllowDrop=true,Background=GlassAppearance.Surface,Child=new TextBlock{Text="把文件拖到这里 · 稍后处理",HorizontalAlignment=HorizontalAlignment.Center}};
        drop.DragOver+=(_,e)=>{e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Link:DragDropEffects.None;e.Handled=true;};drop.Drop+=(_,e)=>{if(e.Data.GetData(DataFormats.FileDrop)is string[] paths)features.KeepFiles(paths,"later");};p.Children.Add(drop);p.Children.Add(files);
        Buttons(p,("打开所在位置",()=>Sync(()=>{if(files.SelectedItem is SavedItem item)CompanionFeatures.Reveal(item.Path);})),("取消暂存",()=>Sync(()=>{if(files.SelectedItem is SavedItem item){features.Store.Data.Files.Remove(item);features.Store.Save();features.Refresh();}})));
        Text(p,"产物快递：填写本机输出文件路径。DSH 也可通过工具送来产物。");var path=Input(p,"完整文件路径");Buttons(p,("把这份产物送来",()=>features.Deliver(path.Text.Trim('"',' '))));return p;
    }
    StackPanel IconOrganizeTab()
    {
        var p=Column();
        Text(p,"图标整理：在左侧 Ctrl 多选（最多 12 个）。按文件类型和名称排列到主屏空位；不改变文件路径。");p.Children.Add(plan);
        Buttons(p,("生成整理计划",()=>Sync(()=>{features.PlanIcons(SelectedIcons());plan.ItemsSource=features.Plan.ToArray();})),("看看目标位置",()=>features.PreviewPlan()),("执行已选图标整理",async()=>{await features.ApplyPlan();plan.ItemsSource=features.Plan.ToArray();}));return p;
    }
    void Refresh()
    {
        status.Text=features.Message;var d=features.Store.Data;
        if(autonomousEnabled!=null&&autonomousEnabled.IsChecked!=features.Autonomy.Enabled)autonomousEnabled.IsChecked=features.Autonomy.Enabled;
        string stamp=string.Join('|',d.Todos.Select(t=>$"{t.Id}:{t.Text}:{t.Done}:{t.Created:O}:{t.Completed:O}"))+string.Join('|',d.Files.Select(f=>f.Id));
        if(stamp==dataStamp)return;dataStamp=stamp;
        RefreshTodos();files.ItemsSource=d.Files.AsEnumerable().Reverse().ToArray();
    }
    void RefreshTodos()
    {
        var all=features.Store.Data.Todos;
        todoSummary.Text=$"未完成 {all.Count(n=>!n.Done)} 条 · 已完成 {all.Count(n=>n.Done)} 条";
        var selected=(todos.SelectedItem as PetNote)?.Id;
        var visible=all.Where(n=>todoFilter.SelectedIndex==0||(todoFilter.SelectedIndex==2)==n.Done).OrderBy(n=>n.Done).ThenByDescending(n=>n.Done?n.Completed??n.Created:n.Created).ToArray();
        todos.ItemsSource=visible;todos.SelectedItem=visible.FirstOrDefault(n=>n.Id==selected);
    }
}

