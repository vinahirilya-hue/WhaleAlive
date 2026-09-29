using System.IO;
using System.Text.Json;
using System.Windows;
namespace WhaleAlive;
public static class Verification
{
    public static async Task InteractionRoundTrip()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WhaleAlive-interaction-" + Guid.NewGuid().ToString("N") + ".txt");
        var history = Path.Combine(AppContext.BaseDirectory, "interaction-test-" + Guid.NewGuid().ToString("N") + ".json");
        var pet = new PetWindow(); pet.Show(); DesktopIcon? fixture = null;
        var evidence = new List<object>();
        try
        {
            File.WriteAllText(path, "Temporary interaction verification fixture."); using var shell = new ShellDesktop();
            for (int n = 0; n < 30; n++) { fixture = shell.List().FirstOrDefault(i => i.Id == path); if (fixture != null) break; await Task.Delay(200); }
            if (fixture == null) throw new Exception("Fixture not enumerated");
            var before = shell.List(); var engine = new Interaction(pet, history) { AllowRealMove = true };
            await engine.Carry(path, "右下角", true);
            var moved = shell.List().Single(i => i.Id == path); if (moved.X == fixture.X && moved.Y == fixture.Y) throw new Exception("Interaction did not commit");
            if (engine.History.Count != 1 || engine.History[0].Status != "completed") throw new Exception("History not committed");
            // A new engine loads the persisted journal, exactly as a restart does.
            var recovered = new Interaction(pet, history); await recovered.Undo();
            var restored = shell.List().Single(i => i.Id == path); if (restored.X != fixture.X || restored.Y != fixture.Y) throw new Exception("Persisted undo failed");
            var cancelled = engine.Carry(path, "左下角", true); await Task.Delay(200); engine.Stop();
            try { await cancelled; throw new Exception("Cancelled interaction reported success"); } catch (OperationCanceledException) { }
            var after = shell.List(); if (before.Any(i => after.Any(j => j.Id == i.Id && (i.X != j.X || i.Y != j.Y)))) throw new Exception("Desktop changed after undo/cancellation");
            evidence.Add(new { test = "animation-real-commit-persisted-undo", pass = true, history = engine.History.Count });
            evidence.Add(new { test = "cancel-does-not-commit", pass = true });
            var pickupEngine = new Interaction(pet, history) { AllowRealMove = true };
            pickupEngine.Progress += (_, phase) => { if (phase == 2) pickupEngine.Stop(); };
            try { await pickupEngine.Carry(path, "左下角", true); throw new Exception("Pickup cancellation reported success"); } catch (OperationCanceledException) { }
            if (pet.State != "idle") throw new Exception("Pickup cancellation left pet in drag state");
            evidence.Add(new { test = "pickup-cancel-restores-idle", pass = true });
            var committedEngine = new Interaction(pet, history) { AllowRealMove = true };
            bool stopped = false;
            committedEngine.Progress += (_, phase) => { if (phase == 5 && !stopped) { stopped = true; committedEngine.Stop(); } };
            await committedEngine.Carry(path, "右下角", true);
            if (committedEngine.History.Last().Status != "completed") throw new Exception("Postcommit stop lost completed history");
            await committedEngine.Undo();
            evidence.Add(new { test = "postcommit-stop-remains-undoable", pass = true });
        }
        finally
        {
            if (fixture != null) { try { using var shell = new ShellDesktop(); shell.Move(path, fixture.X, fixture.Y); } catch { } }
            pet.Close(); if (File.Exists(path)) File.Delete(path); if (File.Exists(history)) File.Delete(history);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "interaction-verification.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    public static void ShellRoundTrip()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WhaleAlive-test-" + Guid.NewGuid().ToString("N") + ".txt");
        DesktopIcon? fixture = null; var evidence = new List<object>();
        try
        {
            File.WriteAllText(path, "Temporary Whale Alive verification fixture. Safe to remove after verification.");
            using var shell = new ShellDesktop();
            for (int n = 0; n < 30; n++) { fixture = shell.List().FirstOrDefault(i => i.Id == path); if (fixture != null) break; Thread.Sleep(200); }
            if (fixture == null) throw new Exception("Explorer did not enumerate fixture");
            var before = shell.List(); var target = shell.Move(path, fixture.X + 152, fixture.Y); Thread.Sleep(300);
            var moved = shell.List().Single(i => i.Id == path);
            if (moved.X == fixture.X && moved.Y == fixture.Y) throw new Exception("Fixture did not move");
            var after = shell.List(); var changedOthers = before.Where(i => i.Id != path).Where(i => after.Any(j => j.Id == i.Id && (j.X != i.X || j.Y != i.Y))).Count();
            var restored = shell.Move(path, fixture.X, fixture.Y); Thread.Sleep(200);
            var final = shell.List().Single(i => i.Id == path);
            if (final.X != fixture.X || final.Y != fixture.Y) throw new Exception("Fixture did not return to exact origin");
            if (changedOthers != 0) throw new Exception("Other icon positions changed");
            if (!CursorControl.Escaped(new(100, 100), new(80, 100)) || CursorControl.Escaped(new(85, 100), new(80, 100))) throw new Exception("Cursor escape threshold failed");
            evidence.Add(new { test = "native-shell-roundtrip", pass = true, from = new { fixture.X, fixture.Y }, to = new { moved.X, moved.Y }, restored = new { final.X, final.Y }, otherIconsChanged = changedOthers });
            evidence.Add(new { test = "cursor-escape-threshold", pass = true });
            evidence.Add(new { workArea = SystemParameters.WorkArea.ToString(), shellOrigin = shell.ToScreen(0, 0).ToString() });
        }
        finally
        {
            if (fixture != null) { try { using var s = new ShellDesktop(); s.Move(path, fixture.X, fixture.Y); } catch { } }
            // Only the uniquely-created fixture is removed; user files are never test targets.
            if (File.Exists(path)) File.Delete(path);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "verification.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
