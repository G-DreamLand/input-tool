using System.Text;

namespace InputTool.Core.Parsing;

/// <summary>
/// 文本归一化。对应《软件开发设计文档》§5.2.2 流水线的前两步：
/// 去除 BOM 残留、把 CRLF / CR / LF 统一为 LF。
/// </summary>
/// <remarks>
/// 转义序列解析、忽略空行、跳过 # 行、单元构建都归属
/// <see cref="IInputUnitBuilder"/>——归一化只负责"把换行统一掉"。
/// </remarks>
public sealed class TextNormalizer : ITextNormalizer
{
    /// <inheritdoc />
    public string Normalize(string rawText, NormalizeOptions options)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return string.Empty;
        }

        var span = rawText.AsSpan();

        // \uFEFF 出现在文本开头时是 BOM 残留（FR-004.2）
        if (span.Length > 0 && span[0] == '\uFEFF')
        {
            span = span[1..];
        }

        // 快速路径：没有 \r 就不必重建字符串
        if (span.IndexOf('\r') < 0)
        {
            return span.ToString();
        }

        var builder = new StringBuilder(span.Length);
        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            if (c == '\r')
            {
                builder.Append('\n');

                // \r\n 视为一个换行单元（SRS §5.1.2）
                if (i + 1 < span.Length && span[i + 1] == '\n')
                {
                    i++;
                }
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
