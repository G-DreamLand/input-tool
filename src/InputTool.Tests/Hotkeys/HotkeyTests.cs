using InputTool.Core.Hotkeys;
using InputTool.Core.Interop;

namespace InputTool.Tests.Hotkeys;

/// <summary>
/// 热键解析与显示（FR-013 / FR-025、设计文档 §5.6.1 / §5.6.4）。
/// </summary>
public sealed class HotkeyGestureTests
{
    [Theory]
    [InlineData("F9", VirtualKey.F9)]
    [InlineData("f9", VirtualKey.F9)]
    [InlineData("Up", VirtualKey.Up)]
    [InlineData("A", VirtualKey.A)]
    [InlineData("z", VirtualKey.Z)]
    [InlineData("5", VirtualKey.D5)]
    [InlineData("Return", VirtualKey.Return)]
    public void TryParse_无修饰键_应解析出主键(string text, VirtualKey expectedKey)
    {
        HotkeyGesture.TryParse(text, out var gesture).Should().BeTrue();

        gesture.Key.Should().Be(expectedKey);
        (gesture.Modifiers & ~HotkeyModifiers.NoRepeat).Should().Be(HotkeyModifiers.None);
        gesture.Modifiers.Should().HaveFlag(HotkeyModifiers.NoRepeat, "统一附加 MOD_NOREPEAT 以避免长按连发");
    }

    [Theory]
    [InlineData("Ctrl+Alt+Up", HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey.Up)]
    [InlineData("ctrl + shift + a", HotkeyModifiers.Control | HotkeyModifiers.Shift, VirtualKey.A)]
    [InlineData("Ctrl+Alt+Down", HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey.Down)]
    [InlineData("Win+D", HotkeyModifiers.Win, VirtualKey.D)]
    public void TryParse_带修饰键_应解析出完整组合(string text, HotkeyModifiers expectedMods, VirtualKey expectedKey)
    {
        HotkeyGesture.TryParse(text, out var gesture).Should().BeTrue();

        gesture.Key.Should().Be(expectedKey);
        (gesture.Modifiers & ~HotkeyModifiers.NoRepeat).Should().Be(expectedMods);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+NoSuchKey")]
    [InlineData("+++")]
    public void TryParse_非法输入_应返回false(string? text)
    {
        HotkeyGesture.TryParse(text, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("F9")]
    [InlineData("F10")]
    [InlineData("Ctrl+Alt+Up")]
    [InlineData("Ctrl+Shift+A")]
    [InlineData("3")]
    public void ToString与TryParse_应往返一致(string text)
    {
        HotkeyGesture.TryParse(text, out var first).Should().BeTrue();

        var rendered = first.ToString();
        HotkeyGesture.TryParse(rendered, out var second).Should().BeTrue();

        second.Key.Should().Be(first.Key);
        second.Modifiers.Should().Be(first.Modifiers);
    }

    [Fact]
    public void ToString_字母与数字主键_应显示为字符本身()
    {
        HotkeyGesture.TryParse("Ctrl+A", out var letter).Should().BeTrue();
        HotkeyGesture.TryParse("Ctrl+1", out var digit).Should().BeTrue();

        letter.ToString().Should().Be("Ctrl+A");
        digit.ToString().Should().Be("Ctrl+1");
    }

    [Fact]
    public void 默认热键_应与设计文档一致()
    {
        HotkeyGesture.DefaultStartPause.Key.Should().Be(VirtualKey.F9);
        HotkeyGesture.DefaultEmergencyStop.Key.Should().Be(VirtualKey.F10);
        HotkeyGesture.DefaultSpeedUp.ToString().Should().Be("Ctrl+Alt+Up");
        HotkeyGesture.DefaultSpeedDown.ToString().Should().Be("Ctrl+Alt+Down");
    }
}

/// <summary>
/// 热键合法性校验（FR-025.3、SRS 附录 B）。
/// </summary>
public sealed class HotkeyValidatorTests
{
    private static IReadOnlyDictionary<HotkeyRole, HotkeyBinding> Defaults()
        => new Dictionary<HotkeyRole, HotkeyBinding>
        {
            [HotkeyRole.StartPause] = new(HotkeyRole.StartPause, HotkeyGesture.DefaultStartPause, "开始/暂停"),
            [HotkeyRole.EmergencyStop] = new(HotkeyRole.EmergencyStop, HotkeyGesture.DefaultEmergencyStop, "紧急停止"),
        };

    [Theory]
    [InlineData("Ctrl+Alt+Delete")]   // 安全注意序列
    [InlineData("Win+L")]             // 锁屏
    [InlineData("Alt+Tab")]           // 切换窗口
    [InlineData("Alt+F4")]            // 关闭窗口
    [InlineData("Ctrl+Shift+Escape")] // 任务管理器
    public void IsReserved_系统保留组合_应识别(string text)
    {
        HotkeyGesture.TryParse(text, out var gesture).Should().BeTrue();

        HotkeyValidator.IsReserved(gesture).Should().BeTrue();
    }

    [Theory]
    [InlineData("F9")]
    [InlineData("Ctrl+Alt+P")]
    public void IsReserved_普通组合_不应误判(string text)
    {
        HotkeyGesture.TryParse(text, out var gesture).Should().BeTrue();

        HotkeyValidator.IsReserved(gesture).Should().BeFalse();
    }

    [Fact]
    public void Validate_纯修饰键_应拒绝()
    {
        HotkeyGesture.TryParse("Ctrl", out var onlyModifier).Should().BeFalse("纯修饰键无法解析出主键");

        // 直接构造以覆盖"主键是修饰键"的分支
        var gesture = new HotkeyGesture(HotkeyModifiers.Control, VirtualKey.Control);
        HotkeyValidator.Validate(gesture, HotkeyRole.StartPause, Defaults()).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_单个Esc_应拒绝()
    {
        HotkeyGesture.TryParse("Escape", out var gesture).Should().BeTrue();

        var result = HotkeyValidator.Validate(gesture, HotkeyRole.StartPause, Defaults());

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("Esc");
    }

    [Fact]
    public void Validate_无修饰键且非功能键_应拒绝()
    {
        HotkeyGesture.TryParse("A", out var gesture).Should().BeTrue();

        var result = HotkeyValidator.Validate(gesture, HotkeyRole.StartPause, Defaults());

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("F1–F12");
    }

    [Fact]
    public void Validate_与其它角色重复_应拒绝并指出占用者()
    {
        HotkeyGesture.TryParse("F10", out var gesture).Should().BeTrue();

        var result = HotkeyValidator.Validate(gesture, HotkeyRole.StartPause, Defaults());

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("紧急停止");
    }

    [Fact]
    public void Validate_与自己相同_应允许()
    {
        HotkeyGesture.TryParse("F9", out var gesture).Should().BeTrue();

        HotkeyValidator.Validate(gesture, HotkeyRole.StartPause, Defaults()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("F10")]
    [InlineData("Ctrl+Alt+Q")]
    [InlineData("Ctrl+Alt+Up")]
    public void Validate_合法组合_应通过(string text)
    {
        HotkeyGesture.TryParse(text, out var gesture).Should().BeTrue();

        HotkeyValidator.Validate(gesture, HotkeyRole.EmergencyStop, Defaults()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_系统保留组合_应拒绝()
    {
        HotkeyGesture.TryParse("Win+L", out var gesture).Should().BeTrue();

        HotkeyValidator.Validate(gesture, HotkeyRole.StartPause, Defaults()).IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsFunctionKey_应只认F1到F12()
    {
        HotkeyValidator.IsFunctionKey(VirtualKey.F1).Should().BeTrue();
        HotkeyValidator.IsFunctionKey(VirtualKey.F12).Should().BeTrue();
        HotkeyValidator.IsFunctionKey((VirtualKey)0x7C).Should().BeFalse("F13 超出 F1–F12 范围");
        HotkeyValidator.IsFunctionKey(VirtualKey.A).Should().BeFalse();
    }
}
