namespace InputTool.Core.Application;

/// <summary>
/// 会话状态机状态。对应《软件开发设计文档》§4.1。
/// 只允许 UI 线程写入，后台线程通过 Post 请求迁移。
/// </summary>
public enum InputSessionState
{
    /// <summary>空闲：可载入文件、可改设置。</summary>
    Idle,

    /// <summary>倒计时：浮层提示中，尚未注入任何按键。</summary>
    Countdown,

    /// <summary>输入中：输入线程正在按节拍注入。</summary>
    Typing,

    /// <summary>暂停：保留进度与位置，目标窗口可自由操作。</summary>
    Paused,

    /// <summary>本次会话正常完成。</summary>
    Finished,

    /// <summary>本次会话因致命错误终止。</summary>
    Error,
}

/// <summary>状态迁移事件参数。</summary>
/// <param name="Previous">原状态。</param>
/// <param name="Current">新状态。</param>
/// <param name="Reason">迁移原因（写日志用）。</param>
public sealed record StateChangedEventArgs(InputSessionState Previous, InputSessionState Current, string? Reason = null);
