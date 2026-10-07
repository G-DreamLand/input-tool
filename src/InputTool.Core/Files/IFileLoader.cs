namespace InputTool.Core.Files;

/// <summary>一次性文件加载的完整结果。</summary>
/// <param name="Success">是否成功取得可用的文本内容。</param>
/// <param name="RawText">原始文本（未做归一化，归一化属于解析层职责）。</param>
/// <param name="Encoding">实际使用的编码。</param>
/// <param name="EncodingName">可显示的编码名。</param>
/// <param name="EncodingReason">编码判定依据。</param>
/// <param name="ByteLength">文件字节数。</param>
/// <param name="Error">失败原因。</param>
/// <param name="Message">面向用户的错误说明。</param>
/// <param name="SuspectedBinary">是否疑似二进制内容（用户可确认继续，FR-001.2）。</param>
public sealed record FileLoadResult(
    bool Success,
    string RawText,
    string EncodingName,
    string EncodingReason,
    long ByteLength,
    FileLoadError Error,
    string? Message,
    bool SuspectedBinary)
{
    /// <summary>文件为空（沿用 SRS：成功读取但内容为空，禁止开始输入）。</summary>
    public bool IsEmpty => Success && RawText.Length == 0;
}

/// <summary>
/// 文件加载。对应《软件开发设计文档》§5.1.1 / §5.1.5。
/// 约束：
/// · 读取失败时不改变当前已加载内容（FR-003、E-03）；
/// · 流式读取，避免大文件一次性占用内存（R-07）；
/// · 只读用户显式选择的文件，不扫描磁盘、不修改源文件（C-02）。
/// </summary>
public interface IFileLoader
{
    Task<FileLoadResult> LoadAsync(string path, EncodingPreference preference, CancellationToken cancellationToken = default);

    /// <summary>仅重新解码已缓存的字节（用户手动切换编码时使用，避免重复读取磁盘）。</summary>
    Task<FileLoadResult> DecodeOnlyAsync(string path, EncodingPreference preference, CancellationToken cancellationToken = default);
}

/// <summary>
/// 最近打开文件列表（上限 10，LRU 去重，FR-002.2）。
/// </summary>
public interface IRecentFilesStore
{
    IReadOnlyList<string> Items { get; }

    void Add(string path);

    void Remove(string path);

    void Clear();

    /// <summary>过滤掉已不存在的文件。</summary>
    IReadOnlyList<string> PruneMissing();
}
