namespace InputTool.App.ViewModels;

/// <summary>
/// 预览区的一行。对应《软件开发设计文档》§6.1：预览要直观显示换行与 Tab，
/// 便于用户确认预处理结果（FR-005.3）。
/// </summary>
/// <param name="LineNumber">1 基行号。</param>
/// <param name="Text">行文本，其中 Tab 已替换为 <c>→</c>，行尾空格保留。</param>
/// <param name="HasTrailingNewline">该行是否以换行结束（决定是否显示 <c>↵</c>）。</param>
/// <param name="IsTruncationMarker">
/// 是否是"内容被截断"的提示行（FR-005.1 要求超出部分以"…"提示）。
/// </param>
public sealed record PreviewLine(
    int LineNumber,
    string Text,
    bool HasTrailingNewline,
    bool IsTruncationMarker = false)
{
    /// <summary>行号显示文本（右对齐用）；截断提示行显示省略号。</summary>
    public string LineNumberText => IsTruncationMarker ? "…" : LineNumber.ToString();
}
