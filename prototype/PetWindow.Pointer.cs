using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
namespace WhaleAlive;

public sealed partial class PetWindow
{
    internal const double LiftHoldMilliseconds=300;
    readonly DispatcherTimer holdTimer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    long pressedAt;
    bool primaryPressed,primaryWasDrag,clickMoved;
    Point pressOrigin,pressPointer;
    public event Action? Clicked;
    public bool IsPressing=>primaryPressed;
    void InitializePointer()
    {
        sprite.MouseLeftButtonDown+=(_,e)=>
        {
            var point=PointToScreen(e.GetPosition(this));
            if(sprite.CaptureMouse())BeginPrimaryPress(point);
            e.Handled=true;
        };
        sprite.MouseMove+=(_,e)=>{if(primaryPressed)MovePrimaryPress(PointToScreen(e.GetPosition(this)));};
        sprite.MouseLeftButtonUp+=(_,e)=>{CompletePrimaryPress();e.Handled=true;};
        sprite.LostMouseCapture+=(_,_)=>CancelPrimaryPress();
        sprite.MouseRightButtonUp+=(_,e)=>{CancelPrimaryPress();OpenMenu?.Invoke();e.Handled=true;};
        holdTimer.Tick+=(_,_)=>
        {
            if(!primaryPressed){holdTimer.Stop();return;}
            if(sprite.IsMouseCaptured&&Mouse.LeftButton==MouseButtonState.Pressed)AdvancePrimaryPress();
        };
    }
    internal void BeginPrimaryPress(Point pointer)
    {
        if(primaryPressed)return;
        primaryPressed=true;primaryWasDrag=false;clickMoved=false;pressedAt=Stopwatch.GetTimestamp();pressPointer=pressOrigin=pointer;
        if(CalibrationDrag!=null){primaryWasDrag=true;BeginDragAt(pointer);return;}
        holdTimer.Start();
    }
    internal void MovePrimaryPress(Point pointer)
    {
        if(!primaryPressed)return;
        pressPointer=pointer;
        if((pointer-pressOrigin).Length>ToPixels(new Point(8,0)).X)clickMoved=true;
        if(dragging)MoveDragTo(pointer);
    }
    internal void AdvancePrimaryPress()
    {
        if(!primaryPressed||primaryWasDrag)return;
        if(Stopwatch.GetElapsedTime(pressedAt).TotalMilliseconds<LiftHoldMilliseconds)return;
        holdTimer.Stop();primaryWasDrag=true;BeginDragAt(pressPointer);
    }
    internal void CompletePrimaryPress()
    {
        if(!primaryPressed)return;
        // A busy dispatcher may deliver button-up before the hold timer callback.
        AdvancePrimaryPress();bool click=!primaryWasDrag&&!clickMoved;
        primaryPressed=false;holdTimer.Stop();EndDrag();sprite.ReleaseMouseCapture();
        if(click){if(Clicked!=null)Clicked.Invoke();else ShowTaskProgress("还没连接到 DSH\n连接后点我查看任务进度。");}
    }
    internal void CancelPrimaryPress()
    {
        primaryPressed=false;holdTimer.Stop();EndDrag();sprite.ReleaseMouseCapture();
    }
    public void ShowTaskProgress(string text)=>ShowSpeech(text,3);
    public void RefreshTaskProgress(string text){if(speechPriority==3&&SpeechVisible)speech.Text=text;}
}
