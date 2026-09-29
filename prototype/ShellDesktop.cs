using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation;

namespace WhaleAlive;
public record DesktopIcon(string Id, string Name, int X, int Y, ImageSource? Image) { public override string ToString() => Name; }

// Shell's supported IFolderView API; no simulated dragging.
public sealed class ShellDesktop : IDisposable
{
    readonly object windows, dispatch, browser, view;
    readonly IFolderView folderView;
    readonly nint hwnd;
    public bool AutoArrange => folderView.GetAutoArrange() == 0;
    public ShellDesktop()
    {
        windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"))!)!;
        object loc = 0, empty = Type.Missing; int handle;
        dispatch = ((dynamic)windows).FindWindowSW(ref loc, ref empty, 8, out handle, 1);
        var sid = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"); var iid = typeof(IShellBrowser).GUID;
        ((IServiceProvider)dispatch).QueryService(ref sid, ref iid, out browser);
        ((IShellBrowser)browser).QueryActiveShellView(out view);
        folderView = (IFolderView)view;
        ((IShellView)view).GetWindow(out hwnd);
    }
    public List<DesktopIcon> List(bool includeImages = true)
    {
        folderView.ItemCount(2, out int count); var result = new List<DesktopIcon>();
        for (int n = 0; n < count; n++)
        {
            folderView.Item(n, out nint pidl);
            try
            {
                var iid = typeof(IShellItem).GUID;
                SHCreateItemFromIDList(pidl, ref iid, out var item);
                try
                {
                    item.GetDisplayName(0, out nint namePtr); string name = Marshal.PtrToStringUni(namePtr)!; Marshal.FreeCoTaskMem(namePtr);
                    item.GetDisplayName(0x80028000, out nint idPtr); string id = Marshal.PtrToStringUni(idPtr)!; Marshal.FreeCoTaskMem(idPtr);
                    folderView.GetItemPosition(pidl, out var p);
                    result.Add(new(id, name, p.X, p.Y, includeImages ? GetImage(pidl) : null));
                }
                finally { Marshal.ReleaseComObject(item); }
            }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }
        return result;
    }
    public Point ToScreen(int x, int y) { var p = new POINT(x, y); ClientToScreen(hwnd, ref p); return new(p.X, p.Y); }
    public IReadOnlyList<IconGeometry> IconRectangles() => DesktopIconGeometry.Read(hwnd);
    // UI Automation supplies the actual item bounds in physical screen pixels;
    // this avoids assuming a desktop icon size or grid spacing.
    public Dictionary<string, Point> VisibleSeats()
    {
        var result = new Dictionary<string, Point>();
        if (!IsWindowVisible(hwnd)) return result;
        var geometry = IconRectangles();
        var items = AutomationElement.FromHandle(hwnd).FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        foreach (AutomationElement item in items)
        {
            var r = item.Current.BoundingRectangle;
            if (r.IsEmpty || r.Width <= 0 || r.Height <= 0 || item.Current.IsOffscreen) continue;
            var native = geometry.FirstOrDefault(g => Math.Abs(g.Item.Left-r.Left)<2 && Math.Abs(g.Item.Top-r.Top)<2 && Math.Abs(g.Item.Width-r.Width)<2);
            if (native is null) continue;
            var p = new Point(native.Icon.Left + native.Icon.Width / 2, native.Icon.Top);
            nint hit = WindowFromPoint(new POINT((int)p.X, (int)p.Y + 12));
            if (hit != hwnd && !IsChild(hwnd, hit)) continue;
            // Duplicate display names are deliberately not guessed.
            string name = item.Current.Name;
            if (result.ContainsKey(name)) result[name] = new(double.NaN, double.NaN);
            else result[name] = p;
        }
        return result.Where(kv => !double.IsNaN(kv.Value.X)).ToDictionary(kv => kv.Key, kv => kv.Value);
    }
    public POINT FromScreen(int x, int y) { var p = new POINT(x, y); ScreenToClient(hwnd, ref p); return p; }
    public bool Exposed(Point screen){nint hit=WindowFromPoint(new POINT((int)screen.X,(int)screen.Y));return hit==hwnd||IsChild(hwnd,hit);}
    public POINT Move(string id, int x, int y)
    {
        if (AutoArrange) throw new InvalidOperationException("桌面开启了自动排列图标，请先在桌面右键 → 查看中关闭自动排列。");
        folderView.ItemCount(2, out int count);
        for (int n = 0; n < count; n++)
        {
            folderView.Item(n, out nint pidl);
            try
            {
                var iid = typeof(IShellItem).GUID; SHCreateItemFromIDList(pidl, ref iid, out var item);
                string candidate;
                try { item.GetDisplayName(0x80028000, out nint ptr); candidate = Marshal.PtrToStringUni(ptr)!; Marshal.FreeCoTaskMem(ptr); }
                finally { Marshal.ReleaseComObject(item); }
                if (candidate != id) continue;
                var points = new[] { new POINT(x, y) };
                folderView.SelectAndPositionItems(1, new[] { pidl }, points, 0x80);
                folderView.GetItemPosition(pidl, out var actual);
                return actual;
            }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }
        throw new InvalidOperationException("目标图标已重命名或移走，请刷新图标列表。");
    }
    public async Task<POINT> MoveSettled(string id, int x, int y)
    {
        Move(id, x, y); POINT last = new(int.MinValue, int.MinValue); int stable = 0;
        // Explorer may apply grid snapping asynchronously after SelectAndPositionItems.
        // Once committed, finish journaling even if cancellation arrives during settling.
        for (int n = 0; n < 20; n++)
        {
            await Task.Delay(100); var item = List(false).SingleOrDefault(i => i.Id == id) ?? throw new InvalidOperationException("移动后目标消失；恢复记录已保留。");
            if (item.X == last.X && item.Y == last.Y) stable++; else stable = 0;
            last = new(item.X, item.Y); if (n >= 5 && stable >= 3) return last;
        }
        throw new InvalidOperationException("桌面图标位置仍在变化，恢复记录已保留。");
    }
    static ImageSource? GetImage(nint pidl)
    {
        SHFILEINFO info = new();
        if (SHGetFileInfo(pidl, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x108) == 0 || info.Icon == 0) return null;
        try { var img = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); img.Freeze(); return img; }
        finally { DestroyIcon(info.Icon); }
    }
    public static ImageSource? ImageForPath(string path)
    {
        SHFILEINFO info = new();
        if (SHGetFileInfoPath(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100) == 0 || info.Icon == 0) return null;
        try { var image=Imaging.CreateBitmapSourceFromHIcon(info.Icon,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());image.Freeze();return image; }
        finally { DestroyIcon(info.Icon); }
    }
    public void Dispose() { foreach (var obj in new[] { view, browser, dispatch, windows }) if (Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct SHFILEINFO { public nint Icon; public int Index; public uint Attributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName; }
    [DllImport("shell32.dll", PreserveSig = false)] static extern void SHCreateItemFromIDList(nint pidl, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern nint SHGetFileInfo(nint pidl, uint attr, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("shell32.dll", EntryPoint="SHGetFileInfoW", CharSet=CharSet.Unicode)] static extern nint SHGetFileInfoPath(string path,uint attr,ref SHFILEINFO info,uint size,uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] static extern bool ClientToScreen(nint hwnd, ref POINT p);
    [DllImport("user32.dll")] static extern bool ScreenToClient(nint hwnd, ref POINT p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] static extern bool IsChild(nint parent, nint child);
    [DllImport("user32.dll")] static extern nint WindowFromPoint(POINT p);
}
[ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IServiceProvider { void QueryService(ref Guid sid, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object obj); }
[ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IShellView { void GetWindow(out nint hwnd); void ContextSensitiveHelp(bool enter); }
[ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellBrowser
{
    void GetWindow(out nint hwnd); void ContextSensitiveHelp(bool enter); void InsertMenusSB(); void SetMenuSB(); void RemoveMenusSB(); void SetStatusTextSB(); void EnableModelessSB(); void TranslateAcceleratorSB(); void BrowseObject(); void GetViewStateStream(); void GetControlWindow(); void SendControlMsg(); void QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out object view);
}
[ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IFolderView
{
    void GetCurrentViewMode(out uint mode); void SetCurrentViewMode(uint mode); void GetFolder(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object folder); void Item(int index, out nint pidl); void ItemCount(uint flags, out int count); void Items(uint flags, ref Guid iid, out nint items); void GetSelectionMarkedItem(out int item); void GetFocusedItem(out int item); void GetItemPosition(nint pidl, out ShellDesktop.POINT point); void GetSpacing(out ShellDesktop.POINT p); void GetDefaultSpacing(out ShellDesktop.POINT p); [PreserveSig] int GetAutoArrange(); void SelectItem(int item, uint flags); void SelectAndPositionItems(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] nint[] pidls, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ShellDesktop.POINT[] points, uint flags);
}
[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellItem
{
    void BindToHandler(nint ctx, ref Guid bhid, ref Guid iid, out nint obj); void GetParent(out IShellItem parent); void GetDisplayName(uint kind, out nint name); void GetAttributes(uint mask, out uint attr); void Compare(IShellItem other, uint hint, out int order);
}
