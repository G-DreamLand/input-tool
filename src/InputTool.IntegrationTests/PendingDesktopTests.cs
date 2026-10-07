namespace InputTool.IntegrationTests;

/// <summary>
/// 尚未自动化的桌面用例（M5 阶段补齐）。
/// · 逐字键入的真实验证见 <see cref="RealTypingTests"/>；
/// · 终端的真实验证见 <see cref="TerminalTypingTests"/>。
/// </summary>
// 桌面用例共享同一集合 → 串行执行；并行时它们会互相抢走前台焦点
[Trait("Category", "Desktop")]
[Collection("Desktop")]
public sealed class PendingDesktopTests
{
    [Fact(Skip = "需要以管理员身份运行的目标程序：M5 阶段验证 UIPI 提示与提权后可用（§12.2 / AC-18）")]
    public void 目标窗口为管理员权限时应给出提权提示()
    {
    }

    [Fact(Skip = "Windows Terminal 的窗口属于独立进程，自动化定位与置前台不可靠：M5 阶段人工验证（NFR-010）")]
    public void WindowsTerminal_回车应真的执行命令()
    {
    }
}
