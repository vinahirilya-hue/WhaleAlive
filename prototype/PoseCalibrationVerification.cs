using System.IO;
using System.Text.Json;
using System.Windows;
namespace WhaleAlive;

public static class PoseCalibrationVerification
{
    sealed class Seats(Point position) : IDesktopSeatSource
    {
        public bool Exists=true;
        public bool Exposed=true;
        public IReadOnlyList<DesktopSeat> Available(PetWindow pet)=>Exposed?[new("fixture","校准测试图标",position,0,0)]:[];
        public bool StillThere(DesktopSeat seat)=>Exists;
    }
    static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    public static async Task Run()
    {
        var defaults=new CompanionData();var legacy=JsonSerializer.Deserialize<CompanionData>("{\"Quiet\":true}")!;
        Check(defaults.SeatCorrection==new PoseCorrection(-5,15)&&defaults.SleepCorrection==new PoseCorrection(6,16),"Approved defaults missing");
        Check(defaults.WindowSeatCorrection==new PoseCorrection(-1,15)&&defaults.WindowSleepCorrection==new PoseCorrection(3,16),"Approved window defaults missing");
        Check(legacy.SeatCorrection==defaults.SeatCorrection&&legacy.SleepCorrection==defaults.SleepCorrection&&legacy.WindowSeatCorrection==defaults.WindowSeatCorrection&&legacy.WindowSleepCorrection==defaults.WindowSleepCorrection,"Old settings do not inherit approved defaults");
        Check(defaults.SeatCorrection.ForDirection(true)==new Vector(5,15)&&defaults.SleepCorrection.ForDirection(true)==new Vector(-6,16),"Default mirroring incorrect");
        var pet=new PetWindow();pet.Show();
        var file=Path.Combine(AppContext.BaseDirectory,"calibration-test-settings.json");
        var store=new CompanionStore(file);store.Data.SeatCorrection=new(0,0);store.Data.SleepCorrection=new(0,0);store.Save();
        var seats=new Seats(pet.Anchor);
        var engine=new Interaction(pet,Path.Combine(AppContext.BaseDirectory,"calibration-test-history.json"),seats);
        using var calibration=new PoseCalibration(pet,engine,store,seats);
        var checks=new List<string>{"approved-defaults-legacy-settings-and-right-mirroring"};
        try
        {
            await Task.Delay(100);
            var pointer=pet.ToPixels(new(700,450));pet.BeginDragAt(pointer);await Task.Delay(150);pet.UpdateLayout();
            Check((pet.RenderedContactScreen-pointer).Length<2,$"Drag origin mismatch: {pet.RenderedContactScreen} vs {pointer}");pet.EndDrag();
            pet.Face(false);await calibration.Start(false);
            Check(calibration.Active&&engine.Busy&&pet.AnimationKey=="sit_idle","Calibration did not hold sitting pose");
            var anchor=pet.Anchor;var frame=pet.CurrentFrame;await Task.Delay(150);Check(pet.CurrentFrame==frame,"Calibration frame moved");
            var start=pet.PointToScreen(new Point(140,160));pet.BeginDragAt(start);
            pet.MoveDragTo(pet.PointToScreen(new Point(149,156)));pet.EndDrag();await Task.Delay(70);
            Check(pet.Anchor==anchor&&pet.AnimationKey=="sit_idle"&&calibration.Offset==new Vector(9,-4),"Drag changed anchor/pose or wrong delta");
            checks.Add("held-pose-drag-and-fixed-icon-anchor");
            calibration.Flip();Check(calibration.Offset==new Vector(-9,-4),"Right direction did not mirror");
            await calibration.Save();Check(!engine.Busy&&!calibration.Active,"Save left active action");
            Check(new CompanionStore(file).Data.SeatCorrection==new PoseCorrection(9,-4),"Correction not persisted");
            Check(calibration.RenderOffset("sit",false)==new Vector(9,-4)&&calibration.RenderOffset("sit_idle",true)==new Vector(-9,-4),"Seated corrections not shared");
            Check(calibration.RenderOffset("walk",true)==new Vector()&&calibration.RenderOffset("idle",false)==new Vector(),"Correction changed unrelated poses");
            checks.Add("persistent-sit-offset-and-right-mirror-only-for-seated-clips");
            await calibration.Start(false);calibration.Nudge(new(5,6));await calibration.Cancel();
            Check(store.Data.SeatCorrection==new PoseCorrection(9,-4),"Cancel saved preview");
            await calibration.Start(false);calibration.Reset();await calibration.Cancel();
            Check(calibration.RenderOffset("sit_idle",false)==new Vector(9,-4),"Reset preview persisted without Save");
            checks.Add("cancel-and-reset-preview-rollback");
            await calibration.Start(true);calibration.Nudge(new(7,3));await calibration.Save();
            Check(store.Data.SleepCorrection==new PoseCorrection(-7,3)&&store.Data.SeatCorrection==new PoseCorrection(9,-4),"Sleep changed sit or wrong direction");
            Check(calibration.RenderOffset("lie_sleep",true)==new Vector(7,3),"Sleep render offset missing");
            checks.Add("independent-sleep-calibration");
            store.Data.PickupCorrection=new(0,0);store.Data.PutdownCorrection=new(0,0);
            store.Data.PickupRightCorrection=null;store.Data.PutdownRightCorrection=null;
            pet.Face(false);await calibration.StartCarry(false);
            Check(pet.AnimationKey=="pickup"&&pet.CurrentFrame==25,"Pickup contact frame not held");
            var pickupBase=pet.Anchor;calibration.Nudge(new(8,-6));
            Check(pet.Anchor==pickupBase+new Vector(8,-6),"Carry nudge failed to move standing anchor");
            calibration.Flip();Check(calibration.Offset==new Vector(-8,-6),"Carry preview failed to mirror");calibration.Flip();await calibration.Save();
            var savedOrigin=new Point(350,400);
            Check(pet.CarryPosition(savedOrigin,"pickup",false)==new Point(382,439)&&pet.CarryPosition(savedOrigin,"pickup",true)==new Point(318,439),"Saved pickup not used by carry approach");
            await calibration.StartCarry(true);Check(pet.CurrentFrame==33&&pet.AnimationKey=="putdown","Putdown contact frame not held");
            calibration.Nudge(new(4,7));await calibration.Save();
            var reloaded=new CompanionStore(file).Data;
            Check(reloaded.PickupCorrection==new PoseCorrection(8,-6)&&reloaded.PutdownCorrection==new PoseCorrection(4,7),"Independent carry corrections not persisted");
            await calibration.StartCarry(false);calibration.Nudge(new(20,10));await calibration.Cancel();
            Check(store.Data.PickupCorrection==new PoseCorrection(8,-6)&&store.Data.SeatCorrection==new PoseCorrection(9,-4)&&store.Data.SleepCorrection==new PoseCorrection(-7,3),"Carry cancellation affected saved offsets");
            checks.Add("carry-contact-frames-independent-persistence-mirroring-and-cancel");
            pet.Face(true);await calibration.StartCarry(false);calibration.Reset();calibration.Nudge(new(21,2));await calibration.Save();
            await calibration.StartCarry(true);calibration.Reset();calibration.Nudge(new(18.4,10.4));await calibration.Save();
            reloaded=new CompanionStore(file).Data;
            Check(reloaded.PickupCorrection==new PoseCorrection(8,-6)&&reloaded.PutdownCorrection==new PoseCorrection(4,7),"Right calibration overwrote left values");
            Check(reloaded.PickupRightCorrection==new PoseCorrection(-21,2)&&reloaded.PutdownRightCorrection==new PoseCorrection(-18.4,10.4),"Right corrections not persisted independently");
            Check((pet.CarryPosition(savedOrigin,"pickup",true)-new Point(347,447)).Length<0.001,"Right pickup did not use independent offset");
            Check((pet.CarryPosition(savedOrigin,"putdown",true)-new Point(344.4,455.4)).Length<0.001,"Right putdown did not use independent offset");
            pet.Face(false);await calibration.StartCarry(false);calibration.Reset();calibration.Nudge(new(21,0));await calibration.Save();
            await calibration.StartCarry(true);calibration.Reset();calibration.Nudge(new(18.6,10.4));await calibration.Save();
            Check((pet.CarryPosition(savedOrigin,"pickup",false)-new Point(395,445)).Length<0.001,"Left pickup did not use second calibration");
            Check((pet.CarryPosition(savedOrigin,"putdown",false)-new Point(392.6,455.4)).Length<0.001,"Left putdown did not use second calibration");
            Check((pet.CarryPosition(savedOrigin,"pickup",true)-new Point(347,447)).Length<0.001&&(pet.CarryPosition(savedOrigin,"putdown",true)-new Point(344.4,455.4)).Length<0.001,"Second left calibration overwrote right placement");
            reloaded=new CompanionStore(file).Data;
            Check(reloaded.PickupRightCorrection==new PoseCorrection(-21,2)&&reloaded.PutdownRightCorrection==new PoseCorrection(-18.4,10.4),"Left save overwrote right persistence");
            pet.Face(true);await calibration.StartCarry(false);calibration.Nudge(new(7,3));await calibration.Cancel();
            Check(store.Data.PickupRightCorrection==new PoseCorrection(-21,2),"Cancel overwrote right correction");
            checks.Add("two-direction-carry-calibration-persistence-real-positions-and-cancel");
            pet.Face(false);await calibration.StartCarry(true,"fixture");calibration.Nudge(new(2,-1));
            var savedLeft=PoseCorrection.FromDisplay(calibration.Offset,false);
            seats.Exposed=false;
            calibration.SaveAndSwitchDirection();
            Check(calibration.Active&&engine.Busy&&pet.FacingRight&&pet.AnimationKey=="putdown"&&pet.CalibrationFrame==33,"Saving left ended calibration instead of switching right");
            Check(calibration.Offset==store.Data.PutdownRightCorrection!.ForDirection(true),"Switch did not load the independent right correction");
            Check(store.Data.PutdownCorrection==savedLeft,"Switch did not save the current direction");
            calibration.Nudge(new(3,2));var savedRight=PoseCorrection.FromDisplay(calibration.Offset,true);
            calibration.SaveAndSwitchDirection();
            Check(calibration.Active&&!pet.FacingRight&&calibration.Offset==savedLeft.ForDirection(false),"Switching back lost the left correction");
            Check(store.Data.PutdownRightCorrection==savedRight,"Switching back did not save the right correction");
            await calibration.Save();seats.Exposed=true;
            reloaded=new CompanionStore(file).Data;
            Check(!engine.Busy&&!calibration.Active&&reloaded.PutdownCorrection==savedLeft&&reloaded.PutdownRightCorrection==savedRight,"Finish did not persist both switch adjustments");
            checks.Add("save-switch-stays-active-with-occluded-icon-and-preserves-both-directions");
            var previousRest=engine.RestSeconds;var requestedRest=new List<bool>();
            engine.RestSeconds=sleep=>{requestedRest.Add(sleep);return 1;};
            var speeds=pet.Clips.ToDictionary(c=>c.Key,c=>c.Value.Fps);
            try
            {
                foreach(var key in new[]{"sit","sit_idle","sleep","yawn","lie_sleep"})pet.Clips[key].Fps=120;
                foreach(var key in new[]{"sit","sleep"})
                {
                    pet.SetAnchor(seats.Available(pet)[0].Surface);
                    var rest=engine.IdleAction(key);Check(await Task.WhenAny(rest,Task.Delay(12000))==rest,"Icon rest ignored selected duration");await rest;
                    Check(!engine.AmbientBusy&&engine.SeatedIcon==null,"Icon rest failed to finish");
                }
                Check(requestedRest.SequenceEqual(new[]{false,true}),"Icon sit/sleep did not use their separate durations");
            }
            finally{engine.RestSeconds=previousRest;foreach(var (key,fps) in speeds)pet.Clips[key].Fps=fps;}
            checks.Add("separate-icon-sit-and-sleep-durations-finish-naturally");
            await VerifyPerch(pet,engine,store,checks);
            await calibration.Start(true);engine.Stop();await engine.WaitForIdle();
            Check(!calibration.Active&&pet.CalibrationFrame==null&&pet.CalibrationDrag==null,"Stop did not clean calibration");
            await calibration.Start(true);seats.Exists=false;await Task.Delay(400);
            Check(!calibration.Active&&!engine.Busy,"Missing icon did not cancel");
            Check(PoseCorrection.FromDisplay(new(200,-200),false)==new PoseCorrection(60,-60),"Offset limits");
            checks.Add("stop-missing-icon-cleanup-and-bounds");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"calibration-verification.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{engine.Stop();pet.Close();}
    }
    static async Task VerifyPerch(PetWindow pet,Interaction engine,CompanionStore store,List<string> checks)
    {
        store.Data.WindowSeatCorrection=new(0,0);store.Data.WindowSleepCorrection=new(0,0);
        var work=DisplayGeometry.WorkArea(pet,pet.Anchor);var top=pet.ToPixels(new Point(work.Left+240,work.Top+300));
        Rect? rect=new Rect(top.X,top.Y,420,300);var window=new PetDesktopWindow(1,1,"测试窗口",rect.Value);
        using var perch=new WindowPerch(pet,engine,store,_=>rect,()=>[window]);
        async Task Until(Func<bool> condition){var limit=DateTime.UtcNow.AddSeconds(12);while(!condition()){Check(DateTime.UtcNow<limit,"Perch transition timeout");await Task.Delay(30);}}
        pet.Face(false);await perch.Start(window,false);await Until(()=>pet.AnimationKey=="sit_idle");
        var before=pet.Anchor;rect=new Rect(top.X+50,top.Y+25,520,300);await Task.Delay(100);
        Check(pet.Anchor.X>before.X&&Math.Abs((pet.Anchor.Y-before.Y)-(pet.FromPixels(new(0,25)).Y))<1,"Perch did not follow window move/resize");
        perch.BeginAdjust();perch.Nudge(new(11,-3));perch.Flip();Check(perch.Offset==new Vector(-11,-3),"Window correction not mirrored");perch.Save();
        Check(store.Data.WindowSeatCorrection==new PoseCorrection(11,-3)&&!perch.Editing&&perch.Active,"Window seat save failed");
        perch.BeginAdjust();perch.Nudge(new(4,5));perch.CancelAdjust();Check(store.Data.WindowSeatCorrection==new PoseCorrection(11,-3),"Window cancel saved draft");
        rect=null;await Until(()=>!perch.Active&&!engine.Busy);Check(pet.SurfaceOffset==null&&pet.CalibrationDrag==null,"Missing window left overrides");
        checks.Add("window-seat-follow-resize-calibration-mirror-and-disappearance");
        rect=window.Bounds;await perch.Start(window,true);await Until(()=>pet.AnimationKey=="lie_sleep"&&pet.CurrentFrame>=pet.Clips["lie_sleep"].LoopStart);
        perch.BeginAdjust();perch.Nudge(new(-6,4));perch.Save();
        Check(store.Data.WindowSleepCorrection==new PoseCorrection(6,4)&&store.Data.WindowSeatCorrection==new PoseCorrection(11,-3),"Window sleep/seat offsets not independent");
        pet.BeginDragAt(pet.ToPixels(pet.Anchor));await Until(()=>!perch.Active);Check(pet.AnimationKey=="lifted","Leaving window on lift overwrote lifted pose");pet.EndDrag();
        Check(pet.SurfaceOffset==null&&!perch.Editing,"Lift failed to clean window state");
        checks.Add("window-yawn-sleep-calibration-and-lift-detachment");
        rect=null;Check(!await perch.TryIdle(),"Ambient perch accepted missing window");
        rect=new Rect(top.X,pet.ToPixels(new Point(work.Left,work.Top+20)).Y,420,300);
        Check(!await perch.TryIdle(),"Ambient perch accepted edge without headroom");
        rect=window.Bounds;
        var fractions=new List<double>();
        for(int i=0;i<32;i++)
        {
            Check(perch.TryIdleFraction(rect.Value,out var selected),"Usable window edge rejected");fractions.Add(selected);
            var point=pet.FromPixels(new(rect.Value.Left+rect.Value.Width*selected,rect.Value.Top));
            Check(point.X>work.Left+100&&point.X<work.Right-100,"Random perch escaped screen margins");
            Check(selected>0&&selected<1,"Random perch escaped its window edge");
        }
        Check(fractions.Distinct().Count()>1,"Window edge position is fixed");
        checks.Add("random-window-edge-position-with-screen-and-corner-margins");
        engine.AllowSleep=false;
        var restSeconds=engine.RestSeconds;engine.RestSeconds=_=>120;
        var idleRest=perch.TryIdle();await Until(()=>pet.AnimationKey=="sit_idle");
        Check(perch.Active&&engine.AmbientBusy&&!engine.Busy,"Random perch did not use cancellable idle channel");
        Check(!await perch.TryIdle(),"Second random perch started while busy");
        engine.SetAmbient("think");Check(await idleRest,"Random perch did not report activity");
        Check(!perch.Active&&!engine.AmbientBusy&&pet.SurfaceOffset==null&&pet.State=="think","DSH did not interrupt ambient perch cleanly");
        Check(!await perch.TryIdle(),"Window rest started during DSH work");
        engine.SetAmbient("idle");
        engine.RestSeconds=_=>1;
        idleRest=perch.TryIdle();await Until(()=>pet.AnimationKey=="sit_idle");
        var timeout=Task.Delay(5000);Check(await Task.WhenAny(idleRest,timeout)==idleRest,"Ambient perch ignored selected duration");
        Check(await idleRest&&!perch.Active&&!engine.AmbientBusy&&pet.SurfaceOffset==null&&pet.State=="idle","Timed rest did not clean up");
        engine.AllowSleep=true;engine.RestSeconds=restSeconds;
        checks.Add("random-window-rest-filtering-sleep-switch-dsh-interruption-and-timed-exit");
        var host=new Window{Title="窗沿原生边界测试",Left=work.Left+200,Top=work.Top+300,Width=420,Height=250,ShowActivated=false,ShowInTaskbar=false};host.Show();
        try
        {
            var actual=new PetDesktopWindow(new System.Windows.Interop.WindowInteropHelper(host).Handle.ToInt64(),(uint)Environment.ProcessId,host.Title,new());
            Check(DesktopWindows.EdgeBounds(actual)!=null,"Native window edge unavailable");
            host.WindowState=WindowState.Minimized;await Task.Delay(100);Check(DesktopWindows.EdgeBounds(actual)==null,"Minimized host still eligible");
            host.WindowState=WindowState.Normal;host.Close();Check(DesktopWindows.EdgeBounds(actual)==null,"Closed host still eligible");
            checks.Add("native-window-bounds-minimize-close");
        }
        finally{host.Close();}
    }
}
