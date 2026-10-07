using System.Text;

namespace InputTool.Core.Interop;

/// <summary>
/// 顶层窗口枚举与前台切换辅助。
/// </summary>
/// <remarks>
/// 存在的理由：
/// · 控制台窗口（cmd / PowerShell）**不属于**其进程的 <c>Process.MainWindowHandle</c>——
///   窗口归 conhost / Windows Terminal 所有，因此只能靠枚举顶层窗口来定位；
/// · Windows 对前台切换有限制（只有当前前台进程才能无条件抢焦点），
///   所以这里提供"激活并**验证**结果"的方法，而不是盲目调用 SetForegroundWindow。
/// </remarks>
public static class WindowEnumerator
{
    /// <summary>SW_RESTORE：把最小化的窗口还原。</summary>
    private const int SwRestore = 9;

    /// <summary>枚举当前所有**可见**的顶层窗口。</summary>
    public static IReadOnlyList<IntPtr> GetVisibleTopLevelWindows()
    {
        var windows = new List<IntPtr>();

        NativeMethods.EnumWindows(
            (handle, _) =>
            {
                if (NativeMethods.IsWindowVisible(handle))
                {
                    windows.Add(handle);
                }

                return true;
            },
            IntPtr.Zero);

        return windows;
    }

    /// <summary>窗口标题（取不到时返回空串）。</summary>
    public static string GetTitle(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return string.Empty;
        }

        var length = NativeMethods.GetWindowTextLengthW(windowHandle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        var copied = NativeMethods.GetWindowTextW(windowHandle, buffer, buffer.Length);
        return copied <= 0 ? string.Empty : new string(buffer, 0, copied);
    }

    /// <summary>窗口所属进程 ID。</summary>
    public static uint GetProcessId(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return 0;
        }

        NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
        return processId;
    }

    /// <summary>窗口类名（取不到时返回空串）。</summary>
    public static string GetClassName(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return string.Empty;
        }

        var buffer = new char[256];
        var length = NativeMethods.GetClassNameW(windowHandle, buffer, buffer.Length);
        return length <= 0 ? string.Empty : new string(buffer, 0, length);
    }

    /// <summary>
    /// 窗口所属进程名（不含扩展名）。取不到时返回空串——
    /// 目标进程若以管理员身份运行，普通权限下可能读不到它的信息。
    /// </summary>
    public static string GetProcessName(uint processId)
    {
        if (processId == 0)
        {
            return string.Empty;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 枚举当前所有可见的顶层窗口，附带标题 / 进程 / 类名。
    /// </summary>
    /// <remarks>
    /// 顺序即 **Z 序**（<c>EnumWindows</c> 自上而下），因此第一个元素就是"最靠前"的窗口。
    /// 目标窗口选择器依赖这个顺序来推断"用户最近在用的终端"。
    /// </remarks>
    public static IReadOnlyList<TargetWindowInfo> EnumerateTargets()
    {
        var targets = new List<TargetWindowInfo>();

        NativeMethods.EnumWindows(
            (handle, _) =>
            {
                if (!NativeMethods.IsWindowVisible(handle))
                {
                    return true;
                }

                var title = GetTitle(handle);
                if (string.IsNullOrWhiteSpace(title))
                {
                    // 无标题的顶层窗口（浮层、工具窗口等）不会是输入目标
                    return true;
                }

                var processId = GetProcessId(handle);
                targets.Add(new TargetWindowInfo(
                    Handle: handle,
                    Title: title,
                    ProcessName: GetProcessName(processId),
                    ClassName: GetClassName(handle),
                    ProcessId: processId));

                return true;
            },
            IntPtr.Zero);

        return targets;
    }

    /// <summary>
    /// 找出"最可能想输入的那个终端窗口"：Z 序最前的终端类可见窗口。
    /// </summary>
    /// <returns>找到返回句柄；没有终端窗口时返回 <see cref="IntPtr.Zero"/>。</returns>
    public static IntPtr FindBestTerminalWindow()
    {
        foreach (var target in EnumerateTargets())
        {
            if (target.IsTerminal)
            {
                return target.Handle;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>当前前台窗口句柄。</summary>
    public static IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    /// <summary>
    /// 尝试把窗口置为前台，并**验证**是否真的成功。
    /// </summary>
    /// <remarks>
    /// Windows 只允许"当前前台进程"无条件切换前台窗口，其它进程直接调用
    /// <c>SetForegroundWindow</c> 往往被静默忽略。这里的做法是把本线程的输入队列
    /// 临时挂到当前前台线程与目标线程上（<c>AttachThreadInput</c>），
    /// 于是本线程在该瞬间被视为前台线程，切换就能生效——这是 Windows 官方文档给出的做法。
    /// 无论成功与否都会在 finally 里解除挂接。
    /// </remarks>
    /// <returns>调用后该窗口确实是前台窗口时返回 true。</returns>
    public static bool TryActivate(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        if (NativeMethods.GetForegroundWindow() == windowHandle)
        {
            return true;
        }

        NativeMethods.ShowWindow(windowHandle, SwRestore);

        var currentThread = NativeMethods.GetCurrentThreadId();
        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var targetThread = NativeMethods.GetWindowThreadProcessId(windowHandle, out _);

        var attached = new List<uint>();

        try
        {
            foreach (var thread in new[] { foregroundThread, targetThread })
            {
                if (thread != 0
                    && thread != currentThread
                    && NativeMethods.AttachThreadInput(currentThread, thread, true))
                {
                    attached.Add(thread);
                }
            }

            NativeMethods.BringWindowToTop(windowHandle);
            NativeMethods.SetForegroundWindow(windowHandle);

            if (NativeMethods.GetForegroundWindow() != windowHandle)
            {
                // 有些窗口（最小化后刚恢复的）需要再试一次
                NativeMethods.SetForegroundWindow(windowHandle);
            }

            return NativeMethods.GetForegroundWindow() == windowHandle;
        }
        finally
        {
            foreach (var thread in attached)
            {
                NativeMethods.AttachThreadInput(currentThread, thread, false);
            }
        }
    }
}
