using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
namespace WhaleAlive;
public static class GlassAppearance
{
    public static Brush Ink {get;} = ColorBrush("#243C68");
    public static Brush Muted {get;} = ColorBrush("#607798");
    public static Brush Blue {get;} = ColorBrush("#3979EF");
    public static Brush Line {get;} = ColorBrush("#D3E2F7");
    public static Brush Surface {get;} = ColorBrush("#EAF3FF");
    public static ImageSource Logo {get;} = CreateLogo();
    public static ImageSource AppIcon {get;} = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/WhaleAlive;component/Branding/WhaleAlive.ico"));
    static ImageSource CreateLogo()
    {
        var source=(DrawingImage)Application.LoadComponent(new Uri("/WhaleAlive;component/Branding/WhaleLogo.xaml",UriKind.Relative));
        source.Freeze();return source;
    }
    public static Brush ColorBrush(string color) {var b=(SolidColorBrush)new BrushConverter().ConvertFromString(color)!;b.Freeze();return b;}
    public static bool Apply(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;
        if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,22621)||handle==0||SystemParameters.HighContrast)return false;
        int light=0,round=2,backdrop=3;
        DwmSetWindowAttribute(handle,20,ref light,sizeof(int));DwmSetWindowAttribute(handle,33,ref round,sizeof(int));
        if(DwmSetWindowAttribute(handle,38,ref backdrop,sizeof(int))!=0)return false;
        var margins=new Margins{Left=-1,Right=-1,Top=-1,Bottom=-1};DwmExtendFrameIntoClientArea(handle,ref margins);
        if(HwndSource.FromHwnd(handle)?.CompositionTarget is { } target)target.BackgroundColor=Colors.Transparent;
        window.Background=Brushes.Transparent;return true;
    }
    [StructLayout(LayoutKind.Sequential)]struct Margins{public int Left,Right,Top,Bottom;}
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(nint hwnd,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")]static extern int DwmExtendFrameIntoClientArea(nint hwnd,ref Margins margins);
}
