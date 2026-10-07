namespace InputTool.Core.Application;

/// <summary>
/// 倒计时浮层的驱动契约（FR-012）。
/// </summary>
/// <remarks>
/// 抽成接口的原因：Core 不能引用 WPF，而"显示一个置顶浮层并等它走完"必然是表现层的事。
/// 表现层实现它（用 <c>OverlayWindow</c>），Core 只负责"等倒计时结束，或被告知取消"。
/// </remarks>
public interface ICountdownPresenter
{
    /// <summary>
    /// 显示倒计时浮层并等待结束。
    /// </summary>
    /// <param name="seconds">倒计时秒数；&lt;= 0 时立即返回 true。</param>
    /// <param name="cancellationToken">取消令牌（用户按 Esc / 点取消时触发）。</param>
    /// <returns>倒计时正常走完返回 true；被取消返回 false。</returns>
    Task<bool> RunAsync(int seconds, CancellationToken cancellationToken);
}

/// <summary>
/// 不显示任何界面、立即完成的倒计时实现（无头场景与单测用）。
/// </summary>
public sealed class ImmediateCountdownPresenter : ICountdownPresenter
{
    /// <inheritdoc />
    public Task<bool> RunAsync(int seconds, CancellationToken cancellationToken)
        => Task.FromResult(!cancellationToken.IsCancellationRequested);
}
