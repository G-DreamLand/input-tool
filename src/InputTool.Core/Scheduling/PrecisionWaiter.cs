using System.Diagnostics;

namespace InputTool.Core.Scheduling;

/// <summary>
/// 高精度等待实现。对应《软件开发设计文档》§5.4.4。
/// </summary>
/// <remarks>
/// 策略：剩余 &gt; 6 ms 时粗睡（配合 <c>timeBeginPeriod(1)</c> 实为 1–2 ms），
/// 2–6 ms 时让出时间片，最后 &lt; 2 ms 用忙等收尾，兼顾精度与 CPU 占用（NFR-002 / NFR-003）。
/// <para>
/// 停止响应：循环**每一轮**都检查取消委托，因此即使处于 10 秒的长延时中，
/// 急停也能在毫秒级生效（NFR-006：≤ 200 ms）。
/// </para>
/// </remarks>
public sealed class PrecisionWaiter : IPrecisionWaiter
{
    private static readonly long Frequency = Stopwatch.Frequency;

    /// <inheritdoc />
    public bool WaitUntil(long targetTimestamp, Func<bool> cancelRequested)
    {
        ArgumentNullException.ThrowIfNull(cancelRequested);

        while (true)
        {
            if (cancelRequested())
            {
                return false;
            }

            var remainingTicks = targetTimestamp - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0)
            {
                return true;
            }

            var remainingMs = remainingTicks * 1000.0 / Frequency;

            if (remainingMs > 6)
            {
                Thread.Sleep(1);
            }
            else if (remainingMs > 2)
            {
                Thread.Sleep(0);
            }
            else if (remainingMs > 0.8)
            {
                Thread.Yield();
            }
            else
            {
                Thread.SpinWait(40);
            }
        }
    }

    /// <inheritdoc />
    public bool Delay(int milliseconds, Func<bool> cancelRequested)
    {
        ArgumentNullException.ThrowIfNull(cancelRequested);

        if (milliseconds <= 0)
        {
            return !cancelRequested();
        }

        var deadline = Stopwatch.GetTimestamp() + MsToTicks(milliseconds);
        return WaitUntil(deadline, cancelRequested);
    }

    internal static long MsToTicks(int milliseconds) => (long)(milliseconds / 1000.0 * Frequency);

    internal static double TicksToMs(long ticks) => ticks * 1000.0 / Frequency;
}
