namespace InputTool.Core.Application;

/// <summary>
/// 「下一行」预览的状态变化（FR 新增：步进时显示下一行）。
/// </summary>
/// <remarks>
/// 控制器只管「把行号翻译成源文本行」并广播出去；怎么画由呈现层决定
/// （见 <c>IStepLinePresenter</c> 与 <c>OverlayStepLinePresenter</c>），
/// 因此 Core 依然不引用 WPF。
/// </remarks>
/// <param name="IsWaiting">
/// true = 已停在行首、正在等步进热键（界面应显示浮层）；
/// false = 已离开等待状态（继续 / 停止 / 会话结束），界面应收起浮层。
/// </param>
/// <param name="LineIndex">正在等待的那一行的 0 基源文本行号；<paramref name="IsWaiting"/> 为 false 时无意义。</param>
/// <param name="LineNumber">界面显示的 1 基行号。</param>
/// <param name="DisplayText">该行的显示文本（Tab 已转为 →；空行显示「（空行）」）。</param>
public sealed record StepLineChangedEventArgs(
    bool IsWaiting,
    int LineIndex,
    int LineNumber,
    string DisplayText)
{
    /// <summary>离开等待状态：收起浮层。</summary>
    public static StepLineChangedEventArgs Hidden { get; } = new(false, -1, 0, string.Empty);
}