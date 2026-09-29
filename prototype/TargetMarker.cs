using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
namespace WhaleAlive;
public sealed class TargetMarker : Window
{
    public TargetMarker(Point point, string label)
    {
        Width = 90; Height = 100; Left = point.X - 10; Top = point.Y - 8; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Content = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(141, 232, 208)), BorderThickness = new(2), CornerRadius = new(12), Child = new TextBlock { Text = label, FontSize = 11, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(30, 50, 60)), Padding = new(5), TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom } };
        SourceInitialized += (_, _) => { var h = new WindowInteropHelper(this).Handle; SetWindowLongPtr(h, -20, GetWindowLongPtr(h, -20) | 0x20 | 0x80 | 0x8000000); };
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern nint SetWindowLongPtr(nint h, int i, nint value);
}
