namespace InputTool.Core.Abstractions;

/// <summary>
/// 高精度时钟抽象。
/// 依据《软件开发设计文档》§5.4.3 / §5.4.4：调度器使用绝对时间基准
/// （<c>目标时刻(n) = 基准 + Σ 间隔</c>），因此需要时间戳与频率两个原语。
/// 生产实现包装 <see cref="System.Diagnostics.Stopwatch"/>；
/// 单元测试注入可快进的假实现（§12.1）。
/// </summary>
public interface IClock
{
    /// <summary>每秒的计时周期数（对应 <c>Stopwatch.Frequency</c>）。</summary>
    long Frequency { get; }

    /// <summary>当前时间戳（计时周期数），对应 <c>Stopwatch.GetTimestamp()</c>。</summary>
    long GetTimestamp();

    /// <summary>从某个起始时间戳到当前时刻的耗时。</summary>
    TimeSpan GetElapsedTime(long startingTimestamp);
}
