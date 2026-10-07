using InputTool.Core.Parsing;

namespace InputTool.Tests.Parsing;

/// <summary>
/// 文本归一化（FR-004.1/.2、SRS §5.1.2）。
/// </summary>
public sealed class TextNormalizerTests
{
    private readonly TextNormalizer _normalizer = new();

    [Fact]
    public void CRLF_应归一化为LF()
    {
        _normalizer.Normalize("第一行\r\n第二行\r\n", NormalizeOptions.Default)
            .Should().Be("第一行\n第二行\n");
    }

    [Fact]
    public void 孤立CR_应归一化为LF()
    {
        _normalizer.Normalize("第一行\r第二行", NormalizeOptions.Default)
            .Should().Be("第一行\n第二行");
    }

    [Fact]
    public void 纯LF_应保持不变()
    {
        _normalizer.Normalize("a\nb\nc", NormalizeOptions.Default).Should().Be("a\nb\nc");
    }

    [Fact]
    public void 混合换行_应全部归一化()
    {
        _normalizer.Normalize("a\r\nb\rc\nd", NormalizeOptions.Default).Should().Be("a\nb\nc\nd");
    }

    [Fact]
    public void 开头的BOM残留_应被去除()
    {
        _normalizer.Normalize("\uFEFF你好", NormalizeOptions.Default).Should().Be("你好");
    }

    [Fact]
    public void 中间的BOM字符_应保留()
    {
        // 只有位于索引 0 的才算 BOM 残留
        _normalizer.Normalize("a\uFEFFb", NormalizeOptions.Default).Should().Be("a\uFEFFb");
    }

    [Fact]
    public void 连续CRLF_应产生连续LF()
    {
        _normalizer.Normalize("a\r\n\r\nb", NormalizeOptions.Default).Should().Be("a\n\nb");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void 空输入_应返回空串(string? input)
    {
        _normalizer.Normalize(input!, NormalizeOptions.Default).Should().BeEmpty();
    }

    [Fact]
    public void 不在开头的BOM与CR混合_应正确处理()
    {
        _normalizer.Normalize("\uFEFFa\r\nb", NormalizeOptions.Default).Should().Be("a\nb");
    }
}
