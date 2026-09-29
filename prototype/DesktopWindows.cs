using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
namespace WhaleAlive;
public record PetDesktopWindow(long Handle, uint ProcessId, string Title, Rect Bounds)
{
    public override string ToString() => Title;
}
public static class DesktopWindows
{
    delegate bool EnumProc(nint h, nint p);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L,T,R,B; }
    public static IReadOnlyList<PetDesktopWindow> List()
    {
        var result = new List<PetDesktopWindow>();
        EnumWindows((h, _) => {
            var title = new StringBuilder(512); GetWindowText(h,title,512); GetWindowThreadProcessId(h,out uint pid);
            if (pid == Environment.ProcessId || title.Length == 0 || !IsWindowVisible(h) || IsIconic(h) || IsZoomed(h) || GetWindow(h,4)!=0) return true;
            if (!GetWindowRect(h,out var r) || r.R-r.L<160 || r.B-r.T<100) return true;
            result.Add(new((long)h,pid,title.ToString(),new Rect(r.L,r.T,r.R-r.L,r.B-r.T))); return true;
        },0); return result;
    }
    public static Rect? Current(PetDesktopWindow target)
    {
        var h=(nint)target.Handle; GetWindowThreadProcessId(h,out var pid);
        if (pid!=target.ProcessId || !IsWindowVisible(h) || IsIconic(h) || IsZoomed(h) || !GetWindowRect(h,out var r)) return null;
        return new Rect(r.L,r.T,r.R-r.L,r.B-r.T);
    }
    public static Rect Move(PetDesktopWindow target, Point point)
    {
        if (Current(target)==null) throw new InvalidOperationException("窗口已关闭、最小化或最大化。");
        if (!SetWindowPos((nint)target.Handle,0,(int)Math.Round(point.X),(int)Math.Round(point.Y),0,0,0x15)) throw new InvalidOperationException("窗口不允许移动。");
        return Current(target) ?? throw new InvalidOperationException("窗口已不可用。");
    }
    public static Rect? EdgeBounds(PetDesktopWindow target)
    {
        if(Current(target) is not Rect bounds)return null;
        if(DwmGetWindowAttribute((nint)target.Handle,9,out RECT r,Marshal.SizeOf<RECT>())==0)
            bounds=new Rect(r.L,r.T,Math.Max(0,r.R-r.L),Math.Max(0,r.B-r.T));
        return bounds;
    }
    public static IReadOnlyList<PetDesktopWindow> PerchWindows()=>List().Where(w=>
    {
        var h=(nint)w.Handle;long style=GetWindowLongPtr(h,-20).ToInt64();
        if((style&(0x80L|0x08000000L))!=0)return false; // Tool/overlay windows have no usable title-bar edge.
        return DwmGetCloaked(h,14,out int cloaked,sizeof(int))!=0||cloaked==0;
    }).ToArray();
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern nint GetWindowLongPtr(nint window,int index);
    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")]static extern int DwmGetCloaked(nint window,uint attribute,out int value,int size);
    [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(nint window,uint attribute,out RECT value,int size);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,nint p);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(nint h,StringBuilder text,int count);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint h,out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] static extern bool IsIconic(nint h);
    [DllImport("user32.dll")] static extern bool IsZoomed(nint h);
    [DllImport("user32.dll")] static extern nint GetWindow(nint h,uint command);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint h,out RECT rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint h,nint after,int x,int y,int w,int height,uint flags);
}
