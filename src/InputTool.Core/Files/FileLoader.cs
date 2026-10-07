using InputTool.Core.Logging;

namespace InputTool.Core.Files;

/// <summary>
/// 文件加载。对应《软件开发设计文档》§5.1.1 / §5.1.5。
/// </summary>
/// <remarks>
/// 行为约定：
/// · 读取失败时**不改变**调用方已加载的内容（由调用方负责，见 FR-003）；
/// · 只读用户显式选择的文件，不扫描磁盘、不修改源文件（C-02）；
/// · 小于 <see cref="ByteCacheLimit"/> 的文件会缓存原始字节，供用户切换编码时免重复读盘（FR-003.6）。
/// </remarks>
public sealed class FileLoader : IFileLoader
{
    /// <summary>超过该大小给出强提示（SRS §5.1.6）。</summary>
    public const long LargeFileThreshold = 50L * 1024 * 1024;

    /// <summary>原始字节缓存上限（超过则不缓存，切换编码时重新读盘）。</summary>
    public const int ByteCacheLimit = 8 * 1024 * 1024;

    private readonly IEncodingDetector _detector;
    private readonly IAppLogger? _logger;

    private byte[]? _cachedBytes;
    private string? _cachedPath;

    /// <summary>构造文件加载器。</summary>
    /// <param name="detector">编码判定器。</param>
    /// <param name="logger">日志（可选，未接入时为 null）。</param>
    public FileLoader(IEncodingDetector? detector = null, IAppLogger? logger = null)
    {
        _detector = detector ?? new EncodingDetector();
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FileLoadResult> LoadAsync(
        string path,
        EncodingPreference preference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Failure(FileLoadError.NotFound, "请先选择文本文件。", path);
        }

        if (Directory.Exists(path))
        {
            return Failure(FileLoadError.IsDirectory, $"“{path}”是一个文件夹，请选择文本文件。", path);
        }

        if (!File.Exists(path))
        {
            return Failure(FileLoadError.NotFound, $"找不到文件：{path}", path);
        }

        byte[] bytes;

        try
        {
            bytes = await ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(FileLoadError.AccessDenied, $"没有读取权限：{path}", path);
        }
        catch (IOException ex) when (IsSharingViolation(ex))
        {
            return Failure(
                FileLoadError.InUse,
                "文件无法读取：可能被其他程序独占占用。请关闭占用该文件的程序后点击【重载】。",
                path);
        }
        catch (IOException ex)
        {
            return Failure(FileLoadError.ReadFailed, $"读取失败：{ex.Message}", path);
        }

        if (bytes.Length == 0)
        {
            return new FileLoadResult(
                Success: true,
                RawText: string.Empty,
                EncodingName: preference.Mode == EncodingMode.Auto ? "自动" : "指定",
                EncodingReason: "空文件",
                ByteLength: 0,
                Error: FileLoadError.Empty,
                Message: "文件内容为空，无法开始输入。",
                SuspectedBinary: false);
        }

        if (bytes.Length > LargeFileThreshold)
        {
            _logger?.Warn($"文件较大：{bytes.Length / (1024 * 1024)} MB（path=\"{path}\"），解析可能较慢", "File");
        }

        CacheBytes(path, bytes);

        var decoded = _detector.Decode(bytes, preference);

        if (decoded.SuspectedBinary)
        {
            _logger?.Warn($"疑似非文本文件（path=\"{path}\"）", "File");
        }

        _logger?.Info(
            $"加载文件 path=\"{path}\" encoding={decoded.EncodingName} bytes={bytes.Length} 依据={decoded.Reason}",
            "File");

        return new FileLoadResult(
            Success: true,
            RawText: decoded.Text,
            EncodingName: decoded.EncodingName,
            EncodingReason: decoded.Reason,
            ByteLength: bytes.Length,
            Error: FileLoadError.None,
            Message: decoded.SuspectedBinary ? "疑似非文本文件，仍要继续吗？" : null,
            SuspectedBinary: decoded.SuspectedBinary);
    }

    /// <inheritdoc />
    public async Task<FileLoadResult> DecodeOnlyAsync(
        string path,
        EncodingPreference preference,
        CancellationToken cancellationToken = default)
    {
        // 命中字节缓存时无需重新读盘（切换编码的高频场景）
        if (_cachedBytes is not null &&
            string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase))
        {
            var decoded = _detector.Decode(_cachedBytes, preference);

            return new FileLoadResult(
                Success: true,
                RawText: decoded.Text,
                EncodingName: decoded.EncodingName,
                EncodingReason: decoded.Reason,
                ByteLength: _cachedBytes.Length,
                Error: FileLoadError.None,
                Message: null,
                SuspectedBinary: decoded.SuspectedBinary);
        }

        return await LoadAsync(path, preference, cancellationToken).ConfigureAwait(false);
    }

    // ── 内部实现 ────────────────────────────────────────────────────────

    private static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        // FileShare.ReadWrite：允许其他程序同时打开（记事本等），只有真正独占时才报 InUse
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 64 * 1024,
            useAsync: true);

        var length = stream.Length;
        if (length == 0)
        {
            return Array.Empty<byte>();
        }

        var buffer = new byte[length];
        var offset = 0;

        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return offset == buffer.Length ? buffer : buffer[..offset];
    }

    private void CacheBytes(string path, byte[] bytes)
    {
        if (bytes.Length > ByteCacheLimit)
        {
            _cachedBytes = null;
            _cachedPath = null;
            return;
        }

        _cachedBytes = bytes;
        _cachedPath = path;
    }

    /// <summary>Win32 错误码 32（共享冲突）/ 33（锁定冲突）→ 文件被占用。</summary>
    private static bool IsSharingViolation(IOException exception)
        => (exception.HResult & 0xFFFF) is 32 or 33;

    private static FileLoadResult Failure(FileLoadError error, string message, string path)
        => new(
            Success: false,
            RawText: string.Empty,
            EncodingName: string.Empty,
            EncodingReason: string.Empty,
            ByteLength: 0,
            Error: error,
            Message: message,
            SuspectedBinary: false);
}
