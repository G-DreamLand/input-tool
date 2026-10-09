namespace InputTool.Tests.App;

/// <summary>
/// 界面上的热键文案必须**取自实际生效的绑定**，不许写死。
/// </summary>
/// <remarks>
/// 真机报障（F10 不起作用）的根因是配置里还留着 v1 的旧默认值 Ctrl+Alt+F11，
/// 而**按钮却写死成「■ 停止 (F10)」**——界面和实际注册各说各话，用户照着按钮按自然是空的。
/// 配置迁移已在 Core 侧修掉；这里再钉一层：界面文案只要写死热键，测试就红。
/// </remarks>
public sealed class HotkeyLabelContractTests
{
    [Fact]
    public void 停止按钮文案_必须绑定而不是写死()
    {
        var xaml = AppSourceFiles.View("MainWindow.xaml");

        xaml.Should().Contain("{Binding StopButtonText}", "停止按钮要显示实际生效的急停热键");
        xaml.Should().NotContain("■ 停止 (F10)", "写死热键正是本次报障的祸首");
    }

    [Fact]
    public void 开始按钮文案_必须绑定而不是写死()
    {
        AppSourceFiles.ViewModel("MainViewModel.cs")
            .Should().NotContain("▶ 开始 (F9)", "开始按钮文案不能写死热键");
    }

    [Fact]
    public void 步进复选框文案与提示_必须绑定而不是写死F9()
    {
        var xaml = AppSourceFiles.View("MainWindow.xaml");

        // 步进继续复用的就是「开始/暂停」键，所以文案也要跟着它走
        xaml.Should().Contain("{Binding StepModeLabel}");
        xaml.Should().Contain("{Binding StepModeTooltip}");
        xaml.Should().NotContain("逐行步进（按 F9");
    }

    [Fact]
    public void 浮层里的热键提示_必须由代码注入而不是写死()
    {
        var xaml = AppSourceFiles.View("OverlayWindow.xaml");

        xaml.Should().Contain("x:Name=\"CountdownHint\"");
        xaml.Should().Contain("x:Name=\"StepLineHint\"");
        xaml.Should().NotContain("（或 F10 紧急停止）", "急停提示写死会在唯一需要急停的时刻骗人");
        xaml.Should().NotContain("（F10 急停）");

        var code = AppSourceFiles.View("OverlayWindow.xaml.cs");

        code.Should().Contain("EmergencyStopHotkeyText");
        code.Should().Contain("StartPauseHotkeyText");
    }

    [Fact]
    public void 主界面按钮文案_必须带上实际生效的热键()
    {
        var code = AppSourceFiles.ViewModel("MainViewModel.cs");

        code.Should().Contain("StartPauseHotkey}", "开始/继续文案要带实际生效的开始/暂停键");
        code.Should().Contain("EmergencyStopHotkey}", "停止文案要带实际生效的急停键");
    }
}