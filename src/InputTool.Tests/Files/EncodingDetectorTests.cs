using System.Text;
using InputTool.Core.Files;

namespace InputTool.Tests.Files;

/// <summary>
/// 编码判定与解码（FR-003、SRS §5.1.1，验收 AC-01 / AC-02 / AC-24）。
/// </summary>
public sealed class EncodingDetectorTests
{
    private readonly EncodingDetector _detector = new();

    static EncodingDetectorTests()
    {
        // 代码页提供程序：EncodingDetector 内部也会注册，这里保证测试自身可直接取 GBK / GB18030
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static byte[] WithBom(Encoding encoding, string text)
        => encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();

    // ── BOM 判定 ────────────────────────────────────────────────────────

    [Fact]
    public void BOM_UTF8_应被识别()
    {
        var bytes = WithBom(new UTF8Encoding(true), "中文ABC");

        var result = _detector.Detect(bytes);

        result.EncodingName.Should().Be("UTF-8");
        result.Reason.Should().Contain("BOM");
    }

    [Fact]
    public void BOM_UTF16LE_应被识别()
    {
        var bytes = WithBom(Encoding.Unicode, "中文ABC");

        _detector.Detect(bytes).EncodingName.Should().Be("UTF-16 LE");
    }

    [Fact]
    public void BOM_UTF16BE_应被识别()
    {
        var bytes = WithBom(Encoding.BigEndianUnicode, "中文ABC");

        _detector.Detect(bytes).EncodingName.Should().Be("UTF-16 BE");
    }

    [Fact]
    public void BOM_UTF32LE_不应被误判为UTF16LE()
    {
        // FF FE 00 00 的判定必须先于 FF FE，否则会被当成 UTF-16 LE（§5.1.2 实现要点）
        var bytes = WithBom(Encoding.UTF32, "中文ABC");

        var result = _detector.Detect(bytes);

        result.EncodingName.Should().Be("UTF-32 LE");
    }

    [Fact]
    public void BOM解码后不应残留BOM字符()
    {
        var bytes = WithBom(new UTF8Encoding(true), "你好");

        var decoded = _detector.Decode(bytes, EncodingPreference.Auto);

        decoded.Text.Should().Be("你好");
    }

    // ── 无 BOM 的自动判定 ───────────────────────────────────────────────

    [Fact]
    public void 无BOM的UTF8中文_应判定为UTF8()
    {
        var bytes = new UTF8Encoding(false).GetBytes("这是一段中文文本，用于验证编码判定。");

        var decoded = _detector.Decode(bytes, EncodingPreference.Auto);

        decoded.EncodingName.Should().Be("UTF-8");
        decoded.Text.Should().Be("这是一段中文文本，用于验证编码判定。");
    }

    [Fact]
    public void 无BOM的GBK中文_应回退到GB18030且不乱码()
    {
        var text = "国标编码测试：中文不乱码。";
        var bytes = Encoding.GetEncoding(936).GetBytes(text);

        var decoded = _detector.Decode(bytes, EncodingPreference.Auto);

        decoded.EncodingName.Should().Be("GB18030");
        decoded.Text.Should().Be(text);
    }

    [Fact]
    public void 所有编码都失败时_应回退Latin1并标记异常()
    {
        // 落单的 0x81：UTF-8 中它是无前导的续字节、GB18030/936 中它缺少后续字节 → 三种编码全部失败
        byte[] bytes = [0x81];

        var decoded = _detector.Decode(bytes, EncodingPreference.Auto);

        decoded.EncodingName.Should().Be("Latin-1");
        decoded.Reason.Should().Contain("疑似异常");
    }

    [Fact]
    public void 空内容_应可安全解码()
    {
        var decoded = _detector.Decode(ReadOnlySpan<byte>.Empty, EncodingPreference.Auto);

        decoded.Text.Should().BeEmpty();
        decoded.SuspectedBinary.Should().BeFalse();
    }

    // ── 手动指定编码（FR-003.6）────────────────────────────────────────

    [Fact]
    public void 手动指定GBK_应跳过自动判定()
    {
        var text = "手动指定编码";
        var bytes = Encoding.GetEncoding(936).GetBytes(text);

        var decoded = _detector.Decode(bytes, new EncodingPreference(EncodingMode.Gbk));

        decoded.EncodingName.Should().Be("GBK");
        decoded.Reason.Should().Contain("手动指定");
        decoded.Text.Should().Be(text);
    }

    [Fact]
    public void 手动指定UTF16LE_应正确解码()
    {
        var text = "UTF16 文本";
        var bytes = Encoding.Unicode.GetBytes(text);

        var decoded = _detector.Decode(bytes, new EncodingPreference(EncodingMode.Utf16Le));

        decoded.Text.Should().Be(text);
    }

    // ── 二进制探测（§5.1.3）───────────────────────────────────────────

    [Fact]
    public void 含大量NUL的字节_应判为疑似二进制()
    {
        var bytes = new byte[64];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i % 2 == 0 ? 0x41 : 0x00);
        }

        _detector.Decode(bytes, EncodingPreference.Auto).SuspectedBinary.Should().BeTrue();
    }

    [Fact]
    public void 普通中文文本_不应判为二进制()
    {
        var bytes = new UTF8Encoding(false).GetBytes("正常文本，含中文与标点。\n第二行\t制表符。");

        _detector.Decode(bytes, EncodingPreference.Auto).SuspectedBinary.Should().BeFalse();
    }

    [Fact]
    public void 带BOM的UTF16_不应被判为二进制()
    {
        // UTF-16 正文里天然有大量 NUL，BOM 是"这是文本"的确凿证据
        var bytes = WithBom(Encoding.Unicode, "这是一段很长的 UTF-16 文本内容，用于验证二进制判定不会误报。");

        _detector.Decode(bytes, EncodingPreference.Auto).SuspectedBinary.Should().BeFalse();
    }

    // ── 显示名解析（配置恢复用）─────────────────────────────────────────

    [Theory]
    [InlineData("utf-8", "utf-8")]
    [InlineData("UTF-16LE", "utf-16")]
    [InlineData("utf-16be", "utf-16BE")]
    [InlineData("gbk", "gb2312")]
    [InlineData("GB18030", "gb18030")]
    [InlineData("ansi", "gb2312")]
    [InlineData("latin-1", "iso-8859-1")]
    public void TryGetEncoding_应识别常见显示名(string name, string expectedWebName)
    {
        _detector.TryGetEncoding(name, out var encoding).Should().BeTrue();

        encoding.WebName.Should().Be(expectedWebName);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("")]
    [InlineData("不存在的编码")]
    public void TryGetEncoding_无法识别时应返回false(string name)
    {
        _detector.TryGetEncoding(name, out _).Should().BeFalse();
    }
}
