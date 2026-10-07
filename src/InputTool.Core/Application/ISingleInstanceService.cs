namespace InputTool.Core.Application;

/// <summary>
/// 单实例运行。对应《软件开发设计文档》§5.9.3（FR-022）。
/// </summary>
/// <remarks>
/// 实现说明（相对 §5.9.3 的调整）：激活通道不用命名管道，改用
/// <c>RegisterWindowMessage</c> + <c>PostMessage(HWND_BROADCAST)</c>。
/// 原因：本机开发环境禁用了命名管道（见 §21.3 / TR-07），
/// 而广播消息不依赖管道、实现更短；代价是暂时无法随激活消息携带文件路径
/// （P2 再用共享内存补上，见 §18 待办）。
/// </remarks>
public interface ISingleInstanceService : IDisposable
{
    /// <summary>本进程是否是第一个实例。</summary>
    bool IsFirstInstance { get; }

    /// <summary>激活消息 ID（0 表示注册失败，此时无法接收激活请求）。</summary>
    int ActivationMessageId { get; }

    /// <summary>收到"请激活已有窗口"的请求（在广播消息的处理线程上回调）。</summary>
    event EventHandler? ActivationRequested;

    /// <summary>向已存在的实例广播"请激活"（第二实例启动时调用）。</summary>
    bool TryRequestActivation();

    /// <summary>宿主窗口消息的转发入口，用法与热键服务一致。</summary>
    bool TryHandleWindowMessage(int message, IntPtr wParam);
}
