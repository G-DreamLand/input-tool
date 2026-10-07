namespace InputTool.Core.Application;

/// <summary>
/// 「下一行」预览的呈现契约（FR 新增：步进时显示下一行）。
/// </summary>
/// <remarks>
/// 与 <see cref="ICountdownPresenter"/> 同样的理由：Core 不能引用 WPF，
/// 而"把一行字显示成置顶浮层"必然是表现层的事。
/// Core 只描述**要给用户看什么**，怎么画（浮层 / 托盘气泡 / 日志）由实现决定。
/// <para>
/// 硬约束由实现负责：
/// · 浮层绝不抢焦点（否则目标窗口失焦，按键会打到别处）；
/// · 限宽限高（超宽折行、超高滚动，不许长满屏幕）。
/// </para>
/// <para>
/// 与 <see cref="ICountdownPresenter"/> 的差别：倒计时要"等它走完"，所以是 async；
/// 这里只是通知式的显示 / 收起，控制器不需要等，因此是同步 void。
/// </para>
/// </remarks>
public interface IStepLinePresenter
{
    /// <summary>显示「下一行」预览。</summary>
    /// <param name="lineNumber">1 基行号（仅用于标题）。</param>
    /// <param name="displayText">已格式化的行文本（空行是「（空行）」）。</param>
    void Show(int lineNumber, string displayText);

    /// <summary>收起「下一行」预览。未显示时必须安全（可重复调用）。</summary>
    void Hide();
}

/// <summary>
/// 不显示任何界面的呈现器（无头场景与单测用）。
/// </summary>
public sealed class NullStepLinePresenter : IStepLinePresenter
{
    /// <inheritdoc />
    public void Show(int lineNumber, string displayText)
    {
        // 无头场景：什么都不做
    }

    /// <inheritdoc />
    public void Hide()
    {
        // 无头场景：什么都不做
    }
}