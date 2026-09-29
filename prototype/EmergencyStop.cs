using System.Runtime.InteropServices;

namespace WhaleAlive;
public sealed class EmergencyStop : IDisposable
{
    readonly Hook callback; readonly nint handle; long last; bool down;
    public EmergencyStop(Action stop) { callback = (n, w, l) => { if (n >= 0 && Marshal.ReadInt32(l) == 0x1B) { if (w == 0x101 || w == 0x105) down = false; else if ((w == 0x100 || w == 0x104) && !down) { down = true; long now = Environment.TickCount64; if (now - last < 650) { stop(); last = 0; } else last = now; } } return CallNextHookEx(0, n, w, l); }; handle = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0); if (handle == 0) throw new InvalidOperationException("无法注册 Esc 紧急停止键。"); }
    public void Dispose() { if (handle != 0) UnhookWindowsHookEx(handle); }
    delegate nint Hook(int code, nint w, nint l);
    [DllImport("user32.dll")] static extern nint SetWindowsHookEx(int id, Hook proc, nint mod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint h);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint h, int code, nint w, nint l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern nint GetModuleHandle(string? name);
}
