namespace InputTool.Core.Scheduling;

/// <summary>会话结束的三种结果。</summary>
public enum SessionOutcome
{
    /// <summary>全部单元正常输入完毕。</summary>
    Completed,

    /// <summary>被用户停止 / 急停 / 焦点守护停止。</summary>
    Stopped,

    /// <summary>因致命错误终止（不可注入、参数错误等）。</summary>
    Error,
}

/// <summary>
/// 进度快照。对应《软件开发设计文档》§5.4.7 与 FR-016。
/// 上报频率不超过 10 次/秒（FR-016.5）。
/// </summary>
/// <param name="Completed">已完成单元数。</param>
/// <param name="Total">总单元数。</param>
/// <param name="Elapsed">已用时。</param>
/// <param name="Remaining">剩余时间估算。</param>
/// <param name="EstimatedEnd">预计结束时刻。</param>
/// <param name="Skipped">已跳过单元数。</param>
/// <param name="Failed">注入失败单元数。</param>
public sealed record ProgressInfo(
    int Completed,
    int Total,
    TimeSpan Elapsed,
    TimeSpan Remaining,
    DateTimeOffset EstimatedEnd,
    int Skipped,
    int Failed)
{
    /// <summary>完成百分比（0–100）。</summary>
    public double Percent => Total > 0 ? Completed * 100.0 / Total : 0.0;

    /// <summary>界面文案：<c>已输入 128 / 800 字符 (16.0%)</c>（FR-016.2）。</summary>
    public string ToDisplayString()
        => $"已输入 {Completed} / {Total} 字符 ({Percent:F1}%)";
}
