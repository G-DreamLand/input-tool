using InputTool.Core.Interop;

namespace InputTool.Core.Application;

/// <summary>
/// 目标窗口的选择方式。
/// </summary>
public enum TargetWindowMode
{
    /// <summary>
    /// 默认：用**倒计时结束时**的前台窗口。
    /// 于是"把焦点切到目标窗口"这件事由用户手完成（在倒计时内切过去）。
    /// </summary>
    Foreground,

    /// <summary>
    /// 自动选择：取 Z 序最前的终端类窗口（控制台 / Windows Terminal / WSL）。
    /// 适合"文件里是要往终端灌的命令"这种场景。
    /// </summary>
    AutoTerminal,

    /// <summary>
    /// 固定窗口：始终用用户在选择器里挑中的那个窗口；开始前会尝试把它切到前台并校验。
    /// </summary>
    Pinned,
}

/// <summary>
/// 目标窗口相关系统能力的抽象。
/// </summary>
/// <remarks>
/// 抽成接口是为了让会话控制器的"目标窗口解析与激活"可测——
/// <see cref="WindowEnumerator"/> 是静态 P/Invoke，无法在单测里替换。
/// </remarks>
public interface ITargetWindowGateway
{
    /// <summary>取当前前台窗口。</summary>
    IntPtr GetForegroundWindow();

    /// <summary>取 Z 序最前的终端类窗口；没有则返回 <see cref="IntPtr.Zero"/>。</summary>
    IntPtr FindBestTerminalWindow();

    /// <summary>尝试把窗口切到前台，并**校验**是否真的成功。</summary>
    bool TryActivate(IntPtr handle);

    /// <summary>取窗口标题（用于界面回显与日志）。</summary>
    string GetTitle(IntPtr handle);
}

/// <summary>基于 Win32 的真实实现。</summary>
public sealed class Win32TargetWindowGateway : ITargetWindowGateway
{
    /// <inheritdoc />
    public IntPtr GetForegroundWindow() => WindowEnumerator.GetForegroundWindow();

    /// <inheritdoc />
    public IntPtr FindBestTerminalWindow() => WindowEnumerator.FindBestTerminalWindow();

    /// <inheritdoc />
    public bool TryActivate(IntPtr handle) => WindowEnumerator.TryActivate(handle);

    /// <inheritdoc />
    public string GetTitle(IntPtr handle) => WindowEnumerator.GetTitle(handle);
}
