using InputTool.Core.Parsing;

namespace InputTool.Core.Application;

/// <summary>
/// 已加载的文档：原始文本 + 解析产物 + 统计。
/// </summary>
/// <param name="Path">文件路径。</param>
/// <param name="RawText">原始文本（未归一化）。</param>
/// <param name="EncodingName">实际使用的编码名。</param>
/// <param name="EncodingReason">编码判定依据（日志与界面提示用）。</param>
/// <param name="Units">按序的输入单元（会话期间视为不可变快照）。</param>
/// <param name="Statistics">统计信息。</param>
/// <param name="Options">本次使用的归一化选项（界面回显用）。</param>
/// <param name="SuspectedBinary">是否疑似二进制内容。</param>
/// <param name="Warnings">解析过程中的非致命警告。</param>
public sealed record LoadedDocument(
    string Path,
    string RawText,
    string EncodingName,
    string EncodingReason,
    IReadOnlyList<InputUnit> Units,
    TextStatistics Statistics,
    NormalizeOptions Options,
    bool SuspectedBinary,
    IReadOnlyList<string> Warnings)
{
    /// <summary>是否为空文档（禁止开始输入，E-02）。</summary>
    public bool IsEmpty => Units.Count == 0;

    /// <summary>用于预览的截断文本（前 500 字符，FR-005.1）。</summary>
    public string PreviewText => RawText.Length <= 500 ? RawText : RawText[..500];
}
