using System.Windows;
namespace WhaleAlive;

public sealed class AutonomousSettings
{
    public bool Patterns {get;set;}=true;
    public bool ReturnAfterPlay {get;set;}=true;
    public bool Sleep {get;set;}=true;
    public int SitSeconds {get;set;}=120;
    public int SleepSeconds {get;set;}=300;
    public int RestSeconds(bool sleeping)=>Math.Clamp(sleeping?SleepSeconds:SitSeconds,30,3600);
    public int IntervalSeconds {get;set;}=180;
    public bool AllIcons {get;set;}=true;
    public bool PrimaryScreenOnly {get;set;}
    public List<string> AllowedIcons {get;set;}=[];
}
public record RestoreTarget(string Id,string Name,int X,int Y,int ExpectedX,int ExpectedY,MoveEntry[] Entries);

public sealed class AutonomousPlay(PetWindow pet,Interaction engine,CompanionFeatures companion)
{
    public AutonomousSettings Settings=>companion.Store.Data.Autonomy;
    public bool Enabled {get;private set;}
    public bool Running {get;private set;}
    bool restoring;
    public bool IdleEnabled {get;set;}=true;
    string session=Guid.NewGuid().ToString("N");
    Task current=Task.CompletedTask;
    DateTime next=DateTime.Now;
    public void Save(){companion.Store.Save();engine.AllowSleep=Settings.Sleep;}
    public void Enable(bool value)
    {
        Enabled=value;next=DateTime.Now.AddSeconds(Math.Clamp(Settings.IntervalSeconds,30,900));
        if(value)session=Guid.NewGuid().ToString("N");else if(Running)engine.Stop();
        companion.Tell(value?"可以自己搬图标啦，玩完可一键恢复。":"自主搬运已关闭。");
    }
    bool Available=>Enabled&&IdleEnabled&&!engine.Quiet&&!pet.IsDragging&&engine.AmbientState=="idle";
    public async Task<bool> TryPlay(bool now=false)
    {
        if(Running||restoring||engine.Busy||engine.AmbientBusy||!Available||(!now&&DateTime.Now<next))return false;
        Running=true;current=Run();await current;return true;
    }
    async Task Run()
    {
        var version=engine.StopVersion;string activity=Guid.NewGuid().ToString("N");
        try{
            using var shell=new ShellDesktop();if(shell.AutoArrange){companion.Tell("桌面自动排列开启，自主搬运暂时休息。");return;}
            var all=shell.List(false);var visible=shell.VisibleSeats();
            var work=Settings.PrimaryScreenOnly?SystemParameters.WorkArea:DisplayGeometry.WorkArea(pet,pet.Anchor);
            var candidates=all.Where(i=>visible.ContainsKey(i.Name)&&(Settings.AllIcons||Settings.AllowedIcons.Contains(i.Id))&&work.Contains(pet.FromPixels(shell.ToScreen(i.X,i.Y)))).OrderBy(_=>Random.Shared.Next()).Take(10).ToArray();
            if(candidates.Length==0){companion.Tell("没有露出来的可玩图标，等桌面空闲再玩。");return;}
            var occupied=all.Select(i=>pet.FromPixels(shell.ToScreen(i.X,i.Y))).ToArray();
            // Derive spacing from native item rectangles so large icons/labels
            // and Windows grid snapping cannot collapse the planned pattern.
            var geometry=shell.IconRectangles();double scale=pet.ToPixels(new(1,0)).X;
            double dx=Math.Max(105,geometry.Select(g=>g.Item.Width/scale+12).DefaultIfEmpty(105).Max());
            double dy=Math.Max(115,geometry.Select(g=>g.Item.Height/scale+12).DefaultIfEmpty(115).Max());
            var targets=PlanTargets(work,occupied,candidates.Length,Settings.Patterns,dx,dy,p=>shell.Exposed(pet.ToPixels(p)));
            if(targets.Count==0){companion.Tell("空位不够，这次先不挪图标。");return;}
            companion.Tell(targets.Count>1?"想到一个小图案，慢慢摆给你看～":"借一个图标，换个地方玩～");
            for(int i=0;i<targets.Count;i++){
                if(version!=engine.StopVersion||!Available)throw new OperationCanceledException();
                var item=candidates[i];var dest=targets[i];
                bool CanMove(){
                    if(!Available||version!=engine.StopVersion)return false;
                    using var check=new ShellDesktop();
                    var source=check.List(false).SingleOrDefault(c=>c.Id==item.Id);
                    if(source==null||source.X!=item.X||source.Y!=item.Y)return false;
                    return check.List(false).Where(c=>c.Id!=item.Id).All(c=>Clear(pet.FromPixels(check.ToScreen(c.X,c.Y)),dest,dx*.8,dy*.8));
                }
                await engine.Carry(item.Id,targets.Count>1?"图案位置":"空位",true,targetPosition:dest,autonomousPermission:CanMove,sessionId:session,activityId:activity);
            }
            if(version!=engine.StopVersion||!Available)throw new OperationCanceledException();
            await engine.RunActivity(async ct=>{await pet.PlayOnce("proud",ct);if(Settings.ReturnAfterPlay)await Task.Delay(8000,ct);});
            if(version!=engine.StopVersion||!Available)throw new OperationCanceledException();
            if(Settings.ReturnAfterPlay)await RestoreEntries(activity,true);
            else companion.Tell("摆好啦！想恢复时去自主设置点一下。");
        }catch(OperationCanceledException){companion.Tell("先停下，已搬的图标仍可恢复。");}
        catch(Exception ex){companion.Tell("这次先不玩了："+ex.Message);}
        finally{Running=false;next=DateTime.Now.AddSeconds(Math.Clamp(Settings.IntervalSeconds,30,900));}
    }
    public static bool Clear(Point a,Point b,double dx,double dy)=>Math.Abs(a.X-b.X)>=dx||Math.Abs(a.Y-b.Y)>=dy;
    public static IReadOnlyList<Point> PlanTargets(Rect work,IReadOnlyList<Point> occupied,int count,bool patterns,double dx=105,double dy=115,Func<Point,bool>? visible=null)
    {
        if(count<1)return [];
        Point[] heart=[new(1,0),new(3,0),new(0,1),new(2,1),new(4,1),new(0,2),new(4,2),new(1,3),new(3,3),new(2,4)];
        Point[] wave=[new(0,1),new(1,0),new(2,0),new(3,1),new(4,2),new(5,2),new(6,1)];
        var shape=patterns&&count>=10?heart:patterns&&count>=7?wave:[new Point(0,0)];
        foreach(var pattern in shape.Length>1?new[]{shape,new[]{new Point(0,0)}}:new[]{shape})
        for(double y=work.Top+90;y<work.Bottom-dy;y+=dy)
        for(double x=work.Left+70;x<work.Right-dx;x+=dx){
            var points=pattern.Select(p=>new Point(x+p.X*dx,y+p.Y*dy)).ToArray();
            if(points.All(p=>p.X<work.Right-dx&&p.Y<work.Bottom-dy&&(visible?.Invoke(p)??true)&&occupied.All(o=>Clear(o,p,dx*.85,dy*.85))))return points;
        }
        return [];
    }
    public static RestoreTarget[] RestorePlan(IEnumerable<MoveEntry> history,string? activity)
    {
        var entries=history.Where(e=>e.SessionId!=null&&(activity==null||e.ActivityId==activity)&&e.Status is "completed" or "pending");
        return entries.GroupBy(e=>e.Id).Select(g=>{
            var chain=g.ToArray();int start=0;
            // A discontinuity means the user moved it between our activities.
            // That newer manual position becomes the restoration baseline.
            for(int i=1;i<chain.Length;i++)if(chain[i].FromX!=chain[i-1].ToX||chain[i].FromY!=chain[i-1].ToY)start=i;
            chain=chain[start..];var a=chain[0];var b=chain[^1];return new RestoreTarget(a.Id,a.Name,a.FromX,a.FromY,b.ToX,b.ToY,chain);
        }).ToArray();
    }
    public static bool CanRestore(RestoreTarget p,DesktopIcon? icon)=>icon!=null&&((icon.X==p.ExpectedX&&icon.Y==p.ExpectedY)||(icon.X==p.X&&icon.Y==p.Y));
    public async Task Restore(bool all)
    {
        if(restoring)throw new InvalidOperationException("正在恢复，请稍候。");
        restoring=true;
        try{
            Enabled=false;engine.Stop();await current;await engine.WaitForIdle();
            var activity=all?null:engine.History.LastOrDefault(e=>e.SessionId!=null&&e.Status is "completed" or "pending")?.ActivityId;
            if(!all&&activity==null){companion.Tell("没有需要恢复的自主活动。");return;}
            await RestoreEntries(activity,false);
        }finally{restoring=false;}
    }
    async Task RestoreEntries(string? activity,bool animate)
    {
        int restored=0,skipped=0;
        var plan=RestorePlan(engine.History,activity);
        foreach(var p in plan){
            var version=engine.StopVersion;
            using var shell=new ShellDesktop();var icon=shell.List(false).SingleOrDefault(i=>i.Id==p.Id);
            if(!CanRestore(p,icon)){skipped++;continue;}
            if(animate){
                var destination=pet.FromPixels(shell.ToScreen(p.X,p.Y));
                await engine.Carry(p.Id,"原来的位置",true,targetPosition:destination,autonomousPermission:()=>Available&&version==engine.StopVersion&&CanRestore(p,shell.List(false).SingleOrDefault(i=>i.Id==p.Id)),sessionId:p.Entries[^1].SessionId,activityId:p.Entries[^1].ActivityId);
                var actual=shell.List(false).Single(i=>i.Id==p.Id);
                if(Math.Abs(actual.X-p.X)>4||Math.Abs(actual.Y-p.Y)>4)throw new InvalidOperationException("对齐设置阻止了精确归位，记录已保留。");
                // The return trip is an implementation detail of the same
                // activity; don't leave a separate undo that undoes the return.
                engine.History[^1]=engine.History[^1] with{Status="undone"};
            }else await engine.RunActivity(async ct=>{
                ct.ThrowIfCancellationRequested();
                if(!CanRestore(p,shell.List(false).SingleOrDefault(i=>i.Id==p.Id)))throw new InvalidOperationException("图标刚被你移动，保留新位置。");
                var actual=await shell.MoveSettled(p.Id,p.X,p.Y);
                if(Math.Abs(actual.X-p.X)>4||Math.Abs(actual.Y-p.Y)>4)throw new InvalidOperationException("系统对齐阻止了精确归位，恢复记录已保留。");
            });
            foreach(var entry in p.Entries){int n=engine.History.IndexOf(entry);if(n>=0)engine.History[n]=entry with{Status="undone"};}
            engine.Save();restored++;
            if(version!=engine.StopVersion)throw new OperationCanceledException();
        }
        companion.Tell($"已恢复 {restored} 个图标"+(skipped>0?$"，跳过 {skipped} 个已被你移动或移走的图标。":"。"));
    }
}
