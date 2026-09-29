using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace WhaleAlive;

public static class DragSurfaceVerification
{
    [StructLayout(LayoutKind.Sequential)]struct RectI{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct PointI{public int X,Y;public PointI(int x,int y){X=x;Y=y;}}
    [DllImport("user32.dll")]static extern bool GetWindowRect(nint h,out RectI rect);
    [DllImport("user32.dll")]static extern nint WindowFromPoint(PointI point);
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    internal static RenderTargetBitmap Crop(PetWindow pet,Point local,int width=280,int height=275)
    {
        pet.UpdateLayout();var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())dc.DrawRectangle(new VisualBrush((Visual)pet.Content){ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(local.X-140,local.Y-195,width,height),Stretch=Stretch.Fill},null,new Rect(0,0,width,height));
        var result=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);result.Render(visual);return result;
    }
    static int OpaquePixels(BitmapSource source){var bytes=new byte[source.PixelWidth*source.PixelHeight*4];source.CopyPixels(bytes,source.PixelWidth*4,0);int count=0;for(int i=3;i<bytes.Length;i+=4)if(bytes[i]>8)count++;return count;}
    public static async Task Run()
    {
        var pet=new PetWindow();pet.Show();pet.Say("");
        try
        {
            await Task.Delay(100);var hwnd=new WindowInteropHelper(pet).Handle;GetWindowRect(hwnd,out var original);
            var work=DisplayGeometry.WorkArea(pet,pet.Anchor);var start=new Point(work.Left+work.Width*.3,work.Top+work.Height*.55);
            var evidence=new List<object>();double maxMilliseconds=0;
            foreach(string pose in new[]{"idle","walk","sit_idle","lie_sleep"})
            foreach(bool right in new[]{false,true})
            for(int repeat=0;repeat<3;repeat++)
            {
                pet.SetAnchor(start);pet.Face(right);pet.SetState(pose,true);await Task.Delay(35);pet.UpdateLayout();
                var oldContact=pet.PointFromScreen(pet.ToPixels(start));
                var pointer=pet.ToPixels(start+new Vector(0,-60));
                var clock=System.Diagnostics.Stopwatch.StartNew();pet.BeginDragAt(pointer);maxMilliseconds=Math.Max(maxMilliseconds,clock.Elapsed.TotalMilliseconds);
                await Task.Delay(35);pet.UpdateLayout();
                Check((pet.RenderedContactScreen-pointer).Length<2,"Lift nape slipped");
                Check(!pet.SpeechVisible&&!pet.TodoBubblesVisible,"Lift bubble returned");
                // Move far enough to have disjoint old/new pixel regions.
                var destination=pointer+new Vector(450,0);pet.MoveDragTo(destination);await Task.Delay(40);pet.UpdateLayout();
                Check((pet.RenderedContactScreen-destination).Length<2,"Dragging lost pointer contact");
                Check(OpaquePixels(Crop(pet,oldContact))==0,"Old sprite pixels survived in the previous region");
                Check(OpaquePixels(Crop(pet,pet.PointFromScreen(destination)))>100,"Lifted sprite vanished");
                pet.EndDrag();await Task.Delay(40);pet.UpdateLayout();
                Check(pet.AnimationKey=="idle"&&!pet.IsDragging,"Drop did not finish");
                Check((pet.RenderedContactScreen-pet.ToPixels(pet.Anchor)).Length<2,"Drop feet slipped");
                GetWindowRect(hwnd,out var current);Check(current.Equals(original),"Native surface moved during a pose transition");
                Check(OpaquePixels(Crop(pet,oldContact))==0,"Drop restored old sprite pixels");
            }
            evidence.Add(new{test="24-grab-move-drop-cycles-from-four-poses-both-directions",pass=true,maxMilliseconds});
            pet.PoseOffset=(key,right)=>(key=="sit"?PoseCorrection.DefaultSeat:key=="lie_sleep"?PoseCorrection.DefaultSleep:new PoseCorrection(0,0)).ForDirection(right);
            var strip=new DrawingVisual();
            using(var dc=strip.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White,null,new Rect(0,0,840,550));int row=0;
                foreach(var key in new[]{"sit","lie_sleep"})
                {
                    foreach(bool facing in new[]{false,true})
                    {
                        pet.Face(facing);pet.SetAnchor(start);pet.CalibrationFrame=0;pet.SetState(key,true);pet.UpdateLayout();
                        var origin=pet.RenderedSpriteOrigin;
                        Check((pet.RenderedContactScreen-pet.ToPixels(pet.SurfaceStandingPosition(start,key,facing))).Length<1,"Approach point does not match first-frame feet");
                        int last=key=="lie_sleep"?pet.Clips[key].LoopEnd:pet.Clips[key].LoopStart;
                        for(int f=0;f<=last;f++)
                        {
                            pet.CalibrationFrame=f;pet.SetState(key,true);pet.UpdateLayout();
                            Check((pet.RenderedSpriteOrigin-origin).Length<.01,"Surface entry or sleeping breath adds translation to source animation");
                        }
                    }
                    pet.Face(false);int col=0;
                    foreach(int f in new[]{0,pet.Clips[key].LoopStart/2,pet.Clips[key].LoopStart})
                    {
                        pet.CalibrationFrame=f;pet.SetState(key,true);pet.UpdateLayout();
                        dc.DrawEllipse(Brushes.CornflowerBlue,null,new Point(col*280+140,row*275+195),5,5);
                        dc.DrawImage(Crop(pet,pet.PointFromScreen(pet.ToPixels(start))),new Rect(col*280,row*275,280,275));col++;
                    }
                    row++;
                }
            }
            var stripBitmap=new RenderTargetBitmap(840,550,96,96,PixelFormats.Pbgra32);stripBitmap.Render(strip);var stripPng=new PngBitmapEncoder();stripPng.Frames.Add(BitmapFrame.Create(stripBitmap));using(var stream=File.Create(Path.Combine(AppContext.BaseDirectory,"surface-entry-strip.png")))stripPng.Save(stream);
            pet.CalibrationFrame=null;pet.PoseOffset=null;pet.SetAnchor(start+new Vector(450,0));pet.SetState("idle",true);
            evidence.Add(new{test="sit-sleep-approach-feet-and-no-entry-or-breathing-slide-both-directions",pass=true});
            await Task.Delay(150);
            var empty=pet.ToPixels(start-new Vector(50,180));
            Check(WindowFromPoint(new((int)empty.X,(int)empty.Y))!=hwnd,"Transparent desktop region captured input");
            evidence.Add(new{test="native-transparent-region-click-through",pass=true});
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(Crop(pet,pet.PointFromScreen(pet.ToPixels(pet.Anchor)))));
            using(var stream=File.Create(Path.Combine(AppContext.BaseDirectory,"drop-surface.png")))png.Save(stream);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"drag-surface-verification.json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{pet.Close();}
    }
}
