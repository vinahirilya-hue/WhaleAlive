using System.Runtime.InteropServices;
using System.Windows;

namespace WhaleAlive;
public static class DisplayGeometry
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] static extern nint MonitorFromPoint(ShellDesktop.POINT point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);
    public static Rect WorkArea(PetWindow pet, Point point)
    {
        var physical = pet.ToPixels(point);
        var monitor = MonitorFromPoint(new((int)physical.X, (int)physical.Y), 2);
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;
        return new Rect(pet.FromPixels(new(info.Work.Left, info.Work.Top)), pet.FromPixels(new(info.Work.Right, info.Work.Bottom)));
    }
}
