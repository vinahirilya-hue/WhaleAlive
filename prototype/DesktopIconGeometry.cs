using System.Runtime.InteropServices;
using System.Windows;

namespace WhaleAlive;
public record IconGeometry(Rect Item, Rect Icon);

// LVM_GETITEMRECT with LVIR_ICON excludes the filename and selection rectangle.
// The message is cross-process, so its 16-byte RECT lives in an allocated scratch
// buffer. We only read/write that buffer; no existing Explorer memory is read.
public static class DesktopIconGeometry
{
    public static IReadOnlyList<IconGeometry> Read(nint view)
    {
        nint list=FindWindowEx(view,0,"SysListView32",null);
        if(list==0) list=view;
        GetWindowThreadProcessId(list,out uint pid);
        nint process=OpenProcess(0x38,false,pid);
        if(process==0) return [];
        nint buffer=0;bool timedOut=false;
        try
        {
            buffer=VirtualAllocEx(process,0,16,0x3000,4);
            if(buffer==0) return [];
            if(SendMessageTimeout(list,0x1004,0,0,3,300,out var count)==0) return [];
            var result=new List<IconGeometry>();
            for(int i=0;i<Math.Min((int)count,2000);i++)
            {
                Rect? ReadRect(int part)
                {
                    var bytes=new byte[16];BitConverter.GetBytes(part).CopyTo(bytes,0);
                    if(!WriteProcessMemory(process,buffer,bytes,16,out var written)||written!=16)return null;
                    if(SendMessageTimeout(list,0x100E,i,buffer,3,300,out var success)==0) { timedOut=true;return null; }
                    if(success==0||!ReadProcessMemory(process,buffer,bytes,16,out var read)||read!=16)return null;
                    var a=new ShellDesktop.POINT(BitConverter.ToInt32(bytes,0),BitConverter.ToInt32(bytes,4));
                    var b=new ShellDesktop.POINT(BitConverter.ToInt32(bytes,8),BitConverter.ToInt32(bytes,12));
                    ClientToScreen(list,ref a);ClientToScreen(list,ref b);
                    if(b.X<=a.X||b.Y<=a.Y)return null;
                    return new Rect(new Point(a.X,a.Y),new Point(b.X,b.Y));
                }
                var bounds=ReadRect(0);if(timedOut)break;
                var icon=ReadRect(1);if(timedOut)break;
                if(bounds.HasValue&&icon.HasValue)result.Add(new(bounds.Value,icon.Value));
            }
            return result;
        }
        finally
        {
            // A timed-out receiver might still touch its scratch memory. In that
            // rare case leave the 16 bytes until Explorer exits instead of freeing
            // memory that an outstanding native call might access.
            if(buffer!=0&&!timedOut)VirtualFreeEx(process,buffer,0,0x8000);
            CloseHandle(process);
        }
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern nint FindWindowEx(nint parent,nint after,string className,string? title);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window,out uint process);
    [DllImport("user32.dll")] static extern nint SendMessageTimeout(nint window,uint message,nint wParam,nint lParam,uint flags,uint timeout,out nint result);
    [DllImport("user32.dll")] static extern bool ClientToScreen(nint window,ref ShellDesktop.POINT point);
    [DllImport("kernel32.dll")] static extern nint OpenProcess(uint access,bool inherit,uint process);
    [DllImport("kernel32.dll")] static extern nint VirtualAllocEx(nint process,nint address,nuint size,uint type,uint protection);
    [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(nint process,nint address,nuint size,uint type);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(nint process,nint address,byte[] data,nuint size,out nuint read);
    [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(nint process,nint address,byte[] data,nuint size,out nuint written);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);
}
