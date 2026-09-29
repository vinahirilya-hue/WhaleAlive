using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace WhaleAlive;
public partial class MainWindow : Window
{
    readonly PetWindow pet;
    readonly Interaction engine;
    readonly CompanionFeatures companion;
    readonly CompanionPanel panel;
    readonly PetQuickMenu quickMenu;
    readonly EmergencyStop killSwitch;
    readonly PipeBridge bridge;
    readonly DshProgress dshProgress=new();
    readonly System.Windows.Threading.DispatcherTimer idleTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    DateTime lastActivity = DateTime.Now, lastWander = DateTime.Now, lastDsh = DateTime.MinValue;
    List<DesktopIcon> allIcons = new();
    bool triedFirstSeat, exiting;
    PutdownAdjustmentWindow? putdownAdjustment;
    public MainWindow(bool desktopMode = false)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => GlassAppearance.Apply(this);
        var work = SystemParameters.WorkArea; Height = Math.Min(Height, work.Height - 40); Width = Math.Min(Width, work.Width - 40);
        pet = new PetWindow(); pet.Show(); engine = new(pet);
        companion = new(pet,engine);
        panel=new CompanionPanel(companion,engine,()=>IconList.SelectedItems.Cast<DesktopIcon>().ToArray());WorkspaceHost.Content=panel;
        panel.ShowAutonomy();
        engine.Progress+=(text,_)=>companion.Tell(text);
        engine.Changed+=()=>{lastActivity=DateTime.Now;if(IsLoaded&&IsVisible&&!engine.Busy&&!pet.IsDragging)RefreshIcons();};
        quickMenu = new(pet, engine, companion, ShowSettings, ExitApplication);
        pet.Grabbed += engine.Stop; pet.OpenMenu += quickMenu.Open;
        pet.Clicked+=()=>pet.ShowTaskProgress(dshProgress.Text);
        killSwitch = new(() => Dispatcher.BeginInvoke(() => engine.Stop()));
        bridge = new(HandleCommand);
        idleTimer.Tick += async (_, _) =>
        {
            pet.RefreshTaskProgress(dshProgress.Text);
            if (lastDsh != DateTime.MinValue && (DateTime.Now - lastDsh).TotalSeconds > 8) { lastDsh = DateTime.MinValue; engine.SetAmbient("idle"); }
            if (!companion.Autonomy.IdleEnabled || companion.Autonomy.Running || engine.Busy || engine.AmbientBusy || pet.IsDragging || pet.IsPressing || pet.IsMenuOpen || engine.AmbientState != "idle") return;
            if ((DateTime.Now - lastActivity).TotalSeconds < 16 || (DateTime.Now - lastWander).TotalSeconds < (engine.Quiet ? 50 : 18)) return;
            try
            {
                if(await companion.Autonomy.TryPlay())return;
                if(Random.Shared.Next(3)==0&&await companion.Perch.TryIdle())return;
                if (!triedFirstSeat && !engine.Quiet) { triedFirstSeat = true; await engine.IdleAction("sit"); }
                else if (Random.Shared.Next(4) == 0) await engine.Wander();
                else await engine.IdleAction();
            }
            catch (Exception ex) { companion.Tell("待机动作已结束：" + ex.Message); }
            finally { lastWander = DateTime.Now; }
        }; idleTimer.Start();
        Closing += (_, e) => { if(desktopMode && !exiting) { e.Cancel = true; Hide(); } };
        Closed += (_, _) => { putdownAdjustment?.Close();idleTimer.Stop(); quickMenu.Dispose(); engine.Stop(); panel.Dispose(); companion.Dispose(); bridge.Dispose(); killSwitch.Dispose(); pet.Close(); };
        Loaded += (_, _) => { RefreshIcons(); companion.SocialMusic.Start(); };
        if(desktopMode) companion.SocialMusic.Start();
    }
    internal void ShowSettings() { Show(); WindowState = WindowState.Normal; Activate(); RefreshIcons(); }
    internal void ExitApplication() { exiting = true; Close(); }
    void MinimizeClick(object s,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    void MaximizeClick(object s,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    void CloseSettingsClick(object s,RoutedEventArgs e)=>Close();
    void RefreshIcons(){try{using var shell=new ShellDesktop();allIcons=shell.List();ApplyFilter();}catch(Exception e){companion.Tell("读取失败："+e.Message);}}
    void SearchChanged(object s,TextChangedEventArgs e){if(IconList!=null)ApplyFilter();}
    void ApplyFilter()
    {
        var selected=IconList.SelectedItems.Cast<DesktopIcon>().Select(i=>i.Id).ToHashSet();
        var icons=allIcons.Where(i=>i.Name.Contains(SearchBox.Text,StringComparison.OrdinalIgnoreCase)).ToArray();
        IconList.ItemsSource=icons;IconCount.Text=$"{icons.Length} / {allIcons.Count} 个图标 · Ctrl 多选";
        foreach(var icon in icons.Where(i=>selected.Contains(i.Id)))IconList.SelectedItems.Add(icon);
        if(IconList.SelectedItems.Count==0)IconList.SelectedItem=icons.FirstOrDefault(i=>i.Name.Equals("Steam",StringComparison.OrdinalIgnoreCase))??icons.FirstOrDefault();
        if(IconList.SelectedItem!=null)IconList.ScrollIntoView(IconList.SelectedItem);
    }
    void RefreshClick(object s,RoutedEventArgs e)=>RefreshIcons();
    void SelectionChanged(object s,SelectionChangedEventArgs e)
    {
        if(PositionText==null)return;
        PositionText.Text=IconList.SelectedItems.Count>1?$"已选择 {IconList.SelectedItems.Count} 个图标":IconList.SelectedItem is DesktopIcon i?$"{i.Name} · ({i.X}, {i.Y})":"没有匹配的图标";
    }
    async Task<object> HandleCommand(JsonElement cmd)
    {
        string action = cmd.GetProperty("action").GetString()!;
        switch (action)
        {
            case "companion": return await CompanionCommand(cmd);
            case "status": return new { busy = engine.Busy, ambientBusy = engine.AmbientBusy, realMoveAllowed = engine.AllowRealMove, cursorAllowed = engine.AllowCursor, state = pet.State, clip = pet.AnimationKey, frame = pet.CurrentFrame, facing = pet.FacingRight ? "right" : "left", seatedIcon = engine.SeatedIcon, animationCount = pet.Clips.Count, renderSize = pet.SpriteDisplaySize, characterHeight = pet.ReferenceCharacterHeight, history = engine.History.Count };
            case "animations": return pet.Clips.Select(kv => new { key = kv.Key, kv.Value.Label, kv.Value.Count, kv.Value.LoopStart, kv.Value.LoopEnd, kv.Value.Directional });
            case "animation": await engine.PreviewAnimation(cmd.GetProperty("name").GetString()!, cmd.TryGetProperty("right", out var facing) && facing.GetBoolean()); return new { played = true };
            case "sit":
                if(cmd.TryGetProperty("name",out var seatName))
                {
                    using var shell=new ShellDesktop();var name=seatName.GetString();
                    var matches=shell.List(false).Where(i=>i.Id==name||i.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).ToArray();
                    if(matches.Length!=1)throw new ArgumentException("图标名必须精确且唯一，请先 list。");
                    await engine.SitAtIcon(matches[0].Id,cmd.TryGetProperty("right",out var seatRight)&&seatRight.GetBoolean());
                }
                else await engine.IdleAction("sit");
                return new { finished = true };
            case "list": using (var shell = new ShellDesktop()) return new { autoArrange = shell.AutoArrange, icons = shell.List().Select(i => new { i.Id, i.Name, i.X, i.Y }) };
            case "stop": engine.Stop(); return new { stopped = true };
            case "undo": await engine.Undo(); RefreshIcons(); return new { undone = true };
            case "cursor": await engine.GrabCursor(); return new { released = true };
            case "say": var text = cmd.GetProperty("text").GetString() ?? ""; pet.Say(text[..Math.Min(80, text.Length)]); return new { spoken = true };
            case "state": var state = cmd.GetProperty("state").GetString()!; if (!new[] { "idle", "think", "working", "wait", "celebrate", "sleep", "error" }.Contains(state)) throw new ArgumentException("不支持的状态"); engine.SetAmbient(state); if (cmd.TryGetProperty("source", out var source) && source.GetString() == "dsh") { companion.OnDsh(state); lastDsh = DateTime.Now;dshProgress.Receive(state);pet.RefreshTaskProgress(dshProgress.Text); } return new { state };
            case "carry":
                using (var shell = new ShellDesktop())
                {
                    var name = cmd.GetProperty("name").GetString()!; var matches = shell.List().Where(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || i.Id == name).ToList();
                    if (matches.Count != 1) throw new ArgumentException("图标名必须精确且唯一，请先 list。");
                    bool real = cmd.TryGetProperty("real", out var r) && r.GetBoolean();
                    await engine.Carry(matches[0].Id, cmd.GetProperty("destination").GetString()!, real, cmd.TryGetProperty("drag",out var drag)&&drag.GetBoolean()); RefreshIcons(); return new { done = true, real };
                }
            default: throw new ArgumentException("未知命令");
        }
    }
    async Task<object> CompanionCommand(JsonElement cmd)
    {
        string Read(string name)=>cmd.TryGetProperty(name,out var value)?value.GetString()??"":"";
        var operation=Read("operation");
        switch(operation)
        {
            case "adjust_putdown":
                if(putdownAdjustment!=null){putdownAdjustment.Activate();break;}
                using(var shell=new ShellDesktop())
                {
                    var name=Read("name");var matches=shell.List(false).Where(i=>i.Id==name||i.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).ToArray();
                    if(matches.Length!=1)throw new ArgumentException("请选择一个唯一的图标。");
                    var adjust=new PutdownAdjustmentWindow(pet,companion.Calibration,matches[0].Id,cmd.TryGetProperty("right",out var initialRight)&&initialRight.GetBoolean());
                    try{await adjust.Begin();}catch{adjust.Close();throw;}
                    putdownAdjustment=adjust;adjust.Closed+=(_,_)=>putdownAdjustment=null;adjust.Show();
                }
                break;
            case "status": return new{todos=companion.Store.Data.Todos,files=companion.Store.Data.Files,windowMoveAllowed=companion.AllowWindowMove,autonomousMoveAllowed=companion.Autonomy.Enabled,sleepEnabled=engine.AllowSleep,sleepPhase=engine.SleepPhase};
            case "todo_add": return companion.AddTodo(Read("text"));
            case "todo_complete": companion.CompleteTodo(Read("id"));break;
            case "borrow": await companion.Borrow(Read("name"));break;
            case "find_cursor": await companion.FindCursor();break;
            case "hide_cursor": await companion.FindCursor(true);break;
            case "guide_cursor": await companion.GuideCursor(Read("name"));break;
            case "deliver": await companion.Deliver(Read("path"));break;
            case "windows": return DesktopWindows.List();
            case "move_window":
                var handle=cmd.GetProperty("handle").GetInt64();var window=DesktopWindows.List().SingleOrDefault(w=>w.Handle==handle)??throw new ArgumentException("窗口已不可用，请重新列出。");
                await companion.MoveWindow(window,Read("direction")=="left"?-1:1);break;
            case "window_undo": companion.UndoWindow();break;
            case "plan_icons": companion.PlanIcons(cmd.GetProperty("names").EnumerateArray().Select(n=>n.GetString()!));return companion.Plan;
            case "preview_plan": await companion.PreviewPlan();break;
            case "apply_plan": await companion.ApplyPlan();break;
            default: throw new ArgumentException("未知陪伴操作。");
        }
        companion.Refresh();return new{done=true};
    }
}



