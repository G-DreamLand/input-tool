namespace InputTool.Core.Scheduling;

/// <summary>
/// 速度参数的边界与归一化（FR-006 参数表、SRS 附录 A）。
/// </summary>
public static class SpeedProfileValidator
{
    /// <summary>
    /// 节拍间隔下限（SRS §5.2.5：T = 0 非法，因此最小为 1 ms）。
    /// </summary>
    /// <remarks>
    /// 调到 1 ms 是为了让"短文本试跑"更快。真实注入速率受 Windows 定时器精度与
    /// 目标程序处理能力限制，低于 <see cref="FastModeWarnThresholdMs"/> 时界面会提示
    /// "极快模式可能被目标程序丢弃按键"，属于用户已知情的选择。
    /// </remarks>
    public const int MinIntervalMs = 1;

    /// <summary>节拍间隔上限。</summary>
    public const int MaxIntervalMs = 5000;

    /// <summary>节拍间隔步进（1 ms，使下限可真正取到）。</summary>
    public const int IntervalStepMs = 1;

    /// <summary>行间隔额外延时的上限与步进。</summary>
    public const int MaxLineExtraDelayMs = 10_000;
    public const int LineExtraDelayStepMs = 50;

    /// <summary>换行前延时的上限与步进。</summary>
    public const int MaxPreEnterDelayMs = 5_000;
    public const int PreEnterDelayStepMs = 50;

    /// <summary>抖动比例上限（50%）。</summary>
    public const double MaxJitterPercent = 0.5;

    /// <summary>抖动步进（5%）。</summary>
    public const double JitterStepPercent = 0.05;

    /// <summary>低于该节拍时提示"极快模式可能被目标程序丢弃按键"（SRS §5.2.5）。</summary>
    public const int FastModeWarnThresholdMs = 20;

    /// <summary>
    /// 单步间隔的下限保护（SRS §5.2.2）。
    /// 由 5 ms 放宽到 1 ms，与 <see cref="MinIntervalMs"/> 对齐，
    /// 否则界面允许填 1 ms、实际注入仍被抬到 5 ms，用户会以为设置没生效。
    /// </summary>
    public const int MinResolvedIntervalMs = 1;

    /// <summary>
    /// 把任意输入归一化到合法范围（越界 clamp、步进对齐）。
    /// </summary>
    /// <param name="profile">原始参数。</param>
    /// <returns>归一化后的参数。</returns>
    public static SpeedProfile Normalize(SpeedProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new SpeedProfile(
            IntervalMs: ClampToStep(profile.IntervalMs, MinIntervalMs, MaxIntervalMs, IntervalStepMs),
            JitterPercent: ClampToStep(profile.JitterPercent, 0, MaxJitterPercent, JitterStepPercent),
            LineExtraDelayMs: ClampToStep(profile.LineExtraDelayMs, 0, MaxLineExtraDelayMs, LineExtraDelayStepMs),
            PreEnterDelayMs: ClampToStep(profile.PreEnterDelayMs, 0, MaxPreEnterDelayMs, PreEnterDelayStepMs),
            NewlineStrategy: profile.NewlineStrategy);
    }

    /// <summary>是否存在被修正的字段（用于界面提示"输入非法值已自动纠正"）。</summary>
    public static bool NeedsCorrection(SpeedProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Normalize(profile) != profile;
    }

    private static int ClampToStep(int value, int min, int max, int step)
    {
        var clamped = Math.Clamp(value, min, max);
        var rounded = (int)Math.Round(clamped / (double)step, MidpointRounding.AwayFromZero) * step;
        return Math.Clamp(rounded, min, max);
    }

    private static double ClampToStep(double value, double min, double max, double step)
    {
        // NaN 视为下限，避免污染后续计算
        if (double.IsNaN(value))
        {
            value = min;
        }

        var clamped = Math.Clamp(value, min, max);
        var rounded = Math.Round(clamped / step, MidpointRounding.AwayFromZero) * step;
        return Math.Clamp(Math.Round(rounded, 4), min, max);
    }
}
