using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace WhaleAlive;
public sealed partial class PetWindow
{
    readonly Canvas todoBubbles = new();
    Rect[] todoLayout=[];
    const double TodoWidth=136,TodoHeight=46,TodoGap=10,TodoArcDepth=44;
    internal static Rect[] TodoArcLayout(Point anchor,Rect work,int count)
    {
        count=Math.Clamp(count,0,4);if(count==0)return [];
        double width=TodoWidth+(count>1?TodoArcDepth:0),height=count*TodoHeight+(count-1)*TodoGap;
        double x=anchor.X-45-width,y=anchor.Y-65-height/2;
        bool onRight=x<work.Left+8&&anchor.X+45+width<=work.Right-8;
        if(onRight)x=anchor.X+45;
        x=Math.Clamp(x,work.Left+8,Math.Max(work.Left+8,work.Right-width-8));
        y=Math.Clamp(y,work.Top+8,Math.Max(work.Top+8,work.Bottom-height-8));
        return Enumerable.Range(0,count).Select(i=>
        {
            double t=count==1?0:(2.0*i/(count-1)-1);
            double bend=count==1?0:TodoArcDepth*t*t;
            return new Rect(x+(onRight?width-TodoWidth-bend:bend),y+i*(TodoHeight+TodoGap),TodoWidth,TodoHeight);
        }).ToArray();
    }
    void PositionTodoBubbles()
    {
        if(viewport.IsEmpty||viewport.Width==0)return;
        var work=DisplayGeometry.WorkArea(this,anchor);
        var local=new Rect(PointFromScreen(ToPixels(work.TopLeft)),PointFromScreen(ToPixels(work.BottomRight)));
        todoLayout=TodoArcLayout(canvasAnchor,local,todoBubbles.Children.Count);
        if(todoLayout.Length==0){todoBubbles.Width=todoBubbles.Height=0;return;}
        double left=todoLayout.Min(r=>r.Left),top=todoLayout.Min(r=>r.Top);
        Canvas.SetLeft(todoBubbles,left);Canvas.SetTop(todoBubbles,top);
        todoBubbles.Width=todoLayout.Max(r=>r.Right)-left;
        todoBubbles.Height=todoLayout.Max(r=>r.Bottom)-top+4;
        todoLayout=todoLayout.Select(r=>new Rect(r.X-left,r.Y-top,r.Width,r.Height)).ToArray();
        AnimateCompanionVisuals();
    }
    void AnimateCompanionVisuals()
    {
        double time=Environment.TickCount64/1000.0;
        for(int i=0;i<todoLayout.Length;i++)
        {
            Canvas.SetLeft(todoBubbles.Children[i],todoLayout[i].X);
            Canvas.SetTop(todoBubbles.Children[i],todoLayout[i].Y+2+Math.Sin(time*1.2+i*.45)*2);
        }
    }
    public event Action<string[],string>? FilesOffered;
    void InitializeCompanionVisuals(Canvas canvas)
    {
        canvas.Children.Add(todoBubbles);Canvas.SetLeft(todoBubbles,0);Canvas.SetTop(todoBubbles,70);
        sprite.AllowDrop = true;
        sprite.DragOver += (_,e)=> {e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Link:DragDropEffects.None;e.Handled=true;};
        sprite.Drop += (_,e)=> {
            if(e.Data.GetData(DataFormats.FileDrop) is not string[] paths)return;
            var menu=new ContextMenu();
            foreach(var choice in new[]{("放进收藏架","favorite"),("留到稍后处理","later")}) {
                var item=new MenuItem{Header=choice.Item1};item.Click+=(_,_)=>FilesOffered?.Invoke(paths,choice.Item2);menu.Items.Add(item);
            }
            menu.PlacementTarget=sprite;menu.IsOpen=true;e.Handled=true;
        };
    }
    public void ShowTodos(IEnumerable<PetNote> notes,Action<string> complete)
    {
        todoBubbles.Children.Clear();
        foreach(var note in notes.Where(t=>!t.Done).Take(4)) {
            var content=new TextBlock{Text="○ "+note.Text,Width=TodoWidth-16,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=32,FontSize=11};
            var b=new Button{Content=content,Width=TodoWidth,Height=TodoHeight,ToolTip="点击完成："+note.Text,Background=new SolidColorBrush(Color.FromArgb(235,216,245,248)),Foreground=new SolidColorBrush(Color.FromRgb(26,74,85)),BorderThickness=new(0),Padding=new(8,5,8,5),FontSize=11};
            b.Tag=note.Id;b.Click+=(_,_)=>complete(note.Id);todoBubbles.Children.Add(b);
        }
        PositionTodoBubbles();
    }
}
