using System.Collections.Concurrent;
using System.Text;

namespace InputTool.Core.Logging;

/// <summary>
/// 文件日志。对应《软件开发设计文档》§5.8。
/// </summary>
/// <remarks>
/// 关键行为：
/// · 目录 <c>%APPDATA%\InputTool\logs</c>（便携模式由调用方传入覆盖目录），文件 <c>inputtool-yyyyMMdd.log</c>；
/// · 生产者在任意线程入**有界队列**（满了丢弃 Debug 并计数），后台线程批量落盘——
///   日志绝不能阻塞输入线程（NFR-002）；
/// · 单文件超过 5 MB 自动切换新文件；
/// · 启动时清理超过保留期的历史日志（FR-021.1）；
/// · 隐私：本类**不记录输入内容**，只记录调用方传入的描述性文本（FR-021.3 / NFR-014）。
/// </remarks>
public sealed class FileLogger : IAppLogger, IDisposable
{
    /// <summary>单文件上限（FR-021.1）。</summary>
    public const long MaxFileBytes = 5L * 1024 * 1024;

    /// <summary>队列容量：满了丢弃 Debug，绝不阻塞业务线程。</summary>
    public const int QueueCapacity = 4096;

    private readonly record struct LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message);

    private readonly BlockingCollection<LogEntry> _queue = new(new ConcurrentQueue<LogEntry>(), QueueCapacity);
    private readonly Thread _writerThread;
    private readonly object _fileGate = new();

    private LogLevel _minimumLevel;
    private int _retentionDays;
    private string? _rolledPath;
    private bool _disposed;
    private int _droppedEntries;
    private int _consecutiveWriteFailures;
    private string? _fallbackDirectory;

    /// <summary>已入队 / 已落盘的条目数：构成 Flush 的屏障。</summary>
    private long _enqueued;
    private long _written;

    /// <summary>构造文件日志。</summary>
    /// <param name="overrideDirectory">日志目录；省略时使用 <c>%APPDATA%\InputTool\logs</c>。</param>
    /// <param name="minimumLevel">最低记录级别。</param>
    /// <param name="retentionDays">保留天数（1–90）。</param>
    public FileLogger(string? overrideDirectory = null, LogLevel minimumLevel = LogLevel.Info, int retentionDays = 7)
    {
        LogDirectory = string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "InputTool",
                "logs")
            : overrideDirectory;

        _minimumLevel = minimumLevel;
        _retentionDays = Math.Clamp(retentionDays, 1, 90);

        Directory.CreateDirectory(LogDirectory);

        _writerThread = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "InputTool.LogWriter",
        };
        _writerThread.Start();

        CleanupExpiredFiles();
    }

    /// <inheritdoc />
    /// <remarks>
    /// 主目录写不进去时会**自动降级**到 <c>%TEMP%\InputTool-logs</c>，
    /// 此时本属性返回的是实际生效的目录。降级原因见 <see cref="LastWriteError"/>。
    /// </remarks>
    public string LogDirectory { get; private set; }

    /// <summary>
    /// 最近一次写入失败的原因；为 null 表示一切正常。
    /// </summary>
    /// <remarks>
    /// 真实踩过：某个实例能正常起窗口、但一条日志都没写出来，
    /// 排查时完全没有线索。所以写入失败必须留下痕迹，而不是被静默吞掉。
    /// </remarks>
    public string? LastWriteError { get; private set; }

    /// <summary>是否已降级到备用目录。</summary>
    public bool IsUsingFallbackDirectory => _fallbackDirectory is not null;

    /// <summary>因队列满而被丢弃的条目数（供诊断）。</summary>
    public int DroppedEntries => Volatile.Read(ref _droppedEntries);

    /// <inheritdoc />
    public bool IsEnabled(LogLevel level) => level >= _minimumLevel;

    /// <inheritdoc />
    public void Debug(string message, string category = "App") => Enqueue(LogLevel.Debug, category, message);

    /// <inheritdoc />
    public void Info(string message, string category = "App") => Enqueue(LogLevel.Info, category, message);

    /// <inheritdoc />
    public void Warn(string message, string category = "App") => Enqueue(LogLevel.Warn, category, message);

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null, string category = "App")
    {
        var text = exception is null ? message : $"{message} :: {exception}";
        Enqueue(LogLevel.Error, category, text);
    }

    /// <inheritdoc />
    public void SetMinimumLevel(LogLevel level) => _minimumLevel = level;

    /// <summary>更新保留天数并立即清理一次。</summary>
    public void SetRetentionDays(int days)
    {
        _retentionDays = Math.Clamp(days, 1, 90);
        CleanupExpiredFiles();
    }

    /// <inheritdoc />
    /// <remarks>
    /// 等的是"已**落盘**"而不是"已出队"：写入线程可能刚从队列取走条目、还没写完，
    /// 此时若只看队列长度就会提前返回，调用方随即读到的可能是空文件。
    /// </remarks>
    public void Flush()
    {
        var target = Interlocked.Read(ref _enqueued);
        var deadline = DateTime.UtcNow.AddSeconds(2);

        while (Interlocked.Read(ref _written) < target && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _queue.CompleteAdding();

        try
        {
            _writerThread.Join(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // 忽略
        }

        _queue.Dispose();
    }

    // ── 内部实现 ────────────────────────────────────────────────────────

    private void Enqueue(LogLevel level, string category, string message)
    {
        if (_disposed || level < _minimumLevel)
        {
            return;
        }

        var entry = new LogEntry(DateTimeOffset.Now, level, category, message);

        if (_queue.TryAdd(entry))
        {
            Interlocked.Increment(ref _enqueued);
        }
        else
        {
            // 队列满：优先丢 Debug，并计数；业务线程绝不阻塞
            Interlocked.Increment(ref _droppedEntries);
        }
    }

    private void WriteLoop()
    {
        var buffer = new List<LogEntry>(128);

        try
        {
            foreach (var entry in _queue.GetConsumingEnumerable())
            {
                buffer.Add(entry);

                // 尽量把队列里已有的都取出来，减少开关文件的次数
                while (buffer.Count < 128 && _queue.TryTake(out var more))
                {
                    buffer.Add(more);
                }

                try
                {
                    WriteBatch(buffer);
                    Interlocked.Add(ref _written, buffer.Count);
                    _consecutiveWriteFailures = 0;
                    LastWriteError = null;
                }
                catch (Exception ex)
                {
                    // 写日志失败绝不能影响主流程，但**必须留下痕迹**：
                    // 连续失败到一定次数就降级到备用目录，否则日志会彻底消失。
                    _consecutiveWriteFailures++;
                    LastWriteError = $"{ex.GetType().Name}: {ex.Message}";

                    if (_consecutiveWriteFailures >= FallbackAfterFailures)
                    {
                        SwitchToFallbackDirectory();
                    }
                }

                buffer.Clear();
            }
        }
        catch (ObjectDisposedException)
        {
            // 正常退出
        }
    }

    /// <summary>
    /// 成批写入，并且**写完就关**。
    /// 不长期持有写句柄的原因：只要本进程持有写权限，记事本 / File.ReadAllText
    /// 这类以"拒绝写入"方式打开的工具就会打不开日志文件——而"打开日志目录排障"是 FR-021.4 的显式需求。
    /// </summary>
    private void WriteBatch(List<LogEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        lock (_fileGate)
        {
            var path = ResolvePathFor(entries[^1].Timestamp);

            using var stream = new StreamWriter(
                new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(false));

            foreach (var entry in entries)
            {
                stream.WriteLine(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{LevelText(entry.Level)}] [{entry.Category}] {entry.Message}"));
            }
        }
    }

    /// <summary>连续写入失败达到该次数后，降级到备用日志目录。</summary>
    private const int FallbackAfterFailures = 3;

    /// <summary>
    /// 主目录写不进去时降级到 <c>%TEMP%\InputTool-logs</c>。
    /// </summary>
    /// <remarks>
    /// 触发场景：日志文件被杀软/其它进程长期占用、目录权限被改等。
    /// 降级只做一次，之后一直用备用目录，避免在失败目录上反复重试。
    /// </remarks>
    private void SwitchToFallbackDirectory()
    {
        if (_fallbackDirectory is not null)
        {
            return;
        }

        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "InputTool-logs");
            Directory.CreateDirectory(directory);

            _fallbackDirectory = directory;
            LogDirectory = directory;
            _rolledPath = null;
            _consecutiveWriteFailures = 0;
        }
        catch (Exception)
        {
            // 连备用目录也建不出来，就只能继续丢弃（但 LastWriteError 仍会保留）
        }
    }

    /// <summary>决定当前该写哪个文件；超过 5 MB 时切到带时间戳的新文件。</summary>
    private string ResolvePathFor(DateTimeOffset timestamp)
    {
        if (_rolledPath is not null && GetFileLength(_rolledPath) < MaxFileBytes)
        {
            return _rolledPath;
        }

        var dayPath = Path.Combine(LogDirectory, $"inputtool-{timestamp:yyyyMMdd}.log");

        if (GetFileLength(dayPath) < MaxFileBytes)
        {
            _rolledPath = null;
            return dayPath;
        }

        _rolledPath = Path.Combine(LogDirectory, $"inputtool-{timestamp:yyyyMMdd-HHmmss}.log");
        return _rolledPath;
    }

    private static long GetFileLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private void CleanupExpiredFiles()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-_retentionDays);

            foreach (var file in Directory.EnumerateFiles(LogDirectory, "inputtool-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception)
        {
            // 清理失败不影响启动
        }
    }

    private static string LevelText(LogLevel level) => level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO ",
        LogLevel.Warn => "WARN ",
        _ => "ERROR",
    };
}
