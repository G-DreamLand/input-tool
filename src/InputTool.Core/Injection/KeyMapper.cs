using InputTool.Core.Interop;

namespace InputTool.Core.Injection;

/// <summary>
/// 字符 → 虚拟键 + 修饰键的映射，用于"兼容模式"（FR-011.4/.5、SRS §5.3.4）。
/// </summary>
/// <remarks>
/// 基于 <c>VkKeyScanEx(char, GetKeyboardLayout(0))</c>：
/// 返回值低字节为虚拟键，高字节为修饰键位（bit0 Shift、bit1 Ctrl、bit2 Alt）；
/// 低字节为 0xFF（或整体为 -1）表示当前布局无法表示该字符，调用方应跳过并计数。
/// <para>
/// 扫描函数通过构造函数注入，使单测可以在不依赖真实键盘布局的情况下断言映射与失败分支。
/// </para>
/// </remarks>
public sealed class KeyMapper : IKeyMapper
{
    private readonly Func<char, short> _vkKeyScan;

    /// <summary>构造映射器。</summary>
    /// <param name="vkKeyScan">字符扫描函数；省略时使用当前键盘布局的真实实现。</param>
    public KeyMapper(Func<char, short>? vkKeyScan = null)
    {
        _vkKeyScan = vkKeyScan ?? ScanWithCurrentLayout;
    }

    /// <inheritdoc />
    public bool TryMapChar(char character, out VirtualKey virtualKey, out ModifierKeys modifiers, out bool isExtended)
    {
        virtualKey = VirtualKey.None;
        modifiers = ModifierKeys.None;
        isExtended = false;

        var scan = _vkKeyScan(character);
        if (scan < 0)
        {
            return false;
        }

        var keyCode = scan & 0xFF;
        if (keyCode == 0xFF || keyCode == 0)
        {
            return false;
        }

        virtualKey = (VirtualKey)keyCode;

        var shiftState = (scan >> 8) & 0xFF;
        if ((shiftState & 0x01) != 0)
        {
            modifiers |= ModifierKeys.Shift;
        }

        if ((shiftState & 0x02) != 0)
        {
            modifiers |= ModifierKeys.Control;
        }

        if ((shiftState & 0x04) != 0)
        {
            modifiers |= ModifierKeys.Alt;
        }

        isExtended = IsExtendedKey(virtualKey);
        return true;
    }

    /// <summary>扩展键需要设置 KEYEVENTF_EXTENDEDKEY，否则部分程序识别不出方向键等。</summary>
    internal static bool IsExtendedKey(VirtualKey key) => key switch
    {
        VirtualKey.Prior or VirtualKey.Next or VirtualKey.End or VirtualKey.Home
            or VirtualKey.Left or VirtualKey.Up or VirtualKey.Right or VirtualKey.Down
            or VirtualKey.Insert or VirtualKey.Delete => true,
        _ => false,
    };

    private static short ScanWithCurrentLayout(char character)
        => NativeMethods.VkKeyScanEx(character, NativeMethods.GetKeyboardLayout(0));
}
