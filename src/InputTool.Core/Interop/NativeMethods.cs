using System.Runtime.InteropServices;

namespace InputTool.Core.Interop;

/// <summary>
/// 全部 Win32 P/Invoke 声明的唯一位置。
/// 约束（设计文档 §10.3 / §16.1）：
/// · 所有声明必须带 <c>SetLastError = true</c>，调用点必须检查返回值与错误码；
/// · 结构体尺寸一律用 <see cref="Marshal.SizeOf{T}"/> 计算，不得写死；
/// · 除本文件外，其它模块不得直接声明 DllImport。
/// </summary>
internal static class NativeMethods
{
    /// <summary>
    /// INPUT 结构体尺寸（x64 = 40）。SendInput 必须传正确尺寸，
    /// 否则返回 0 且 GetLastError = ERROR_INVALID_PARAMETER(87)。
    /// </summary>
    internal static readonly int InputSize = Marshal.SizeOf<INPUT>();

    // ── user32：键盘注入（§5.5.2）───────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    // ── user32：全局热键（§5.6.2）───────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ── user32：前台窗口 / 目标窗口（§5.5.8、§6.4）──────────────────────

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowTextW(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowTextLengthW(IntPtr hWnd);

    // ── user32：兼容模式的字符 → 虚拟键映射（§5.5.5）────────────────────

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern short VkKeyScanEx(char ch, IntPtr dwhkl);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetKeyboardLayout(uint idThread);

    // ── user32：窗口扩展样式（倒计时浮层不抢焦点，§6.3）─────────────────

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    // ── user32：窗口枚举与前台切换（§6.3、FR-020）───────────────────────

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassNameW(IntPtr hWnd, char[] lpClassName, int nMaxCount);

    /// <summary>
    /// 把一个线程的输入队列挂到另一个线程上。
    /// 用于绕过 Windows 的前台切换限制：挂接之后，调用线程就有资格调用
    /// <c>SetForegroundWindow</c>（这是 Windows 官方文档承认的做法）。
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BringWindowToTop(IntPtr hWnd);

    // ── user32：桌面 / 锁屏检测（§5.5.8）───────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr OpenInputDesktop(uint dwFlags, [MarshalAs(UnmanagedType.Bool)] bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseDesktop(IntPtr hDesktop);

    // ── user32：鼠标位置（浮层"鼠标附近"定位，§6.3）─────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    // ── kernel32：进程与句柄 ────────────────────────────────────────────

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    // ── user32：单实例激活消息（§5.9.3）─────────────────────────────────

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // ── kernel32：会话（§5.5.8）─────────────────────────────────────────

    [DllImport("kernel32.dll")]
    internal static extern uint WTSGetActiveConsoleSessionId();

    // ── kernel32 / advapi32：进程令牌与完整性级别（FR-027）──────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation, uint tokenInformationLength, out uint returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern IntPtr GetSidSubAuthority(IntPtr pSid, uint nSubAuthority);

    /// <summary>返回指向 SID 子机构数量的指针（PUCHAR），用 Marshal.ReadByte 取值。</summary>
    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern IntPtr GetSidSubAuthorityCount(IntPtr pSid);

    // ── winmm：定时器精度（§5.4.6）──────────────────────────────────────

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    internal static extern uint TimeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    internal static extern uint TimeEndPeriod(uint uPeriod);
}
