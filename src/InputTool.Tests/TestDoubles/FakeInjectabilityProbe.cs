using InputTool.Core.Application;
using InputTool.Core.Injection;

namespace InputTool.Tests.TestDoubles;

/// <summary>可控的可注入性探测：让会话控制器的前置校验分支可测。</summary>
public sealed class FakeInjectabilityProbe : IInjectabilityProbe
{
    /// <summary>Check 返回的阻塞原因。</summary>
    public InjectabilityBlocker Blocker { get; set; } = InjectabilityBlocker.None;

    /// <summary>原因说明。</summary>
    public string Reason { get; set; } = "测试：可以注入";

    /// <summary>Check 被调用的次数。</summary>
    public int CheckCount { get; private set; }

    /// <inheritdoc />
    public InjectabilityCheck Check()
    {
        CheckCount++;
        return new InjectabilityCheck(Blocker, Reason);
    }

    /// <inheritdoc />
    public bool IsTargetProcessHigherIntegrity(IntPtr windowHandle) => false;
}

/// <summary>
/// 可控的倒计时呈现器：默认立即完成，也可挂起等待测试显式完成 / 取消。
/// </summary>
public sealed class ControllableCountdownPresenter : ICountdownPresenter
{
    private TaskCompletionSource<bool>? _pending;

    /// <summary>为 true 时 RunAsync 立即返回 true（默认）。</summary>
    public bool AutoComplete { get; set; } = true;

    /// <summary>是否被调用过。</summary>
    public bool WasCalled { get; private set; }

    /// <summary>最后一次的秒数参数。</summary>
    public int LastSeconds { get; private set; } = -1;

    /// <inheritdoc />
    public Task<bool> RunAsync(int seconds, CancellationToken cancellationToken)
    {
        WasCalled = true;
        LastSeconds = seconds;

        if (AutoComplete)
        {
            return Task.FromResult(!cancellationToken.IsCancellationRequested);
        }

        _pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = cancellationToken.Register(() => _pending.TrySetResult(false));
        return _pending.Task;
    }

    /// <summary>让倒计时正常结束。</summary>
    public void Complete() => _pending?.TrySetResult(true);

    /// <summary>让倒计时被取消。</summary>
    public void Cancel() => _pending?.TrySetResult(false);
}
