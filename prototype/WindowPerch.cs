using System.Windows;
using System.Windows.Threading;
namespace WhaleAlive;

// A perch observes its host window; it never moves or resizes that window.
public sealed class WindowPerch : IDisposable
{
    readonly PetWindow pet;readonly Interaction engine;readonly CompanionStore store;
    readonly Func<PetDesktopWindow,Rect?> bounds;
    readonly Func<IReadOnlyList<PetDesktopWindow>> windows;
    readonly DispatcherTimer follow=new(){Interval=TimeSpan.FromMilliseconds(33)};
    CancellationTokenSource? lifetime;Task running=Task.CompletedTask;
    PetDesktopWindow? target;bool sleeping,preparing,disposed,approaching;
    double fraction=.5;PoseCorrection draft=new(0,0);
    public bool Active=>target!=null;
    public bool Editing{get;private set;}
    public string TargetName=>target?.Title??"";
    public Vector Offset=>(Editing?draft:Saved).ForDirection(pet.FacingRight);
    public event Action? Changed;
    PoseCorrection Saved{get=>sleeping?store.Data.WindowSleepCorrection:store.Data.WindowSeatCorrection;set{if(sleeping)store.Data.WindowSleepCorrection=value;else store.Data.WindowSeatCorrection=value;}}
    public WindowPerch(PetWindow pet,Interaction engine,CompanionStore store,Func<PetDesktopWindow,Rect?>? bounds=null,Func<IReadOnlyList<PetDesktopWindow>>? windows=null)
    {
        this.pet=pet;this.engine=engine;this.store=store;this.bounds=bounds??DesktopWindows.EdgeBounds;
        this.windows=windows??DesktopWindows.PerchWindows;
        follow.Tick+=(_,_)=>Follow();
    }
    bool Locate(Rect r,out Point point)
    {
        point=pet.FromPixels(new(r.Left+r.Width*fraction,r.Top));
        var work=DisplayGeometry.WorkArea(pet,point);
        // Leave room for the head above the top edge, including vertical calibration.
        return r.Width>160&&point.X>work.Left+100&&point.X<work.Right-100&&point.Y>work.Top+140&&point.Y<work.Bottom-60;
    }
    internal bool TryIdleFraction(Rect r,out double selected)
    {
        selected=.5;
        var center=pet.FromPixels(new(r.Left+r.Width/2,r.Top));
        var work=DisplayGeometry.WorkArea(pet,center);
        var screen=new Rect(pet.ToPixels(work.TopLeft),pet.ToPixels(work.BottomRight));
        double margin=pet.ToPixels(new Point(100,0)).X-pet.ToPixels(new Point()).X;
        double left=Math.Max(r.Left+margin,screen.Left+margin+1),right=Math.Min(r.Right-margin,screen.Right-margin-1);
        if(r.Width<=160||right<left||center.Y<=work.Top+140||center.Y>=work.Bottom-60)return false;
        selected=(left+Random.Shared.NextDouble()*(right-left)-r.Left)/r.Width;
        return true;
    }
    public async Task<bool> TryIdle()
    {
        if(Active||preparing||disposed||engine.Busy||engine.AmbientBusy||engine.AmbientState!="idle"||pet.IsDragging||pet.IsPressing||pet.IsMenuOpen)return false;
        var candidates=new List<(PetDesktopWindow Window,double Fraction)>();
        foreach(var window in windows())
        {
            if(bounds(window) is Rect r&&TryIdleFraction(r,out var spot))candidates.Add((window,spot));
        }
        if(candidates.Count==0)return false;
        var chosen=candidates[Random.Shared.Next(candidates.Count)];
        await Start(chosen.Window,engine.AllowSleep&&Random.Shared.Next(3)==0,true,chosen.Fraction);
        await running;return true;
    }
    public Task Start(PetDesktopWindow window,bool sleep)=>Start(window,sleep,false);
    async Task Start(PetDesktopWindow window,bool sleep,bool ambient,double? selectedFraction=null)
    {
        if(preparing||disposed)return;preparing=true;
        try
        {
            var r=bounds(window)??throw new InvalidOperationException("窗口已关闭、最小化或最大化，请选择普通窗口。");
            fraction=selectedFraction??Math.Clamp((pet.ToPixels(pet.Anchor).X-r.Left)/r.Width,.2,.8);
            if(!Locate(r,out var point))throw new InvalidOperationException("窗口上方需要留出桌宠的空间，请先把窗口往下移一点。");
            if(!ambient){engine.Stop();await engine.WaitForIdle();}
            sleeping=sleep;draft=Saved;target=window;
            lifetime=new();var token=lifetime.Token;
            async Task Rest(CancellationToken ct)
            {
                using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,token);
                try
                {
                    pet.SurfaceOffset=(clip,right)=>clip is "sit" or "sit_idle" or "lie_sleep"?(Editing?draft:Saved).ForDirection(right):new Vector();
                    pet.Say("");
                    if(ambient)
                    {
                        if(sleep){await pet.PlayOnce("sleep",linked.Token);await pet.PlayOnce("yawn",linked.Token);}
                        bool facingRight=point.X>pet.Anchor.X;
                        await pet.WalkTo(pet.SurfaceStandingPosition(point,sleep?"lie_sleep":"sit",facingRight),linked.Token);
                        if(bounds(window) is not Rect current||!Locate(current,out point))return;
                        pet.Face(facingRight);
                    }
                    approaching=sleep&&!ambient;pet.SetAnchor(approaching?pet.SurfaceStandingPosition(point,"lie_sleep",pet.FacingRight):point);follow.Start();Changed?.Invoke();
                    if(sleep){if(!ambient)await pet.PlayOnce("yawn",linked.Token);approaching=false;Follow();await pet.PlayOnce("lie_sleep",linked.Token,0,29);var clip=pet.Clips["lie_sleep"];pet.LoopSegment("lie_sleep",clip.LoopStart,clip.LoopEnd);}
                    else{await pet.PlayOnce("sit",linked.Token,0,pet.Clips["sit"].LoopStart);pet.SetState("sit_idle");}
                    await Task.Delay(ambient?engine.RestSeconds(sleep)*1000:Timeout.Infinite,linked.Token);
                    if(sleep)await pet.PlayReverse("lie_sleep",linked.Token,18);
                    else await pet.PlayOnce("sit",linked.Token,pet.Clips["sit"].LoopEnd+1);
                    follow.Stop();
                    var standing=pet.SurfaceStandingPosition(pet.Anchor,sleep?"lie_sleep":"sit",pet.FacingRight,leaving:!sleep);
                    pet.SetState("idle");pet.SetAnchor(standing);
                }
                finally
                {
                    follow.Stop();if(Editing)pet.EndDrag();pet.CalibrationDrag=null;pet.CalibrationFrame=null;
                    pet.SurfaceOffset=null;Editing=false;target=null;lifetime?.Dispose();lifetime=null;Changed?.Invoke();
                }
            }
            running=ambient?engine.RunIdleActivity(Rest):engine.RunActivity(Rest);
            if(!ambient)_=Observe(running);
        }
        finally{preparing=false;}
    }
    async Task Observe(Task task){try{await task;}catch(OperationCanceledException){}catch(Exception ex){pet.Say(ex.Message);}}
    void Follow()
    {
        if(target==null)return;
        if(pet.IsDragging&&!Editing){lifetime?.Cancel();return;}
        if(bounds(target) is Rect r&&Locate(r,out var p)){pet.SetAnchor(approaching?pet.SurfaceStandingPosition(p,"lie_sleep",pet.FacingRight):p);return;}
        lifetime?.Cancel();
    }
    public void BeginAdjust()
    {
        if(!Active)return;
        if(pet.AnimationKey is not ("sit_idle" or "lie_sleep"))throw new InvalidOperationException("等她坐稳或趴下后再调整。");
        if(sleeping&&pet.CurrentFrame<pet.Clips["lie_sleep"].LoopStart)throw new InvalidOperationException("等她趴稳后再调整。");
        draft=Saved;Editing=true;pet.CalibrationFrame=sleeping?pet.Clips["lie_sleep"].LoopStart:0;pet.CalibrationDrag=Nudge;Changed?.Invoke();
    }
    public void Nudge(Vector delta){if(Editing){draft=PoseCorrection.FromDisplay(Offset+delta,pet.FacingRight);Changed?.Invoke();}}
    public void Flip(){if(Active){pet.Face(!pet.FacingRight);Changed?.Invoke();}}
    public void Reset(){if(Editing){draft=new(0,0);Changed?.Invoke();}}
    public void Save()
    {
        if(!Editing)return;var old=Saved;Saved=draft;try{store.Save();}catch{Saved=old;throw;}CancelAdjust();
    }
    public void CancelAdjust(){if(!Editing)return;pet.EndDrag();Editing=false;pet.CalibrationDrag=null;pet.CalibrationFrame=null;Changed?.Invoke();}
    public async Task Leave(){lifetime?.Cancel();try{await running;}catch(OperationCanceledException){}}
    public void Dispose(){disposed=true;lifetime?.Cancel();follow.Stop();}
}
