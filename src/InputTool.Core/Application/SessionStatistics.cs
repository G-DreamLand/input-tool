using InputTool.Core.Scheduling;

namespace InputTool.Core.Application;

/// <summary>
/// 一次输入会话的统计结果（FR-016、FR-017 文案数据来源）。
/// </summary>
/// <param name="SessionId">会话标识。</param>
/// <param name="TotalUnits">总单元数。</param>
/// <param name="CompletedUnits">已完成单元数。</param>
/// <param name="SkippedUnits">跳过单元数。</param>
/// <param name="FailedUnits">注入失败单元数。</param>
/// <param name="Elapsed">用时。</param>
/// <param name="AverageIntervalMs">平均实际节拍，用于自证 NFR-003。</param>
/// <param name="MaxDeviationMs">单步最大偏差（绝对值）。</param>
/// <param name="TargetWindow">目标窗口句柄（FR-012.4 / FR-020.1）。</param>
/// <param name="StartedAt">开始时刻。</param>
/// <param name="EndedAt">结束时刻。</param>
/// <param name="Outcome">结束结果。</param>
public sealed record SessionStatistics(
    Guid SessionId,
    int TotalUnits,
    int CompletedUnits,
    int SkippedUnits,
    int FailedUnits,
    TimeSpan Elapsed,
    double AverageIntervalMs,
    double MaxDeviationMs,
    IntPtr TargetWindow,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    SessionOutcome Outcome)
{
    /// <summary>完成提示文案：<c>输入完成，共 N 个字符，用时 X 分 Y 秒</c>（FR-017）。</summary>
    public string ToFinishMessage()
        => Outcome switch
        {
            SessionOutcome.Completed => $"输入完成，共 {CompletedUnits} 个字符，用时 {Elapsed.Minutes} 分 {Elapsed.Seconds} 秒",
            SessionOutcome.Stopped => $"已停止，已完成 {CompletedUnits} / {TotalUnits}",
            _ => $"输入中断，已完成 {CompletedUnits} / {TotalUnits}",
        };
}
