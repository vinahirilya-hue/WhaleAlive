namespace WhaleAlive;

// The bridge currently reports stages, not a step count or a completion percentage.
public sealed class DshProgress(Func<DateTimeOffset>? clock=null)
{
    readonly Func<DateTimeOffset> now=clock??(()=>DateTimeOffset.UtcNow);
    DateTimeOffset? received;
    string state="idle",lastOutcome="";
    public void Receive(string value)
    {
        state=value;received=now();
        if(value is "think" or "working" or "wait")lastOutcome="";
        else if(value is "celebrate" or "error")lastOutcome=value;
    }
    public string Text
    {
        get
        {
            if(received==null)return "还没连接到 DSH\n连接后点我查看任务进度。";
            if(now()-received.Value>TimeSpan.FromSeconds(8))return "DSH 连接暂时中断\n暂时无法获取最新任务进度。";
            return state switch
            {
                "think"=>"DSH · 正在思考\n正在分析任务、规划下一步。",
                "working"=>"DSH · 正在执行\n正在调用工具处理任务。",
                "wait"=>"DSH · 等待你确认\n回到 DSH 查看需要处理的事项。",
                "celebrate"=>"DSH · 本轮任务已完成",
                "error"=>"DSH · 执行遇到问题\n回到 DSH 查看具体原因。",
                _=>lastOutcome=="celebrate"?"DSH · 最近一轮已完成\n当前没有正在执行的任务。":lastOutcome=="error"?"DSH · 当前空闲\n最近一次执行出现异常。":"DSH · 当前空闲\n没有正在执行的任务。"
            };
        }
    }
}
