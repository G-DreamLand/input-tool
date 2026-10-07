using InputTool.Core.Interop;

namespace InputTool.Core.Injection;

/// <summary>
/// 定时器精度提升作用域（§5.4.6）。
/// 进入输入会话时调用 <c>timeBeginPeriod(1)</c>，退出时在 <see cref="Dispose"/> 中
/// 还原 <c>timeEndPeriod(1)</c>，避免空闲时也影响系统功耗（NFR-002）。
/// </summary>
public sealed class TimerResolutionScope : IDisposable
{
    private bool _disposed;

    private TimerResolutionScope(uint periodMs)
    {
        PeriodMs = periodMs;
        NativeMethods.TimeBeginPeriod(periodMs);
    }

    /// <summary>本次提升的精度（毫秒）。</summary>
    public uint PeriodMs { get; }

    /// <summary>开始提升定时器精度。</summary>
    /// <param name="periodMs">目标精度（毫秒），默认 1。</param>
    public static TimerResolutionScope Begin(uint periodMs = 1) => new(periodMs);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NativeMethods.TimeEndPeriod(PeriodMs);
    }
}
