using System.Text;
using InputTool.Core.Configuration;
using InputTool.Core.Injection;
using InputTool.Core.Interop;
using InputTool.Core.Parsing;
using InputTool.Core.Scheduling;
using InputTool.Tests.TestDoubles;

namespace InputTool.Tests.Injection;

/// <summary>
/// 键盘注入（FR-010 / FR-011，设计文档 §5.5）。
/// 通过假原生层断言"到底发了哪些按键事件"，因此不需要真实桌面会话。
/// </summary>
public sealed class Win32KeyboardEmitterTests
{
    // Win32 常量（Core 内部定义，这里按官方值复述以便独立断言）
    private const uint KeyUp = 0x0002;
    private const uint Unicode = 0x0004;
    private const uint Extended = 0x0001;

    private const ushort VkBack = 0x08;
    private const ushort VkTab = 0x09;
    private const ushort VkReturn = 0x0D;
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkSpace = 0x20;

    private static (Win32KeyboardEmitter Emitter, RecordingKeyboardNative Native) Build(
        KeyboardMode mode = KeyboardMode.Unicode,
        Func<char, short>? scan = null)
    {
        var native = new RecordingKeyboardNative();
        var emitter = new Win32KeyboardEmitter(native, new KeyMapper(scan));
        emitter.Mode = mode;
        return (emitter, native);
    }

    private static InputUnit Char(char c) => InputUnit.Character(new Rune(c), 0, 0);

    private static InputUnit Char(Rune rune) => InputUnit.Character(rune, 0, 0);

    private static InputUnit Function(VirtualKey key) => InputUnit.Function(key, 0, 0);

    private static InputUnit Newline() => InputUnit.NewLine(0, 0);

    // ── Unicode 注入（FR-011）─────────────────────────────────────────

    [Fact]
    public void ASCII字符_应以Unicode方式成对注入()
    {
        var (emitter, native) = Build();

        emitter.Emit(Char('a'), SpeedProfile.Default).Should().Be(EmitResult.Sent);

        native.Calls.Should().HaveCount(2);
        native.Calls[0].Should().Be((0, (ushort)'a', Unicode), "按下：wVk=0、wScan=码元、含 KEYEVENTF_UNICODE");
        native.Calls[1].Should().Be((0, (ushort)'a', Unicode | KeyUp), "抬起");
    }

    [Fact]
    public void 中文字符_应以Unicode方式注入()
    {
        var (emitter, native) = Build();

        emitter.Emit(Char('中'), SpeedProfile.Default).Should().Be(EmitResult.Sent);

        native.Calls.Should().HaveCount(2);
        native.Calls[0].Should().Be((0, (ushort)'中', Unicode));
    }

    [Fact]
    public void Emoji_应按代理对拆成两个码元各自成对发送()
    {
        var (emitter, native) = Build();
        var emoji = new Rune(0x1F600);
        Span<char> buffer = stackalloc char[2];
        var length = emoji.EncodeToUtf16(buffer);
        length.Should().Be(2, "U+1F600 位于 BMP 之外，必然是两个 UTF-16 码元");

        emitter.Emit(Char(emoji), SpeedProfile.Default).Should().Be(EmitResult.Sent);

        native.Calls.Should().HaveCount(4);
        native.Calls[0].ScanCode.Should().Be(buffer[0]);
        native.Calls[1].ScanCode.Should().Be(buffer[0]);
        native.Calls[2].ScanCode.Should().Be(buffer[1]);
        native.Calls[3].ScanCode.Should().Be(buffer[1]);
        native.Calls.Should().OnlyContain(c => c.VirtualKey == 0);
    }

    [Fact]
    public void NUL字符_应跳过且不触碰底层()
    {
        var (emitter, native) = Build();

        emitter.Emit(Char(new Rune(0)), SpeedProfile.Default).Should().Be(EmitResult.Skipped);

        native.Calls.Should().BeEmpty();
    }

    // ── 功能键（FR-008）────────────────────────────────────────────────

    [Fact]
    public void 空格功能键_应注入VK_SPACE()
    {
        var (emitter, native) = Build();

        emitter.Emit(Function(VirtualKey.Space), SpeedProfile.Default).Should().Be(EmitResult.Sent);

        native.Calls.Should().Equal(
            (VkSpace, (ushort)0, 0u),
            (VkSpace, (ushort)0, KeyUp));
    }

    [Fact]
    public void 制表符_应注入VK_TAB()
    {
        var (emitter, native) = Build();

        emitter.Emit(Function(VirtualKey.Tab), SpeedProfile.Default).Should().Be(EmitResult.Sent);

        native.Calls[0].VirtualKey.Should().Be(VkTab);
    }

    [Fact]
    public void 退格_应注入VK_BACK()
    {
        var (emitter, native) = Build();

        emitter.Emit(Function(VirtualKey.Back), SpeedProfile.Default).Should().Be(EmitResult.Sent);

        native.Calls[0].VirtualKey.Should().Be(VkBack);
    }

    // ── 换行策略（FR-009）──────────────────────────────────────────────

    [Fact]
    public void 默认Enter策略_应注入真实的VK_RETURN()
    {
        // 必须是 VK_RETURN 的 down/up，而不是 Unicode 注入 \n —— 否则终端不会执行命令
        var (emitter, native) = Build();

        emitter.Emit(Newline(), new SpeedProfile(NewlineStrategy: NewlineStrategy.Enter)).Should().Be(EmitResult.Sent);

        native.Calls.Should().Equal(
            (VkReturn, (ushort)0, 0u),
            (VkReturn, (ushort)0, KeyUp));
    }

    [Fact]
    public void ShiftEnter策略_应按Shift按下_回车_Shift抬起的顺序()
    {
        var (emitter, native) = Build();

        emitter.Emit(Newline(), new SpeedProfile(NewlineStrategy: NewlineStrategy.ShiftEnter)).Should().Be(EmitResult.Sent);

        native.Calls.Select(c => (c.VirtualKey, c.Flags)).Should().Equal(
            (VkShift, 0u),
            (VkReturn, 0u),
            (VkReturn, KeyUp),
            (VkShift, KeyUp));
    }

    [Fact]
    public void CtrlEnter策略_应按Ctrl按下_回车_Ctrl抬起的顺序()
    {
        var (emitter, native) = Build();

        emitter.Emit(Newline(), new SpeedProfile(NewlineStrategy: NewlineStrategy.CtrlEnter)).Should().Be(EmitResult.Sent);

        native.Calls.Select(c => (c.VirtualKey, c.Flags)).Should().Equal(
            (VkControl, 0u),
            (VkReturn, 0u),
            (VkReturn, KeyUp),
            (VkControl, KeyUp));
    }

    [Fact]
    public void 不发送回车策略_应完全不触碰底层()
    {
        var (emitter, native) = Build();

        emitter.Emit(Newline(), new SpeedProfile(NewlineStrategy: NewlineStrategy.None)).Should().Be(EmitResult.Sent);

        native.Calls.Should().BeEmpty();
    }

    // ── 兼容模式（FR-011.4/.5）────────────────────────────────────────

    [Fact]
    public void 兼容模式_大写字母应走虚拟键加Shift()
    {
        // 扫描码 0x0141：VK_A + Shift
        var (emitter, native) = Build(KeyboardMode.Compatibility, _ => 0x0141);

        emitter.Emit(Char('A'), SpeedProfile.Default).Should().Be(EmitResult.Sent);

        native.Calls.Select(c => (c.VirtualKey, c.Flags)).Should().Equal(
            (VkShift, 0u),
            (0x41, 0u),
            (0x41, KeyUp),
            (VkShift, KeyUp));
    }

    [Fact]
    public void 兼容模式_非ASCII仍应走Unicode注入()
    {
        var (emitter, native) = Build(KeyboardMode.Compatibility, _ => -1);

        emitter.Emit(Char('中'), SpeedProfile.Default).Should().Be(EmitResult.Sent);

        native.Calls.Should().HaveCount(2);
        native.Calls.Should().OnlyContain(c => (c.Flags & Unicode) != 0);
    }

    [Fact]
    public void 兼容模式_映射失败应回退到Unicode()
    {
        var (emitter, native) = Build(KeyboardMode.Compatibility, _ => -1);

        emitter.Emit(Char('a'), SpeedProfile.Default).Should().Be(EmitResult.Sent);

        native.Calls.Should().OnlyContain(c => (c.Flags & Unicode) != 0);
    }

    [Fact]
    public void 兼容模式_扩展键应带上扩展标志()
    {
        var (emitter, native) = Build(KeyboardMode.Compatibility, _ => 0x0025); // VK_LEFT

        emitter.Emit(Char('a'), SpeedProfile.Default).Should().Be(EmitResult.Sent);

        (native.Calls[0].Flags & Extended).Should().NotBe(0u);
    }

    // ── 失败与重试（FR-007.5 / FR-010.6）──────────────────────────────

    [Fact]
    public void 单次失败_应重试一次后成功()
    {
        var (emitter, native) = Build();
        native.FailFirstNCalls = 1;

        emitter.Emit(Char('a'), SpeedProfile.Default).Should().Be(EmitResult.Sent);

        // 第一次按下失败 → 重试成功 → 抬起成功 = 3 次调用
        native.Calls.Should().HaveCount(3);
    }

    [Fact]
    public void 持续失败_应返回Failed并记录错误码()
    {
        var (emitter, native) = Build();
        native.AlwaysFail = true;
        native.ErrorCode = 5; // ERROR_ACCESS_DENIED

        emitter.Emit(Char('a'), SpeedProfile.Default).Should().Be(EmitResult.Failed);

        emitter.LastErrorCode.Should().Be(5);
        emitter.HasAccessDenied.Should().BeTrue("错误码 5 通常意味着 UIPI 限制");
        emitter.SendFailures.Should().BeGreaterThan(0);
    }

    // ── 急停清理（FR-014.4 / NFR-005）─────────────────────────────────

    [Fact]
    public void ReleaseAll_应逆序补发抬起事件()
    {
        var (emitter, native) = Build();

        emitter.KeyDown(VirtualKey.Shift);
        emitter.KeyDown(VirtualKey.Control);
        native.Calls.Clear();

        emitter.ReleaseAll();

        // 逆序：先按下的后释放，避免修饰键残留
        native.Calls.Select(c => c.VirtualKey).Should().Equal(VkControl, VkShift);
        native.Calls.Should().OnlyContain(c => c.Flags == KeyUp);
    }

    [Fact]
    public void ReleaseAll_在没有按下键时_不应产生任何事件()
    {
        var (emitter, native) = Build();

        emitter.ReleaseAll();

        native.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ReleaseAll_可从未绑定线程调用()
    {
        // 急停与崩溃清理可能来自其它线程，此方法必须永远可用
        var (emitter, native) = Build();
        emitter.KeyDown(VirtualKey.Shift);
        native.Calls.Clear();

        await Task.Run(() => emitter.ReleaseAll(), TestContext.Current.CancellationToken);

        native.Calls.Should().HaveCount(1);
    }

    // ── 线程亲和（§9.3 约定 1）────────────────────────────────────────

    [Fact]
    public void 绑定线程后跨线程注入_应抛出()
    {
        var (emitter, _) = Build();
        emitter.AttachThread();

        // 不能用 Task.Run 来制造"另一个线程"：线程池完全可能把排队中的委托交给
        // 刚刚被释放的当前线程去执行，那样 Emit 根本没有跨线程，用例就会偶发假失败
        // （加入并行度更高的测试后实测复现）。专用线程才能确定性地保证"不是绑定线程"。
        Exception? captured = null;

        var worker = new Thread(() =>
        {
            try
            {
                emitter.Emit(Char('a'), SpeedProfile.Default);
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        worker.Start();
        worker.Join(TimeSpan.FromSeconds(10)).Should().BeTrue("专用线程应当在超时前结束");

        captured.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public void 未绑定线程时_允许任意线程调用()
    {
        var (emitter, native) = Build();

        emitter.Emit(Char('a'), SpeedProfile.Default).Should().Be(EmitResult.Sent);

        native.Calls.Should().NotBeEmpty();
    }
}
