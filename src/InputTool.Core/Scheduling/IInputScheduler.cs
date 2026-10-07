using InputTool.Core.Parsing;

namespace InputTool.Core.Scheduling;

/// <summary>
/// 节拍调度器。对应《软件开发设计文档》§5.4。
/// 关键约定：
/// · 采用绝对时间基准（目标时刻 = 基准 + Σ 间隔），杜绝长时间运行漂移（FR-007.1）；
/// · 输入线程与 UI 线程分离，UI 卡顿不影响节奏（FR-007.6）；
/// · 停止/暂停响应 ≤ 200 ms，所有长等待切片化（FR-007.7、NFR-006）；
/// · 触发停止时若处于"down 未 up"中间态，必须补发 up（FR-007.8）。
/// </summary>
public interface IInputScheduler
{
    /// <summary>调度器当前状态。</summary>
    SchedulerState State { get; }

    /// <summary>
    /// 开始一次输入。
    /// </summary>
    /// <param name="units">待注入的单元序列（会话期间视为不可变快照）。</param>
    /// <param name="profileProvider">每拍读取最新速度，使"输入中改速度下一拍生效"（FR-006.3）。</param>
    /// <param name="progress">进度回调；由 WPF 的 SynchronizationContext 自动回到 UI 线程。</param>
    void Start(IReadOnlyList<InputUnit> units, Func<SpeedProfile> profileProvider, IProgress<ProgressInfo>? progress = null);

    /// <summary>暂停：停止注入，保留进度与位置；目标窗口可自由操作（FR-015）。</summary>
    void Pause();

    /// <summary>继续：从当前单元继续，并重建时间基准，使暂停时长不影响节奏（FR-015）。</summary>
    void Resume();

    /// <summary>停止：幂等，可从任意线程调用，响应 ≤ 200 ms。</summary>
    void Stop();

    /// <summary>调度结束（正常完成 / 停止 / 出错）。</summary>
    event EventHandler<SessionFinishedEventArgs>? Finished;
}
