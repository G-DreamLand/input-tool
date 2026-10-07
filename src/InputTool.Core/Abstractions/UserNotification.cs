namespace InputTool.Core.Abstractions;

/// <summary>
/// 错误严重级别。
/// 依据《软件开发设计文档》§10.1 的统一呈现规则：
/// Info → 状态栏/气泡；Warning → 黄色提示条；Recoverable → 红色错误条 + 查看详情；
/// Fatal → 模态对话框 + 记录日志 + 释放按键 + 终止会话。
/// </summary>
public enum ErrorSeverity
{
    /// <summary>提示信息，不打断操作。</summary>
    Info,

    /// <summary>警告，不打断操作（例如极快模式提示）。</summary>
    Warning,

    /// <summary>可恢复错误：当前会话中止，但程序可继续使用。</summary>
    Recoverable,

    /// <summary>致命错误：必须立即终止会话并清理键盘状态。</summary>
    Fatal,
}

/// <summary>
/// 面向用户的统一通知对象。
/// 文案原则（NFR-008）：每条错误都必须给出"怎么办"，即 <see cref="Advice"/> 不得为空。
/// </summary>
/// <param name="Severity">严重级别。</param>
/// <param name="Title">标题（短语）。</param>
/// <param name="Message">正文说明。</param>
/// <param name="Advice">解决建议（必填，例如"请以管理员身份重启本工具"）。</param>
/// <param name="Details">技术细节（可选，展开"查看详情"时显示）。</param>
/// <param name="ActionText">可执行动作的按钮文案（可选，例如"以管理员身份重启"）。</param>
public sealed record UserNotification(
    ErrorSeverity Severity,
    string Title,
    string Message,
    string? Advice = null,
    string? Details = null,
    string? ActionText = null);
