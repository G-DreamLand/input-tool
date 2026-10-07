namespace InputTool.Core.Parsing;

/// <summary>
/// 文本归一化与单元构建的开关集合（FR-004、SRS §5.1.4 / §5.1.5）。
/// 所有会改变输入内容的开关默认关闭——"默认安全"原则（§2.2）。
/// </summary>
/// <param name="ParseEscapes">
/// 是否解析转义序列（\n \t \r \\ \x1B）。默认 false：
/// 代码、正则、路径中的 \n 就是字面量，不应被擅自替换。
/// </param>
/// <param name="IgnoreEmptyLines">是否忽略完全空行（默认 false）。</param>
/// <param name="SkipCommentLines">是否跳过以 # 开头的整行（默认 false）。</param>
/// <param name="AppendTrailingEnter">
/// 末尾补回车：当且仅当文本末尾本来没有换行符时，在单元序列末尾补一个换行单元。
/// 与 <see cref="NewlineStrategy.LastLineEnterOnly"/> 组合时恰好得到"仅末尾一次 Enter"（Q-01 决议）。
/// </param>
/// <param name="MaxUnits">
/// 单次输入的单元上限（默认 100,000，SRS §5.1.6）。超出时由上层提示用户截断或改用起止行范围。
/// </param>
public sealed record NormalizeOptions(
    bool ParseEscapes = false,
    bool IgnoreEmptyLines = false,
    bool SkipCommentLines = false,
    bool AppendTrailingEnter = false,
    int MaxUnits = 100_000)
{
    /// <summary>默认选项：全部安全开关关闭。</summary>
    public static NormalizeOptions Default { get; } = new();
}
