using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace WhaleAlive;
public sealed class CompanionFeatures : IDisposable
{
    readonly PetWindow pet; readonly Interaction engine; readonly DispatcherTimer clock=new(){Interval=TimeSpan.FromSeconds(1)};
    public CompanionStore Store {get;}
    public AutonomousPlay Autonomy {get;}
    public SocialMusicService SocialMusic {get;}
    public PoseCalibration Calibration {get;}
    public WindowPerch Perch {get;}
    public bool AllowWindowMove {get;set;}
    public event Action? Changed;
    public string Message {get;private set;}="来试试新的陪伴玩法。";
    string previousDsh="idle";
    bool pendingApproval;
    bool batchRunning;
    long seenStopVersion;
    public List<IconPlan> Plan {get;private set;}=[];
    public void PlanIcons(IEnumerable<string> names)
    {
        if(batchRunning)throw new InvalidOperationException("请先停止正在执行的整理计划。");
        using var shell=new ShellDesktop();var all=shell.List(false);var selected=names.Distinct().ToArray();
        if(selected.Length is <1 or >12)throw new ArgumentException("每批选择 1–12 个图标。");
        var ordered=selected.Select(FindIcon).OrderBy(i=>Path.GetExtension(i.Id)).ThenBy(i=>i.Name).ToArray();
        var occupied=all.Where(i=>!ordered.Any(s=>s.Id==i.Id)).Select(i=>pet.FromPixels(shell.ToScreen(i.X,i.Y))).ToArray();
        var work=SystemParameters.WorkArea;var free=new List<Point>();
        var existing=all.Select(i=>pet.FromPixels(shell.ToScreen(i.X,i.Y))).Where(work.Contains).ToArray();
        static double Step(IEnumerable<double> values,double fallback){var sorted=values.Distinct().Order().ToArray();return sorted.Zip(sorted.Skip(1),(a,b)=>b-a).Where(d=>d>=40).DefaultIfEmpty(fallback).Min();}
        double dx=Step(existing.Select(p=>p.X),100),dy=Step(existing.Select(p=>p.Y),120);
        double originX=existing.Length>0?existing.Min(p=>p.X):work.Left+20,originY=existing.Length>0?existing.Min(p=>p.Y):work.Top+10;
        for(double x=originX;x<work.Right-dx;x+=dx)
        for(double y=originY;y<work.Bottom-dy;y+=dy)
            if(occupied.All(p=>Math.Abs(p.X-x)>dx*.7||Math.Abs(p.Y-y)>dy*.7))free.Add(new(x,y));
        if(free.Count<ordered.Length)throw new InvalidOperationException("主屏没有足够空位，请少选一些图标。");
        Plan=ordered.Select((i,n)=>new IconPlan(i.Id,i.Name,i.X,i.Y,free[n])).ToList();Tell("整理计划已生成，先检查再执行。只排列已选图标。");
    }
    public Task PreviewPlan()=>engine.RunActivity(async ct=>{
        if(Plan.Count==0)throw new InvalidOperationException("请先生成整理计划。");
        var markers=new List<PetScene>();try{foreach(var p in Plan){var scene=new PetScene(pet.ToPixels(p.Destination),p.Name,[],true);scene.Show();markers.Add(scene);}await Task.Delay(4000,ct);}finally{foreach(var m in markers)m.Dispose();}
    });
    public async Task ApplyPlan()
    {
        if(batchRunning||engine.Busy)throw new InvalidOperationException("请先停止当前动作。");
        if(!engine.AllowRealMove)throw new InvalidOperationException("请先在设置中开启真实图标移动权限。");
        if(Plan.Count==0)throw new InvalidOperationException("请先生成计划。");
        var pending=Plan.ToArray();batchRunning=true;
        try{await ExecutePlan(pending,()=>engine.StopVersion,async item=>{
            using(var shell=new ShellDesktop()){var current=shell.List(false).SingleOrDefault(i=>i.Id==item.Id);if(current==null||current.X!=item.FromX||current.Y!=item.FromY)throw new InvalidOperationException("图标位置已改变，请重新生成计划。");}
            await engine.Carry(item.Id,"计划位置",true,targetPosition:item.Destination);Plan.Remove(item);
        });}finally{batchRunning=false;}
        Tell("整理完成，需要时可通过 DSH 逐项撤销。");
    }
    public static async Task ExecutePlan(IReadOnlyList<IconPlan> items,Func<long> stopVersion,Func<IconPlan,Task> execute)
    {
        long original=stopVersion();
        foreach(var item in items){if(stopVersion()!=original)throw new OperationCanceledException();await execute(item);}
        if(stopVersion()!=original)throw new OperationCanceledException();
    }
    public CompanionFeatures(PetWindow pet,Interaction engine,CompanionStore? store=null)
    {
        this.pet=pet;this.engine=engine;Store=store??new();Autonomy=new(pet,engine,this);SocialMusic=new(pet,Store);engine.AllowSleep=Store.Data.Autonomy.Sleep;
        engine.RestSeconds=sleep=>Store.Data.Autonomy.RestSeconds(sleep);
        Calibration=new(pet,engine,Store);Perch=new(pet,engine,Store);
        pet.FilesOffered+=KeepFiles;clock.Tick+=(_,_)=>Tick();clock.Start();Refresh();Tick();
    }
    public void Tell(string text){Message=text;pet.Say(text);Changed?.Invoke();}
    public void Refresh()
    {
        pet.ShowTodos(Store.Data.Todos,CompleteTodo);
        engine.Quiet=Store.Data.Quiet;engine.AllowSleep=Store.Data.Autonomy.Sleep;Changed?.Invoke();
    }
    void Tick()
    {
        if(seenStopVersion!=engine.StopVersion){seenStopVersion=engine.StopVersion;pendingApproval=false;}
        engine.Quiet=Store.Data.Quiet;
        if(pendingApproval&&!engine.Busy&&!engine.AmbientBusy&&!pet.IsDragging&&!pet.IsPressing&&!pet.IsMenuOpen){pendingApproval=false;_=ApprovalNotice();}
        Changed?.Invoke();
    }
    async Task Celebrate(){try{await engine.RunActivity(ct=>pet.PlayOnce("proud",ct));}catch(OperationCanceledException){}catch(Exception ex){Tell(ex.Message);}}
    public PetNote AddTodo(string text){var note=Store.AddNote(text);Tell("把这件事装进泡泡里啦。");Refresh();return note;}
    public void CompleteTodo(string id){if(!Store.Complete(id))return;Tell("又完成一件！");Refresh();if(!engine.Busy&&!engine.AmbientBusy)_=Celebrate();}
    public void KeepFiles(string[] paths,string kind)
    {
        int count=0;var errors=new List<string>();
        foreach(var path in paths.Take(50))try{Store.KeepFile(path,kind);count++;}catch(Exception e){errors.Add(e.Message);}
        Tell($"已记住 {count} 个文件的位置。"+(errors.Count>0?" 部分文件未能暂存："+errors[0]:""));Refresh();
    }
    public static void Reveal(string path)
    {
        if(!File.Exists(path)&&!Directory.Exists(path))throw new FileNotFoundException("原文件已移动或删除；暂存条目仍保留。",path);
        var start=new ProcessStartInfo("explorer.exe"){UseShellExecute=false};
        if(File.Exists(path))start.ArgumentList.Add("/select,"+Path.GetFullPath(path));else start.ArgumentList.Add(Path.GetFullPath(path));
        Process.Start(start);
    }
    Point Limit(Point p){var w=DisplayGeometry.WorkArea(pet,p);return new(Math.Clamp(p.X,w.Left+100,w.Right-100),Math.Clamp(p.Y,w.Top+160,w.Bottom-45));}
    DesktopIcon FindIcon(string name)
    {
        using var shell=new ShellDesktop();var matches=shell.List().Where(i=>i.Id==name||i.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length==1?matches[0]:throw new ArgumentException("请选择一个唯一的桌面图标。");
    }
    Point IconPoint(DesktopIcon icon)
    {
        using var shell=new ShellDesktop();var seats=shell.VisibleSeats();
        return pet.FromPixels(seats.TryGetValue(icon.Name,out var p)?p:shell.ToScreen(icon.X,icon.Y));
    }
    public Task Borrow(string name)=>engine.RunActivity(async ct=>{
        var icon=FindIcon(name);using var shell=new ShellDesktop();var origin=pet.FromPixels(shell.ToScreen(icon.X,icon.Y));
        bool pickupRight=origin.X>pet.Anchor.X;var start=pet.CarryPosition(origin,"pickup",pickupRight);
        Tell("借 "+icon.Name+" 陪我散一小圈，很快送回来。");
        await pet.WalkTo(start,ct);pet.Face(pickupRight);await pet.PlayOnce("pickup",ct,0,25);pet.Attach(icon.Image);await pet.PlayOnce("pickup",ct,26);
        await pet.WalkTo(Limit(start+new Vector(120,-25)),ct,carrying:true);await Task.Delay(450,ct);
        bool putdownRight=origin.X>pet.Anchor.X;
        await pet.WalkTo(pet.CarryPosition(origin,"putdown",putdownRight),ct,carrying:true);pet.Face(putdownRight);await pet.PlayOnce("putdown",ct,0,33);pet.Attach(null);await pet.PlayOnce("putdown",ct,34);Tell("借玩结束，已经送回原处啦。");
    });
    public Task FindCursor(bool hide=false)=>engine.RunActivity(async ct=>{
        using var bounded=CancellationTokenSource.CreateLinkedTokenSource(ct);bounded.CancelAfter(TimeSpan.FromSeconds(65));ct=bounded.Token;
        int rounds=hide?4:1;
        for(int i=0;i<rounds;i++){
            var p=CursorControl.Position();var target=pet.FromPixels(new(p.X,p.Y));
            Tell(hide?"捉迷藏开始，看看你往哪里走～":"鼠标在这里！");
            await pet.WalkTo(Limit(target+new Vector(55,70)),ct,run:true);
            using var ripple=new PetScene(pet.ToPixels(target),"光标在这里",[],true);ripple.Show();
            await pet.PlayOnce(hide?"peek":"wave",ct);
        }
    });
    public Task GuideCursor(string iconName)=>engine.RunActivity(async ct=>{
        if(!engine.AllowCursor)throw new InvalidOperationException("请先开启短暂鼠标互动权限。");
        var icon=FindIcon(iconName);var target=pet.ToPixels(IconPoint(icon)+new Vector(0,22));
        await pet.WalkTo(Limit(pet.FromPixels(target)+new Vector(55,50)),ct,run:true);
        Tell("把指针带到图标旁；移动鼠标或点击即可取消。");
        using var marker=new PetScene(target,"指针目的地",[],true);marker.Show();await Task.Delay(1000,ct);
        bool escaped=await CursorControl.Guide(new((int)target.X,(int)target.Y),ct,()=>engine.AllowCursor);
        Tell(escaped?"你接手啦。":"指针到了，由你决定是否打开。");
    });
    public void OnDsh(string state)
    {
        if(state=="wait"&&previousDsh!="wait")pendingApproval=true;
        if(state!="wait")pendingApproval=false;previousDsh=state;
    }
    async Task ApprovalNotice()
    {
        try{await engine.RunActivity(async ct=>{
            Tell("DSH 在等你确认，我就在这里等。");await pet.PlayOnce("wave",ct);
        });}catch(OperationCanceledException){}catch(Exception e){Tell(e.Message);}
    }
    public Task Deliver(string path)=>engine.RunActivity(async ct=>{
        var item=Store.KeepFile(path,"delivery");Refresh();
        Tell("收到一份新产物："+Path.GetFileName(item.Path));
        pet.Attach(FileImage(item.Path));pet.SetState("carry_idle");await Task.Delay(650,ct);
        var w=DisplayGeometry.WorkArea(pet,pet.Anchor);await pet.WalkTo(new(w.Right-150,w.Bottom-80),ct,carrying:true);
        pet.Attach(null);Tell("产物送到了，在「文件与产物」里定位打开。");
    });
    static ImageSource? FileImage(string path)
    {
        return ShellDesktop.ImageForPath(path);
    }
    public async Task MoveWindow(PetDesktopWindow window,int direction)
    {
        if(!AllowWindowMove)throw new InvalidOperationException("请先在设置中开启窗口移动权限。");
        await engine.RunActivity(async ct=>{
        var original=DesktopWindows.Current(window)??throw new InvalidOperationException("窗口不可移动。");
            var work=DisplayGeometry.WorkArea(pet,pet.FromPixels(original.TopLeft));var left=pet.ToPixels(work.TopLeft).X;var right=pet.ToPixels(work.BottomRight).X;
            double destination=Math.Clamp(original.Left+Math.Sign(direction)*140,left,Math.Max(left,right-original.Width));
            var entry=new WindowMoveRecord(window,original,original,Process.GetProcessById((int)window.ProcessId).StartTime.ToUniversalTime(),false);Store.Data.WindowMoves.Add(entry);Store.Save();
            Tell(direction<0?"抓住窗口，往左拉一点。":"把窗口往右推一点。");
            var anchor=Limit(pet.FromPixels(new(original.Left,original.Top+100)));await pet.WalkTo(anchor,ct);pet.SetState("pull");
            try{
                for(int i=1;i<=28;i++){
                    ct.ThrowIfCancellationRequested();if(!AllowWindowMove)throw new OperationCanceledException();
                    var current=DesktopWindows.Current(window);if(current==null||(current.Value.TopLeft-entry.Last.TopLeft).Length>3)throw new InvalidOperationException("你移动了窗口，我先松手。");
                    var actual=DesktopWindows.Move(window,new(original.Left+(destination-original.Left)*i/28,original.Top));
                    entry=entry with{Last=actual};Store.Data.WindowMoves[^1]=entry;Store.Save();
                    pet.SetAnchor(anchor+new Vector((actual.Left-original.Left)/pet.ToPixels(new(1,0)).X,0));await Task.Delay(30,ct);
                }
            }finally{Store.Data.WindowMoves[^1]=entry;Store.Save();}
            Tell("挪好了，可点击「撤销窗口移动」恢复。");
        });
    }
    public void UndoWindow()
    {
        if(engine.Busy)throw new InvalidOperationException("请先停止动作。");
        var entry=Store.Data.WindowMoves.LastOrDefault(e=>!e.Undone)??throw new InvalidOperationException("没有可撤销的窗口移动。");
        if(Process.GetProcessById((int)entry.Window.ProcessId).StartTime.ToUniversalTime()!=entry.ProcessStart)throw new InvalidOperationException("原窗口进程已结束。");
        var current=DesktopWindows.Current(entry.Window);if(current==null||(current.Value.TopLeft-entry.Last.TopLeft).Length>3)throw new InvalidOperationException("窗口已被你移动，不覆盖当前位置。");
        DesktopWindows.Move(entry.Window,entry.Original.TopLeft);Store.Data.WindowMoves[Store.Data.WindowMoves.IndexOf(entry)]=entry with{Undone=true};Store.Save();Tell("已恢复窗口位置。");
    }
    public void Dispose(){clock.Stop();Perch.Dispose();Calibration.Dispose();SocialMusic.Dispose();pet.FilesOffered-=KeepFiles;}
}
public record WindowMoveRecord(PetDesktopWindow Window,Rect Original,Rect Last,DateTime ProcessStart,bool Undone);
public record IconPlan(string Id,string Name,int FromX,int FromY,Point Destination)
{
    public override string ToString()=>$"{Name} → ({Destination.X:0}, {Destination.Y:0})";
}

