using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace WhaleAlive;
public static class CompanionVerification
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    public static async Task Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"WhaleAlive-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var results=new List<object>();var store=new CompanionStore(Path.Combine(root,"companion.json"));
        var heart=AutonomousPlay.PlanTargets(new Rect(-1600,0,1600,1000),[],10,true);
        Check(heart.Count==10&&heart.Distinct().Count()==10&&heart.All(p=>p.X<0),"heart layout or negative monitor coordinates");
        var small=AutonomousPlay.PlanTargets(new Rect(0,0,400,400),[],10,true);Check(small.Count==1,"insufficient space must fall back to single icon");
        Check(AutonomousPlay.PlanTargets(new Rect(0,0,1600,1000),[],10,true,visible:_=>false).Count==0,"covered desktop accepted");
        var blocked=AutonomousPlay.PlanTargets(new Rect(0,0,400,400),[new(70,90),new(175,90),new(280,90),new(70,205),new(175,205),new(280,205)],10,true);Check(blocked.Count==0,"occupied cells accepted");
        var moves=new[]{new MoveEntry("a","a",0,0,100,100,"completed",DateTime.Now,"s","one"),new MoveEntry("a","a",100,100,200,200,"completed",DateTime.Now,"s","two"),new MoveEntry("b","b",10,10,50,50,"completed",DateTime.Now)};
        var restore=AutonomousPlay.RestorePlan(moves,null);Check(restore.Length==1&&restore[0].X==0&&restore[0].ExpectedX==200,"whole round restoration includes manual changes or loses baseline");
        Check(AutonomousPlay.RestorePlan(moves,"two")[0].X==100,"last activity restoration wrong baseline");
        Check(!AutonomousPlay.CanRestore(restore[0],new DesktopIcon("a","a",300,200,null)),"manual move must be preserved");
        Check(AutonomousPlay.CanRestore(restore[0],new DesktopIcon("a","a",200,200,null)),"unchanged pet move must be restorable");
        var interrupted=AutonomousPlay.RestorePlan(moves.Append(new MoveEntry("a","a",350,350,450,450,"completed",DateTime.Now,"s","three")),null);
        Check(interrupted[0].X==350&&interrupted[0].Entries.Length==1,"later pet play must not erase an intervening manual arrangement");
        results.Add(new{test="autonomy-pattern-space-occlusion-and-restoration-baselines",pass=true});
        var todo=store.AddNote("回归测试泡泡");Check(store.Complete(todo.Id),"complete first time");Check(!store.Complete(todo.Id),"complete idempotency");
        store=new CompanionStore(Path.Combine(root,"companion.json"));Check(store.Data.Todos.Single().Done,"notes survive restart");
        var legacyPath=Path.Combine(root,"legacy.json");File.WriteAllText(legacyPath,"""{"Weather":"snow","FocusUntil":"2027-01-01T00:00:00Z","Memories":[{"Text":"kept"}]}""");
        var legacy=new CompanionStore(legacyPath);legacy.Save();legacy=new CompanionStore(legacyPath);
        Check(legacy.Data.LegacyData?["Weather"].GetString()=="snow"&&legacy.Data.LegacyData["Memories"].GetArrayLength()==1&&!legacy.Data.Quiet,"retired data preserved without restoring behavior");
        string file=Path.Combine(root,"交付物.txt");File.WriteAllText(file,"fixture");var saved=store.KeepFile(file);Check(store.KeepFile(file)==saved&&store.Data.Files.Count==1,"file deduplication");Check(File.ReadAllText(file)=="fixture","file contents changed");
        results.Add(new{test="notes-files-persistence-and-retired-data-compatibility",pass=true});
        var pet=new PetWindow();pet.Show();pet.SetAnchor(new(800,600));var engine=new Interaction(pet,Path.Combine(root,"history.json"));using var features=new CompanionFeatures(pet,engine,store);
        Window? fixture=null,panelHost=null;CompanionPanel? panel=null;
        try{
            Check(!features.AllowWindowMove,"window permission default");
            Check(!features.Autonomy.Enabled&&!await features.Autonomy.TryPlay(true),"autonomy must require opt-in");
            var work=engine.RunActivity(async ct=>{pet.SetState("pull");await Task.Delay(5000,ct);});await Task.Delay(80);Check(engine.Busy,"activity busy");
            try{await engine.RunActivity(_=>Task.CompletedTask);throw new Exception("concurrent activity accepted");}catch(InvalidOperationException){}
            var version=engine.StopVersion;engine.Stop();try{await work;throw new Exception("cancel ignored");}catch(OperationCanceledException){}Check(engine.StopVersion==version+1&&!engine.Busy&&pet.State=="idle","activity cancel cleanup");
            results.Add(new{test="exclusive-actions-concurrent-rejection-stop-cleanup",pass=true});
            int executed=0;long stopSequence=0;
            try{await CompanionFeatures.ExecutePlan([new("a","a",0,0,new()),new("b","b",0,0,new())],()=>stopSequence,_=>{executed++;stopSequence++;return Task.CompletedTask;});throw new Exception("batch stop ignored");}catch(OperationCanceledException){}
            Check(executed==1,"next plan item executed after stop");
            using(var shell=new ShellDesktop()){
                var beforeIcons=shell.List(false);var names=beforeIcons.Take(3).Select(i=>i.Id).ToArray();features.PlanIcons(names);
                Check(features.Plan.Count==3&&features.Plan.Select(p=>p.Destination).Distinct().Count()==3,"plan destinations overlap");
                try{await features.ApplyPlan();throw new Exception("plan permission bypass");}catch(InvalidOperationException){}
                Check(beforeIcons.Select(i=>(i.Id,i.X,i.Y)).SequenceEqual(shell.List(false).Select(i=>(i.Id,i.X,i.Y))),"planning changed desktop icons");
            }
            try{await features.GuideCursor("anything");throw new Exception("cursor permission bypass");}catch(InvalidOperationException){}
            results.Add(new{test="plan-read-only-unique-targets-permission-and-stop-between-steps",pass=true});
            fixture=new Window{Title="WhaleAlive verification fixture",Width=300,Height=200,Left=300,Top=250,ShowInTaskbar=false};fixture.Show();await Task.Delay(100);
            var target=new PetDesktopWindow((long)new WindowInteropHelper(fixture).Handle,(uint)Environment.ProcessId,fixture.Title,new());
            var before=DesktopWindows.Current(target)!.Value;
            try{await features.MoveWindow(target,1);throw new Exception("permission bypass");}catch(InvalidOperationException){}
            Check(DesktopWindows.Current(target)==before,"denied move changed window");
            pet.SetAnchor(pet.FromPixels(new(before.Left,before.Top+100)));features.AllowWindowMove=true;await features.MoveWindow(target,1);
            var moved=DesktopWindows.Current(target)!.Value;Check(moved.Left>before.Left,"window failed to move");
            var reloaded=new CompanionStore(Path.Combine(root,"companion.json"));Check(reloaded.Data.WindowMoves.Count==1,"window undo persistence");
            features.UndoWindow();Check((DesktopWindows.Current(target)!.Value.TopLeft-before.TopLeft).Length<2,"window undo mismatch");
            await features.MoveWindow(target,1);var latest=DesktopWindows.Current(target)!.Value;DesktopWindows.Move(target,latest.TopLeft+new Vector(20,0));
            try{features.UndoWindow();throw new Exception("manual change overwritten");}catch(InvalidOperationException){}
            results.Add(new{test="own-fixture-window-permission-move-undo-persistence-conflict",pass=true});
            var area=DisplayGeometry.WorkArea(pet,pet.Anchor);pet.SetAnchor(new(area.Right-150,area.Bottom-80));await features.Deliver(file);
            Check(store.Data.Files.Any(f=>f.Path==file&&f.Kind=="delivery")&&File.ReadAllText(file)=="fixture","delivery changed file or lost shelf entry");
            results.Add(new{test="artifact-delivery-keeps-source-and-records-location",pass=true});
            await VerifyAutonomousRestore(root,pet);results.Add(new{test="own-icon-autonomous-journal-restart-last-all-restore-and-manual-conflict",pass=true});
            features.AddTodo("点一下泡泡，完成测试");
            panel=new CompanionPanel(features,engine,()=>[]);panelHost=new Window{Content=panel,Width=730,Height=740,Background=Brushes.DarkSlateGray};panelHost.Show();await Task.Delay(150);panel.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)panel.ActualWidth,(int)panel.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(panel);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(AppContext.BaseDirectory,"companion-panel.png")))png.Save(stream);
            panel.ShowAutonomy();panel.UpdateLayout();var settingsBitmap=new RenderTargetBitmap((int)panel.ActualWidth,(int)panel.ActualHeight,96,96,PixelFormats.Pbgra32);settingsBitmap.Render(panel);var settingsPng=new PngBitmapEncoder();settingsPng.Frames.Add(BitmapFrame.Create(settingsBitmap));using(var stream=File.Create(Path.Combine(AppContext.BaseDirectory,"autonomy-panel.png")))settingsPng.Save(stream);
            results.Add(new{test="companion-controls-layout-and-overlays",pass=true});
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"companion-verification.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{engine.Stop();panel?.Dispose();panelHost?.Close();fixture?.Close();pet.Close();}
        // This unique test directory contains only files created above.
        if(!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(root).StartsWith("WhaleAlive-tests-"))throw new InvalidOperationException("Unexpected test cleanup path");
        Directory.Delete(root,true);
    }
    static async Task VerifyAutonomousRestore(string root,PetWindow pet)
    {
        using var shell=new ShellDesktop();Check(!shell.AutoArrange,"fixture requires free icon positioning");
        string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"WhaleAlive-autonomy-test-"+Guid.NewGuid().ToString("N")+".txt");
        DesktopIcon? original=null;var before=shell.List(false);var speeds=pet.Clips.ToDictionary(k=>k.Key,k=>k.Value.Fps);
        try{
            File.WriteAllText(file,"Own temporary autonomous-restore fixture.");
            for(int n=0;n<30;n++){original=shell.List(false).SingleOrDefault(i=>i.Id==file);if(original!=null)break;await Task.Delay(200);}
            Check(original!=null,"fixture enumeration failed");
            foreach(var c in pet.Clips.Values)c.Fps=120;
            string history=Path.Combine(root,"autonomy-history.json");var mover=new Interaction(pet,history);
            var start=pet.FromPixels(shell.ToScreen(original!.X,original.Y));pet.SetAnchor(start+new Vector(24,45));
            await mover.Carry(file,"fixture",true,targetPosition:start+new Vector(110,0),autonomousPermission:()=>true,sessionId:"fixture",activityId:"one");
            var first=shell.List(false).Single(i=>i.Id==file);
            await mover.Carry(file,"fixture",true,targetPosition:start+new Vector(220,0),autonomousPermission:()=>true,sessionId:"fixture",activityId:"two");
            var recovered=new Interaction(pet,history);using var companion=new CompanionFeatures(pet,recovered,new CompanionStore(Path.Combine(root,"auto-store.json")));
            await companion.Autonomy.Restore(false);var last=shell.List(false).Single(i=>i.Id==file);Check(last.X==first.X&&last.Y==first.Y,"last activity failed to restore");
            var changed=await shell.MoveSettled(file,first.X+40,first.Y+40);
            await companion.Autonomy.Restore(true);var preserved=shell.List(false).Single(i=>i.Id==file);Check(preserved.X==changed.X&&preserved.Y==changed.Y,"manual adjustment overwritten");
            await shell.MoveSettled(file,first.X,first.Y);
            await companion.Autonomy.Restore(true);var restored=shell.List(false).Single(i=>i.Id==file);Check(restored.X==original.X&&restored.Y==original.Y,"whole round original position not restored");
            Check(recovered.History.All(h=>h.Status=="undone"),"restored journal remains active");
            var after=shell.List(false);Check(before.All(i=>after.Any(j=>j.Id==i.Id&&j.X==i.X&&j.Y==i.Y)),"fixture test altered other icons");
        }finally{
            foreach(var c in pet.Clips)c.Value.Fps=speeds[c.Key];
            if(original!=null)await shell.MoveSettled(file,original.X,original.Y);
            if(Path.GetFileName(file).StartsWith("WhaleAlive-autonomy-test-")&&File.Exists(file))File.Delete(file);
        }
    }
}
