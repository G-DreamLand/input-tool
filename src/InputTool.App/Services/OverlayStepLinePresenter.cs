using InputTool.App.Views;
using InputTool.Core.Application;
using InputTool.Core.Configuration;

namespace InputTool.App.Services;

/// <summary>
/// 用浮层的「下一行」面板实现 <see cref="IStepLinePresenter"/>（FR 新增：步进时显示下一行）。
/// </summary>
/// <remarks>
/// 「浮层不抢焦点」由 <see cref="OverlayWindow"/> 自己保证
/// （<c>ShowActivated=False</c> + <c>WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW</c>），
/// 这里只负责填内容、挑一个不遮住输入区的位置。
/// <para>
/// 必须在界面线程上调用：调用方是控制器的 <c>StepLineChanged</c>，
/// 而控制器保证所有对外事件都回到界面线程（见 <c>InputSessionController.RaiseOnUi</c>）。
/// </para>
/// </remarks>
public sealed class OverlayStepLinePresenter : IStepLinePresenter
{
    private readonly OverlayWindow _overlay;
    private readonly OverlayPosition _position;

    /// <summary>构造呈现器。</summary>
    /// <param name="overlay">浮层窗口（与倒计时共用同一个窗口，靠模式隔离互不干扰）。</param>
    /// <param name="position">
    /// 浮层位置。默认右下角：比"跟随光标"更不容易遮住正在输入的那一行。
    /// </param>
    public OverlayStepLinePresenter(
        OverlayWindow overlay,
        OverlayPosition position = OverlayPosition.BottomRight)
    {
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _position = position;
    }

    /// <inheritdoc />
    public void Show(int lineNumber, string displayText)
        => _overlay.ShowStepLine(lineNumber, displayText, _position);

    /// <inheritdoc />
    public void Hide()
        => _overlay.HideStepLine();
}