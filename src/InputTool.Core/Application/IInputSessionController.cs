using InputTool.Core.Abstractions;
using InputTool.Core.Scheduling;

namespace InputTool.Core.Application;

/// <summary>
/// 输入会话总控。对应《软件开发设计文档》§5.9.1 与 §4.2 状态机。
/// </summary>
/// <remarks>
/// 职责边界（本实现相对 §5.9.1 的一处细化）：
/// · 会话控制器只管**一次输入会话**的生命周期（前置校验 → 倒计时 → 注入 → 暂停/急停 → 统计）；
/// · 文档的加载与解析由 <see cref="DocumentPipeline"/> 负责，解析结果 <see cref="LoadedDocument"/>
///   从这里传入。这样"读文件"与"放键盘"互不耦合，界面也能独立复用前者。
/// </remarks>
public interface IInputSessionController
{
    /// <summary>当前状态。</summary>
    InputSessionState State { get; }

    /// <summary>当前会话的文档；未开始时为 null。</summary>
    LoadedDocument? Document { get; }

    /// <summary>最近一次会话的统计结果。</summary>
    SessionStatistics? LastResult { get; }

    /// <summary>目标窗口的选择方式（§6.3 / FR-012.4）。</summary>
    TargetWindowMode TargetMode { get; set; }

    /// <summary>
    /// 固定目标窗口句柄（<see cref="TargetWindowMode.Pinned"/> 时使用）。
    /// 窗口被关掉后激活会失败，此时会明确提示而不是打到别的窗口上。
    /// </summary>
    IntPtr PinnedTargetWindow { get; set; }

    /// <summary>本次会话锁定的目标窗口（开始输入后才有值）。</summary>
    IntPtr ActiveTargetWindow { get; }

    /// <summary>是否启用焦点守护（FR-020）。</summary>
    bool FocusGuardEnabled { get; set; }

    /// <summary>失去焦点时的动作（FR-020.3）：自动暂停 / 继续输入 / 自动停止。</summary>
    Configuration.FocusGuardAction FocusGuardAction { get; set; }

    /// <summary>状态迁移（在界面线程上回调）。</summary>
    event EventHandler<StateChangedEventArgs>? StateChanged;

    /// <summary>进度更新（≤10 次/秒，在界面线程上回调）。</summary>
    event EventHandler<ProgressInfo>? ProgressChanged;

    /// <summary>需要向用户呈现的通知（§10.1）。</summary>
    event EventHandler<UserNotification>? Notified;

    /// <summary>会话结束（正常完成 / 停止 / 出错）。</summary>
    event EventHandler<SessionStatistics>? Finished;

    /// <summary>
    /// 开始一次输入：前置校验 → 倒计时 → 注入（T1~T5）。
    /// </summary>
    /// <param name="document">已解析的文档。</param>
    /// <param name="countdownSeconds">倒计时秒数（0 表示立即开始）。</param>
    /// <param name="profileProvider">每拍读取最新速度，使"输入中改速度下一拍生效"（FR-006.3）。</param>
    void Start(LoadedDocument document, int countdownSeconds, Func<SpeedProfile> profileProvider);

    /// <summary>暂停（T8）。</summary>
    void Pause();

    /// <summary>继续（T15）。</summary>
    void Resume();

    /// <summary>暂停 / 继续三态切换（FR-013.3）。</summary>
    void TogglePause();

    /// <summary>停止 / 紧急停止（T9，幂等）。</summary>
    void Stop();

    /// <summary>取消倒计时（T6 / FR-012.5）。</summary>
    void CancelCountdown();

    /// <summary>清空运行时状态，保留文档（T20）。</summary>
    void Reset();
}
