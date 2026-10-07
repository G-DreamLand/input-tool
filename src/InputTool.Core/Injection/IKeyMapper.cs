using InputTool.Core.Interop;

namespace InputTool.Core.Injection;

/// <summary>
/// 字符 → 虚拟键 + 修饰键的映射，仅用于"兼容模式"（FR-011.4/.5、SRS §5.3.4）。
/// 实现基于 <c>VkKeyScanEx(char, GetKeyboardLayout(0))</c>：
/// 返回值的低字节为虚拟键，高字节为修饰键位（bit0 Shift、bit1 Ctrl、bit2 Alt）；
/// 低字节为 0xFF 表示当前布局无法表示该字符，应跳过并计数。
/// </summary>
public interface IKeyMapper
{
    /// <summary>尝试把字符映射为虚拟键与修饰键组合。</summary>
    /// <param name="character">待映射字符（仅 ASCII 可打印字符有意义）。</param>
    /// <param name="virtualKey">映射出的虚拟键。</param>
    /// <param name="modifiers">需要同时按下的修饰键。</param>
    /// <param name="isExtended">是否属于扩展键（方向键、Home/End 等）。</param>
    /// <returns>映射成功返回 true。</returns>
    bool TryMapChar(char character, out VirtualKey virtualKey, out ModifierKeys modifiers, out bool isExtended);
}
