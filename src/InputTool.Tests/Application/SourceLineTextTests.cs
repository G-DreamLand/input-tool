using InputTool.Core.Application;
using InputTool.Core.Parsing;

namespace InputTool.Tests.Application;

/// <summary>
/// 「下一行」预览的源文本取行（FR 新增：步进时显示下一行）。
/// </summary>
/// <remarks>
/// 这里守的是四条硬约束中能在 Core 层自动验证的两条：
/// **显示源文本行** 与 **空行显示「（空行）」**。
/// 「浮层不抢焦点 / 限宽限高」属于 WPF 层，见 <c>OverlayXamlContractTests</c>。
/// </remarks>
public sealed class SourceLineTextTests
{
    [Fact]
    public void 空行_应显示为空行标记()
        => SourceLineText.Resolve("aa\n\nbb", 1).Should().Be(SourceLineText.EmptyLineMarker);

    [Fact]
    public void 开头的空行与末尾的空行_同样显示为空行标记()
    {
        // 首行是空行
        SourceLineText.Resolve("\nbb", 0).Should().Be(SourceLineText.EmptyLineMarker);

        // 末尾换行后还有一段空行（行号与解析器一致：文本末尾以 \n 结束时最后一段是空行）
        SourceLineText.Resolve("aa\n", 1).Should().Be(SourceLineText.EmptyLineMarker);
    }

    [Fact]
    public void Tab_应显示为箭头_与预览区保持一致()
        => SourceLineText.Resolve("a\tb", 0).Should().Be("a→b");

    [Fact]
    public void 纯Tab行_应显示为箭头而不是空行()
        => SourceLineText.Resolve("\t", 0).Should().Be("→");

    [Theory]
    [InlineData("aa\nbb")]
    [InlineData("aa\r\nbb")]
    [InlineData("aa\rbb")]
    public void 三种换行_都应正确切分(string text)
    {
        var lines = SourceLineText.SplitLines(text);

        lines.Should().HaveCount(2, "LF / CRLF / CR 各算一个换行（SRS §5.1.2）");
        lines[0].Should().Be("aa");
        lines[1].Should().Be("bb");
    }

    [Fact]
    public void 行文本取自源文本_而不是归一化结果()
    {
        // 浮层要显示"文件里真正写着什么"，所以第 2 行是 "bb" 而不是带 \r 的 "bb\r"。
        // 归一化只影响注入内容，不该影响用户看到的预览。
        SourceLineText.Resolve("aa\r\nbb", 1).Should().Be("bb");
    }

    [Fact]
    public void 空文本_没有任何一行()
        => SourceLineText.SplitLines(string.Empty).Should().BeEmpty();

    [Fact]
    public void 越界行号_应明确提示而不是显示别的行()
    {
        SourceLineText.Resolve("aa\nbb", 99).Should().Be(SourceLineText.UnavailableMarker);
        SourceLineText.Resolve("aa\nbb", -1).Should().Be(SourceLineText.UnavailableMarker);
    }

    [Fact]
    public void 开启转义解析后行数会变多_越界必须显式提示()
    {
        // 「解析转义序列」开着时，字面量 \n 会被展开成真正的换行，
        // 于是解析器看到的行数多于源文本行数。
        // 这种时候绝不能静默显示错行（§21.14 第七条：状态文案不回退）。
        var options = NormalizeOptions.Default with { ParseEscapes = true };

        var normalized = new TextNormalizer().Normalize(@"a\nb", options);
        var units = new InputUnitBuilder().Build(normalized, options).Units;

        var maxLineIndex = units.Max(u => u.LineIndex);
        maxLineIndex.Should().BeGreaterThan(0, "转义展开后解析器看到了更多行");

        SourceLineText.Resolve(@"a\nb", maxLineIndex).Should().Be(SourceLineText.UnavailableMarker);
    }

    [Fact]
    public void 开头BOM残留_不应计入首行内容()
        => SourceLineText.Resolve("\uFEFFaa\nbb", 0).Should().Be("aa", "BOM 残留不是行内容（FR-004.2）");

    [Fact]
    public void 只含控制字符的行_应视同空行()
    {
        // 这些字符本来就不会被注入（FR-008），显示成「（空行）」比显示成方块更诚实
        SourceLineText.Resolve("aa\n\u0001\u0002\nbb", 1).Should().Be(SourceLineText.EmptyLineMarker);
    }

    [Theory]
    [InlineData("aa\nbb\ncc")]
    [InlineData("aa\r\nbb\r\ncc")]
    [InlineData("aa\rbb\rcc")]
    [InlineData("aa\n\nbb")]
    [InlineData("\n\n\n")]
    [InlineData("aa\n")]
    public void 行号切分_应与单元构建器完全一致(string text)
    {
        // 这是整个功能的地基：解析器给出的行号必须能在**源文本行**里定位到，
        // 否则浮层会显示成别的行，而且不会有任何报错。
        var options = NormalizeOptions.Default;
        var normalized = new TextNormalizer().Normalize(text, options);
        var units = new InputUnitBuilder().Build(normalized, options).Units;
        var lines = SourceLineText.SplitLines(text);

        units.Should().NotBeEmpty();

        foreach (var unit in units)
        {
            unit.LineIndex.Should().BeInRange(
                0,
                lines.Count - 1,
                "解析器行号 {0} 必须能在源文本的 {1} 行里定位到", unit.LineIndex, lines.Count);
        }
    }

    [Fact]
    public void 空行也算一行_不能把空行从行号里挤掉()
    {
        // "aa\n\nbb" 的第 2 行是空行；如果切分时把空行丢掉，第 3 行就会被错报成第 2 行
        var lines = SourceLineText.SplitLines("aa\n\nbb");

        lines.Should().HaveCount(3);
        lines[1].Should().BeEmpty();
        lines[2].Should().Be("bb");
    }
}