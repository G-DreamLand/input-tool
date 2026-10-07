using System.Text;
using InputTool.Core.Application;
using InputTool.Core.Files;
using InputTool.Core.Parsing;
using InputTool.Core.Scheduling;

namespace InputTool.Tests.Application;

/// <summary>
/// 文档管线端到端测试：文件读取 → 编码判定 → 归一化 → 单元构建 → 统计（设计文档 §5.1 + §5.2）。
/// </summary>
public sealed class DocumentPipelineTests : IDisposable
{
    private readonly string _directory;
    private readonly DocumentPipeline _pipeline = new();

    static DocumentPipelineTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public DocumentPipelineTests()
    {
        // 放在测试程序集目录下（位于工作区内），保证可写且自包含
        _directory = Path.Combine(AppContext.BaseDirectory, "test-tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败不影响测试结论
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UTF8文件_应产出文档与完整统计()
    {
        var path = WriteText("utf8.txt", "第一行\n第二行\n", new UTF8Encoding(false));

        var outcome = await _pipeline.LoadAsync(path, EncodingPreference.Auto, NormalizeOptions.Default, Ct);

        outcome.Succeeded.Should().BeTrue();
        var document = outcome.Document!;
        document.EncodingName.Should().Be("UTF-8");
        document.Statistics.CharacterUnits.Should().Be(6);
        document.Statistics.NewlineUnits.Should().Be(2);
        document.Statistics.LineCount.Should().Be(2);
        document.Units.Should().HaveCount(8);
    }

    [Fact]
    public async Task GBK文件_自动判定应不乱码()
    {
        var path = WriteText("gbk.txt", "国标编码：中文内容。\n第二行。", Encoding.GetEncoding(936));

        var outcome = await _pipeline.LoadAsync(path, EncodingPreference.Auto, NormalizeOptions.Default, Ct);

        outcome.Succeeded.Should().BeTrue();
        outcome.Document!.RawText.Should().Be("国标编码：中文内容。\n第二行。");
        outcome.Document.EncodingName.Should().Be("GB18030");
    }

    [Fact]
    public async Task 空文件_应返回null文档并带Empty错误()
    {
        var path = WriteText("empty.txt", string.Empty, new UTF8Encoding(false));

        var outcome = await _pipeline.LoadAsync(path, EncodingPreference.Auto, NormalizeOptions.Default, Ct);

        outcome.Succeeded.Should().BeFalse();
        outcome.Document.Should().BeNull();
        outcome.FileResult.Error.Should().Be(FileLoadError.Empty);
    }

    [Fact]
    public async Task 归一化选项_应影响单元数量()
    {
        var path = WriteText("blank.txt", "a\n\n\nb", new UTF8Encoding(false));

        var withBlank = await _pipeline.LoadAsync(path, EncodingPreference.Auto, NormalizeOptions.Default, Ct);
        var ignoreBlank = await _pipeline.LoadAsync(
            path,
            EncodingPreference.Auto,
            new NormalizeOptions(IgnoreEmptyLines: true),
            Ct);

        withBlank.Document!.Statistics.NewlineUnits.Should().Be(3);
        ignoreBlank.Document!.Statistics.NewlineUnits.Should().Be(1);
    }

    [Fact]
    public async Task 切换编码_应走缓存重新解码()
    {
        var path = WriteText("switch.txt", "切换编码验证", Encoding.GetEncoding(936));

        var first = await _pipeline.LoadAsync(path, EncodingPreference.Auto, NormalizeOptions.Default, Ct);
        first.Succeeded.Should().BeTrue();

        File.Delete(path);

        var second = await _pipeline.ReloadAsync(path, new EncodingPreference(EncodingMode.Gbk), NormalizeOptions.Default, Ct);

        second.Succeeded.Should().BeTrue();
        second.Document!.RawText.Should().Be("切换编码验证");
    }

    [Fact]
    public async Task 预估时长_应与统计一致()
    {
        var path = WriteText("estimate.txt", new string('a', 100), new UTF8Encoding(false));

        var outcome = await _pipeline.LoadAsync(path, EncodingPreference.Auto, NormalizeOptions.Default, Ct);
        var estimate = DurationEstimator.Estimate(outcome.Document!.Statistics, new SpeedProfile(IntervalMs: 200));

        estimate.Should().Be(TimeSpan.FromSeconds(20));
        DurationEstimator.ToDisplayString(estimate).Should().Be("约 20 秒");
    }

    [Fact]
    public async Task 不存在的文件_应返回失败且文档为null()
    {
        var outcome = await _pipeline.LoadAsync(
            Path.Combine(_directory, "缺失.txt"),
            EncodingPreference.Auto,
            NormalizeOptions.Default,
            Ct);

        outcome.Succeeded.Should().BeFalse();
        outcome.FileResult.Error.Should().Be(FileLoadError.NotFound);
    }

    private string WriteText(string name, string text, Encoding encoding)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, encoding.GetBytes(text));
        return path;
    }
}
