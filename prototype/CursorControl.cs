using System.Runtime.InteropServices;

namespace WhaleAlive;
public static class CursorControl
{
    public static ShellDesktop.POINT Position() { if (!GetCursorPos(out var p)) throw new InvalidOperationException("无法读取鼠标位置。"); return p; }
    public static bool Escaped(ShellDesktop.POINT actual, ShellDesktop.POINT expected) => Math.Abs(actual.X - expected.X) > 12 || Math.Abs(actual.Y - expected.Y) > 12;
    public static async Task<bool> Guide(ShellDesktop.POINT target, CancellationToken ct, Func<bool> allowed)
    {
        var start=Position();var expected=start;
        for(int i=1;i<=45;i++)
        {
            ct.ThrowIfCancellationRequested();if(!allowed())throw new OperationCanceledException();
            var actual=Position();if(Escaped(actual,expected)||(GetAsyncKeyState(1)&0x8000)!=0||(GetAsyncKeyState(2)&0x8000)!=0)return true;
            double t=i/45.0;t=t*t*(3-2*t);
            if(!SetCursorPos((int)Math.Round(start.X+(target.X-start.X)*t),(int)Math.Round(start.Y+(target.Y-start.Y)*t)))throw new InvalidOperationException("Windows 拒绝移动指针。");
            expected=Position();await Task.Delay(20,ct);
        }
        return false;
    }
    public static async Task<bool> Pull(CancellationToken ct, Func<bool> allowed)
    {
        var expected = Position(); var begin = DateTime.UtcNow;
        // Never capture or block input. The OS cursor remains under the user's control.
        while ((DateTime.UtcNow - begin).TotalMilliseconds < 900)
        {
            ct.ThrowIfCancellationRequested(); if (!allowed()) throw new OperationCanceledException();
            var actual = Position();
            if (Escaped(actual, expected) || (GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0) return true;
            if (!SetCursorPos(Math.Max(GetSystemMetrics(76), actual.X - 2), actual.Y)) throw new InvalidOperationException("Windows 拒绝了鼠标移动。");
            expected = Position(); await Task.Delay(20, ct);
        }
        return false;
    }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out ShellDesktop.POINT p);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vkey);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
}
