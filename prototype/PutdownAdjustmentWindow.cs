using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace WhaleAlive;

// Opened on demand through the local bridge; it adds no permanent menu entry.
public sealed class PutdownAdjustmentWindow : Window
{
    readonly PetWindow pet;
    readonly PoseCalibration calibration;
    readonly string iconId;
    readonly TextBlock state=new(){Margin=new(0,12,0,12),Foreground=GlassAppearance.Blue};
    readonly StackPanel body=new(){Margin=new(20)};
    bool right;
    public PutdownAdjustmentWindow(PetWindow pet,PoseCalibration calibration,string iconId,bool initialRight=false)
    {
        this.pet=pet;this.calibration=calibration;this.iconId=iconId;right=initialRight;
        Title="放下图标 · 位置微调";Icon=GlassAppearance.AppIcon;Width=420;SizeToContent=SizeToContent.Height;
        ResizeMode=ResizeMode.NoResize;Topmost=true;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Background=GlassAppearance.Surface;Foreground=GlassAppearance.Ink;FontFamily=new("Microsoft YaHei UI");FontSize=14;Content=body;
        body.Children.Add(new TextBlock{Text="调整放下时的站位",FontSize=20,FontWeight=FontWeights.SemiBold});
        body.Children.Add(new TextBlock{Text="直接拖动人物，让放下时的双手对准图标。\n也可用箭头每次微调 1 像素，按住 Shift 为 5 像素。",TextWrapping=TextWrapping.Wrap,Margin=new(0,10,0,0)});
        body.Children.Add(state);
        var arrows=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};
        foreach(var (label,delta) in new[]{("←",new Vector(-1,0)),("↑",new Vector(0,-1)),("↓",new Vector(0,1)),("→",new Vector(1,0))})
            arrows.Children.Add(Button(label,()=>{calibration.Nudge(delta*(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?5:1));return Task.CompletedTask;}));
        body.Children.Add(arrows);
        body.Children.Add(new TextBlock{Text="左右方向分别保存；抱起、坐下的位置不受影响。",TextWrapping=TextWrapping.Wrap,Foreground=GlassAppearance.Muted,Margin=new(0,12,0,12)});
        body.Children.Add(Button("保存当前方向，再调另一边",()=>{calibration.SaveAndSwitchDirection();right=calibration.FacingRight;Refresh();return Task.CompletedTask;}));
        body.Children.Add(Button("保存并完成",async()=>{await calibration.Save();Close();}));
        body.Children.Add(Button("取消当前调整",async()=>{await calibration.Cancel();Close();}));
        calibration.Changed+=Refresh;
        Closed+=async(_,_)=>{calibration.Changed-=Refresh;await calibration.Cancel();};
    }
    Button Button(string title,Func<Task> action)
    {
        var button=new Button{Content=title,Margin=new(3),Padding=new(12,7,12,7)};
        button.Click+=async(_,_)=>{body.IsEnabled=false;try{await action();}catch(Exception ex){state.Text=ex.Message;}finally{body.IsEnabled=true;}};
        return button;
    }
    public async Task Begin(){pet.Face(right);await calibration.StartCarry(true,iconId);Refresh();}
    void Refresh(){if(calibration.Active)state.Text=$"{calibration.TargetName} · 朝{(calibration.FacingRight?"右":"左")}\n横向 {calibration.Offset.X:0.#}，纵向 {calibration.Offset.Y:0.#}";}
}
