using InputTool.Core.Abstractions;
using InputTool.Core.Parsing;

namespace InputTool.Core.Scheduling;

/// <summary>
/// 单步间隔计算。对应《软件开发设计文档》§5.3.2 / SRS §5.2.2。
/// </summary>
/// <remarks>
/// 只负责**基础节拍**（含抖动与下限保护），不包含换行附加延时：
/// 换行前延时 P 与行间隔延时 L 由调度器在注入前后分别等待，
/// 从而保证 P 发生在回车之前、L 发生在回车之后；
/// 合并看，换行单元耗时 = P + b + L，与 SRS §5.2.2 公式一致。
/// 抽成接口是为了让单测注入固定随机源后断言间隔可复现（§12.1）。
/// </remarks>
public interface IIntervalCalculator
{
    /// <summary>计算"下一个单元"的基础节拍（毫秒，含抖动与下限保护）。</summary>
    int NextIntervalMs(InputUnit next, SpeedProfile profile);
}

/// <summary>
/// 高精度等待。对应《软件开发设计文档》§5.4.4。
/// 实现须保证：等待循环每一轮都检查取消委托，
/// 这是"急停 ≤ 200 ms"的基础（NFR-006）。
/// </summary>
public interface IPrecisionWaiter
{
    /// <summary>
    /// 等待到指定时间戳。命中目标时刻返回 true；被取消请求打断返回 false。
    /// </summary>
    bool WaitUntil(long targetTimestamp, Func<bool> cancelRequested);

    /// <summary>等待指定毫秒数；被取消请求打断返回 false。</summary>
    bool Delay(int milliseconds, Func<bool> cancelRequested);
}

/// <summary>
/// 高精度时钟的默认实现：包装 <see cref="System.Diagnostics.Stopwatch"/>。
/// </summary>
public sealed class StopwatchClock : IClock
{
    /// <inheritdoc />
    public long Frequency => System.Diagnostics.Stopwatch.Frequency;

    /// <inheritdoc />
    public long GetTimestamp() => System.Diagnostics.Stopwatch.GetTimestamp();

    /// <inheritdoc />
    public TimeSpan GetElapsedTime(long startingTimestamp) => System.Diagnostics.Stopwatch.GetElapsedTime(startingTimestamp);
}
