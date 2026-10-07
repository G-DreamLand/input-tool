namespace InputTool.Core.Application;

/// <summary>
/// 前台焦点监视器（FR-020）。
/// </summary>
/// <remarks>
/// 抽成接口的原因：真实实现靠定时轮询前台窗口，单测里无法确定性地制造"焦点变化"，
/// 因此需要一个可以手动触发事件的替身。
/// </remarks>
public interface IFocusMonitor : IDisposable
{
    /// <summary>目标窗口失去前台焦点。</summary>
    event EventHandler? FocusLost;

    /// <summary>目标窗口重新获得前台焦点。</summary>
    event EventHandler? FocusRegained;

    /// <summary>是否正在监视。</summary>
    bool IsRunning { get; }

    /// <summary>当前被监视的目标窗口。</summary>
    IntPtr TargetWindow { get; }

    /// <summary>开始监视；会立刻同步一次当前状态作为基准。</summary>
    /// <param name="targetWindow">目标窗口句柄。</param>
    /// <param name="interval">检查间隔；小于 <see cref="ForegroundFocusMonitor.MinimumInterval"/> 时会被抬到下限。</param>
    void Start(IntPtr targetWindow, TimeSpan interval);

    /// <summary>停止监视（幂等）。</summary>
    void Stop();
}
