using InputTool.Core.Abstractions;
using InputTool.Core.Scheduling;

namespace InputTool.Tests.TestDoubles;

/// <summary>
/// 可控时钟：1 个 tick = 1 毫秒，便于对节拍做精确断言。
/// </summary>
public sealed class FakeClock : IClock
{
    private long _now;

    /// <inheritdoc />
    public long Frequency => 1000;

    /// <inheritdoc />
    public long GetTimestamp() => _now;

    /// <inheritdoc />
    public TimeSpan GetElapsedTime(long startingTimestamp) => TimeSpan.FromMilliseconds(_now - startingTimestamp);

    /// <summary>把时钟向前推进。</summary>
    public void Advance(long milliseconds) => _now += milliseconds;

    /// <summary>重置到指定时刻。</summary>
    public void SetTo(long milliseconds) => _now = milliseconds;
}

/// <summary>
/// 假等待器：不真的睡，而是直接推进 <see cref="FakeClock"/>，让调度器测试在毫秒级完成。
/// </summary>
/// <remarks>
/// 记录每次等待的「目标时刻」与「当时时刻」，并可在等待前后插入回调——
/// 这是断言"暂停恢复后重建时间基准""急停能打断等待""落后时进入追赶模式"的关键。
/// </remarks>
public sealed class FakeWaiter : IPrecisionWaiter
{
    private readonly FakeClock _clock;
    private int _waitCalls;
    private int _delayCalls;

    /// <summary>构造假等待器。</summary>
    /// <param name="clock">与之联动的假时钟。</param>
    /// <param name="events">可选的事件日志（与注入器共用，用于断言"先等 P 再注入再等 L"的顺序）。</param>
    public FakeWaiter(FakeClock clock, List<string>? events = null)
    {
        _clock = clock;
        Events = events ?? new List<string>();
    }

    /// <summary>共享事件日志。</summary>
    public List<string> Events { get; }

    /// <summary>每次 WaitUntil 的（目标时刻, 当时时刻）。</summary>
    public List<(long Target, long Now)> Waits { get; } = new();

    /// <summary>每次 Delay 的毫秒数。</summary>
    public List<int> Delays { get; } = new();

    /// <summary>在每次等待之前调用（参数为第几次等待，从 1 开始）。</summary>
    public Action<int>? BeforeWait { get; set; }

    /// <summary>在每次 Delay 之前调用。</summary>
    public Action<int>? BeforeDelay { get; set; }

    /// <summary>每次等待后额外推进的毫秒数，用于模拟"线程被抢占导致落后"。</summary>
    public int OvershootMs { get; set; }

    /// <inheritdoc />
    public bool WaitUntil(long targetTimestamp, Func<bool> cancelRequested)
    {
        _waitCalls++;
        BeforeWait?.Invoke(_waitCalls);

        Waits.Add((targetTimestamp, _clock.GetTimestamp()));

        if (cancelRequested())
        {
            return false;
        }

        if (targetTimestamp > _clock.GetTimestamp())
        {
            _clock.Advance(targetTimestamp - _clock.GetTimestamp());
        }

        if (OvershootMs > 0)
        {
            _clock.Advance(OvershootMs);
        }

        return true;
    }

    /// <inheritdoc />
    public bool Delay(int milliseconds, Func<bool> cancelRequested)
    {
        _delayCalls++;
        BeforeDelay?.Invoke(_delayCalls);

        Delays.Add(milliseconds);
        Events.Add($"delay:{milliseconds}");

        if (cancelRequested())
        {
            return false;
        }

        _clock.Advance(milliseconds);
        return true;
    }
}
