using InputTool.Core.Interop;
using InputTool.Core.Logging;

namespace InputTool.Core.Application;

/// <summary>
/// 单实例服务。对应《软件开发设计文档》§5.9.3（FR-022）。
/// </summary>
/// <remarks>
/// · 互斥体用 <c>Local\</c> 命名（Q-08 决议）：每用户会话一个实例，多用户互不干扰；
/// · 任何一步失败都**保守当作第一实例**——单实例是体验优化，绝不能变成"程序打不开"；
/// · 激活走广播消息，不依赖命名管道（见 <see cref="ISingleInstanceService"/> 的说明）。
/// </remarks>
public sealed class SingleInstanceService : ISingleInstanceService
{
    /// <summary>互斥体名（每用户会话）。</summary>
    public const string DefaultMutexName = @"Local\InputTool.SingleInstance.v1";

    private const string ActivationMessageName = "InputTool.Activate.v1";

    private readonly IAppLogger? _logger;
    private Mutex? _mutex;
    private bool _disposed;

    /// <summary>构造单实例服务。</summary>
    /// <param name="mutexName">互斥体名（单测用；省略时使用默认名）。</param>
    /// <param name="logger">日志（可选）。</param>
    public SingleInstanceService(string? mutexName = null, IAppLogger? logger = null)
    {
        _logger = logger;

        try
        {
            _mutex = new Mutex(initiallyOwned: true, mutexName ?? DefaultMutexName, out var createdNew);
            IsFirstInstance = createdNew;
        }
        catch (Exception ex)
        {
            // 拿不到互斥体时保守放行，避免用户完全打不开程序
            _logger?.Warn($"创建单实例互斥体失败，按首实例继续：{ex.Message}", "SingleInstance");
            IsFirstInstance = true;
        }

        try
        {
            ActivationMessageId = unchecked((int)NativeMethods.RegisterWindowMessageW(ActivationMessageName));
        }
        catch (Exception ex)
        {
            _logger?.Warn($"注册激活消息失败，将无法激活已有窗口：{ex.Message}", "SingleInstance");
            ActivationMessageId = 0;
        }

        _logger?.Info($"单实例检查完成：IsFirstInstance={IsFirstInstance}, 消息 ID={ActivationMessageId}", "SingleInstance");
    }

    /// <inheritdoc />
    public bool IsFirstInstance { get; }

    /// <inheritdoc />
    public int ActivationMessageId { get; }

    /// <inheritdoc />
    public event EventHandler? ActivationRequested;

    /// <inheritdoc />
    public bool TryRequestActivation()
    {
        if (ActivationMessageId == 0)
        {
            return false;
        }

        return NativeMethods.PostMessage(
            Win32Constants.HwndBroadcast,
            ActivationMessageId,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    /// <inheritdoc />
    public bool TryHandleWindowMessage(int message, IntPtr wParam)
    {
        if (ActivationMessageId == 0 || message != ActivationMessageId)
        {
            return false;
        }

        ActivationRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var mutex = _mutex;
        _mutex = null;

        if (mutex is null)
        {
            return;
        }

        try
        {
            if (IsFirstInstance)
            {
                mutex.ReleaseMutex();
            }
        }
        catch (ApplicationException)
        {
            // 没有持有该互斥体时释放会抛异常，忽略
        }
        finally
        {
            mutex.Dispose();
        }
    }
}
