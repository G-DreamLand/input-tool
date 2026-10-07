using InputTool.Core.Parsing;

namespace InputTool.Core.Scheduling;

/// <summary>
/// 速度参数快照（FR-006）。不可变，会话进行中通过整体替换生效——下一拍即按新速度执行。
/// </summary>
/// <param name="IntervalMs">基础节拍 T（10–5000 ms，默认 200）。</param>
/// <param name="JitterPercent">抖动比例 J（0–0.5，默认 0）。</param>
/// <param name="LineExtraDelayMs">行间隔额外延时 L（0–10000 ms），在回车发送完成后等待。</param>
/// <param name="PreEnterDelayMs">换行前延时 P（0–5000 ms），在回车发送之前等待。</param>
/// <param name="NewlineStrategy">换行策略（FR-009）。</param>
/// <remarks>
/// 说明：SRS §9.2 的示意记录中还包含 <c>AppendTrailingEnter</c>。本设计把它归入
/// <see cref="NormalizeOptions.AppendTrailingEnter"/>（属于解析层职责，决定是否补一个换行单元），
/// 避免同一开关在两个对象里重复表达。
/// </remarks>
public sealed record SpeedProfile(
    int IntervalMs = 200,
    double JitterPercent = 0,
    int LineExtraDelayMs = 0,
    int PreEnterDelayMs = 0,
    NewlineStrategy NewlineStrategy = NewlineStrategy.Enter)
{
    /// <summary>默认速度：每 0.2 秒一个字符，无抖动、无额外延时（SRS 附录 A）。</summary>
    public static SpeedProfile Default { get; } = new();

    /// <summary>由节拍间隔换算的字符/秒（FR-006.5）。</summary>
    public double CharactersPerSecond => IntervalMs > 0 ? 1000.0 / IntervalMs : 0.0;

    /// <summary>由字符/秒换算节拍间隔，四舍五入到整数毫秒。</summary>
    public static int IntervalFromCps(double cps)
        => cps > 0 ? (int)Math.Round(1000.0 / cps) : SpeedProfileValidator.MaxIntervalMs;
}
