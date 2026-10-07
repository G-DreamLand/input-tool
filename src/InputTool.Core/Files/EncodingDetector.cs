using System.Text;

namespace InputTool.Core.Files;

/// <summary>
/// 编码判定与解码。对应《软件开发设计文档》§5.1.2 / §5.1.3。
/// 判定顺序：BOM → 严格 UTF-8 → GB18030 → 系统 ANSI(936) → Latin-1 兜底。
/// </summary>
public sealed class EncodingDetector : IEncodingDetector
{
    private const int BinarySampleSize = 8192;
    private const double BinaryScoreThreshold = 0.05;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static EncodingDetector()
    {
        // .NET 8 默认不带 GBK / GB18030 / ANSI(936) 编码，必须注册代码页提供程序，
        // 否则 Encoding.GetEncoding(936) 会抛 ArgumentException（设计文档 §5.1.2）。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <inheritdoc />
    public EncodingDetectionResult Detect(ReadOnlySpan<byte> head)
    {
        if (TryDetectBom(head, out var bomEncoding, out var bomName, out _))
        {
            return new EncodingDetectionResult(bomEncoding, bomName, $"BOM：{bomName}", LooksBinary(head));
        }

        if (TryDecodeStrict(StrictUtf8, head, out _))
        {
            return new EncodingDetectionResult(StrictUtf8, "UTF-8", "无 BOM，严格 UTF-8 解码成功", LooksBinary(head));
        }

        if (TryGetCodePage(54936, out var gb18030) && TryDecodeStrict(gb18030, head, out _))
        {
            return new EncodingDetectionResult(gb18030, "GB18030", "严格 UTF-8 失败 → GB18030", LooksBinary(head));
        }

        if (TryGetCodePage(936, out var ansi) && TryDecodeStrict(ansi, head, out _))
        {
            return new EncodingDetectionResult(ansi, "ANSI(936)", "严格 UTF-8 与 GB18030 均失败 → 系统 ANSI", LooksBinary(head));
        }

        return new EncodingDetectionResult(Encoding.Latin1, "Latin-1", "所有编码均失败 → Latin-1 兜底，编码疑似异常", LooksBinary(head));
    }

    /// <inheritdoc />
    public DecodedText Decode(ReadOnlySpan<byte> bytes, EncodingPreference preference)
    {
        // 有 BOM 就是"这是文本"的确凿证据：UTF-16/32 正文里本来就大量出现 NUL，
        // 若仍按控制字符比例判定，会把正常的 UTF-16 文件误报为二进制。
        var hasBom = TryDetectBom(bytes, out _, out _, out _);
        var suspectedBinary = !hasBom && LooksBinary(Head(bytes, BinarySampleSize));

        // 用户手动指定编码：跳过自动判定
        if (preference.Mode != EncodingMode.Auto)
        {
            var specified = ResolveEncoding(preference.Mode);
            var name = DisplayName(preference.Mode);
            return new DecodedText(SafeDecode(specified, bytes), specified, name, $"用户手动指定：{name}", suspectedBinary);
        }

        // 1) BOM
        if (TryDetectBom(bytes, out var bomEncoding, out var bomName, out var bomLength))
        {
            return new DecodedText(
                SafeDecode(bomEncoding, bytes[bomLength..]),
                bomEncoding,
                bomName,
                $"BOM：{bomName}",
                false);
        }

        // 2) 严格 UTF-8：成功即为最终结果，避免重复解码
        if (TryDecodeStrict(StrictUtf8, bytes, out var utf8Text))
        {
            return new DecodedText(utf8Text, StrictUtf8, "UTF-8", "无 BOM，严格 UTF-8 解码成功", suspectedBinary);
        }

        // 3) GB18030（GBK 超集，优先于 GBK）
        if (TryGetCodePage(54936, out var gb18030) && TryDecodeStrict(gb18030, bytes, out var gbText))
        {
            return new DecodedText(gbText, gb18030, "GB18030", "严格 UTF-8 失败 → GB18030", suspectedBinary);
        }

        // 4) 系统 ANSI（中文 Windows 为 936）
        if (TryGetCodePage(936, out var ansi) && TryDecodeStrict(ansi, bytes, out var ansiText))
        {
            return new DecodedText(ansiText, ansi, "ANSI(936)", "严格 UTF-8 与 GB18030 均失败 → 系统 ANSI", suspectedBinary);
        }

        // 5) Latin-1 兜底，并标记异常
        return new DecodedText(
            SafeDecode(Encoding.Latin1, bytes),
            Encoding.Latin1,
            "Latin-1",
            "所有编码均失败 → Latin-1 兜底，编码疑似异常",
            suspectedBinary);
    }

    /// <inheritdoc />
    public bool TryGetEncoding(string encodingName, out Encoding encoding)
    {
        encoding = StrictUtf8;

        if (string.IsNullOrWhiteSpace(encodingName))
        {
            return false;
        }

        switch (encodingName.Trim().ToLowerInvariant())
        {
            case "auto":
            case "自动":
                return false;
            case "utf-8":
            case "utf8":
            case "utf8 bom":
                encoding = StrictUtf8;
                return true;
            case "utf-16":
            case "utf-16le":
            case "utf16":
            case "unicode":
                encoding = Encoding.Unicode;
                return true;
            case "utf-16be":
            case "utf16be":
                encoding = Encoding.BigEndianUnicode;
                return true;
            case "gbk":
                if (TryGetCodePage(936, out encoding))
                {
                    return true;
                }

                break;
            case "gb18030":
                if (TryGetCodePage(54936, out encoding))
                {
                    return true;
                }

                break;
            case "ansi":
            case "936":
            case "gb2312":
                if (TryGetCodePage(936, out encoding))
                {
                    return true;
                }

                break;
            case "latin-1":
            case "latin1":
                encoding = Encoding.Latin1;
                return true;
        }

        return false;
    }

    // ── 内部实现 ────────────────────────────────────────────────────────

    private static ReadOnlySpan<byte> Head(ReadOnlySpan<byte> bytes, int maxLength)
        => bytes.Length <= maxLength ? bytes : bytes[..maxLength];

    /// <summary>BOM 探测。注意 UTF-32 必须先于 UTF-16 判定，否则 FF FE 00 00 会被误判。</summary>
    private static bool TryDetectBom(ReadOnlySpan<byte> bytes, out Encoding encoding, out string name, out int bomLength)
    {
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            encoding = Encoding.UTF32;
            name = "UTF-32 LE";
            bomLength = 4;
            return true;
        }

        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
            name = "UTF-32 BE";
            bomLength = 4;
            return true;
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = StrictUtf8;
            name = "UTF-8";
            bomLength = 3;
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = Encoding.Unicode;
            name = "UTF-16 LE";
            bomLength = 2;
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = Encoding.BigEndianUnicode;
            name = "UTF-16 BE";
            bomLength = 2;
            return true;
        }

        encoding = StrictUtf8;
        name = string.Empty;
        bomLength = 0;
        return false;
    }

    private static bool TryDecodeStrict(Encoding encoding, ReadOnlySpan<byte> bytes, out string text)
    {
        try
        {
            text = encoding.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
        catch (ArgumentException)
        {
            // 非法代理项等边界情况
            text = string.Empty;
            return false;
        }
    }

    /// <summary>宽松解码：遇到非法字节用替换字符代替，绝不抛异常。</summary>
    private static string SafeDecode(Encoding encoding, ReadOnlySpan<byte> bytes)
    {
        try
        {
            return encoding.GetString(bytes);
        }
        catch (Exception)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static bool TryGetCodePage(int codePage, out Encoding encoding)
    {
        try
        {
            encoding = Encoding.GetEncoding(
                codePage,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            return true;
        }
        catch (ArgumentException)
        {
            encoding = Encoding.Latin1;
            return false;
        }
    }

    private static Encoding ResolveEncoding(EncodingMode mode) => mode switch
    {
        EncodingMode.Utf8 => StrictUtf8,
        EncodingMode.Utf16Le => Encoding.Unicode,
        EncodingMode.Utf16Be => Encoding.BigEndianUnicode,
        EncodingMode.Gbk => TryGetCodePage(936, out var gbk) ? gbk : Encoding.Latin1,
        EncodingMode.Gb18030 => TryGetCodePage(54936, out var gb) ? gb : Encoding.Latin1,
        EncodingMode.Ansi => TryGetCodePage(936, out var ansi) ? ansi : Encoding.Latin1,
        _ => StrictUtf8,
    };

    private static string DisplayName(EncodingMode mode) => mode switch
    {
        EncodingMode.Utf8 => "UTF-8",
        EncodingMode.Utf16Le => "UTF-16 LE",
        EncodingMode.Utf16Be => "UTF-16 BE",
        EncodingMode.Gbk => "GBK",
        EncodingMode.Gb18030 => "GB18030",
        EncodingMode.Ansi => "ANSI(936)",
        _ => "自动",
    };

    /// <summary>
    /// 疑似二进制判定（§5.1.3）：NUL 记 3 分、其它控制字符记 1 分，得分占比 &gt; 5% 即判为二进制。
    /// </summary>
    private static bool LooksBinary(ReadOnlySpan<byte> sample)
    {
        if (sample.Length == 0)
        {
            return false;
        }

        var score = 0;
        foreach (var b in sample)
        {
            if (b == 0x00)
            {
                score += 3;
            }
            else if (b < 0x09 || (b >= 0x0E && b < 0x20))
            {
                // 排除 \t(09) \n(0A) \r(0D)
                score += 1;
            }
        }

        return score / (double)sample.Length > BinaryScoreThreshold;
    }
}
