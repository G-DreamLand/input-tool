using System.Runtime.InteropServices;
using InputTool.Core.Interop;

namespace InputTool.Core.Hotkeys;

/// <summary><see cref="IHotkeyNative"/> 的 Win32 实现。</summary>
internal sealed class Win32HotkeyNative : IHotkeyNative
{
    /// <inheritdoc />
    public bool Register(IntPtr windowHandle, int id, uint modifiers, uint virtualKey, out int error)
    {
        if (NativeMethods.RegisterHotKey(windowHandle, id, modifiers, virtualKey))
        {
            error = 0;
            return true;
        }

        error = Marshal.GetLastWin32Error();
        return false;
    }

    /// <inheritdoc />
    public bool Unregister(IntPtr windowHandle, int id)
        => NativeMethods.UnregisterHotKey(windowHandle, id);
}
