using InputTool.Core.Interop;
using InputTool.Core.Parsing;

namespace InputTool.Tests.Parsing;

/// <summary>
/// 输入单元构建（FR-004 / FR-008 / FR-011、SRS §5.3.2）。
/// </summary>
public sealed class InputUnitBuilderTests
{
    private readonly InputUnitBuilder _builder = new();

    private UnitBuildResult Build(string text, NormalizeOptions? options = null)
        => _builder.Build(text, options ?? NormalizeOptions.Default);

    // ── 基本单元与统计 ──────────────────────────────────────────────────

    [Fact]
    public void 单行纯字符_应逐字符生成单元()
    {
        var result = Build("abc");

        result.Units.Should().HaveCount(3);
        result.Units.Should().OnlyContain(u => u.Kind == InputUnitKind.Character);
        result.Statistics.CharacterUnits.Should().Be(3);
        result.Statistics.TotalUnits.Should().Be(3);
        result.Statistics.LineCount.Should().Be(1);
    }

    [Fact]
    public void 两行文本_换行单元与行号应正确()
    {
        var result = Build("a\nb");

        result.Units.Should().HaveCount(3);
        result.Units[0].Kind.Should().Be(InputUnitKind.Character);
        result.Units[0].LineIndex.Should().Be(0);
        result.Units[1].Kind.Should().Be(InputUnitKind.NewLine);
        result.Units[1].LineIndex.Should().Be(0);
        result.Units[2].Kind.Should().Be(InputUnitKind.Character);
        result.Units[2].LineIndex.Should().Be(1);
        result.Statistics.LineCount.Should().Be(2);
    }

    [Fact]
    public void 源索引_应指向原始文本中的位置()
    {
        var result = Build("a\nb");

        result.Units[0].SourceIndex.Should().Be(0);  // a
        result.Units[1].SourceIndex.Should().Be(1);  // \n
        result.Units[2].SourceIndex.Should().Be(2);  // b
    }

    [Fact]
    public void 末尾换行符_应产生换行单元且不再补()
    {
        var result = Build("a\n");

        result.Units.Should().HaveCount(2);
        result.Units[^1].Kind.Should().Be(InputUnitKind.NewLine);
        result.Statistics.NewlineUnits.Should().Be(1);
    }

    [Fact]
    public void 连续空行_应产生两个换行单元()
    {
        // SRS §5.1.2：空行在终端中表现为再执行一次空命令
        var result = Build("a\n\nb");

        result.Statistics.NewlineUnits.Should().Be(2);
        result.Statistics.LineCount.Should().Be(3);
    }

    // ── 特殊字符映射（FR-008）─────────────────────────────────────────

    [Fact]
    public void Tab_应映射为功能键()
    {
        var result = Build("a\tb");

        result.Units[1].Kind.Should().Be(InputUnitKind.FunctionKey);
        result.Units[1].Key.Should().Be(VirtualKey.Tab);
        result.Statistics.FunctionKeyUnits.Should().Be(1);
    }

    [Fact]
    public void 退格字符_应映射为VK_BACK()
    {
        var result = Build("a\bb");

        result.Units[1].Kind.Should().Be(InputUnitKind.FunctionKey);
        result.Units[1].Key.Should().Be(VirtualKey.Back);
    }

    [Fact]
    public void Esc字符_应映射为VK_ESCAPE()
    {
        // 注意写成 \u001B：C# 的 \x 转义会贪婪地吃掉后续十六进制字符（\x1Bb 会被解析成 U+01BB）
        var result = Build("a\u001Bb");

        result.Units[1].Kind.Should().Be(InputUnitKind.FunctionKey);
        result.Units[1].Key.Should().Be(VirtualKey.Escape);
    }

    [Fact]
    public void NUL_应被跳过并计数()
    {
        var result = Build("a\0b");

        result.Units.Should().HaveCount(2);
        result.Statistics.SkippedUnits.Should().Be(1);
        result.Warnings.Should().Contain(w => w.Contains("跳过"));
    }

    [Fact]
    public void 其它C0控制字符_应被跳过并计数()
    {
        var result = Build("a\u0001\u0002b");

        result.Statistics.CharacterUnits.Should().Be(2);
        result.Statistics.SkippedUnits.Should().Be(2);
    }

    [Fact]
    public void 零宽字符_应保留为普通字符()
    {
        var result = Build("a\u200Bb");

        result.Statistics.CharacterUnits.Should().Be(3);
        result.Units[1].Rune.Value.Should().Be(0x200B);
    }

    [Fact]
    public void Emoji代理对_应折叠为单个Rune()
    {
        // FR-011.2：BMP 外字符按代理对发送，但解析层必须折叠成一个单元（ADR-12）
        var result = Build("a😀b");

        result.Statistics.CharacterUnits.Should().Be(3);
        result.Units[1].Kind.Should().Be(InputUnitKind.Character);
        result.Units[1].Rune.Value.Should().Be(0x1F600);
    }

    [Fact]
    public void 组合字符_应按码元顺序各自成单元()
    {
        // e + U+0301（组合尖音符）
        var result = Build("e\u0301");

        result.Statistics.CharacterUnits.Should().Be(2);
        result.Units[0].Rune.Value.Should().Be('e');
        result.Units[1].Rune.Value.Should().Be(0x0301);
    }

    // ── 归一化开关（FR-004.3~.6 / Q-01）──────────────────────────────

    [Fact]
    public void 忽略空行_应跳过空行的换行单元()
    {
        var result = Build("a\n\nb", new NormalizeOptions(IgnoreEmptyLines: true));

        result.Statistics.NewlineUnits.Should().Be(1);
    }

    [Fact]
    public void 跳过注释行_应整行不产生单元()
    {
        var result = Build("a\n# 注释\nb", new NormalizeOptions(SkipCommentLines: true));

        result.Statistics.CharacterUnits.Should().Be(2);
        result.Statistics.NewlineUnits.Should().Be(1);
    }

    [Fact]
    public void 末尾补回车_文本末尾无换行时应补一个()
    {
        var result = Build("a", new NormalizeOptions(AppendTrailingEnter: true));

        result.Units[^1].Kind.Should().Be(InputUnitKind.NewLine);
        result.Statistics.NewlineUnits.Should().Be(1);
    }

    [Fact]
    public void 末尾补回车_文本末尾已有换行时不应重复补()
    {
        var result = Build("a\n", new NormalizeOptions(AppendTrailingEnter: true));

        result.Statistics.NewlineUnits.Should().Be(1);
        result.Units[^1].Kind.Should().Be(InputUnitKind.NewLine);
    }

    [Fact]
    public void 超出上限_应截断并给出警告()
    {
        var text = new string('a', 100);

        var result = Build(text, new NormalizeOptions(MaxUnits: 10));

        result.Units.Should().HaveCount(10);
        result.Statistics.TruncatedByLimit.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Contains("上限"));
    }

    // ── 转义序列解析（FR-004.3、SRS §5.1.4）──────────────────────────

    [Theory]
    [InlineData(@"a\nb", "a\nb")]
    [InlineData(@"a\rb", "a\nb")]
    [InlineData(@"a\tb", "a\tb")]
    [InlineData(@"a\\b", @"a\b")]
    [InlineData(@"a\eb", "a\u001Bb")]
    [InlineData(@"\x41", "A")]
    public void ParseEscapes_应按规则解析(string input, string expected)
    {
        _builder.ParseEscapes(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"a\qb")]      // 未识别的转义：原样保留
    [InlineData(@"a\xZZb")]    // \x 后不是十六进制
    [InlineData(@"a\")]        // 末尾单独的反斜杠
    public void ParseEscapes_无法识别时_应原样保留(string input)
    {
        _builder.ParseEscapes(input).Should().Be(input);
    }

    [Fact]
    public void 关闭转义解析时_字面量反斜杠n应作为两个字符输入()
    {
        var result = Build(@"a\nb", new NormalizeOptions(ParseEscapes: false));

        // 关闭时 \n 是两个普通字符（"\" 与 "n"），而不是换行
        result.Statistics.CharacterUnits.Should().Be(4);
        result.Statistics.NewlineUnits.Should().Be(0);
    }

    [Fact]
    public void 开启转义解析时_字面量反斜杠n应成为换行单元()
    {
        var result = Build(@"a\nb", new NormalizeOptions(ParseEscapes: true));

        result.Statistics.CharacterUnits.Should().Be(2);
        result.Statistics.NewlineUnits.Should().Be(1);
    }

    // ── 边界 ────────────────────────────────────────────────────────────

    [Fact]
    public void 空文本_应返回空结果()
    {
        var result = Build(string.Empty);

        result.Units.Should().BeEmpty();
        result.Statistics.Should().Be(TextStatistics.Empty);
    }

    [Fact]
    public void 只有换行_应只有一个换行单元()
    {
        var result = Build("\n");

        result.Units.Should().HaveCount(1);
        result.Units[0].Kind.Should().Be(InputUnitKind.NewLine);
    }

    [Fact]
    public void 只有空格的行_应是空格单元加换行单元()
    {
        var result = Build("  \n");

        result.Statistics.CharacterUnits.Should().Be(2);
        result.Statistics.NewlineUnits.Should().Be(1);
    }
}
