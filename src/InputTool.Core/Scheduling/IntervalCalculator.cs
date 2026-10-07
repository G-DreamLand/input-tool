using InputTool.Core.Abstractions;
using InputTool.Core.Parsing;

namespace InputTool.Core.Scheduling;

/// <summary>
/// 节拍计算实现。对应《软件开发设计文档》§5.3.2 / SRS §5.2.2。
/// </summary>
/// <remarks>
/// 只产出**基础节拍**（含抖动与下限保护），不含换行附加延时：
/// 换行前延时 P 与行间隔延时 L 由 <see cref="InputScheduler"/> 在注入前后分别等待，
/// 从而保证 P 确实发生在回车**之前**、L 发生在回车**之后**。
/// 因此换行单元的实际耗时 = P + b + L，与 SRS §5.2.2 的公式一致。
/// </remarks>
public sealed class IntervalCalculator : IIntervalCalculator
{
    private readonly IRandomSource _random;

    /// <summary>构造节拍计算器。</summary>
    /// <param name="random">随机源；省略时使用系统随机源（单测注入固定序列即可复现抖动）。</param>
    public IntervalCalculator(IRandomSource? random = null)
    {
        _random = random ?? new SystemRandomSource();
    }

    /// <inheritdoc />
    public int NextIntervalMs(InputUnit next, SpeedProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        double baseInterval = profile.IntervalMs;

        if (profile.JitterPercent > 0)
        {
            // r ∈ [-1, +1)，抖动范围 ±J×T（FR-006.4）
            var r = (_random.NextUnit() * 2.0) - 1.0;
            baseInterval = Math.Round(profile.IntervalMs * (1.0 + (profile.JitterPercent * r)));
        }

        // 下限保护：任何情况下不低于 5 ms（SRS §5.2.2）
        return Math.Max((int)baseInterval, SpeedProfileValidator.MinResolvedIntervalMs);
    }
}
