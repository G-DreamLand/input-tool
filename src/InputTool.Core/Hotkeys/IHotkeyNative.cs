namespace InputTool.Core.Hotkeys;

/// <summary>
/// 对 <c>RegisterHotKey</c> / <c>UnregisterHotKey</c> 的最小封装。
/// </summary>
/// <remarks>
/// 抽出来的目的与 <c>IKeyboardNative</c> 相同：让热键注册、冲突处理、
/// 备用热键回退这些**逻辑**可以在单元测试里断言，而不需要真实窗口与系统热键表。
/// </remarks>
public interface IHotkeyNative
{
    /// <summary>注册全局热键。</summary>
    /// <param name="windowHandle">接收 WM_HOTKEY 的窗口句柄。</param>
    /// <param name="id">热键 ID。</param>
    /// <param name="modifiers">修饰键（含 MOD_NOREPEAT）。</param>
    /// <param name="virtualKey">虚拟键码。</param>
    /// <param name="error">失败时的 Win32 错误码。</param>
    /// <returns>成功返回 true。</returns>
    bool Register(IntPtr windowHandle, int id, uint modifiers, uint virtualKey, out int error);

    /// <summary>注销全局热键。</summary>
    bool Unregister(IntPtr windowHandle, int id);
}
