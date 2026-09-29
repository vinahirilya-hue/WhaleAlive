using System.Windows;

namespace WhaleAlive;

// Corrections are stored in local display units for the left-facing art; right mirrors X.
public sealed record PoseCorrection(double X, double Y)
{
    // Approved visual alignment, measured against the top of desktop icons.
    public static PoseCorrection DefaultSeat {get;} = new(-5,15);
    public static PoseCorrection DefaultSleep {get;} = new(6,16);
    public Vector ForDirection(bool right) => new(right ? -X : X,Y);
    public static PoseCorrection FromDisplay(Vector v,bool right) => new(Math.Clamp(right ? -v.X : v.X,-60,60),Math.Clamp(v.Y,-60,60));
}

public sealed class PoseCalibration : IDisposable
{
    readonly PetWindow pet;
    readonly Interaction engine;
    readonly CompanionStore store;
    readonly IDesktopSeatSource seats;
    TaskCompletionSource? completion;
    Task running = Task.CompletedTask;
    PoseCorrection draft = new(0,0);
    bool preparing;
    readonly bool nativeSeats;
    string pose="sit_idle";
    Point iconOrigin;
    bool Carrying => pose is "pickup" or "putdown";
    public string PoseName => pose=="pickup" ? "抱起" : pose=="putdown" ? "放下" : pose=="lie_sleep" ? "睡觉" : "坐下";
    public bool Active => completion != null;
    public string TargetName {get;private set;} = "";
    public Vector Offset => draft.ForDirection(pet.FacingRight);
    public bool FacingRight => pet.FacingRight;
    public event Action? Changed;
    public PoseCalibration(PetWindow pet,Interaction engine,CompanionStore store,IDesktopSeatSource? seats=null)
    {
        this.pet=pet;this.engine=engine;this.store=store;this.seats=seats??new DesktopSeats();
        nativeSeats=seats==null;pet.PoseOffset=RenderOffset;pet.CarryOffset=StandingOffset;
    }
    internal Vector RenderOffset(string clip,bool right)
    {
        bool sit = clip is "sit" or "sit_idle", sleep = clip == "lie_sleep";
        if(!sit&&!sleep)return new();
        var value=Active && !Carrying && sleep==(pose=="lie_sleep") ? draft : sleep ? store.Data.SleepCorrection : store.Data.SeatCorrection;
        return value.ForDirection(right);
    }
    PoseCorrection Saved(string key,bool right)=>key switch {"pickup"=>right ? store.Data.PickupRightCorrection??store.Data.PickupCorrection : store.Data.PickupCorrection,"putdown"=>right ? store.Data.PutdownRightCorrection??store.Data.PutdownCorrection : store.Data.PutdownCorrection,"lie_sleep"=>store.Data.SleepCorrection,_=>store.Data.SeatCorrection};
    void SetSaved(PoseCorrection value,bool right)
    {
        if(right&&Carrying){if(pose=="pickup")store.Data.PickupRightCorrection=value;else store.Data.PutdownRightCorrection=value;return;}
        switch(pose){case "pickup":store.Data.PickupCorrection=value;break;case "putdown":store.Data.PutdownCorrection=value;break;case "lie_sleep":store.Data.SleepCorrection=value;break;default:store.Data.SeatCorrection=value;break;}
    }
    internal Vector StandingOffset(string key,bool right)=>new Vector(right?-24:24,45)+(Active&&pose==key?draft:Saved(key,right)).ForDirection(right);
    void UpdateStanding(){if(Active&&Carrying)pet.SetAnchor(pet.CarryPosition(iconOrigin,pose,pet.FacingRight));}
    public Task Start(bool sleep)=>StartPose(sleep?"lie_sleep":"sit_idle");
    public Task StartCarry(bool puttingDown,string? iconId=null)=>StartPose(puttingDown?"putdown":"pickup",iconId);
    async Task StartPose(string key,string? iconId=null)
    {
        if(Active||preparing)throw new InvalidOperationException("请先保存或取消当前校准。");
        preparing=true;
        try
        {
            engine.Stop();await engine.WaitForIdle();
            var seat=seats.Available(pet).Where(s=>iconId==null||s.Id==iconId).OrderBy(s=>(s.Surface-pet.Anchor).Length).FirstOrDefault()
                ??throw new InvalidOperationException("先露出桌面上的图标，再右键桌宠开始校准。");
            pose=key;draft=Saved(pose,pet.FacingRight);TargetName=seat.Name;iconOrigin=seat.Surface;
            if(Carrying&&nativeSeats){using var shell=new ShellDesktop();iconOrigin=pet.FromPixels(shell.ToScreen(seat.IconX,seat.IconY));}
            running=engine.RunActivity(async ct=>
            {
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);completion=done;
                try
                {
                    pet.SetAnchor(Carrying?pet.CarryPosition(iconOrigin,pose,pet.FacingRight):seat.Surface);
                    pet.CalibrationFrame=pose=="pickup"?25:pose=="putdown"?33:pet.Clips[pose].LoopStart;pet.SetState(pose,true);
                    pet.CalibrationDrag=Nudge;
                    if(Carrying)pet.Say("");else pet.Say("保持姿势，拖到合适位置；右键保存。\n图标："+seat.Name);Changed?.Invoke();
                    while(!done.Task.IsCompleted)
                    {
                        await Task.WhenAny(done.Task,Task.Delay(250,ct));ct.ThrowIfCancellationRequested();
                        if(!seats.StillThere(seat))throw new InvalidOperationException("目标图标已移动或消失，校准已取消。");
                    }
                }
                finally
                {
                    pet.EndDrag();pet.CalibrationDrag=null;pet.CalibrationFrame=null;completion=null;Changed?.Invoke();
                }
            });
            if(running.IsCompleted)await running;
            else _=Observe(running);
        }
        finally {preparing=false;}
    }
    async Task Observe(Task task)
    {
        try {await task;} catch(OperationCanceledException) {} catch(Exception ex) {pet.Say(ex.Message);}
    }
    public void Nudge(Vector delta)
    {
        if(!Active)return;
        draft=PoseCorrection.FromDisplay(Offset+delta,pet.FacingRight);UpdateStanding();Changed?.Invoke();
    }
    public void Flip() {if(Active){pet.Face(!pet.FacingRight);UpdateStanding();Changed?.Invoke();}}
    public void Reset() {if(Active){draft=new(0,0);UpdateStanding();Changed?.Invoke();}}
    void SaveDraft()
    {
        bool right=pet.FacingRight;
        var previous=Saved(pose,right);
        var previousRight=pose=="pickup"?store.Data.PickupRightCorrection:store.Data.PutdownRightCorrection;
        SetSaved(draft,right);
        try {store.Save();}
        catch
        {
            SetSaved(previous,right);
            if(right&&Carrying){if(pose=="pickup")store.Data.PickupRightCorrection=previousRight;else store.Data.PutdownRightCorrection=previousRight;}
            throw;
        }
    }
    public void SaveAndSwitchDirection()
    {
        if(!Active||!Carrying)throw new InvalidOperationException("当前没有正在调整的抱放姿势。");
        SaveDraft();
        // Keep the same live calibration and icon. The pet itself can cover the
        // icon after saving, so ending and re-querying exposed icons loses it.
        bool nextRight=!pet.FacingRight;
        draft=Saved(pose,nextRight);pet.Face(nextRight);UpdateStanding();Changed?.Invoke();
    }
    public async Task Save()
    {
        if(!Active)return;
        SaveDraft();
        bool carry=Carrying;
        await Cancel();pet.Say(carry?"位置已保存，当前朝向会沿用。":"位置已保存，左右方向都会沿用。");
    }
    public async Task Cancel()
    {
        completion?.TrySetResult();try {await running;} catch(OperationCanceledException) {}
    }
    public void Dispose(){completion?.TrySetResult();pet.EndDrag();pet.CalibrationDrag=null;pet.CalibrationFrame=null;pet.PoseOffset=null;pet.CarryOffset=null;}
}
