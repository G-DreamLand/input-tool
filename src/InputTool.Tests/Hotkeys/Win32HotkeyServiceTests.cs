using InputTool.Core.Hotkeys;
using InputTool.Core.Interop;
using InputTool.Tests.TestDoubles;

namespace InputTool.Tests.Hotkeys;

/// <summary>
/// 全局热键服务（FR-013 / FR-014 / FR-025，设计文档 §5.6）。
/// 通过假原生层断言注册、冲突与回退逻辑，不需要真实窗口与系统热键表。
/// </summary>
public sealed class Win32HotkeyServiceTests
{
    private const int WmHotkey = 0x0312;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    /// <summary>MOD_NOREPEAT，避免长按连发。</summary>
    private const uint ModNoRepeat = 0x4000;

    private static (Win32HotkeyService Service, FakeHotkeyNative Native) Build()
    {
        var native = new FakeHotkeyNative();
        var service = new Win32HotkeyService(IntPtr.Zero, native);
        return (service, native);
    }

    // ── 注册（FR-013.5 / FR-014）──────────────────────────────────────

    [Fact]
    public void 注册默认热键_应全部成功()
    {
        var (service, native) = Build();

        var results = service.RegisterDefaults();

        results.Should().OnlyContain(r => r.Status == HotkeyRegistrationStatus.Success);
        service.Bindings.Should().HaveCount(4);
        service.EmergencyStopAvailable.Should().BeTrue();
        native.Registrations.Should().HaveCount(4);
    }

    [Fact]
    public void 注册时应附加MOD_NOREPEAT()
    {
        var (service, native) = Build();

        service.Register(HotkeyRole.StartPause, HotkeyGesture.DefaultStartPause);

        (native.Registrations[0].Modifiers & ModNoRepeat).Should().NotBe(0u, "必须附加 MOD_NOREPEAT 以免长按连发");
        native.Registrations[0].VirtualKey.Should().Be((uint)VirtualKey.F9);
    }

    [Fact]
    public void 开始暂停热键被占用_应回退到备用组合()
    {
        var (service, native) = Build();
        native.ResultFactory = (_, virtualKey) => virtualKey == (uint)VirtualKey.F9 ? ErrorHotkeyAlreadyRegistered : 0;

        service.RegisterDefaults();

        // SRS 附录 B 的备选：Ctrl+Alt+P
        service.Bindings[HotkeyRole.StartPause].Gesture.Key.Should().Be(VirtualKey.P);
        service.Bindings[HotkeyRole.StartPause].Gesture.Should().NotBe(HotkeyGesture.DefaultStartPause);
    }

    [Fact]
    public void 紧急停止热键无法注册时_应标记为不可用()
    {
        var (service, native) = Build();

        // 默认急停键 F10 与备用组合 Ctrl+Alt+Q 都被占用
        native.ResultFactory = (_, virtualKey) => virtualKey switch
        {
            (uint)VirtualKey.F10 => ErrorHotkeyAlreadyRegistered,
            (uint)VirtualKey.Q => ErrorHotkeyAlreadyRegistered,
            _ => 0,
        };

        service.RegisterDefaults();

        service.EmergencyStopAvailable.Should().BeFalse("急停热键失败时必须阻断开始输入（FR-014.5）");
    }

    [Fact]
    public void 热键被占用_应返回AlreadyInUse并触发失败事件()
    {
        var (service, native) = Build();
        native.ResultFactory = (_, _) => ErrorHotkeyAlreadyRegistered;

        HotkeyRegistrationFailedEventArgs? failure = null;
        service.RegistrationFailed += (_, e) => failure = e;

        var result = service.Register(HotkeyRole.StartPause, HotkeyGesture.DefaultStartPause);

        result.Status.Should().Be(HotkeyRegistrationStatus.AlreadyInUse);
        result.Message.Should().Contain("占用");
        failure.Should().NotBeNull();
        failure!.Role.Should().Be(HotkeyRole.StartPause);
    }

    [Fact]
    public void 非法组合_应被拒绝且不触碰底层()
    {
        var (service, native) = Build();
        HotkeyGesture.TryParse("Win+L", out var reserved).Should().BeTrue();

        var result = service.Register(HotkeyRole.StartPause, reserved);

        result.Status.Should().Be(HotkeyRegistrationStatus.Invalid);
        native.Registrations.Should().BeEmpty();
    }

    [Fact]
    public void 与其它角色重复_应被拒绝()
    {
        var (service, _) = Build();
        service.Register(HotkeyRole.EmergencyStop, HotkeyGesture.DefaultEmergencyStop);

        var result = service.Register(HotkeyRole.StartPause, HotkeyGesture.DefaultEmergencyStop);

        result.Status.Should().Be(HotkeyRegistrationStatus.Invalid);
        result.Message.Should().Contain("紧急停止");
    }

    [Fact]
    public void 重新注册同一角色_应先注销旧热键()
    {
        var (service, native) = Build();
        service.Register(HotkeyRole.StartPause, HotkeyGesture.DefaultStartPause);
        var firstId = native.Registrations[0].Id;

        HotkeyGesture.TryParse("Ctrl+Alt+P", out var replacement).Should().BeTrue();
        service.Register(HotkeyRole.StartPause, replacement);

        native.Unregistrations.Should().Contain(firstId);
        service.Bindings[HotkeyRole.StartPause].Gesture.Key.Should().Be(VirtualKey.P);
    }

    // ── 消息分发（§5.6.2）─────────────────────────────────────────────

    [Fact]
    public void 收到已注册的热键消息_应触发按下事件()
    {
        var (service, native) = Build();
        service.Register(HotkeyRole.StartPause, HotkeyGesture.DefaultStartPause);
        var id = native.Registrations[0].Id;

        HotkeyPressedEventArgs? pressed = null;
        service.Pressed += (_, e) => pressed = e;

        service.TryHandleWindowMessage(WmHotkey, new IntPtr(id)).Should().BeTrue();

        pressed.Should().NotBeNull();
        pressed!.Role.Should().Be(HotkeyRole.StartPause);
    }

    [Fact]
    public void 非热键消息_应返回未消费()
    {
        var (service, _) = Build();

        service.TryHandleWindowMessage(0x0001, IntPtr.Zero).Should().BeFalse();
    }

    [Fact]
    public void 未知热键ID_应返回未消费()
    {
        var (service, _) = Build();
        service.Register(HotkeyRole.StartPause, HotkeyGesture.DefaultStartPause);

        service.TryHandleWindowMessage(WmHotkey, new IntPtr(9999)).Should().BeFalse();
    }

    // ── 生命周期 ────────────────────────────────────────────────────────

    [Fact]
    public void 注销全部_应清空绑定并逐个注销()
    {
        var (service, native) = Build();
        service.RegisterDefaults();
        var registeredCount = native.Registrations.Count;

        service.UnregisterAll();

        native.Unregistrations.Should().HaveCount(registeredCount);
        service.Bindings.Should().BeEmpty();
        service.EmergencyStopAvailable.Should().BeFalse();
    }

    [Fact]
    public void Dispose_应注销全部热键()
    {
        var (service, native) = Build();
        service.RegisterDefaults();

        service.Dispose();

        native.Unregistrations.Should().NotBeEmpty();
    }

    [Fact]
    public void Dispose后注册_应抛出()
    {
        var (service, _) = Build();
        service.Dispose();

        var act = () => service.Register(HotkeyRole.StartPause, HotkeyGesture.DefaultStartPause);

        act.Should().Throw<ObjectDisposedException>();
    }
}
