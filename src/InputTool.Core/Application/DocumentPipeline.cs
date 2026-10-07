using InputTool.Core.Files;
using InputTool.Core.Parsing;

namespace InputTool.Core.Application;

/// <summary>一次文档加载的结果：文档可能为 null（读取失败或空文件），此时看 <see cref="FileResult"/>。</summary>
/// <param name="Document">解析完成的文档；失败时为 null。</param>
/// <param name="FileResult">文件层结果（含错误分类与面向用户的消息）。</param>
public sealed record DocumentLoadOutcome(LoadedDocument? Document, FileLoadResult FileResult)
{
    /// <summary>是否成功得到可用的文档。</summary>
    public bool Succeeded => Document is not null;
}

/// <summary>
/// 文档管线：文件读取 → 编码判定 → 归一化 → 单元构建 → 统计。
/// 对应《软件开发设计文档》§5.1 + §5.2 的完整链路。
/// </summary>
/// <remarks>
/// 这是界面与将来的 <c>InputSessionController</c> 共用的入口：
/// 界面负责把用户选项翻译成 <see cref="EncodingPreference"/> 与 <see cref="NormalizeOptions"/>，
/// 管线负责产出 <see cref="LoadedDocument"/>。所有依赖都可注入，便于单测。
/// </remarks>
public sealed class DocumentPipeline
{
    private readonly IFileLoader _fileLoader;
    private readonly ITextNormalizer _normalizer;
    private readonly IInputUnitBuilder _unitBuilder;

    /// <summary>构造文档管线。</summary>
    /// <param name="fileLoader">文件加载器；省略时使用默认实现。</param>
    /// <param name="normalizer">归一化器；省略时使用默认实现。</param>
    /// <param name="unitBuilder">单元构建器；省略时使用默认实现。</param>
    public DocumentPipeline(
        IFileLoader? fileLoader = null,
        ITextNormalizer? normalizer = null,
        IInputUnitBuilder? unitBuilder = null)
    {
        _fileLoader = fileLoader ?? new FileLoader();
        _normalizer = normalizer ?? new TextNormalizer();
        _unitBuilder = unitBuilder ?? new InputUnitBuilder();
    }

    /// <summary>从磁盘读取并完整解析一个文件。</summary>
    public async Task<DocumentLoadOutcome> LoadAsync(
        string path,
        EncodingPreference preference,
        NormalizeOptions options,
        CancellationToken cancellationToken = default)
    {
        var fileResult = await _fileLoader
            .LoadAsync(path, preference, cancellationToken)
            .ConfigureAwait(false);

        return Build(path, fileResult, options);
    }

    /// <summary>
    /// 仅重新解码 + 解析（用户切换编码时使用）：
    /// 命中文件层的字节缓存则不再读盘（FR-003.6）。
    /// </summary>
    public async Task<DocumentLoadOutcome> ReloadAsync(
        string path,
        EncodingPreference preference,
        NormalizeOptions options,
        CancellationToken cancellationToken = default)
    {
        var fileResult = await _fileLoader
            .DecodeOnlyAsync(path, preference, cancellationToken)
            .ConfigureAwait(false);

        return Build(path, fileResult, options);
    }

    private DocumentLoadOutcome Build(string path, FileLoadResult fileResult, NormalizeOptions options)
    {
        // 空文件：成功读取但不可用 → 提示"文件内容为空"（E-02）
        if (!fileResult.Success || fileResult.Error == FileLoadError.Empty)
        {
            return new DocumentLoadOutcome(null, fileResult);
        }

        var normalized = _normalizer.Normalize(fileResult.RawText, options);
        var built = _unitBuilder.Build(normalized, options);

        var document = new LoadedDocument(
            Path: path,
            RawText: fileResult.RawText,
            EncodingName: fileResult.EncodingName,
            EncodingReason: fileResult.EncodingReason,
            Units: built.Units,
            Statistics: built.Statistics,
            Options: options,
            SuspectedBinary: fileResult.SuspectedBinary,
            Warnings: built.Warnings);

        return new DocumentLoadOutcome(document, fileResult);
    }
}
