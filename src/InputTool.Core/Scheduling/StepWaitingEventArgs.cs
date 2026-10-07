namespace InputTool.Core.Scheduling;

/// <summary>
/// 步进等待事件参数：调度器已停在某一行开头，等用户按「开始/暂停」键（F9）继续。
/// </summary>
/// <remarks>
/// 「步进时显示下一行」的数据来源：调度器只知道行号；行文本由
/// <c>InputSessionController</c> 从 <c>LoadedDocument.RawText</c> 里取。
/// 这样调度器不必持有原文，界面显示的也是"源文本行"而不是归一化后的结果。
/// </remarks>
/// <param name="LineIndex">
/// 即将输入的那一行——也就是界面要提示的「下一行」——的 <b>0 基源文本行号</b>，
/// 取值来自 <see cref="Parsing.InputUnit.LineIndex"/>。
/// 调度器对第一行不停，因此该值总是 &gt;= 1（确实存在「上一行」）。
/// </param>
public sealed record StepWaitingEventArgs(int LineIndex);