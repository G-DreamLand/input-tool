using System.Text;
using InputTool.Core.Files;

namespace InputTool.Tests.Files;

/// <summary>
/// 文件加载与错误分类（FR-001 / FR-003、E-02 / E-03）。
/// </summary>
public sealed class FileLoaderTests : IDisposable
{
    private readonly string _directory;
    private readonly FileLoader _loader = new();

    static FileLoaderTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public FileLoaderTests()
    {
        // 临时目录放在测试程序集目录下（位于工作区内）：
        // 既保证可写，也让测试数据自包含、不污染系统 Temp。
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

    /// <summary>测试取消令牌：xUnit v3 要求传入它以保证测试可被取消（分析器 xUnit1051）。</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── 错误路径 ────────────────────────────────────────────────────────

    [Fact]
    public async Task 路径为空_应返回NotFound且不抛异常()
    {
        var result = await _loader.LoadAsync("   ", EncodingPreference.Auto, Ct);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(FileLoadError.NotFound);
    }

    [Fact]
    public async Task 文件不存在_应返回NotFound()
    {
        var result = await _loader.LoadAsync(Path.Combine(_directory, "不存在.txt"), EncodingPreference.Auto, Ct);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(FileLoadError.NotFound);
        result.Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task 路径是目录_应返回IsDirectory()
    {
        var result = await _loader.LoadAsync(_directory, EncodingPreference.Auto, Ct);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(FileLoadError.IsDirectory);
    }

    [Fact]
    public async Task 空文件_应提示内容为空且禁止开始()
    {
        var path = WriteBytes("empty.txt", []);

        var result = await _loader.LoadAsync(path, EncodingPreference.Auto, Ct);

        result.Success.Should().BeTrue();
        result.Error.Should().Be(FileLoadError.Empty);
        result.RawText.Should().BeEmpty();
        result.Message.Should().Contain("空");
    }

    [Fact]
    public async Task 文件被独占占用_应返回InUse并给出可操作建议()
    {
        var path = WriteText("locked.txt", "内容", new UTF8Encoding(false));

        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var result = await _loader.LoadAsync(path, EncodingPreference.Auto, Ct);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(FileLoadError.InUse);
        result.Message.Should().Contain("占用");
    }

    // ── 正常读取 ────────────────────────────────────────────────────────

    [Fact]
    public async Task UTF8中文文件_应正确读取并记录判定依据()
    {
        var text = "第一行：中文内容。\n第二行：结束。";
        var path = WriteText("utf8.txt", text, new UTF8Encoding(false));

        var result = await _loader.LoadAsync(path, EncodingPreference.Auto, Ct);

        result.Success.Should().BeTrue();
        result.RawText.Should().Be(text);
        result.EncodingName.Should().Be("UTF-8");
        result.EncodingReason.Should().NotBeNullOrEmpty();
        result.ByteLength.Should().BeGreaterThan(0);
        result.SuspectedBinary.Should().BeFalse();
    }

    [Fact]
    public async Task GBK文件_手动指定编码应正确读取()
    {
        var text = "国标文件内容";
        var path = WriteText("gbk.txt", text, Encoding.GetEncoding(936));

        var result = await _loader.LoadAsync(path, new EncodingPreference(EncodingMode.Gbk), Ct);

        result.Success.Should().BeTrue();
        result.RawText.Should().Be(text);
        result.EncodingName.Should().Be("GBK");
    }

    [Fact]
    public async Task 切换编码时_应复用字节缓存而无需重新读盘()
    {
        var text = "缓存复用验证";
        var path = WriteText("cache.txt", text, Encoding.GetEncoding(936));

        // 第一次读取：建立缓存
        var first = await _loader.LoadAsync(path, EncodingPreference.Auto, Ct);
        first.Success.Should().BeTrue();

        // 删除文件后再切换编码：命中缓存仍应成功
        File.Delete(path);
        var second = await _loader.DecodeOnlyAsync(path, new EncodingPreference(EncodingMode.Gbk), Ct);

        second.Success.Should().BeTrue();
        second.RawText.Should().Be(text);
    }

    [Fact]
    public async Task 大文件阈值内的文件_字节数应准确()
    {
        var text = new string('A', 4096);
        var path = WriteText("big.txt", text, new UTF8Encoding(false));

        var result = await _loader.LoadAsync(path, EncodingPreference.Auto, Ct);

        result.ByteLength.Should().Be(4096);
        result.RawText.Length.Should().Be(4096);
    }

    // ── 辅助 ────────────────────────────────────────────────────────────

    private string WriteBytes(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string WriteText(string name, string text, Encoding encoding)
        => WriteBytes(name, encoding.GetBytes(text));
}
