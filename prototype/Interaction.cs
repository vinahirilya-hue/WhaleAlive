using System.IO;
using System.Text.Json;
using System.Windows;

namespace WhaleAlive;
public record MoveEntry(string Id, string Name, int FromX, int FromY, int ToX, int ToY, string Status, DateTime Timestamp, string? SessionId=null, string? ActivityId=null);
public sealed class Interaction
{
    readonly PetWindow pet;
    readonly IDesktopSeatSource seatSource;
    CancellationTokenSource? active;
    CancellationTokenSource? wander;
    Task wanderTask = Task.CompletedTask;
    string? lastIdleAction;
    public bool Busy => active != null;
    public bool AmbientBusy => wander != null;
    public string? SeatedIcon { get; private set; }
    public bool AllowRealMove { get; set; }
    public bool AllowCursor { get; set; }
    public bool Quiet { get; set; }
    public bool AllowSleep { get; set; } = true;
    internal Func<bool,int> RestSeconds {get;set;}=new AutonomousSettings().RestSeconds;
    public string? SleepPhase {get;private set;}
    public long StopVersion {get;private set;}
    public string AmbientState { get; set; } = "idle";
    public event Action<string, int>? Progress;
    public event Action? Changed;
    public List<MoveEntry> History { get; private set; } = new();
    readonly string historyPath;
    public Interaction(PetWindow pet, string? historyFile = null, IDesktopSeatSource? seats = null) { this.pet = pet; seatSource = seats ?? new DesktopSeats(); historyPath = historyFile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhaleAlive", "history.json"); if (File.Exists(historyPath)) { try { History = JsonSerializer.Deserialize<List<MoveEntry>>(File.ReadAllText(historyPath)) ?? new(); } catch (Exception e) { throw new InvalidOperationException("撤销记录无法读取，已保留原文件，请先修复或备份后重试。", e); } } }
    public void Save() { Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!); File.WriteAllText(historyPath + ".tmp", JsonSerializer.Serialize(History, new JsonSerializerOptions { WriteIndented = true })); File.Move(historyPath + ".tmp", historyPath, true); }
    void Report(string s, int phase) { pet.Say(s); Progress?.Invoke(s, phase); Changed?.Invoke(); }
    public void Stop() { StopVersion++; active?.Cancel(); wander?.Cancel(); SeatedIcon = null; pet.Attach(null); if (!pet.IsDragging) { pet.SetState("idle"); Report("已停止，控制权交还给你。", 0); } else Changed?.Invoke(); }
    public void StopAmbient() => wander?.Cancel();
    public async Task WaitForIdle()
    {
        var deadline=DateTime.UtcNow.AddSeconds(6);
        while(Busy||AmbientBusy){if(DateTime.UtcNow>deadline)throw new InvalidOperationException("当前动作正在收尾，请稍后恢复。");await Task.Delay(20);}
    }
    public void SetAmbient(string state) { AmbientState = state; if (state != "idle") wander?.Cancel(); if (!Busy && !AmbientBusy && !pet.IsDragging) pet.SetState(state); }
    public Task Wander()
    {
        if (Quiet || Busy || wander != null || pet.IsDragging || AmbientState != "idle") return Task.CompletedTask;
        wander = new CancellationTokenSource(); wanderTask = RunWander(wander); return wanderTask;
    }
    async Task RunWander(CancellationTokenSource cts)
    {
        try { var p = pet.Anchor; var w = DisplayGeometry.WorkArea(pet, p); var dest = new Point(Math.Clamp(p.X + Random.Shared.Next(-250, 251), w.Left + 100, w.Right - 120), Math.Clamp(p.Y, w.Top + 220, w.Bottom - 20)); bool run = Random.Shared.Next(4) == 0; bool brake = run && Random.Shared.NextDouble() < .3; await pet.WalkTo(dest, cts.Token, run: run, brake: brake); }
        catch (OperationCanceledException) { }
        finally { cts.Dispose(); wander = null; if (!pet.IsDragging && !Busy) pet.SetState(AmbientState); }
    }
    async Task StopWander() { var version=StopVersion; wander?.Cancel(); await wanderTask; if(version!=StopVersion)throw new OperationCanceledException(); }
    public Task IdleAction(string? action = null)
    {
        if (Busy || AmbientBusy || pet.IsDragging || AmbientState != "idle") return Task.CompletedTask;
        wander = new CancellationTokenSource(); wanderTask = RunIdle(wander, action); return wanderTask;
    }
    public Task RunIdleActivity(Func<CancellationToken,Task> play)
    {
        if(Busy||AmbientBusy||pet.IsDragging||pet.IsPressing||pet.IsMenuOpen||AmbientState!="idle")return Task.CompletedTask;
        wander=new CancellationTokenSource();wanderTask=RunIdleActivity(wander,play);return wanderTask;
    }
    async Task RunIdleActivity(CancellationTokenSource cts,Func<CancellationToken,Task> play)
    {
        try{await play(cts.Token);}
        catch(OperationCanceledException){}
        finally{cts.Dispose();wander=null;if(!pet.IsDragging&&!Busy)pet.SetState(AmbientState);}
    }
    async Task RunIdle(CancellationTokenSource cts, string? action)
    {
        try
        {
            string[] choices = ["sit", "sit", "stretch", "curious", "tidy", "wave", "peek", "proud", "sleep"];
            if (action == null) { var pool = choices.Where(c => c != lastIdleAction && (AllowSleep || c!="sleep") && (!Quiet || c is "curious" or "sleep" or "tidy")).ToArray(); action = pool[Random.Shared.Next(pool.Length)]; }
            lastIdleAction = action;
            if (action == "sit")
            {
                if (!await SitOnIcon(cts.Token)) { pet.Say("暂时没有露出来又坐得下的图标，先在这里休息。打开桌面再试试吧。"); await pet.PlayOnce("curious", cts.Token); }
            }
            else if(action=="sleep")await SleepOnIcon(cts.Token);
            else await pet.PlayOnce(action, cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { pet.Say("这次先在这里休息。"); Progress?.Invoke("待机动作结束：" + ex.Message, 0); }
        finally { SleepPhase=null;SeatedIcon = null; cts.Dispose(); wander = null; if (!pet.IsDragging && !Busy) pet.SetState(AmbientState); }
    }
    async Task SleepOnIcon(CancellationToken ct)
    {
        SleepPhase="drowsy";await pet.PlayOnce("sleep",ct);
        SleepPhase="yawn";await pet.PlayOnce("yawn",ct);
        SleepPhase="search";
        var candidates=seatSource.Available(pet);
        if(candidates.Count==0){pet.Say("没有合适的小枕头，先站着歇会儿。");return;}
        var target=candidates.OrderBy(s=>(s.Surface-pet.Anchor).Length).First();
        var work=DisplayGeometry.WorkArea(pet,target.Surface);bool facingRight=target.Surface.X>work.Left+work.Width/2;
        pet.Say("去 "+target.Name+" 上趴一会儿…");
        await pet.WalkTo(pet.SurfaceStandingPosition(target.Surface,"lie_sleep",facingRight),ct);ct.ThrowIfCancellationRequested();
        if(!seatSource.StillThere(target))return;
        pet.Face(facingRight);pet.SetAnchor(target.Surface);
        SeatedIcon=target.Name;SleepPhase="lie_down";
        await pet.PlayOnce("lie_sleep",ct,0,29);
        var clip=pet.Clips["lie_sleep"];pet.LoopSegment("lie_sleep",clip.LoopStart,clip.LoopEnd);SleepPhase="sleeping";pet.Say("呼…");
        int duration=RestSeconds(true);
        for(int i=0;i<duration;i++){
            await Task.Delay(1000,ct);if(!seatSource.StillThere(target))break;
        }
        SleepPhase="wake";await pet.PlayReverse("lie_sleep",ct,18);pet.SetState("idle");pet.SetAnchor(pet.SurfaceStandingPosition(target.Surface,"lie_sleep",facingRight));SeatedIcon=null;
    }
    public Task SitAtIcon(string id,bool facingRight)=>RunActivity(async ct=>
    {
        if(!await SitOnIcon(ct,id,facingRight))throw new InvalidOperationException("这个图标没有露出可坐的位置。");
    });
    async Task<bool> SitOnIcon(CancellationToken ct,string? id=null,bool? direction=null)
    {
        var candidates = seatSource.Available(pet);
        if(id!=null)candidates=candidates.Where(s=>s.Id==id).ToArray();
        if (candidates.Count == 0) return false;
        var target = candidates[Random.Shared.Next(candidates.Count)];
        var work = DisplayGeometry.WorkArea(pet, target.Surface);
        bool facingRight=direction??target.Surface.X>work.Left+work.Width/2;
        pet.Say("去 " + target.Name + " 上坐一会儿。");
        await pet.WalkTo(pet.SurfaceStandingPosition(target.Surface,"sit",facingRight), ct);
        ct.ThrowIfCancellationRequested();
        if (!seatSource.StillThere(target)) return false;
        pet.Face(facingRight);pet.SetAnchor(target.Surface);
        SeatedIcon = target.Name;
        await pet.PlayOnce("sit", ct, 0, pet.Clips["sit"].LoopStart);
        pet.SetState("sit_idle"); pet.Say("这里刚刚好～");
        int duration = RestSeconds(false);
        for (int i = 0; i < duration; i++)
        {
            await Task.Delay(1000, ct);
            if (!seatSource.StillThere(target)) break;
        }
        await pet.PlayOnce("sit", ct, pet.Clips["sit"].LoopEnd + 1);
        pet.SetState("idle");pet.SetAnchor(pet.SurfaceStandingPosition(target.Surface,"sit",facingRight,leaving:true));
        SeatedIcon = null; return true;
    }
    public async Task PreviewAnimation(string name, bool faceRight)
    {
        if (!pet.Clips.ContainsKey(name)) throw new ArgumentException("未知动作");
        if (Busy) throw new InvalidOperationException("请先停止当前动作。");
        await StopWander();
        if (Busy) throw new InvalidOperationException("请先停止当前动作。");
        using var cts = new CancellationTokenSource(); active = cts; Changed?.Invoke();
        try { pet.Face(faceRight); await pet.PlayOnce(name, cts.Token); }
        finally { active = null; if (!pet.IsDragging) pet.SetState(AmbientState); Changed?.Invoke(); }
    }
    public async Task RunActivity(Func<CancellationToken, Task> play)
    {
        if (Busy) throw new InvalidOperationException("请先停止当前动作。");
        await StopWander();
        if (Busy) throw new InvalidOperationException("请先停止当前动作。");
        using var cts = new CancellationTokenSource(); active = cts; Changed?.Invoke();
        try { await play(cts.Token); }
        finally { pet.Attach(null); if (!pet.IsDragging) pet.SetState(AmbientState); active = null; Changed?.Invoke(); }
    }
    public async Task Carry(string id, string destination, bool real, bool draggingIcon = false, Point? targetPosition = null, Func<bool>? autonomousPermission=null, string? sessionId=null, string? activityId=null)
    {
        if (Busy) throw new InvalidOperationException("当前动作还在进行，请等待或先停止。");
        await StopWander();
        if (Busy) throw new InvalidOperationException("当前动作还在进行，请等待或先停止。");
        if (real && !(autonomousPermission?.Invoke() ?? AllowRealMove)) throw new InvalidOperationException("请先在设置中开启图标移动权限。");
        using var shell = new ShellDesktop(); var icon = shell.List().SingleOrDefault(i => i.Id == id) ?? throw new InvalidOperationException("未找到这个图标，请刷新。");
        if (real && shell.AutoArrange) throw new InvalidOperationException("自动排列已开启，真实移动不可用。请在桌面右键 → 查看中关闭自动排列。");
        var dest = targetPosition ?? Destination(destination); var physical = pet.ToPixels(dest); var target = shell.FromScreen((int)physical.X, (int)physical.Y);
        using var cts = new CancellationTokenSource(); active = cts; Changed?.Invoke();
        bool committed = false;
        try
        {
            Report("去接 " + icon.Name + "…", 1);
            var source = pet.FromPixels(shell.ToScreen(icon.X, icon.Y));bool pickupRight=source.X>pet.Anchor.X;
            await pet.WalkTo(pet.CarryPosition(source,"pickup",pickupRight),cts.Token);pet.Face(pickupRight);
            Report(draggingIcon ? "抓稳，往后拉～" : "拿稳啦！", 2);
            if (draggingIcon) { await pet.PlayOnce("grab", cts.Token); pet.Attach(icon.Image); }
            else { await pet.PlayOnce("pickup", cts.Token, 0, 25); pet.Attach(icon.Image); await pet.PlayOnce("pickup", cts.Token, 26); }
            pet.SetState(draggingIcon ? "pull" : "carry_idle"); await Task.Delay(350, cts.Token);
            bool putdownRight=dest.X>pet.Anchor.X;
            Report("把 " + icon.Name + " 搬到" + destination, 3); await pet.WalkTo(pet.CarryPosition(dest,"putdown",putdownRight), cts.Token, carrying: !draggingIcon, pulling: draggingIcon);pet.Face(putdownRight);
            Report("轻轻放下…", 4); await pet.PlayOnce("putdown", cts.Token, 0, 33); cts.Token.ThrowIfCancellationRequested();
            if (real)
            {
                if (!(autonomousPermission?.Invoke() ?? AllowRealMove)) throw new OperationCanceledException();
                var fresh = shell.List().SingleOrDefault(i => i.Id == id) ?? throw new InvalidOperationException("目标已消失，取消移动。");
                if (fresh.X != icon.X || fresh.Y != icon.Y) throw new InvalidOperationException("你刚刚移动了这个图标，本次操作已取消。");
                var entry = new MoveEntry(id, icon.Name, icon.X, icon.Y, target.X, target.Y, "pending", DateTime.Now,sessionId,activityId);
                History.Add(entry); Save();
                var actual = await shell.MoveSettled(id, target.X, target.Y);
                History[^1] = entry with { ToX = actual.X, ToY = actual.Y, Status = "completed" }; Save();
                committed = true;
                if (actual.X == icon.X && actual.Y == icon.Y) throw new InvalidOperationException("Windows 没有改变图标位置，请检查自动排列设置。");
            }
            pet.Attach(null); await pet.PlayOnce("putdown", cts.Token, 34); pet.SetState("celebrate"); Report(real ? "搬好啦！可以随时撤销。" : "预演完成！真实图标保持原位。", 5);
            await Task.Delay(1200, cts.Token);
        }
        catch (OperationCanceledException) { if (committed) Report("动画已停止；图标已放下，可撤销。", 5); else { Report("动作已取消，图标未移动。", 0); throw; } }
        finally { pet.Attach(null); if (!pet.IsDragging) pet.SetState(AmbientState); active = null; Changed?.Invoke(); }
    }
    public async Task GrabCursor()
    {
        if (Busy) throw new InvalidOperationException("请先等待当前动作结束。");
        await StopWander();
        if (Busy) throw new InvalidOperationException("请先等待当前动作结束。");
        if (!AllowCursor) throw new InvalidOperationException("请先在设置中开启鼠标互动。");
        using var cts = new CancellationTokenSource(); active = cts; Changed?.Invoke();
        try
        {
            Report("让我看看鼠标在哪里…", 0);
            await pet.PlayOnce("notice", cts.Token);
            var p = CursorControl.Position(); await pet.WalkTo(pet.FromPixels(new Point(p.X, p.Y)) + new Vector(-30, 75) * PetWindow.VisualScale, cts.Token);
            cts.Token.ThrowIfCancellationRequested(); if (!AllowCursor) throw new OperationCanceledException();
            await pet.PlayOnce("grab", cts.Token);
            for (int n = 3; n > 0; n--) { Report($"{n} 秒后牵一下鼠标；快速移动或点击可挣脱。", 0); await Task.Delay(1000, cts.Token); }
            cts.Token.ThrowIfCancellationRequested(); if (!AllowCursor) throw new OperationCanceledException();
            pet.SetState("pull"); Report("拉一下就放手～", 0);
            bool escaped = await CursorControl.Pull(cts.Token, () => AllowCursor);
            pet.SetState(escaped ? "disappointed" : "joy"); Report(escaped ? "哎呀，被你挣脱了！" : "好啦，还给你。", 0); await Task.Delay(650, cts.Token);
        }
        finally { active = null; if (!pet.IsDragging) pet.SetState(AmbientState); Changed?.Invoke(); }
    }
    public static Point Destination(string name) { var w = SystemParameters.WorkArea; return name switch { "左下角" => new(w.Left + 100, w.Bottom - 155), "右上角" => new(w.Right - 190, w.Top + 100), "桌面中央" => new(w.Left + w.Width * .5, w.Top + w.Height * .5), "右下角" => new(w.Right - 190, w.Bottom - 155), _ => throw new ArgumentException("不支持的目的地") }; }
    public async Task Undo()
    {
        if (Busy) throw new InvalidOperationException("请先停止当前动作。"); await StopWander(); if (Busy) throw new InvalidOperationException("请先停止当前动作。");
        var entry = History.LastOrDefault(e => e.Status is "completed" or "pending") ?? throw new InvalidOperationException("还没有可撤销的真实移动。");
        using var shell = new ShellDesktop(); var current = shell.List(false).SingleOrDefault(i => i.Id == entry.Id) ?? throw new InvalidOperationException("图标已重命名或移走，撤销记录仍保留。");
        if (entry.Status == "completed" && (current.X != entry.ToX || current.Y != entry.ToY)) throw new InvalidOperationException("图标被再次移动了，已保留你现在的位置；本次没有覆盖。");
        using var cts = new CancellationTokenSource(); active = cts; Changed?.Invoke();
        try { var actual = await shell.MoveSettled(entry.Id, entry.FromX, entry.FromY); if (Math.Abs(actual.X - entry.FromX) > 4 || Math.Abs(actual.Y - entry.FromY) > 4) throw new InvalidOperationException("系统对齐设置阻止了精确恢复。记录已保留，请关闭对齐网格后重试。"); History[History.IndexOf(entry)] = entry with { Status = "undone" }; Save(); Report("已将 " + entry.Name + " 恢复原位。", 0); }
        finally { active = null; Changed?.Invoke(); }
    }
}

