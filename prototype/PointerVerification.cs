using System.IO;
using System.Text.Json;
using System.Windows;
namespace WhaleAlive;

public static class PointerVerification
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static async Task Run()
    {
        var checks=new List<string>();var now=DateTimeOffset.UtcNow;var progress=new DshProgress(()=>now);
        Check(progress.Text.Contains("还没连接"),"Missing disconnected state");
        foreach(var (stage,text) in new[]{("think","正在思考"),("working","正在执行"),("wait","等待你确认"),("celebrate","本轮任务已完成"),("error","执行遇到问题")})
        {progress.Receive(stage);Check(progress.Text.Contains(text),"Wrong DSH stage: "+stage);}
        progress.Receive("celebrate");progress.Receive("idle");Check(progress.Text.Contains("最近一轮已完成"),"Completion lost on heartbeat");
        progress.Receive("think");progress.Receive("idle");Check(!progress.Text.Contains("已完成"),"Aborted turn retained stale success");
        now=now.AddSeconds(9);Check(progress.Text.Contains("连接暂时中断"),"Stale state displayed as live progress");progress.Receive("working");
        checks.Add("dsh-stages-completion-idle-disconnect-and-reconnect");
        var pet=new PetWindow();pet.Show();int clicks=0,grabs=0;
        pet.Clicked+=()=>{clicks++;pet.ShowTaskProgress(progress.Text);};pet.Grabbed+=()=>grabs++;
        try
        {
            await Task.Delay(70);var p=pet.ToPixels(pet.Anchor+new Vector(0,-50));var anchor=pet.Anchor;var surface=pet.PointToScreen(new Point());
            pet.BeginPrimaryPress(p);await Task.Delay(80);pet.CompletePrimaryPress();
            Check(clicks==1&&grabs==0&&!pet.IsDragging&&pet.Anchor==anchor&&pet.DisplayedSpeech.Contains("正在执行"),"Short click lifted or missed progress");
            await Task.Delay(350);Check(!pet.IsDragging&&grabs==0,"Released click later became a lift");
            pet.Say("普通待机台词");Check(pet.DisplayedSpeech.Contains("正在执行"),"Ambient speech replaced requested progress");
            progress.Receive("wait");pet.RefreshTaskProgress(progress.Text);Check(pet.DisplayedSpeech.Contains("等待你确认"),"Visible progress did not update");
            checks.Add("short-click-only-shows-live-progress-without-moving-pet");
            pet.BeginPrimaryPress(p);await Task.Delay(100);pet.AdvancePrimaryPress();Check(!pet.IsDragging,"Hold threshold fired early");
            pet.MovePrimaryPress(p+new Vector(15,6));await Task.Delay(230);pet.AdvancePrimaryPress();
            Check(pet.IsDragging&&grabs==1&&!pet.SpeechVisible&&pet.AnimationKey=="lifted","Long press did not lift/hide bubble");
            pet.UpdateLayout();Check((pet.RenderedContactScreen-(p+new Vector(15,6))).Length<2,"Hold used outdated pointer");
            pet.MovePrimaryPress(p+new Vector(35,16));pet.CompletePrimaryPress();
            Check(clicks==1&&!pet.IsDragging&&pet.PointToScreen(new Point())==surface,"Drop clicked or moved native surface");
            checks.Add("hold-threshold-current-pointer-drag-and-drop-without-click");
            pet.BeginPrimaryPress(p);pet.CancelPrimaryPress();await Task.Delay(350);pet.AdvancePrimaryPress();pet.CompletePrimaryPress();
            Check(grabs==1&&clicks==1&&!pet.IsPressing,"Lost capture did not cancel pending input");
            pet.BeginPrimaryPress(p);pet.MovePrimaryPress(p+new Vector(30,0));pet.CompletePrimaryPress();Check(clicks==1,"Quick swipe became a click");
            checks.Add("cancelled-press-and-short-swipe-never-open-progress");
            Vector delta=new();pet.CalibrationDrag=v=>delta+=v;pet.SetState("pickup",true);
            pet.BeginPrimaryPress(p);Check(pet.IsDragging&&pet.AnimationKey=="pickup","Calibration no longer drags immediately");
            pet.MovePrimaryPress(p+new Vector(9,3));pet.CompletePrimaryPress();pet.CalibrationDrag=null;
            Check(delta.Length>0&&clicks==1&&grabs==1&&!pet.IsDragging,"Calibration triggered lift or progress");
            checks.Add("calibration-retains-immediate-position-drag");
            pet.ShowTaskProgress(progress.Text);await Task.Delay(3200);pet.RefreshTaskProgress(progress.Text);await Task.Delay(3100);
            Check(!pet.SpeechVisible,"Progress updates prevented automatic dismissal");
            checks.Add("progress-expires-even-with-heartbeat-updates");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"pointer-verification.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{pet.CancelPrimaryPress();pet.Close();}
    }
}
