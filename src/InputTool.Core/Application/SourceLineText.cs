using System.Text;

namespace InputTool.Core.Application;

/// <summary>
/// 「下一行」预览的源文本取行工具（FR 新增：步进时显示下一行）。
/// </summary>
/// <remarks>
/// 放在 Core 的原因：行号 → 行文本的映射是纯逻辑，必须可单测；WPF 只负责把它画出来。
/// <para>
/// 为什么按<b>源文本</b>切分而不是复用归一化结果：硬约束要求显示源文本行 ——
/// 用户要看的是文件里真正写着什么（含 Tab 缩进、空行），而不是预处理后的样子。
/// <see cref="Parsing.TextNormalizer"/> 只把 CRLF / CR 统一成 LF，<b>不增删行</b>，
/// 因此这里的切分规则（<c>\r\n</c> / <c>\r</c> / <c>\n</c> 各算一个换行）
/// 与 <see cref="Parsing.InputUnitBuilder"/> 使用的行号完全一致。
/// </para>
/// </remarks>
public static class SourceLineText
{
    /// <summary>空行的显示文案。硬约束：空行必须显示为「（空行）」，不能是一片空白。</summary>
    public const string EmptyLineMarker = "（空行）";

    /// <summary>
    /// 源文本里找不到该行时的显示文案。
    /// </summary>
    /// <remarks>
    /// 只有一种情况会走到这里：开着「解析转义序列」且正文含字面量 <c>\n</c>，
    /// 解析后行数多于源文本行数。此时宁可显式说明，也不静默显示别的行
    /// —— 遵循 §21.14 第七条「状态文案不回退」原则。
    /// </remarks>
    public const string UnavailableMarker = "（无法定位源文本行）";

    /// <summary>
    /// 按行切分源文本；换行规则与 <see cref="Parsing.InputUnitBuilder"/> 一致。
    /// </summary>
    /// <param name="rawText">原始文本（未归一化）。</param>
    /// <returns>行文本序列。空文本返回空序列（没有任何一行）。</returns>
    public static IReadOnlyList<string> SplitLines(string? rawText)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return Array.Empty<string>();
        }

        var span = rawText.AsSpan();

        // 与 TextNormalizer 一致：开头的 \uFEFF 是 BOM 残留，不算行内容（FR-004.2）
        if (span[0] == '\uFEFF')
        {
            span = span[1..];
        }

        var lines = new List<string>();
        var start = 0;

        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];

            if (c == '\r')
            {
                lines.Add(span[start..i].ToString());

                // \r\n 算一个换行（SRS §5.1.2）
                if (i + 1 < span.Length && span[i + 1] == '\n')
                {
                    i++;
                }

                start = i + 1;
            }
            else if (c == '\n')
            {
                lines.Add(span[start..i].ToString());
                start = i + 1;
            }
        }

        // 最后一段：即使为空也要收进来，行号才能与解析器对齐
        lines.Add(span[start..].ToString());
        return lines;
    }

    /// <summary>
    /// 把一行源文本转成可直接显示的文本。
    /// </summary>
    /// <param name="sourceLine">该行的原始内容。</param>
    /// <remarks>
    /// · Tab → <c>→</c>：与预览区一致（FR-005.3），缩进要看得见；
    /// · 控制字符不渲染（它们本来就不会被注入，FR-008），免得浮层出现方块；
    /// · 空行 → <see cref="EmptyLineMarker"/>。
    /// </remarks>
    public static string ToDisplayText(string? sourceLine)
    {
        if (string.IsNullOrEmpty(sourceLine))
        {
            return EmptyLineMarker;
        }

        var builder = new StringBuilder(sourceLine.Length);

        foreach (var c in sourceLine)
        {
            if (c == '\t')
            {
                builder.Append('→');
            }
            else if (c >= 0x20 && c != 0x7F)
            {
                builder.Append(c);
            }
        }

        // 只含控制字符 / Tab 之外什么也没有的行：实际打不出任何字，按空行呈现
        return builder.Length == 0 ? EmptyLineMarker : builder.ToString();
    }

    /// <summary>取第 <paramref name="lineIndex"/> 行（0 基）的显示文本。</summary>
    /// <param name="rawText">原始文本。</param>
    /// <param name="lineIndex">0 基行号。</param>
    public static string Resolve(string? rawText, int lineIndex)
        => Describe(SplitLines(rawText), lineIndex);

    /// <summary>在已切分好的行集合里取第 <paramref name="lineIndex"/> 行（0 基）的显示文本。</summary>
    /// <param name="lines">已切分的行集合（见 <see cref="SplitLines"/>）。</param>
    /// <param name="lineIndex">0 基行号。</param>
    /// <remarks>
    /// 提供这个重载是为了让调用方**缓存**切分结果：大文件每跨一行都重新切分整个文本
    /// 会退化成 O(n²)，步进几十万行时足以卡住界面。
    /// </remarks>
    public static string Describe(IReadOnlyList<string>? lines, int lineIndex)
        => lines is not null && lineIndex >= 0 && lineIndex < lines.Count
            ? ToDisplayText(lines[lineIndex])
            : UnavailableMarker;
}