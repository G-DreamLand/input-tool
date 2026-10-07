namespace InputTool.Core.Interop;

/// <summary>
/// 窗口扩展样式辅助。对应《软件开发设计文档》§6.3：
/// 倒计时浮层必须**不可获得焦点**（否则会抢走用户目标窗口的焦点，导致按键注入到错误位置）。
/// </summary>
public static class WindowInterop
{
    /// <summary>
    /// 取当前前台窗口句柄。用于在倒计时结束时记录"目标窗口"（FR-012.4 / FR-020.1）。
    /// </summary>
    public static IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    /// <summary>
    /// 把指定窗口置为前台。注意 Windows 对前台切换有限制（只有当前前台进程才能成功），
    /// 失败时应降级为提示用户手动点击（FR-020.4）。
    /// </summary>
    public static bool SetForegroundWindow(IntPtr windowHandle)
        => windowHandle != IntPtr.Zero && NativeMethods.SetForegroundWindow(windowHandle);

    /// <summary>
    /// 给窗口加上 <c>WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW</c>：
    /// 前者保证点击浮层不会激活它，后者让它不出现在 Alt+Tab 列表中。
    /// </summary>
    /// <param name="windowHandle">窗口句柄。</param>
    public static void ApplyNoActivateToolWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(windowHandle, Win32Constants.GwlExStyle).ToInt64();
        style |= Win32Constants.WsExNoActivate | Win32Constants.WsExToolWindow;
        NativeMethods.SetWindowLongPtr(windowHandle, Win32Constants.GwlExStyle, new IntPtr(style));
    }
}
