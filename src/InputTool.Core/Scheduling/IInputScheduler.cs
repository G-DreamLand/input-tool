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
    /// 逐行步进：每跨到**新的一行**先暂停，等 <see cref="Resume"/> 再继续。
    /// </summary>
    /// <remarks>
    /// 行边界取自 <see cref="InputUnit.LineIndex"/>，因此与"换行策略"无关——
    /// 即使策略是「不发送回车」，步进依然按源文本的行工作。
    /// 空行也是一行，同样会停（脚本语义）。
    /// 默认关闭，关闭时行为与之前完全一致。
    /// </remarks>
    bool StepMode { get; set; }

    /// <summary>因步进而停在某一行开头（在调度线程上触发；界面据此提示"等待下一步"）。</summary>
    /// <remarks>
    /// 载荷给出**下一行**的行号（<see cref="StepWaitingEventArgs.LineIndex"/>），
    /// 界面据此提示"等待下一步"并预先显示该行内容。
    /// 触发时机在 <see cref="Pause"/> 之后，因此此刻 <see cref="State"/> 已是 <see cref="SchedulerState.Paused"/>。
    /// </remarks>
    event EventHandler<StepWaitingEventArgs>? StepWaiting;

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
