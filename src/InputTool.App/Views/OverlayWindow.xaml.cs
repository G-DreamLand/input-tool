using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using InputTool.Core.Configuration;
using InputTool.Core.Interop;

namespace InputTool.App.Views;

/// <summary>
/// 倒计时 / 输入进度浮层。见《软件开发设计文档》§6.3、FR-012。
/// </summary>
/// <remarks>
/// 计时用 <see cref="Stopwatch"/> 计算真实剩余时间，<see cref="DispatcherTimer"/> 只负责刷新，
/// 因此不会像"每秒减一"那样累积误差。
/// Esc 取消按 Q-02 决议应由热键层在倒计时期间临时注册；热键服务落地前，
/// 界面提供「取消」按钮，代码里保留 <see cref="Cancelled"/> 事件供其接入。
/// </remarks>
public partial class OverlayWindow : Window
{
    private const double EdgeMargin = 24;

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch = new();

    private int _totalSeconds;
    private bool _isCountingDown;

    /// <summary>
    /// 浮层当前显示的是哪种内容。浮层是倒计时 / 进度 / 「下一行」三种模式**共用**的一个窗口，
    /// 因此各自"收起"时必须只关自己那一种，不能顺手把别人的面板关掉。
    /// </summary>
    private OverlayMode _mode = OverlayMode.None;

    /// <summary>浮层显示模式。</summary>
    private enum OverlayMode
    {
        None,
        Countdown,
        Progress,
        StepLine,
    }

    /// <summary>构造浮层。</summary>
    public OverlayWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _timer.Tick += OnTimerTick;
    }

    /// <summary>倒计时结束（应开始输入）。</summary>
    public event EventHandler? CountdownFinished;

    /// <summary>用户取消（点取消按钮；后续 Esc 也走这里）。</summary>
    public event EventHandler? Cancelled;

    /// <summary>是否正在倒计时。</summary>
    public bool IsCountingDown => _isCountingDown;

    /// <summary>剩余整秒数（向上取整，用于显示）。</summary>
    public int RemainingSeconds { get; private set; }

    /// <summary>开始倒计时并显示浮层。</summary>
    /// <param name="seconds">倒计时秒数；0 表示不显示浮层。</param>
    /// <param name="position">浮层位置。</param>
    public void StartCountdown(int seconds, OverlayPosition position)
    {
        _mode = OverlayMode.Countdown;
        CountdownPanel.Visibility = Visibility.Visible;
        ProgressPanel.Visibility = Visibility.Collapsed;
        StepLinePanel.Visibility = Visibility.Collapsed;

        _totalSeconds = Math.Max(0, seconds);
        RemainingSeconds = _totalSeconds;
        CountdownNumber.Text = _totalSeconds.ToString();
        CountdownCaption.Text = "秒后开始输入…";

        _isCountingDown = true;
        _stopwatch.Restart();
        _timer.Start();

        ShowAt(position);
    }

    /// <summary>进入输入进度模式并显示浮层。</summary>
    public void ShowProgress(string caption, double percent, string detail, OverlayPosition position)
    {
        _isCountingDown = false;
        _timer.Stop();
        _stopwatch.Reset();

        _mode = OverlayMode.Progress;
        CountdownPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        StepLinePanel.Visibility = Visibility.Collapsed;

        UpdateProgress(caption, percent, detail);
        ShowAt(position);
    }

    /// <summary>
    /// 显示「下一行」预览并浮出（FR 新增：步进时显示下一行）。
    /// </summary>
    /// <param name="lineNumber">1 基行号（仅用于标题）。</param>
    /// <param name="displayText">已格式化好的行文本（空行是「（空行）」，见 <c>SourceLineText</c>）。</param>
    /// <param name="position">浮层位置。</param>
    public void ShowStepLine(int lineNumber, string displayText, OverlayPosition position)
    {
        _mode = OverlayMode.StepLine;
        _isCountingDown = false;
        _timer.Stop();
        _stopwatch.Reset();

        CountdownPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;
        StepLinePanel.Visibility = Visibility.Visible;

        StepLineCaption.Text = $"下一行（第 {lineNumber} 行）";
        StepLineText.Text = displayText;

        ShowAt(position);
    }

    /// <summary>
    /// 收起「下一行」预览。
    /// </summary>
    /// <remarks>
    /// 只在自己的模式下动手：浮层是共用的，无条件 <see cref="HideOverlay"/> 会把正在显示的
    /// 倒计时 / 进度一起关掉（步进等待与倒计时虽不会同时发生，但这个不变量不该靠"不会同时发生"来维持）。
    /// </remarks>
    public void HideStepLine()
    {
        if (_mode != OverlayMode.StepLine)
        {
            return;
        }

        HideOverlay();
    }

    /// <summary>刷新进度显示。</summary>
    public void UpdateProgress(string caption, double percent, string detail)
    {
        ProgressCaption.Text = caption;
        ProgressElement.Value = Math.Clamp(percent, 0, 100);
        ProgressDetail.Text = detail;
    }

    /// <summary>隐藏浮层并复位计时状态。</summary>
    public void HideOverlay()
    {
        _mode = OverlayMode.None;
        _isCountingDown = false;
        _timer.Stop();
        _stopwatch.Reset();
        Hide();
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // 关键：不抢焦点（否则目标窗口会失去焦点，按键注入到错误位置）
        var handle = new WindowInteropHelper(this).Handle;
        WindowInterop.ApplyNoActivateToolWindow(handle);
    }

    private void ShowAt(OverlayPosition position)
    {
        // 先让 WPF 完成一次布局，才能拿到 ActualWidth/ActualHeight
        UpdateLayout();

        var (left, top) = position == OverlayPosition.NearCursor
            ? GetNearCursorPosition()
            : GetBottomRightPosition();

        Left = left;
        Top = top;

        if (!IsVisible)
        {
            Show();
        }
    }

    private (double Left, double Top) GetBottomRightPosition()
    {
        var workArea = SystemParameters.WorkArea;
        return (workArea.Right - ActualWidth - EdgeMargin, workArea.Bottom - ActualHeight - EdgeMargin);
    }

    private (double Left, double Top) GetNearCursorPosition()
    {
        if (!ScreenMetrics.TryGetCursorPosition(out var physicalX, out var physicalY))
        {
            return GetBottomRightPosition();
        }

        // 物理像素 → DIP（高 DPI 下必须换算，否则浮层会偏移到屏幕外）
        var dpi = VisualTreeHelper.GetDpi(this);
        var x = physicalX / dpi.DpiScaleX;
        var y = physicalY / dpi.DpiScaleY;

        var workArea = SystemParameters.WorkArea;
        var left = Math.Clamp(x + 16, workArea.Left, Math.Max(workArea.Left, workArea.Right - ActualWidth));
        var top = Math.Clamp(y + 16, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - ActualHeight));
        return (left, top);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var elapsed = _stopwatch.Elapsed.TotalSeconds;
        var remaining = _totalSeconds - elapsed;

        if (remaining <= 0)
        {
            _timer.Stop();
            _isCountingDown = false;
            RemainingSeconds = 0;

            CountdownFinished?.Invoke(this, EventArgs.Empty);
            return;
        }

        var secondsLeft = (int)Math.Ceiling(remaining);
        if (secondsLeft != RemainingSeconds)
        {
            RemainingSeconds = secondsLeft;
            CountdownNumber.Text = secondsLeft.ToString();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _isCountingDown = false;
        HideOverlay();

        Cancelled?.Invoke(this, EventArgs.Empty);
    }
}
