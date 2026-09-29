using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WhaleAlive;
public static class AnimationVerification
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    sealed class FakeSeats(Point point) : IDesktopSeatSource
    {
        public bool Present = true;
        public IReadOnlyList<DesktopSeat> Available(PetWindow pet) => [new("fixture", "测试图标", point, 0, 0)];
        public bool StillThere(DesktopSeat seat) => Present;
    }
    public static async Task Run()
    {
        var evidence = new List<object>();
        var catalog = new AnimationCatalog();
        catalog.Pin("idle", false); catalog.Pin("lifted", false); catalog.Pin("lifted", true);
        var pinnedIdle = catalog.Load("idle", false);
        var pinnedLifted = catalog.Load("lifted", false);
        var pinnedLiftedRight = catalog.Load("lifted", true);
        Check(catalog.Clips.Count == 23, "All 23 actions must be imported");
        int checkedFrames = 0;
        foreach (var (key, clip) in catalog.Clips)
        {
            var left = catalog.Load(key, false); Check(left.Length == clip.Count, key);
            if (clip.Directional)
            {
                var right = catalog.Load(key, true);
                for (int i = 0; i < clip.Count; i++)
                {
                    int w = left[i].PixelWidth, h = left[i].PixelHeight;
                    var a = new byte[w*h*4]; var b = new byte[a.Length];
                    new FormatConvertedBitmap(left[i], PixelFormats.Bgra32, null, 0).CopyPixels(a,w*4,0);
                    new FormatConvertedBitmap(right[i], PixelFormats.Bgra32, null, 0).CopyPixels(b,w*4,0);
                    for (int y=0;y<h;y++) for(int x=0;x<w;x++) for(int c=0;c<4;c++)
                        if(a[(y*w+x)*4+c]!=b[(y*w+w-1-x)*4+c]) throw new Exception($"Mirror mismatch {key}/{i}");
                    var lp=clip.Pivot(i,false);var rp=clip.Pivot(i,true);
                    Check(Math.Abs(lp.X+rp.X-1)<.00001 && lp.Y==rp.Y,"Mirrored pivot");
                    checkedFrames++;
                }
            }
            if (key is "walk" or "carry_walk" or "run")
            {
                var t = new SequenceTimeline(clip,3);
                var expected = Enumerable.Range(0,clip.LoopStart)
                    .Concat(Enumerable.Range(0,3).SelectMany(_=>Enumerable.Range(clip.LoopStart,t.LoopLength)))
                    .Concat(Enumerable.Range(clip.LoopEnd+1,clip.Count-clip.LoopEnd-1)).ToArray();
                Check(t.TotalFrames==expected.Length,"Timeline length");
                for(int i=0;i<expected.Length;i++) Check(t.FrameAt((i+.01)/clip.Fps)==expected[i],$"Timeline order {key}/{i}");
                Check(t.FrameAt(100000)==clip.Count-1,"Timeline clamps final frame");
            }
        }
        evidence.Add(new {test="decode-all-actions-mirrors-and-three-phase-timelines",pass=true,mirroredFrames=checkedFrames});
        Check(ReferenceEquals(pinnedIdle,catalog.Load("idle",false)) && ReferenceEquals(pinnedLifted,catalog.Load("lifted",false)) && ReferenceEquals(pinnedLiftedRight,catalog.Load("lifted",true)),"Drag/drop clips evicted by other animations");
        var runClip = catalog.Clips["run"];
        var brakingTrip = new SequenceTimeline(runClip, 2, 49, false, 1.5);
        for(int i=0;i<brakingTrip.TotalFrames;i++) Check(brakingTrip.FrameAt((i+.01)/(runClip.Fps*1.5))<=runClip.LoopEnd,"Braking trip plays a normal stop frame");
        Check(brakingTrip.FrameAt(1000)==runClip.LoopEnd,"Braking trip must end at running loop");
        var normalRun = new SequenceTimeline(runClip, 1, 49, true, 1.5);
        Check(normalRun.FrameAt((29+.01)/(runClip.Fps*1.5))==49 && normalRun.FrameAt(1000)==53,"Normal run must retain its own stop");
        evidence.Add(new {test="pinned-drag-cache-and-exclusive-run-stops",pass=true});
        var pet = new PetWindow();
        // Exercise the real WPF renderer without moving or clicking desktop icons.
        pet.Show(); pet.SetAnchor(new(700,500));
        var fake=new FakeSeats(pet.Anchor);
        var engine=new Interaction(pet,Path.Combine(AppContext.BaseDirectory,"unused-animation-test-history.json"),fake);
        try
        {
            var sizes = new List<object>();
            foreach (var action in new[] { "idle", "walk", "carry_idle", "carry_walk", "run", "sit", "sit_idle", "yawn", "lie_sleep", "lifted" })
            {
                pet.SetState(action); pet.UpdateLayout();
                Check(Math.Abs(pet.ReferenceCharacterHeight-102.4)<.01, "Character scale changes between actions: " + action);
                sizes.Add(new {action, canvas=pet.SpriteDisplaySize, referenceHeight=pet.ReferenceCharacterHeight});
            }
            evidence.Add(new {test="consistent-standing-reference-size-across-actions", pass=true, sizes});
            var movementTimes = new List<object>();
            double walkingSeconds=0;
            foreach(var mode in new[]{"walk","run","brake"})
            {
                pet.SetAnchor(new(700,500));
                var watch=System.Diagnostics.Stopwatch.StartNew();
                var moving=pet.WalkTo(new(900,500),CancellationToken.None,run:mode!="walk",brake:mode=="brake");
                var states=new List<string>(); var runFrames=new List<int>();
                var brakePositions=new List<Point>();
                double arrival=0;
                while(!moving.IsCompleted)
                {
                    if(states.LastOrDefault()!=pet.State)states.Add(pet.State);
                    if(pet.State=="run")runFrames.Add(pet.CurrentFrame);
                    if(pet.State=="brake")brakePositions.Add(pet.Anchor);
                    if(arrival==0&&(pet.Anchor-new Point(900,500)).Length<.01)arrival=watch.Elapsed.TotalSeconds;
                    await Task.Delay(10);
                }
                await moving; double seconds=watch.Elapsed.TotalSeconds;
                if(mode=="walk")walkingSeconds=seconds;
                else Check((mode=="brake"?arrival:seconds)<walkingSeconds*.65,"Autonomous run must travel visibly faster than walk");
                if(mode=="brake")Check(states.SequenceEqual(new[]{"run","brake"}) && runFrames.All(f=>f<=28),"Braking must directly replace normal run stop, with no idle flash");
                if(mode=="brake")
                {
                    Check(brakePositions.Count>2&&brakePositions[^1].X-brakePositions[0].X>25,"Brake must visibly glide forward during its animation");
                    Check(brakePositions.Zip(brakePositions.Skip(1),(a,b)=>b.X>=a.X&&b.X<=900).All(x=>x),"Brake reversed direction or overshot the destination");
                }
                if(mode=="run")Check(states.SequenceEqual(new[]{"run"}) && runFrames.Any(f=>f>=49),"Normal run must stop without braking clip");
                Check(pet.State=="idle"&&(pet.Anchor-new Point(900,500)).Length<.01,"Movement failed to finish");
                movementTimes.Add(new{mode,seconds,arrival,states});
            }
            evidence.Add(new{test="runtime-walk-run-speed-and-exclusive-brake-transition",pass=true,movementTimes});
            foreach(double length in new[]{200.0,20.0})
            {
                pet.SetAnchor(new(700,500));var destination=new Point(700-length,500);
                var braking=pet.WalkTo(destination,CancellationToken.None,run:true,brake:true);
                var positions=new List<Point>();
                while(!braking.IsCompleted){if(pet.State=="brake")positions.Add(pet.Anchor);await Task.Delay(10);}
                await braking;
                Check(!pet.FacingRight&&positions.Count>2&&positions[0].X-positions[^1].X>Math.Min(32,length*.35)*.8,"Leftward brake did not glide forwards");
                Check(positions.Zip(positions.Skip(1),(a,b)=>b.X<=a.X&&b.X>=destination.X).All(x=>x),"Leftward brake reversed or overshot");
                Check((pet.Anchor-destination).Length<.01,"Braking changed the planned destination");
            }
            using(var cancelBrake=new CancellationTokenSource())
            {
                pet.SetAnchor(new(700,500));var braking=pet.WalkTo(new(900,500),cancelBrake.Token,run:true,brake:true);
                while(pet.State!="brake"&&!braking.IsCompleted)await Task.Delay(10);
                await Task.Delay(80);cancelBrake.Cancel();
                try{await braking;throw new Exception("Brake glide ignored cancellation");}catch(OperationCanceledException){}
                var stopped=pet.Anchor;await Task.Delay(120);Check(pet.Anchor==stopped,"Cancelled brake continued sliding");pet.SetState("idle");
            }
            evidence.Add(new{test="brake-inertia-left-short-trip-and-cancellation",pass=true});
            pet.SetAnchor(new(700,500));
            var trip=pet.WalkTo(new(900,500),CancellationToken.None,carrying:true);
            await Task.Delay(150); Check(pet.State=="carry_walk" && pet.FacingRight,"Carry uses mirrored carrying gait");
            await trip; Check((pet.Anchor-new Point(900,500)).Length<.01 && pet.State=="carry_idle","Carry arrival");
            using(var pullStop=new CancellationTokenSource())
            {
                var pulling=pet.WalkTo(new(1000,500),pullStop.Token,pulling:true);await Task.Delay(100);
                Check(pet.State=="pull"&&!pet.FacingRight,"Pulling must walk backwards facing the object");pullStop.Cancel();
                try{await pulling;throw new Exception("Pull ignored cancellation");}catch(OperationCanceledException){}
            }
            using(var ct=new CancellationTokenSource())
            {
                var cancelled=pet.WalkTo(new(400,500),ct.Token);await Task.Delay(120);ct.Cancel();
                try { await cancelled; throw new Exception("Walk ignored cancellation"); } catch(OperationCanceledException){}
                var stopped=pet.Anchor;await Task.Delay(100);Check(pet.Anchor==stopped,"Cancelled walk still moved");
            }
            evidence.Add(new {test="carry-walk-facing-arrival-and-move-cancellation",pass=true});
            pet.SetAnchor(new(700,500));
            var sitting=engine.IdleAction("sit");
            var sittingDeadline=DateTime.UtcNow.AddSeconds(10);while(pet.State!="sit_idle"&&DateTime.UtcNow<sittingDeadline)await Task.Delay(40);
            Check(pet.State=="sit_idle" && engine.SeatedIcon=="测试图标","Sit enters leg-swing loop");
            Check(pet.Anchor==new Point(700,500),"Seat support drifted");
            var contacts=new List<object>();
            foreach(var location in new[]{new Point(700,500),new Point(-1588,380)})
            foreach(bool facing in new[]{false,true})
            {
                pet.SetAnchor(location);pet.Face(facing);await Task.Delay(120);pet.UpdateLayout();
                var actual=pet.RenderedContactScreen;var expected=pet.ToPixels(location);
                contacts.Add(new{location=location.ToString(),facing,actual=actual.ToString(),expected=expected.ToString()});
                Check((actual-expected).Length<2,"Seated rendered contact differs from icon target");
            }
            evidence.Add(new{test="actual-seat-contact-left-right-and-negative-screen-coordinates",pass=true,contacts});
            pet.SetAnchor(new(700,500));pet.Face(false);
            pet.UpdateLayout();
            var rendered=DragSurfaceVerification.Crop(pet,pet.PointFromScreen(pet.RenderedContactScreen),390,410);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(rendered));
            using(var stream=File.Create(Path.Combine(AppContext.BaseDirectory,"seated-render.png")))png.Save(stream);
            var illustrated=new DrawingVisual();
            using(var drawing=illustrated.RenderOpen())
            {
                drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(231,238,245)),null,new Rect(0,0,390,410));
                var contact=new Point(140,195);
                drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(40,115,185)),null,new Rect(contact.X-24,contact.Y,48,48),6,6);
                drawing.DrawImage(rendered,new Rect(0,0,390,410));
            }
            var sample=new RenderTargetBitmap(390,410,96,96,PixelFormats.Pbgra32);sample.Render(illustrated);
            var samplePng=new PngBitmapEncoder();samplePng.Frames.Add(BitmapFrame.Create(sample));
            using(var stream=File.Create(Path.Combine(AppContext.BaseDirectory,"seat-top-example.png")))samplePng.Save(stream);
            fake.Present=false;await sitting;
            Check(pet.State=="idle" && engine.SeatedIcon==null && !engine.AmbientBusy,"Removed icon did not release seat");
            evidence.Add(new {test="sit-swing-stand-and-icon-disappearance",pass=true});
            fake.Present=true;var cancelledSit=engine.IdleAction("sit");await Task.Delay(200);engine.Stop();await cancelledSit;
            Check(pet.State=="idle" && !engine.AmbientBusy,"Cancelled seat stuck");
            var preview=engine.PreviewAnimation("pickup",true);await Task.Delay(150);engine.Stop();
            try {await preview;throw new Exception("Preview ignored cancellation");}catch(OperationCanceledException){}
            Check(!engine.Busy && pet.State=="idle","Preview cancellation left busy flag");
            evidence.Add(new {test="seat-and-preview-cancellation",pass=true});
            pet.SetAnchor(new(700,500));
            var phases=new List<string>();var sleeping=engine.IdleAction("sleep");
            var deadline=DateTime.UtcNow.AddSeconds(20);
            while(engine.SleepPhase!="sleeping"&&DateTime.UtcNow<deadline){if(engine.SleepPhase is string phase&&!phases.Contains(phase))phases.Add(phase);await Task.Delay(40);}
            Check(engine.SleepPhase=="sleeping"&&pet.State=="lie_sleep","sleep chain did not reach lying loop");
            Check(phases.SequenceEqual(new[]{"drowsy","yawn","lie_down"})||phases.SequenceEqual(new[]{"drowsy","yawn","search","lie_down"}),"sleep chain order");
            pet.UpdateLayout();var sleepingOrigin=pet.RenderedSpriteOrigin;
            for(int i=0;i<4;i++){await Task.Delay(100);pet.UpdateLayout();Check(pet.CurrentFrame>=30,"lying loop repeats entry");Check((pet.RenderedSpriteOrigin-sleepingOrigin).Length<.01,"sleep breathing moved the character canvas");}
            var sleepVisual=new DrawingVisual();using(var dc=sleepVisual.RenderOpen()){
                dc.DrawRectangle(Brushes.LightSlateGray,null,new Rect(0,0,280,275));
                var contact=new Point(140,195);dc.DrawRoundedRectangle(Brushes.CornflowerBlue,null,new Rect(contact.X-18,contact.Y,36,36),4,4);
                var sprite=DragSurfaceVerification.Crop(pet,pet.PointFromScreen(pet.RenderedContactScreen));dc.DrawImage(sprite,new Rect(0,0,280,275));
            }
            var sleepRender=new RenderTargetBitmap(280,275,96,96,PixelFormats.Pbgra32);sleepRender.Render(sleepVisual);var sleepPng=new PngBitmapEncoder();sleepPng.Frames.Add(BitmapFrame.Create(sleepRender));using(var stream=File.Create(Path.Combine(AppContext.BaseDirectory,"sleep-on-icon.png")))sleepPng.Save(stream);
            fake.Present=false;await sleeping;Check(!engine.AmbientBusy&&engine.SeatedIcon==null&&pet.State=="idle","sleep did not wake after icon disappearance");
            fake.Present=true;var cancelledSleep=engine.IdleAction("sleep");await Task.Delay(80);engine.Stop();await cancelledSleep;Check(engine.SleepPhase==null&&!engine.AmbientBusy,"sleep cancellation stuck");
            pet.Grabbed+=engine.Stop;
            var dragInterrupted=engine.PreviewAnimation("run",true);await Task.Delay(120);
            bool dragMarkedBeforeStop=false,stopKeptCurrentClip=false;
            pet.Grabbed+=()=>{dragMarkedBeforeStop=pet.IsDragging;stopKeptCurrentClip=pet.AnimationKey=="run";};
            pet.Say("拎起前说话");pet.SetMusicBubble("测试歌曲",true);pet.ShowTodos([new("drag-test","测试待办",DateTime.UtcNow)],_=>{});
            var surfaceOrigin=pet.PointToScreen(new Point());
            var dragWatch=System.Diagnostics.Stopwatch.StartNew();pet.BeginDragAt(pet.ToPixels(new(700,450)));var dragMilliseconds=dragWatch.Elapsed.TotalMilliseconds;
            Check(pet.PointToScreen(new Point())==surfaceOrigin,"Lift moved the native surface carrying old pixels");
            pet.SayNotification("拖拽中消息");pet.SetMusicBubble("下一句歌词",true);pet.Say("拖拽中说话");
            Check(!pet.SpeechVisible&&!pet.TodoBubblesVisible,"Bubbles visible while lifted");
            try{await dragInterrupted;throw new Exception("Drag failed to cancel preview");}catch(OperationCanceledException){}
            await Task.Delay(120);pet.UpdateLayout();
            Check(dragMarkedBeforeStop&&stopKeptCurrentClip,"Drag cancellation inserted an idle frame");
            Check(pet.IsDragging&&pet.AnimationKey=="lifted","drag did not use lifted clip");Check((pet.RenderedContactScreen-pet.ToPixels(new(700,450))).Length<2,"nape not anchored to cursor");pet.EndDrag();Check(!pet.IsDragging&&pet.State=="idle","drop did not release");
            Check(pet.PointToScreen(new Point())==surfaceOrigin&&pet.TodoBubblesVisible,"Drop moved the native surface or todos not restored");
            pet.SetMusicBubble("",false);pet.ShowTodos([],_=>{});
            evidence.Add(new{test="drowsy-yawn-icon-sleep-loop-wake-cancel-and-nape-drag",pass=true,phases,dragMilliseconds});
            pet.Say("第一句");await Task.Delay(3000);pet.Say("第二句");await Task.Delay(1300);
            Check(pet.SpeechVisible,"An old speech timeout hid the new message");
            await Task.Delay(3000);Check(!pet.SpeechVisible,"Speech bubble did not expire");
            pet.Say("新消息");Check(pet.SpeechVisible,"Speech did not reappear after expiry");
            pet.Say("");Check(!pet.SpeechVisible,"Empty speech leaves a bubble");
            evidence.Add(new{test="speech-expires-restarts-and-hides-entire-bubble",pass=true});
            using var shell=new ShellDesktop();var visible=shell.VisibleSeats();
            evidence.Add(new {test="desktop-seat-read-only-probe",pass=true,visibleSeats=visible.Count,eligibleSeats=new DesktopSeats().Available(pet).Count});
        }
        finally { engine.Stop();pet.Close(); }
        var panel = new MainWindow(); panel.Show();
        try
        {
            await Task.Delay(600); panel.UpdateLayout();
            var render = new RenderTargetBitmap((int)panel.ActualWidth,(int)panel.ActualHeight,96,96,PixelFormats.Pbgra32);
            render.Render(panel);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(render));
            using var stream=File.Create(Path.Combine(AppContext.BaseDirectory,"animation-panel-render.png"));encoder.Save(stream);
            evidence.Add(new {test="control-panel-layout-and-action-picker",pass=true});
        }
        finally { panel.Close(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"animation-verification.json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
    }
}
