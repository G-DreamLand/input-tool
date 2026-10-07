using System.Windows.Threading;
using InputTool.Core.Application;
using InputTool.Core.Configuration;
using InputTool.App.Views;

namespace InputTool.App.Services;

/// <summary>
/// 用倒计时浮层实现 <see cref="ICountdownPresenter"/>（FR-012）。
/// </summary>
/// <remarks>
/// 把"等浮层走完或被取消"翻译成一个 <see cref="Task{Boolean}"/>：
/// · 浮层倒计时结束 → true；
/// · 用户点取消 / 控制器取消令牌 → false。
/// Core 因此完全不需要知道 WPF 的存在。
/// </remarks>
public sealed class OverlayCountdownPresenter : ICountdownPresenter
{
    private readonly OverlayWindow _overlay;
    private readonly OverlayPosition _position;
    private readonly Dispatcher _dispatcher;

    /// <summary>构造倒计时呈现器。</summary>
    /// <param name="overlay">浮层窗口。</param>
    /// <param name="position">浮层位置。</param>
    public OverlayCountdownPresenter(OverlayWindow overlay, OverlayPosition position = OverlayPosition.BottomRight)
    {
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _position = position;
        _dispatcher = overlay.Dispatcher;
    }

    /// <inheritdoc />
    public async Task<bool> RunAsync(int seconds, CancellationToken cancellationToken)
    {
        if (seconds <= 0)
        {
            return !cancellationToken.IsCancellationRequested;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnFinished(object? sender, EventArgs e) => completion.TrySetResult(true);
        void OnCancelled(object? sender, EventArgs e) => completion.TrySetResult(false);

        _overlay.CountdownFinished += OnFinished;
        _overlay.Cancelled += OnCancelled;

        await using var registration = cancellationToken.Register(() =>
        {
            // 用 BeginInvoke 而不是 Invoke：取消可能就发生在界面线程上，同步等待会死锁
            try
            {
                _dispatcher.BeginInvoke(() =>
                {
                    _overlay.HideOverlay();
                    completion.TrySetResult(false);
                });
            }
            catch (TaskCanceledException)
            {
                completion.TrySetResult(false);
            }
        }).ConfigureAwait(true);

        try
        {
            _overlay.StartCountdown(seconds, _position);
            return await completion.Task.ConfigureAwait(true);
        }
        finally
        {
            _overlay.CountdownFinished -= OnFinished;
            _overlay.Cancelled -= OnCancelled;
            _overlay.HideOverlay();
        }
    }
}
