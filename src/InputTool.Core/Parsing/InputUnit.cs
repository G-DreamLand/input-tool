using System.Text;
using InputTool.Core.Interop;

namespace InputTool.Core.Parsing;

/// <summary>输入单元的类型。</summary>
public enum InputUnitKind
{
    /// <summary>普通文本字符（含空格、中文、Emoji、组合字符），走 Unicode 注入。</summary>
    Character,

    /// <summary>功能性按键（Tab / 退格 / Esc 等），走虚拟键注入。</summary>
    FunctionKey,

    /// <summary>换行，具体发送内容由 <see cref="NewlineStrategy"/> 决定（FR-009）。</summary>
    NewLine,
}

/// <summary>
/// 调度器的最小执行单位。对应《软件开发设计文档》§5.2.1。
/// </summary>
/// <remarks>
/// 设计决策 ADR-12：字符承载使用 <see cref="System.Text.Rune"/> 而非 SRS §9.2 示例中的
/// <c>char</c>。原因：Emoji 等 BMP 外字符（代理对）是 P0 需求（FR-011.2 / AC-10），
/// 在解析层折叠为单个 Rune 后，注入层只需处理"一个 Rune → 1 或 2 个 UTF-16 码元"，
/// 避免拆对逻辑散落到各处。
/// </remarks>
/// <param name="Kind">单元类型。</param>
/// <param name="Rune">字符内容（仅 <see cref="InputUnitKind.Character"/> 有意义）。</param>
/// <param name="Key">虚拟键（仅 <see cref="InputUnitKind.FunctionKey"/> / <see cref="InputUnitKind.NewLine"/> 有意义）。</param>
/// <param name="LineIndex">0 基原始行号，用于日志定位与 P2 的起止行范围（FR-024）。</param>
/// <param name="SourceIndex">原始文本中的 UTF-16 索引，用于"查看完整内容"定位。</param>
public readonly record struct InputUnit(
    InputUnitKind Kind,
    Rune Rune,
    VirtualKey Key,
    int LineIndex,
    int SourceIndex)
{
    /// <summary>构造一个普通字符单元。</summary>
    public static InputUnit Character(Rune rune, int lineIndex, int sourceIndex)
        => new(InputUnitKind.Character, rune, VirtualKey.None, lineIndex, sourceIndex);

    /// <summary>构造一个功能性按键单元。</summary>
    public static InputUnit Function(VirtualKey key, int lineIndex, int sourceIndex)
        => new(InputUnitKind.FunctionKey, default, key, lineIndex, sourceIndex);

    /// <summary>构造一个换行单元。</summary>
    public static InputUnit NewLine(int lineIndex, int sourceIndex)
        => new(InputUnitKind.NewLine, default, VirtualKey.Return, lineIndex, sourceIndex);
}
