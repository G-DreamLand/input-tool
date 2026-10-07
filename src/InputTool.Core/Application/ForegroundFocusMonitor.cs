using InputTool.Core.Logging;

namespace InputTool.Core.Application;

/// <summary>
/// 基于定时轮询的前台焦点监视器（FR-020.2 / NFR：监视线程间隔 ≥ 200 ms）。
/// </summary>
/// <remarks>
/// 设计要点：
/// · **轮询而不是 WinEvent 钩子**——SRS 明确写的是"周期性（如每 200 ms）检查"，
///   而且轮询不需要消息泵，可以在任意线程上工作；
/// · 只在**状态发生变化**时触发事件：离开目标窗口触发一次 <see cref="FocusLost"/>，
///   回到目标窗口触发一次 <see cref="FocusRegained"/>，不会每 200 ms 反复通知；
/// · 轮询开销极低（一次 <c>GetForegroundWindow</c>），远小于注入本身；
/// · "不实现强制抢焦点"（FR-020.5）：本类只**观察**，从不调用 SetForegroundWindow。
/// </remarks>
public sealed class ForegroundFocusMonitor : IFocusMonitor
{
    /// <summary>检查间隔下限：NFR 要求监视线程间隔 ≥ 200 ms。</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>默认检查间隔。</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(200);

    private readonly object _gate = new();
    private readonly ITargetWindowGateway _windows;
    private readonly IAppLogger? _logger;

    private Timer? _timer;
    private IntPtr _targetWindow = IntPtr.Zero;
    private bool _isFocused = true;
    private bool _disposed;

    /// <summary>构造监视器。</summary>
    /// <param name="windows">前台窗口来源；省略时使用 Win32 实现。</param>
    /// <param name="logger">日志（可选）。</param>
    public ForegroundFocusMonitor(ITargetWindowGateway? windows = null, IAppLogger? logger = null)
    {
        _windows = windows ?? new Win32TargetWindowGateway();
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler? FocusLost;

    /// <inheritdoc />
    public event EventHandler? FocusRegained;

    /// <inheritdoc />
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _timer is not null;
            }
        }
    }

    /// <inheritdoc />
    public IntPtr TargetWindow
    {
        get
        {
            lock (_gate)
            {
                return _targetWindow;
            }
        }
    }

    /// <inheritdoc />
    public void Start(IntPtr targetWindow, TimeSpan interval)
    {
        if (targetWindow == IntPtr.Zero)
        {
            return;
        }

        if (interval < MinimumInterval)
        {
            interval = MinimumInterval;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _targetWindow = targetWindow;

            // 以"开始这一刻"的真实状态为基准，避免刚启动就误报一次
            _isFocused = _windows.GetForegroundWindow() == targetWindow;

            _timer?.Dispose();
            _timer = new Timer(_ => PollOnce(), null, interval, interval);
        }

        _logger?.Info(
            $"焦点守护已启动：目标=0x{targetWindow.ToInt64():X}，间隔={interval.TotalMilliseconds:F0} ms",
            "FocusGuard");
    }

    /// <inheritdoc />
    public void Stop()
    {
        Timer? timer;

        lock (_gate)
        {
            timer = _timer;
            _timer = null;
            _targetWindow = IntPtr.Zero;
        }

        timer?.Dispose();
    }

    /// <summary>
    /// 立即检查一次并按需触发事件。
    /// </summary>
    /// <remarks>
    /// 定时器回调走的就是这里；公开出来是为了让单测能够**确定性地**驱动检查，
    /// 而不必等待真实的 200 ms 时钟。
    /// </remarks>
    public void PollOnce()
    {
        IntPtr target;
        bool wasFocused;

        lock (_gate)
        {
            if (_timer is null || _targetWindow == IntPtr.Zero)
            {
                return;
            }

            target = _targetWindow;
            wasFocused = _isFocused;
        }

        var isFocused = _windows.GetForegroundWindow() == target;

        if (isFocused == wasFocused)
        {
            // 状态没变：不重复通知（否则每 200 ms 都会弹一次托盘提示）
            return;
        }

        lock (_gate)
        {
            if (_timer is null || _targetWindow != target)
            {
                return;
            }

            _isFocused = isFocused;
        }

        if (isFocused)
        {
            _logger?.Info("目标窗口重新获得焦点", "FocusGuard");
            FocusRegained?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _logger?.Info("目标窗口失去焦点", "FocusGuard");
            FocusLost?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }

        FocusLost = null;
        FocusRegained = null;
    }
}
