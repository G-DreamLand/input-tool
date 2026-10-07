using InputTool.Core.Application;

namespace InputTool.Tests.TestDoubles;

/// <summary>
/// 可控的焦点监视器：让"失焦 / 焦点回来"能被确定性地制造出来，
/// 而不必等待真实的 200 ms 轮询时钟。
/// </summary>
public sealed class FakeFocusMonitor : IFocusMonitor
{
    /// <inheritdoc />
    public event EventHandler? FocusLost;

    /// <inheritdoc />
    public event EventHandler? FocusRegained;

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public IntPtr TargetWindow { get; private set; }

    /// <summary>Start 被调用的次数。</summary>
    public int StartCount { get; private set; }

    /// <summary>Stop 被调用的有效次数（已经停了就不重复计数）。</summary>
    public int StopCount { get; private set; }

    /// <summary>最后一次的检查间隔。</summary>
    public TimeSpan Interval { get; private set; }

    /// <inheritdoc />
    public void Start(IntPtr targetWindow, TimeSpan interval)
    {
        StartCount++;
        IsRunning = true;
        TargetWindow = targetWindow;
        Interval = interval;
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (IsRunning)
        {
            StopCount++;
        }

        IsRunning = false;
        TargetWindow = IntPtr.Zero;
    }

    /// <summary>制造"目标窗口失去前台焦点"。</summary>
    public void RaiseFocusLost() => FocusLost?.Invoke(this, EventArgs.Empty);

    /// <summary>制造"目标窗口重新获得前台焦点"。</summary>
    public void RaiseFocusRegained() => FocusRegained?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public void Dispose() => Stop();
}
