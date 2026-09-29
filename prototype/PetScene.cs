using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
namespace WhaleAlive;
public sealed class PetScene : Window,IDisposable
{
    readonly Point point;
    public PetScene(Point physical,string label,ImageSource?[] icons,bool ring=false)
    {
        point=physical;Width=240;Height=100;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ShowActivated=false;IsHitTestVisible=false;
        var canvas=new Canvas();Content=canvas;
        if(ring){var shape=new Ellipse{Width=54,Height=54,Stroke=Brushes.Aquamarine,StrokeThickness=3};Canvas.SetLeft(shape,93);Canvas.SetTop(shape,20);canvas.Children.Add(shape);}
        else for(int i=0;i<icons.Length;i++){var image=new Image{Source=icons[i],Width=36,Height=36};Canvas.SetLeft(image,25+i*50);Canvas.SetTop(image,35);canvas.Children.Add(image);}
        var text=new TextBlock{Text=label,Foreground=Brushes.White,Background=new SolidColorBrush(Color.FromArgb(210,30,46,64)),Padding=new(7),FontSize=11};Canvas.SetLeft(text,65);Canvas.SetTop(text,78);canvas.Children.Add(text);
        SourceInitialized+=(_,_)=>{var h=new WindowInteropHelper(this).Handle;SetWindowLongPtr(h,-20,GetWindowLongPtr(h,-20)|0x20|0x80|0x8000000);Place();};
        DpiChanged+=(_,_)=>Dispatcher.BeginInvoke(Place);
    }
    void Place(){var d=VisualTreeHelper.GetDpi(this);SetWindowPos(new WindowInteropHelper(this).Handle,0,(int)(point.X-120*d.DpiScaleX),(int)(point.Y-47*d.DpiScaleY),0,0,0x15);}
    public void Dispose()=>Close();
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern nint GetWindowLongPtr(nint h,int i);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern nint SetWindowLongPtr(nint h,int i,nint v);
    [DllImport("user32.dll")]static extern bool SetWindowPos(nint h,nint after,int x,int y,int w,int height,uint flags);
}
