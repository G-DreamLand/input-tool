using InputTool.Core.Parsing;

namespace InputTool.Core.Scheduling;

/// <summary>
/// 时长预估（FR-023）。对应《软件开发设计文档》§5.2.4 / §5.3.4。
/// 公式：<c>字符与功能键单元数 × T + 换行单元数 × (P + T + L)</c>，
/// 抖动按期望值 0 计算。
/// </summary>
public static class DurationEstimator
{
    /// <summary>按给定速度估算总时长。</summary>
    /// <param name="statistics">文本统计。</param>
    /// <param name="profile">速度参数。</param>
    /// <returns>预估总时长。</returns>
    /// <remarks>
    /// 公式（SRS §5.2.4 / FR-023）：
    /// <c>(字符单元 + 功能键单元) × T + 换行单元 × (P + T + L)</c>。
    /// 抖动按期望值 0 计算，因此这是"期望用时"而非上界。
    /// </remarks>
    public static TimeSpan Estimate(TextStatistics statistics, SpeedProfile profile)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(profile);

        var interval = (long)Math.Max(profile.IntervalMs, 0);
        var newlineCost = (long)Math.Max(profile.PreEnterDelayMs, 0) + interval + (long)Math.Max(profile.LineExtraDelayMs, 0);

        var orderedUnits = (long)statistics.CharacterUnits + statistics.FunctionKeyUnits;
        var totalMs = (orderedUnits * interval) + ((long)statistics.NewlineUnits * newlineCost);

        return TimeSpan.FromMilliseconds(totalMs);
    }

    /// <summary>把时长格式化为界面文案，例如"约 2 分 43 秒"。</summary>
    public static string ToDisplayString(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return "--";
        }

        if (duration.TotalHours >= 1)
        {
            return $"约 {(int)duration.TotalHours} 小时 {duration.Minutes} 分";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"约 {duration.Minutes} 分 {duration.Seconds} 秒";
        }

        return $"约 {Math.Max(1, (int)Math.Round(duration.TotalSeconds))} 秒";
    }
}
