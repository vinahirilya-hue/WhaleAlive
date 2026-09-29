using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WhaleAlive;
public sealed partial class PetWindow : Window
{
    public const double VisualScale = .5;
    const double SpriteSize = 256 * VisualScale, AnchorX = 140, AnchorY = 195;
    readonly Image sprite = new() { Width = SpriteSize, Height = SpriteSize };
    readonly Image attachment = new() { Width = 34 * VisualScale, Height = 34 * VisualScale, Visibility = Visibility.Hidden, IsHitTestVisible = false };
    readonly TextBlock speech = new() { FontSize = 12, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    readonly Border bubble;
    readonly StackPanel musicControls = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
    readonly Button previousSong = new() { Content = "◀", ToolTip = "上一首" }, toggleSong = new() { Content = "▶", ToolTip = "播放 / 暂停" }, nextSong = new() { Content = "▶▶", ToolTip = "下一首" };
    string musicText = "";
    int speechPriority;
    public event Action<string>? MusicCommand;
    long speechStart;
    double speechDuration;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
    readonly AnimationCatalog catalog = new();
    BitmapSource[] frames = [];
    long stateStart = Stopwatch.GetTimestamp();
    string state = "idle", clipKey = "idle";
    bool dragging, right, once, reverse;
    int rangeStart, rangeEnd;
    SequenceTimeline? timeline;
    Point lastMouse;
    Point anchor;
    static readonly double DesktopScale = GetDpiForSystem() / 96.0;
    public event Action? Grabbed;
    public event Action? OpenMenu;
    public bool IsMenuOpen { get; internal set; }
    internal Action<Vector>? CalibrationDrag { get; set; }
    internal Func<string, bool, Vector>? PoseOffset { get; set; }
    internal Func<string,bool,Vector>? CarryOffset {get;set;}
    internal Func<string,bool,Vector>? SurfaceOffset {get;set;}
    Point canvasAnchor=new(AnchorX,AnchorY);
    Rect viewport;
    bool configuringViewport;
    internal int? CalibrationFrame { get; set; }
    public string State => state;
    public string AnimationKey => clipKey;
    public bool FacingRight => right;
    public int CurrentFrame { get; private set; }
    public bool IsDragging => dragging;
    public bool SpeechVisible => bubble.Visibility == Visibility.Visible;
    internal bool TodoBubblesVisible=>todoBubbles.Visibility==Visibility.Visible;
    internal string DisplayedSpeech => speech.Text;
    public double SpriteDisplaySize => SpriteSize * catalog.Clips[clipKey].DisplayScale;
    public double ReferenceCharacterHeight => SpriteDisplaySize * catalog.Clips[clipKey].ReferenceHeight;
    public Point Anchor => anchor;
    internal Point RenderedSpriteOrigin=>sprite.PointToScreen(new Point());
    public Point RenderedContactScreen
    {
        get { var p = catalog.Clips[clipKey].Pivot(CurrentFrame, right); return sprite.PointToScreen(new(p.X * sprite.ActualWidth, p.Y * sprite.ActualHeight)); }
    }
    public ImageSource Portrait => catalog.Load("idle", false)[0];
    public IReadOnlyDictionary<string, AnimationClip> Clips => catalog.Clips;
    static readonly Dictionary<string, string> Aliases = new()
    {
        ["think"]="curious", ["working"]="tidy", ["wait"]="idle", ["celebrate"]="proud",
        ["joy"]="proud", ["error"]="curious", ["disappointed"]="curious", ["drag"]="lifted"
    };
    public PetWindow()
    {
        Icon = GlassAppearance.AppIcon;
        Title = "Whale Alive · 桌宠"; Width = 280; Height = 275; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        SourceInitialized += (_, _) => ConfigureViewport();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(ConfigureViewport);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplaySettingsChanged;
        var canvas = new Canvas(); Content = canvas;
        var bubbleContent = new StackPanel(); bubbleContent.Children.Add(speech); bubbleContent.Children.Add(musicControls);
        bubble = new Border { Background = new SolidColorBrush(Color.FromRgb(30, 46, 64)), CornerRadius = new(14), Padding = new(12, 8, 12, 8), Width = 210, Child = bubbleContent, Visibility = Visibility.Hidden };
        foreach(var (button, command) in new[]{(previousSong,"previous"),(toggleSong,"toggle"),(nextSong,"next")})
        {
            button.Padding = new(8, 2, 8, 2); button.Margin = new(3, 6, 3, 0); button.FontSize = 10;
            button.Click += (_, e) => { MusicCommand?.Invoke(command); e.Handled = true; }; musicControls.Children.Add(button);
        }
        bubble.MouseEnter += (_, _) => UpdateMusicControls();
        bubble.MouseLeave += (_, _) => musicControls.Visibility = Visibility.Collapsed;
        canvas.Children.Add(bubble); Canvas.SetTop(bubble, 12); Canvas.SetLeft(bubble, 35);
        canvas.Children.Add(sprite); canvas.Children.Add(attachment);
        InitializeCompanionVisuals(canvas);
        RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.HighQuality);
        InitializePointer();
        // Drag/drop must never decode PNGs in the mouse-down/up handler, even after a long idle session.
        catalog.Pin("idle", false); catalog.Pin("lifted", false); catalog.Pin("lifted", true);
        timer.Tick += (_, _) => { RenderFrame(); AnimateSpeech(); AnimateCompanionVisuals(); }; timer.Start(); Closed += (_, _) => {holdTimer.Stop();timer.Stop();Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplaySettingsChanged;};
        var work = SystemParameters.WorkArea; SetAnchor(new(work.Right - 190, work.Bottom - 30));
        SetState("idle", true); Say("你好呀，今天也陪着你。");
    }
    public void BeginDragAt(Point physicalPointer)
    {
        if (dragging) return;
        dragging=true; lastMouse=physicalPointer;
        if(CalibrationDrag != null) return;
        speechPriority=0;speech.Text="";bubble.Visibility=Visibility.Hidden;musicControls.Visibility=Visibility.Collapsed;todoBubbles.Visibility=Visibility.Hidden;
        Grabbed?.Invoke();
        SetState("drag",true);SetAnchor(FromPixels(physicalPointer));
    }
    public void EndDrag()
    {
        if(!dragging)return;
        if(CalibrationDrag != null) { dragging=false;sprite.ReleaseMouseCapture();return; }
        UpdateLayout();var feet=new Point(right? .52:.48,.91);
        var screen=sprite.PointToScreen(new(feet.X*SpriteDisplaySize,feet.Y*SpriteDisplaySize));
        dragging=false;sprite.ReleaseMouseCapture();SetState("idle");SetAnchor(FromPixels(screen));
        todoBubbles.Visibility=Visibility.Visible;ShowMusicFallback();
    }
    internal void MoveDragTo(Point p)
    {
        if(!dragging) return;
        if(CalibrationDrag != null) CalibrationDrag(PointFromScreen(p)-PointFromScreen(lastMouse));
        else SetAnchor(Anchor+(p-lastMouse)/DesktopScale);
        lastMouse=p;
    }
    void Begin(string name, bool playOnce, int start, int end, SequenceTimeline? trip = null)
    {
        state = name; clipKey = Aliases.GetValueOrDefault(name, name);
        if (!catalog.Clips.ContainsKey(clipKey)) { state = clipKey = "idle"; }
        frames = catalog.Load(clipKey, right); once = playOnce; timeline = trip;reverse=false;
        rangeStart = start; rangeEnd = end < 0 ? frames.Length - 1 : end;
        stateStart = Stopwatch.GetTimestamp(); RenderFrame();
    }
    void RenderFrame()
    {
        if (frames.Length == 0) return;
        var c = catalog.Clips[clipKey]; double t = Stopwatch.GetElapsedTime(stateStart).TotalSeconds;
        int tick = (int)(t * c.Fps), f;
        if (timeline != null) f = timeline.FrameAt(t);
        else if (once) f = reverse ? Math.Max(rangeEnd-tick,rangeStart) : Math.Min(rangeStart + tick, rangeEnd);
        else f = rangeStart + tick % (rangeEnd - rangeStart + 1);
        f = CalibrationFrame ?? f;
        CurrentFrame = f; sprite.Source = frames[f];
        double size = SpriteSize * c.DisplayScale;
        sprite.Width = sprite.Height = size;
        var pivot = SurfacePivot(clipKey,f,right);
        double x = canvasAnchor.X - pivot.X * size, y = canvasAnchor.Y - pivot.Y * size;
        var adjustment = (SurfaceOffset??PoseOffset)?.Invoke(clipKey, right) ?? new Vector();
        x += adjustment.X; y += adjustment.Y;
        Canvas.SetLeft(sprite, x); Canvas.SetTop(sprite, y);
        var hand = HandPoint(clipKey, f, c.Count);
        if (right && c.Directional) hand.X = 1 - hand.X;
        Canvas.SetLeft(attachment, x + hand.X * size - attachment.Width / 2);
        Canvas.SetTop(attachment, y + hand.Y * size - attachment.Height / 2);
    }
    static Point HandPoint(string key, int frame, int count)
    {
        if (key is "pickup" or "putdown")
        {
            double bend = Math.Sin(Math.PI * frame / Math.Max(1, count - 1));
            return new(.35, .56 + .20 * bend);
        }
        return new(.35, .56);
    }
    Point SurfacePivot(string key,int frame,bool facingRight)
    {
        var clip=catalog.Clips[key];
        // Keep the source canvas registered to its settled support point throughout
        // entry/exit. Interpolating from feet to hip/hand adds an artificial slide.
        // The sleeping hand changes shape with breathing; tracking its centroid
        // translates the entire character sideways. Keep the settled registration.
        int support=key=="sit"?Math.Clamp(frame,clip.LoopStart,clip.LoopEnd):key=="lie_sleep"?clip.LoopStart:frame;
        return clip.Pivot(support,facingRight);
    }
    public Point SurfaceStandingPosition(Point surface,string key,bool facingRight,bool leaving=false)
    {
        var clip=catalog.Clips[key];int frame=leaving?clip.Count-1:0;
        var correction=(SurfaceOffset??PoseOffset)?.Invoke(key,facingRight)??new Vector();
        var delta=(clip.Pivot(frame,facingRight)-SurfacePivot(key,frame,facingRight))*(SpriteSize*clip.DisplayScale)+correction;
        // Convert local WPF units to the desktop coordinates used by movement.
        var dpi=VisualTreeHelper.GetDpi(this);
        return surface+new Vector(delta.X*dpi.DpiScaleX/DesktopScale,delta.Y*dpi.DpiScaleY/DesktopScale);
    }
    public void SetAnchor(Point p)
    {
        anchor = p;
        if(viewport.IsEmpty||viewport.Width==0)return;
        var physical=ToPixels(p);var dpi=VisualTreeHelper.GetDpi(this);
        canvasAnchor=new((physical.X-viewport.Left)/dpi.DpiScaleX,(physical.Y-viewport.Top)/dpi.DpiScaleY);
        Canvas.SetLeft(bubble,canvasAnchor.X-105);Canvas.SetTop(bubble,canvasAnchor.Y-183);
        PositionTodoBubbles();
        AnimateCompanionVisuals();RenderFrame();
    }
    void DisplaySettingsChanged(object? sender,EventArgs e)=>Dispatcher.BeginInvoke(ConfigureViewport);
    void ConfigureViewport()
    {
        if(configuringViewport||!IsLoaded&&new WindowInteropHelper(this).Handle==0)return;
        configuringViewport=true;
        try
        {
            // One stationary, per-pixel-transparent surface. Both poses and positions now
            // belong to the same WPF composition frame; a move cannot carry old pixels.
            viewport=new(GetSystemMetrics(76),GetSystemMetrics(77),GetSystemMetrics(78),GetSystemMetrics(79));
            SetWindowPos(new WindowInteropHelper(this).Handle,0,(int)viewport.X,(int)viewport.Y,(int)viewport.Width,(int)viewport.Height,0x14);
            SetAnchor(anchor);
        }
        finally{configuringViewport=false;}
    }
    [DllImport("user32.dll")]static extern int GetSystemMetrics(int index);
    public Point CarryPosition(Point icon,string pose,bool facingRight)=>icon+(CarryOffset?.Invoke(pose,facingRight)??new Vector(facingRight?-24:24,45));
    public void Face(bool faceRight)
    {
        if (right == faceRight) return;
        right = faceRight; frames = catalog.Load(clipKey, right); RenderFrame();
    }
    public void SetState(string s, bool restart = false)
    {
        if (state == s && !restart) return;
        var key = Aliases.GetValueOrDefault(s, s);
        var c = catalog.Clips.GetValueOrDefault(key, catalog.Clips["idle"]);
        Begin(s, c.Mode == "once", c.Mode == "loop" ? c.LoopStart : 0, c.Mode == "loop" ? c.LoopEnd : -1,
            c.Mode == "phased" ? new SequenceTimeline(c, 1000000) : null);
    }
    public async Task PlayOnce(string name, CancellationToken ct, int start = 0, int end = -1)
    {
        ct.ThrowIfCancellationRequested(); Begin(name, true, start, end);
        await Task.Delay(TimeSpan.FromSeconds((rangeEnd - rangeStart + 1) / catalog.Clips[clipKey].Fps), ct);
        ct.ThrowIfCancellationRequested();
    }
    public void LoopSegment(string name,int start,int end)=>Begin(name,false,start,end);
    public async Task PlayReverse(string name,CancellationToken ct,int end)
    {
        ct.ThrowIfCancellationRequested();Begin(name,true,0,end);reverse=true;RenderFrame();
        await Task.Delay(TimeSpan.FromSeconds((end+1)/catalog.Clips[clipKey].Fps),ct);
    }
    public void Say(string text)
    {
        if(speechPriority >= 2 && Stopwatch.GetElapsedTime(speechStart).TotalSeconds < speechDuration) return;
        ShowSpeech(text, 1);
    }
    public void SayNotification(string text) => ShowSpeech(text, 2);
    public void ClearNotification() { if(speechPriority == 2) { speechPriority = 0; ShowMusicFallback(); } }
    void ShowSpeech(string text, int priority)
    {
        if(dragging&&CalibrationDrag==null){bubble.Visibility=Visibility.Hidden;return;}
        speechPriority = string.IsNullOrWhiteSpace(text) ? 0 : priority;
        speech.Text = text; speechStart = Stopwatch.GetTimestamp();
        speechDuration = priority >= 2 ? 6 : Math.Clamp(2 + text.Length * .12, 4, 8);
        bubble.Opacity = 1;
        bubble.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Hidden : Visibility.Visible;
        UpdateMusicControls();
        if(speechPriority == 0) ShowMusicFallback();
    }
    public void SetMusicBubble(string text, bool playing, bool canPrevious = false, bool canToggle = false, bool canNext = false)
    {
        musicText = text.Length > 160 ? text[..160] + "…" : text;
        toggleSong.Content = playing ? "Ⅱ" : "▶";
        previousSong.IsEnabled = canPrevious; toggleSong.IsEnabled = canToggle; nextSong.IsEnabled = canNext;
        if(speechPriority == 0) ShowMusicFallback();
    }
    void UpdateMusicControls() => musicControls.Visibility = speechPriority == 0 && musicText.Length > 0 && bubble.IsMouseOver ? Visibility.Visible : Visibility.Collapsed;
    void ShowMusicFallback()
    {
        if(dragging&&CalibrationDrag==null){bubble.Visibility=Visibility.Hidden;musicControls.Visibility=Visibility.Collapsed;return;}
        speech.Text = musicText; bubble.Opacity = 1;
        bubble.Visibility = musicText.Length == 0 ? Visibility.Hidden : Visibility.Visible;
        UpdateMusicControls();
    }
    void AnimateSpeech()
    {
        if (!SpeechVisible || speechPriority == 0) return;
        double remaining = speechDuration - Stopwatch.GetElapsedTime(speechStart).TotalSeconds;
        bubble.Opacity = Math.Clamp(remaining / .35, 0, 1);
        if (remaining <= 0) { speechPriority = 0; ShowMusicFallback(); }
    }
    public void Attach(ImageSource? image)
    {
        attachment.Source = image;
        if(image != null)
        {
            // Keep the icon's source size, independently of the pet's scale.
            attachment.Width = double.IsFinite(image.Width)&&image.Width>0 ? image.Width : 32;
            attachment.Height = double.IsFinite(image.Height)&&image.Height>0 ? image.Height : 32;
        }
        attachment.Visibility = image is null ? Visibility.Hidden : Visibility.Visible;
    }
    public async Task WalkTo(Point destination, CancellationToken ct, bool carrying = false, bool run = false, bool pulling = false, bool brake = false)
    {
        ct.ThrowIfCancellationRequested(); var start = Anchor; double distance = (destination - start).Length;
        if (distance < 2) return;
        right = pulling ? destination.X < start.X : destination.X > start.X;
        string key = pulling ? "pull" : carrying ? "carry_walk" : run ? "run" : "walk";
        bool running = key == "run"; brake &= running;
        var c = catalog.Clips[key]; double speed = running ? 210 : carrying ? 90 : 110;
        // Reserve the last part of the route for inertia during the braking pose.
        // The destination stays unchanged, including near a screen edge.
        double glideDistance = brake ? Math.Min(32, distance * .35) : 0;
        var brakeStart = destination - (destination - start) * (glideDistance / distance);
        // Frames 29–48 are more running, not deceleration. Use whole gait loops instead.
        int exitStart = running ? 49 : c.LoopEnd + 1;
        double rate = running ? 1.5 : 1, fps = c.Fps * rate;
        double entryExit = (c.LoopStart + (brake ? 0 : c.Count - exitStart)) / fps;
        int cycles = Math.Max(1, (int)Math.Ceiling(((distance - glideDistance) / speed - entryExit) / ((c.LoopEnd - c.LoopStart + 1) / fps)));
        var trip = new SequenceTimeline(c, cycles, exitStart, !brake, rate);
        if (brake) catalog.Load("brake", right);
        Begin(key, false, 0, -1, trip);
        var begin = Stopwatch.GetTimestamp();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            double t = Math.Min(1, Stopwatch.GetElapsedTime(begin).TotalSeconds / trip.Duration);
            // A braking run keeps moving into the brake; only a normal stop eases out here.
            double ease = brake ? (t < .15 ? t*t/.2775 : (t-.075)/.925)
                : t < .15 ? t*t/.255 : t > .85 ? 1-(1-t)*(1-t)/.255 : (t-.075)/.85;
            SetAnchor(start + (brakeStart - start) * ease);
            if (t >= 1) break;
            await Task.Delay(16, ct);
        }
        // Jump directly from the running loop to the braking clip, without the normal stop or an idle frame.
        if (brake)
        {
            var stopping = PlayOnce("brake", ct, 30);
            double entrySpeed = (distance - glideDistance) / (trip.Duration * .925);
            // Quadratic ease-out starts at the incoming speed and reaches zero.
            double glideSeconds = 2 * glideDistance / entrySpeed;
            var glideBegin = Stopwatch.GetTimestamp();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                double t = Math.Min(1, Stopwatch.GetElapsedTime(glideBegin).TotalSeconds / glideSeconds);
                SetAnchor(brakeStart + (destination - brakeStart) * (1 - (1-t)*(1-t)));
                if (t >= 1) break;
                await Task.Delay(16, ct);
            }
            await stopping;
        }
        SetState(carrying ? "carry_idle" : "idle");
    }
    // Desktop coordinates use one fixed scale, independent of the pet's current monitor.
    public Point FromPixels(Point p) => new(p.X / DesktopScale, p.Y / DesktopScale);
    public Point ToPixels(Point p) => new(p.X * DesktopScale, p.Y * DesktopScale);
    [DllImport("user32.dll")] static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}

