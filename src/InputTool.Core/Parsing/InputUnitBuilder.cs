using System.Buffers;
using System.Text;
using InputTool.Core.Interop;

namespace InputTool.Core.Parsing;

/// <summary>
/// 输入单元构建。对应《软件开发设计文档》§5.2.2 流水线的后两步与 §5.3.2 映射表。
/// </summary>
/// <remarks>
/// 关键约定：
/// · 代理对（Emoji 等 BMP 外字符）在这里折叠为**单个** <see cref="Rune"/>（ADR-12）；
/// · 每个输入单元都记录原始行号与源索引，供日志定位与 P2 的起止行范围使用；
/// · 遇到控制字符与 NUL 时跳过并计数，绝不抛异常（§10.3 防御性要求 3）。
/// </remarks>
public sealed class InputUnitBuilder : IInputUnitBuilder
{
    /// <inheritdoc />
    public string ParseEscapes(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('\\') < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (c != '\\' || i + 1 >= text.Length)
            {
                builder.Append(c);
                i++;
                continue;
            }

            var next = text[i + 1];
            switch (next)
            {
                case 'n':
                case 'r':
                    builder.Append('\n');
                    i += 2;
                    break;

                case 't':
                    builder.Append('\t');
                    i += 2;
                    break;

                case 'e':
                    builder.Append('\x1B');
                    i += 2;
                    break;

                case '\\':
                    builder.Append('\\');
                    i += 2;
                    break;

                case 'x':
                case 'X':
                    if (i + 3 < text.Length &&
                        TryParseHex(text[i + 2], out var high) &&
                        TryParseHex(text[i + 3], out var low))
                    {
                        builder.Append((char)((high << 4) | low));
                        i += 4;
                    }
                    else
                    {
                        // \x 后面不是两位十六进制：按字面量保留
                        builder.Append(c);
                        i++;
                    }

                    break;

                default:
                    // 未识别的转义：保留反斜杠与后续字符（SRS §5.1.4）
                    builder.Append(c);
                    i++;
                    break;
            }
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public UnitBuildResult Build(string normalizedText, NormalizeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(normalizedText))
        {
            return new UnitBuildResult(Array.Empty<InputUnit>(), TextStatistics.Empty, Array.Empty<string>());
        }

        var text = options.ParseEscapes ? ParseEscapes(normalizedText) : normalizedText;

        var units = new List<InputUnit>();
        var warnings = new List<string>();
        var characterUnits = 0;
        var newlineUnits = 0;
        var functionKeyUnits = 0;
        var skippedUnits = 0;
        var truncated = false;

        var lineIndex = 0;
        var position = 0;

        while (true)
        {
            var newlineIndex = text.IndexOf('\n', position);
            var hasNewline = newlineIndex >= 0;
            var end = hasNewline ? newlineIndex : text.Length;
            var lineText = text.AsSpan(position, end - position);

            var isCommentLine = options.SkipCommentLines && lineText.Length > 0 && lineText[0] == '#';
            var skipLine = isCommentLine || (options.IgnoreEmptyLines && lineText.IsEmpty);

            if (!skipLine)
            {
                // 手工按 Rune 游标推进，才能为每个单元记录精确的源索引（EnumerateRunes 不提供下标）
                var offset = 0;
                while (offset < lineText.Length)
                {
                    if (units.Count >= options.MaxUnits)
                    {
                        truncated = true;
                        break;
                    }

                    var status = Rune.DecodeFromUtf16(lineText[offset..], out var rune, out var consumed);
                    if (status != OperationStatus.Done)
                    {
                        // 落单的代理项等非法序列：跳过并计数
                        skippedUnits++;
                        offset++;
                        continue;
                    }

                    var sourceIndex = position + offset;
                    offset += consumed;

                    // NUL 不注入（SRS §5.3.3）
                    if (rune.Value == 0)
                    {
                        skippedUnits++;
                        continue;
                    }

                    if (TryMapFunctionKey(rune, out var functionKey))
                    {
                        units.Add(InputUnit.Function(functionKey, lineIndex, sourceIndex));
                        functionKeyUnits++;
                        continue;
                    }

                    // 其它 C0 控制字符与 DEL：跳过并计数（FR-008）
                    if (rune.Value < 0x20 || rune.Value == 0x7F)
                    {
                        skippedUnits++;
                        continue;
                    }

                    units.Add(InputUnit.Character(rune, lineIndex, sourceIndex));
                    characterUnits++;
                }

                if (!truncated && hasNewline)
                {
                    if (units.Count >= options.MaxUnits)
                    {
                        truncated = true;
                    }
                    else
                    {
                        units.Add(InputUnit.NewLine(lineIndex, newlineIndex));
                        newlineUnits++;
                    }
                }
            }

            if (truncated || !hasNewline)
            {
                break;
            }

            position = newlineIndex + 1;
            lineIndex++;
        }

        // 末尾补回车（Q-01）：仅当单元序列末尾不是换行单元时才补
        if (options.AppendTrailingEnter &&
            !truncated &&
            units.Count > 0 &&
            units[^1].Kind != InputUnitKind.NewLine &&
            units.Count < options.MaxUnits)
        {
            units.Add(InputUnit.NewLine(lineIndex, text.Length));
            newlineUnits++;
        }

        if (truncated)
        {
            warnings.Add($"文本超过单次输入上限（{options.MaxUnits} 个单元），已截断。");
        }

        if (skippedUnits > 0)
        {
            warnings.Add($"已跳过 {skippedUnits} 个控制字符（NUL / C0 控制字符 / DEL）。");
        }

        var lineCount = text.Length == 0
            ? 0
            : text.AsSpan().Count('\n') + (text.EndsWith('\n') ? 0 : 1);

        var statistics = new TextStatistics(
            TotalUnits: units.Count,
            CharacterUnits: characterUnits,
            NewlineUnits: newlineUnits,
            FunctionKeyUnits: functionKeyUnits,
            SkippedUnits: skippedUnits,
            LineCount: lineCount,
            CharCount: characterUnits,
            TruncatedByLimit: truncated);

        return new UnitBuildResult(units, statistics, warnings);
    }

    // ── 内部实现 ────────────────────────────────────────────────────────

    /// <summary>功能字符 → 虚拟键（SRS §5.3.2）。换行由行切分处理，不在此处。</summary>
    private static bool TryMapFunctionKey(Rune rune, out VirtualKey key)
    {
        key = rune.Value switch
        {
            0x08 => VirtualKey.Back,
            0x09 => VirtualKey.Tab,
            0x1B => VirtualKey.Escape,
            _ => VirtualKey.None,
        };

        return key != VirtualKey.None;
    }

    private static bool TryParseHex(char c, out int value)
    {
        value = c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1,
        };

        return value >= 0;
    }
}
