using System.IO;
using System.Text.Json;
using System.Windows;

namespace WhaleAlive;
public class App : Application
{
    public App() { Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/WhaleAlive;component/UiTheme.xaml", UriKind.Relative) }); }
    [STAThread]
    public static void Main(string[] args)
    {
        if(args.Contains("--verify-pointer"))
        {
            var verifier=new App{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            verifier.Startup+=async(_,_)=>{try{await PointerVerification.Run();}catch(Exception ex){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"pointer-verification-error.txt"),ex.ToString());Environment.ExitCode=1;}finally{verifier.Shutdown();}};
            verifier.Run();return;
        }
        if(args.Contains("--verify-drag-surface"))
        {
            var verifier=new App{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            verifier.Startup+=async(_,_)=>{try{await DragSurfaceVerification.Run();}catch(Exception ex){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"drag-surface-verification-error.txt"),ex.ToString());Environment.ExitCode=1;}finally{verifier.Shutdown();}};
            verifier.Run();return;
        }
        if(args.Contains("--verify-calibration"))
        {
            var verifier=new App{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            verifier.Startup+=async(_,_)=>{try{await PoseCalibrationVerification.Run();}catch(Exception ex){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"calibration-verification-error.txt"),ex.ToString());Environment.ExitCode=1;}finally{verifier.Shutdown();}};
            verifier.Run();return;
        }
        if(args.Contains("--inspect-menu"))
        {
            var inspector = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            inspector.Startup += (_, _) =>
            {
                var pet = new PetWindow { Title = "Whale Alive · 右键菜单核对", ShowInTaskbar = true };
                pet.Show(); var engine = new Interaction(pet, Path.Combine(AppContext.BaseDirectory,"menu-inspect-history.json"));
                pet.Grabbed+=engine.Stop;
                var store = new CompanionStore(Path.Combine(AppContext.BaseDirectory,"menu-inspect-settings.json"));
                store.Data.SocialMusic.Notifications = false;
                var features = new CompanionFeatures(pet,engine,store);
                PetQuickMenu? menu = null;
                menu = new(pet,engine,features,()=>pet.Say("设置入口已触发"),()=>pet.Close());
                pet.OpenMenu += menu.Open;
                pet.Closed += (_, _) => { menu.Dispose();features.Dispose();engine.Stop();inspector.Shutdown(); };
                features.SocialMusic.Start();
            };
            inspector.Run(); return;
        }
        if(args.Contains("--verify-social-music"))
        {
            var verifier = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            verifier.Startup += async (_, _) => { try { await SocialMusicVerification.Run(); } catch(Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"social-music-verification-error.txt"),ex.ToString()); Environment.ExitCode=1; } finally { verifier.Shutdown(); } };
            verifier.Run(); return;
        }
        if(args.Contains("--verify-companion"))
        {
            var verifier=new App{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            verifier.Startup+=async(_,_)=>{try{await CompanionVerification.Run();}catch(Exception ex){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"companion-verification-error.txt"),ex.ToString());Environment.ExitCode=1;}finally{verifier.Shutdown();}};
            verifier.Run();return;
        }
        if (args.Contains("--inspect-seat"))
        {
            var inspectorApp = new App { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var pet = new PetWindow { Title = "Whale Alive · 坐姿核对", ShowInTaskbar = true };
            pet.Loaded += (_, _) =>
            {
                var seats = new DesktopSeats().Available(pet);
                var seat = seats.OrderBy(s => Math.Abs(s.Surface.X + 450) + Math.Abs(s.Surface.Y - 630)).FirstOrDefault();
                if (seat == null) { pet.Say("没有可用于核对的露出图标"); return; }
                pet.SetAnchor(seat.Surface); pet.Face(args.Contains("--right"));pet.SetState("sit_idle");pet.Say("坐姿核对 · " + seat.Name);
            };
            inspectorApp.Run(pet); return;
        }
        if (args.Contains("--verify-animations"))
        {
            var verifier = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            verifier.Startup += async (_, _) => { try { await AnimationVerification.Run(); } catch (Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "animation-verification-error.txt"), e.ToString()); Environment.ExitCode = 1; } finally { verifier.Shutdown(); } };
            verifier.Run(); return;
        }
        if (args.Contains("--verify-shell")) { try { Verification.ShellRoundTrip(); } catch (Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "verification-error.txt"), e.ToString()); Environment.ExitCode = 1; } return; }
        if (args.Contains("--verify-interaction"))
        {
            var verifier = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            verifier.Startup += async (_, _) => { try { await Verification.InteractionRoundTrip(); } catch (Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "interaction-verification-error.txt"), e.ToString()); Environment.ExitCode = 1; } finally { verifier.Shutdown(); } };
            verifier.Run(); return;
        }
        if (args.Contains("--probe"))
        {
            try { using var shell = new ShellDesktop(); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "probe.json"), JsonSerializer.Serialize(new { shell.AutoArrange, WorkArea = SystemParameters.WorkArea.ToString(), Rectangles = shell.IconRectangles().Take(4).Select(g => new { Item = g.Item.ToString(), Icon = g.Icon.ToString() }), Seats = shell.VisibleSeats().Select(kv => new { Name = kv.Key, X = kv.Value.X, Y = kv.Value.Y }), Icons = shell.List().Select(i => new { i.Name, i.Id, i.X, i.Y }) })); }
            catch (Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "probe.json"), e.ToString()); Environment.ExitCode = 1; }
            return;
        }
        using var mutex = new Mutex(true, "Local\\WhaleAlive.Prototype", out bool first);
        if (!first) { MessageBox.Show("Whale Alive 已经在运行。右键桌宠，选择“设置”即可打开设置窗口。"); return; }
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => { MessageBox.Show(e.Exception.Message, "Whale Alive"); e.Handled = true; };
        app.Startup += (_, _) => { var main = new MainWindow(desktopMode: true); app.MainWindow = main; main.Closed += (_, _) => app.Shutdown(); if(args.Contains("--settings"))main.ShowSettings(); };
        app.Run();
    }
}


