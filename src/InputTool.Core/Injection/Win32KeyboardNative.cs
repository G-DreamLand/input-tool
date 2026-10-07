using System.Runtime.InteropServices;
using InputTool.Core.Interop;

namespace InputTool.Core.Injection;

/// <summary>
/// <see cref="IKeyboardNative"/> 的 Win32 实现：把一次按键事件翻译成一个
/// <c>INPUT</c> 结构并调用 <c>SendInput</c>。
/// </summary>
/// <remarks>
/// 单独成类的原因见 <see cref="IKeyboardNative"/>：把 Win32 结构体挡在这层之内，
/// 让"按键序列"逻辑可以在没有真实桌面的单元测试里断言。
/// 注意一次调用只发送一个按键事件，保证时序可控（FR-010.1）。
/// </remarks>
internal sealed class Win32KeyboardNative : IKeyboardNative
{
    private int _lastError;

    /// <inheritdoc />
    public bool SendKey(ushort virtualKey, ushort scanCode, uint flags)
    {
        var input = new INPUT
        {
            type = Win32Constants.InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = scanCode,
                    dwFlags = flags,

                    // time / dwExtraInfo 保持 0：由系统填充，不伪造来源（FR-010.7）
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };

        var sent = NativeMethods.SendInput(1, [input], NativeMethods.InputSize);
        if (sent != 1)
        {
            _lastError = Marshal.GetLastWin32Error();
            return false;
        }

        _lastError = 0;
        return true;
    }

    /// <inheritdoc />
    public int GetLastError() => _lastError;
}
