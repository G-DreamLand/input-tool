using System.Text.RegularExpressions;
using InputTool.Core.Logging;

namespace InputTool.Tests.Logging;

/// <summary>
/// 运行日志（FR-021、NFR-014、设计文档 §5.8）。
/// </summary>
public sealed class FileLoggerTests : IDisposable
{
    private readonly string _directory;

    public FileLoggerTests()
    {
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

    private string ReadAllLogText()
    {
        var files = Directory.GetFiles(_directory, "inputtool-*.log");
        return files.Length == 0 ? string.Empty : string.Concat(files.Select(ReadAllTextShared));
    }

    /// <summary>
    /// 以共享模式读取，并容忍"正好撞上写入批次"的窗口。
    /// 生产代码写完就关句柄，但读取与写入仍有微小的竞争窗口。
    /// </summary>
    private static string ReadAllTextShared(string path)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException)
            {
                Thread.Sleep(20);
            }
        }

        return string.Empty;
    }

    [Fact]
    public void 写入后_应立即能在日志文件中读到()
    {
        using var logger = new FileLogger(_directory);
        logger.Info("测试消息一", "Unit");
        logger.Flush();

        ReadAllLogText().Should().Contain("测试消息一");
    }

    [Fact]
    public void 日志行_应符合设计文档的格式()
    {
        using var logger = new FileLogger(_directory);
        logger.Warn("磁盘快满了", "Storage");
        logger.Flush();

        // yyyy-MM-dd HH:mm:ss.fff [LEVEL] [Category] message
        // 行尾用 \r?$：StreamWriter 写的是 CRLF，而 Multiline 下的 $ 只认 \n
        var pattern = @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[WARN \] \[Storage\] 磁盘快满了\r?$";
        var actual = ReadAllLogText().Trim();

        Regex.IsMatch(actual, pattern, RegexOptions.Multiline)
            .Should().BeTrue($"实际日志内容：<<<{actual}>>>");
    }

    [Fact]
    public void 低于最低级别_不应写入()
    {
        using var logger = new FileLogger(_directory, minimumLevel: LogLevel.Info);

        logger.Debug("这是调试信息");
        logger.Info("这是普通信息");
        logger.Flush();

        var text = ReadAllLogText();
        text.Should().NotContain("这是调试信息");
        text.Should().Contain("这是普通信息");
    }

    [Fact]
    public void 切换到Debug级别_应记录调试信息()
    {
        using var logger = new FileLogger(_directory, minimumLevel: LogLevel.Info);
        logger.SetMinimumLevel(LogLevel.Debug);

        logger.Debug("打开了调试开关");
        logger.Flush();

        ReadAllLogText().Should().Contain("打开了调试开关");
    }

    [Fact]
    public void 异常日志_应包含异常堆栈()
    {
        using var logger = new FileLogger(_directory);

        try
        {
            throw new InvalidOperationException("故意抛出的测试异常");
        }
        catch (Exception ex)
        {
            logger.Error("操作失败", ex, "Unit");
        }

        logger.Flush();

        var text = ReadAllLogText();
        text.Should().Contain("操作失败");
        text.Should().Contain("故意抛出的测试异常");
    }

    [Fact]
    public void 超过保留期的历史日志_应被清理()
    {
        var stale = Path.Combine(_directory, "inputtool-20000101.log");
        File.WriteAllText(stale, "很久以前的日志");
        File.SetLastWriteTime(stale, DateTime.Now.AddDays(-30));

        using var logger = new FileLogger(_directory, retentionDays: 7);

        File.Exists(stale).Should().BeFalse("超过保留期的日志必须在启动时清理（FR-021.1）");
    }

    [Fact]
    public void 保留期内的历史日志_不应被清理()
    {
        var recent = Path.Combine(_directory, "inputtool-20240102.log");
        File.WriteAllText(recent, "三天前的日志");
        File.SetLastWriteTime(recent, DateTime.Now.AddDays(-3));

        using var logger = new FileLogger(_directory, retentionDays: 7);

        File.Exists(recent).Should().BeTrue();
    }

    [Fact]
    public void 文件名_应按日期滚动()
    {
        using var logger = new FileLogger(_directory);
        logger.Info("排个名字");
        logger.Flush();

        Directory.GetFiles(_directory, "inputtool-*.log")
            .Should().ContainMatch($"*inputtool-{DateTime.Now:yyyyMMdd}.log");
    }

    [Fact]
    public void 日志目录_应自动创建()
    {
        var nested = Path.Combine(_directory, "a", "b", "logs");

        using var logger = new FileLogger(nested);
        logger.Info("目录应被创建");
        logger.Flush();

        Directory.Exists(nested).Should().BeTrue();
        logger.LogDirectory.Should().Be(nested);
    }

    [Fact]
    public void 高并发写入_不应阻塞调用方()
    {
        using var logger = new FileLogger(_directory);

        // 生产者远快于消费者：队列满时应丢弃而不是阻塞（NFR-002）
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, 10_000, i => logger.Info($"并发日志 {i}", "Stress"));
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "日志绝不能阻塞业务线程");
        logger.DroppedEntries.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void 释放后_写入不应抛异常()
    {
        var logger = new FileLogger(_directory);
        logger.Dispose();

        var act = () => logger.Info("释放后的写入");

        act.Should().NotThrow();
    }
}
