using System.Text.RegularExpressions;
using InputTool.Core.Interop;

namespace InputTool.Tests.App;

/// <summary>
/// 浮层硬约束的守卫（FR 新增：步进时显示下一行）。
/// </summary>
/// <remarks>
/// 四条硬约束里有两条 —— <b>绝不抢焦点</b> 与 <b>限宽限高</b> —— 只能在 WPF 里真正验证，
/// 而按用户约定测试不允许自动启动程序。
/// 于是这里退一步：直接读 XAML 与代码源码，把这几条钉成回归用例。
/// 谁把它们改掉了，测试立刻变红，而不是等到真机上"字打到别的窗口"才发现。
/// </remarks>
public sealed class OverlayXamlContractTests
{
    // ── 硬约束 1：浮层绝不能抢焦点 ──────────────────────────────────────

    [Fact]
    public void 浮层_必须声明显示时不激活且不进任务栏()
    {
        var xaml = OverlayXaml();

        xaml.Should().Contain("ShowActivated=\"False\"",
            "浮层一旦被激活就会抢走目标窗口的焦点，按键会打到别的地方");
        xaml.Should().Contain("ShowInTaskbar=\"False\"");
        xaml.Should().Contain("WindowStyle=\"None\"");
        xaml.Should().Contain("Topmost=\"True\"");
    }

    [Fact]
    public void 浮层_必须在句柄创建时挂上_无激活_与_工具窗口_扩展样式()
    {
        var code = OverlayCode();

        code.Should().Contain("OnSourceInitialized",
            "扩展样式必须在窗口句柄创建时挂上，这样才能在 Show 之前生效");
        code.Should().Contain("ApplyNoActivateToolWindow");
    }

    [Fact]
    public void 无激活扩展样式的常量_取值必须正确()
    {
        // 这两个位写错不会有任何编译错误，只会让浮层悄悄开始抢焦点 —— 正是最该守的地方
        Win32Constants.WsExNoActivate.Should().Be(0x08000000);
        Win32Constants.WsExToolWindow.Should().Be(0x00000080);
    }

    [Fact]
    public void 下一行面板_不得包含可获得焦点的控件()
    {
        // 面板里出现 Button / TextBox 之类可获焦控件时，鼠标点上去仍可能把焦点从目标窗口夺走
        var panel = StepLinePanelXaml();

        panel.Should().NotContain("<Button");
        panel.Should().NotContain("TextBox");
    }

    // ── 硬约束 4：限宽限高 ──────────────────────────────────────────────

    [Fact]
    public void 下一行面板_必须限宽限高()
    {
        var panel = StepLinePanelXaml();

        panel.Should().Contain("MaxWidth=",
            "限宽：长行要折行，不能把浮层撑成一条横向长带");
        panel.Should().Contain("MaxHeight=",
            "限高：超长行要在内部滚动，不能占满屏幕");
    }

    [Fact]
    public void 下一行文本_必须折行且放在有高度上限的滚动区里()
    {
        var panel = StepLinePanelXaml();

        panel.Should().Contain("TextWrapping=\"Wrap\"", "折行是限宽真正生效的前提");
        panel.Should().Contain("ScrollViewer", "超长行要能滚到，而不是被静默截断");
        panel.Should().Contain("VerticalScrollBarVisibility=\"Auto\"");
    }

    // ── 源码定位（公共读取见 AppSourceFiles）────────────────────────────

    private static string OverlayXaml() => AppSourceFiles.View("OverlayWindow.xaml");

    private static string OverlayCode() => AppSourceFiles.View("OverlayWindow.xaml.cs");

    /// <summary>取出「下一行」面板那一段 XAML（面板内没有嵌套 StackPanel，取第一个闭合标签即可）。</summary>
    private static string StepLinePanelXaml()
    {
        var match = Regex.Match(
            OverlayXaml(),
            "<StackPanel x:Name=\"StepLinePanel\".*?</StackPanel>",
            RegexOptions.Singleline);

        match.Success.Should().BeTrue("OverlayWindow.xaml 里应当有 x:Name=\"StepLinePanel\" 的面板");

        return match.Value;
    }
}