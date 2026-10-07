namespace InputTool.Core.Scheduling;

/// <summary>调度器的运行状态（与界面状态机 <c>InputSessionState</c> 不同，这里只描述调度线程自身）。</summary>
public enum SchedulerState
{
    /// <summary>未启动。</summary>
    Idle,

    /// <summary>正在按节拍注入。</summary>
    Running,

    /// <summary>已暂停，等待继续。</summary>
    Paused,

    /// <summary>已请求停止，等待输入线程收尾（≤200 ms）。</summary>
    Stopping,
}

/// <summary>调度结束事件参数（FR-007、FR-017）。</summary>
/// <param name="Outcome">结束结果。</param>
/// <param name="Completed">已完成单元数。</param>
/// <param name="Total">总单元数。</param>
/// <param name="Elapsed">用时。</param>
/// <param name="Skipped">跳过单元数。</param>
/// <param name="Failed">注入失败单元数。</param>
/// <param name="Message">附加说明（错误原因等）。</param>
public sealed record SessionFinishedEventArgs(
    SessionOutcome Outcome,
    int Completed,
    int Total,
    TimeSpan Elapsed,
    int Skipped,
    int Failed,
    string? Message = null);
