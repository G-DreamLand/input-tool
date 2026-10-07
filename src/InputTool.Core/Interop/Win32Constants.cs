namespace InputTool.Core.Interop;

/// <summary>
/// Win32 常量集中定义。
/// 依据《软件开发设计文档》§5.5.2 常量表、§5.6.2 热键注册、§6.3 浮层扩展样式。
/// </summary>
internal static class Win32Constants
{
    // ── SendInput / INPUT ────────────────────────────────────────────────

    /// <summary>INPUT.type：键盘输入。</summary>
    internal const uint InputKeyboard = 1;

    internal const uint KeyeventfExtendedKey = 0x0001;
    internal const uint KeyeventfKeyUp = 0x0002;

    /// <summary>Unicode 注入标志：wVk=0，wScan=UTF-16 码元（FR-011）。</summary>
    internal const uint KeyeventfUnicode = 0x0004;

    internal const uint KeyeventfScancode = 0x0008;

    // ── 热键 ─────────────────────────────────────────────────────────────

    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;
    internal const uint ModWin = 0x0008;

    /// <summary>抑制热键自动重复，避免长按 F9 反复切换状态（§5.6.2）。</summary>
    internal const uint ModNoRepeat = 0x4000;

    /// <summary>WM_HOTKEY 消息 ID。</summary>
    internal const int WmHotkey = 0x0312;

    // ── 窗口样式（倒计时浮层：不抢焦点，FR-012.3）────────────────────────

    internal const int GwlExStyle = -20;

    internal const int WsExNoActivate = 0x08000000;
    internal const int WsExToolWindow = 0x00000080;
    internal const int WsExTransparent = 0x00000020;
    internal const int WsExTopmost = 0x00000008;

    // ── 错误码（§5.5.7）─────────────────────────────────────────────────

    internal const int ErrorSuccess = 0;

    /// <summary>ERROR_ACCESS_DENIED：通常表示 UIPI 限制（FR-010.6）。</summary>
    internal const int ErrorAccessDenied = 5;

    internal const int ErrorInvalidParameter = 87;

    /// <summary>ERROR_SHARING_VIOLATION / LOCK_VIOLATION：文件被占用（E-03）。</summary>
    internal const int ErrorSharingViolation = 32;
    internal const int ErrorLockViolation = 33;

    /// <summary>ERROR_HOTKEY_ALREADY_REGISTERED：热键被其他程序占用（FR-013.5）。</summary>
    internal const int ErrorHotkeyAlreadyRegistered = 1409;

    // ── 进程 / 令牌（FR-027 权限自检）──────────────────────────────────

    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint TokenQuery = 0x0008;

    /// <summary>TokenIntegrityLevel。</summary>
    internal const int TokenIntegrityLevelClass = 25;

    /// <summary>TokenElevation：判断本进程是否已提权。</summary>
    internal const int TokenElevationClass = 20;

    /// <summary>ERROR_CANCELLED：用户取消了 UAC 提权（FR-027）。</summary>
    internal const int ErrorCancelled = 1223;

    /// <summary>HWND_BROADCAST：向所有顶层窗口广播消息（单实例激活用）。</summary>
    internal static readonly IntPtr HwndBroadcast = new(0xFFFF);

    // ── 桌面 / 会话（FR-010.4 可注入性检测）─────────────────────────────

    internal const uint DesktopSwitchDesktop = 0x0100;
    internal const int DesktopReadObjects = 0x0001;

    // ── 定时器精度（§5.4.6）──────────────────────────────────────────────

    internal const uint TimerResolutionMs = 1;
}
