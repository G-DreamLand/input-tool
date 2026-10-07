using System.Text;

namespace InputTool.Core.Files;

/// <summary>编码判定的结果。</summary>
/// <param name="Encoding">判定出的编码。</param>
/// <param name="EncodingName">可显示的编码名（如 "UTF-8"、"GB18030"、"ANSI(936)"）。</param>
/// <param name="Reason">判定依据，写入日志便于乱码排查（FR-021.2）。</param>
/// <param name="SuspectedBinary">是否疑似二进制内容。</param>
public sealed record EncodingDetectionResult(
    Encoding Encoding,
    string EncodingName,
    string Reason,
    bool SuspectedBinary);

/// <summary>解码结果。</summary>
/// <param name="Text">解码后的文本。</param>
/// <param name="Encoding">实际使用的编码。</param>
/// <param name="EncodingName">可显示的编码名。</param>
/// <param name="Reason">判定/回退依据。</param>
/// <param name="SuspectedBinary">是否疑似二进制内容。</param>
public sealed record DecodedText(
    string Text,
    Encoding Encoding,
    string EncodingName,
    string Reason,
    bool SuspectedBinary);
