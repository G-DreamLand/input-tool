using InputTool.Core.Application;

namespace InputTool.Tests.TestDoubles;

/// <summary>可控的目标窗口网关：让"选窗口 → 激活 → 校验"这条路径可测。</summary>
public sealed class FakeTargetWindowGateway : ITargetWindowGateway
{
    /// <summary>当前前台窗口。</summary>
    public IntPtr Foreground { get; set; } = new(0x1000);

    /// <summary>"自动选终端窗口"模式会返回的句柄。</summary>
    public IntPtr BestTerminal { get; set; } = new(0x2000);

    /// <summary>激活是否成功（模拟 Windows 前台切换被拒）。</summary>
    public bool ActivationSucceeds { get; set; } = true;

    /// <summary>每次尝试激活的句柄，用于断言"到底去激活了谁"。</summary>
    public List<IntPtr> ActivationAttempts { get; } = new();

    /// <summary>窗口标题（回显用）。</summary>
    public string Title { get; set; } = "测试目标窗口";

    /// <inheritdoc />
    public IntPtr GetForegroundWindow() => Foreground;

    /// <inheritdoc />
    public IntPtr FindBestTerminalWindow() => BestTerminal;

    /// <inheritdoc />
    public bool TryActivate(IntPtr handle)
    {
        ActivationAttempts.Add(handle);
        return ActivationSucceeds;
    }

    /// <inheritdoc />
    public string GetTitle(IntPtr handle) => Title;
}
