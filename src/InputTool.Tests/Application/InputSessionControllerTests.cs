using InputTool.Core.Abstractions;
using InputTool.Core.Application;
using InputTool.Core.Configuration;
using InputTool.Core.Injection;
using InputTool.Core.Parsing;
using InputTool.Core.Scheduling;
using InputTool.Tests.TestDoubles;

namespace InputTool.Tests.Application;

/// <summary>
/// 会话控制器状态机（设计文档 §4.2 / §5.9.1）。
/// 用真实调度器 + 假时钟/等待器/注入器，覆盖前置校验、倒计时、暂停、急停与统计。
/// </summary>
public sealed class InputSessionControllerTests
{
    private const int Interval = 50;

    private sealed record Harness(
        InputSessionController Controller,
        FakeClock Clock,
        FakeWaiter Waiter,
        RecordingKeyboardEmitter Emitter,
        FakeInjectabilityProbe Probe,
        ControllableCountdownPresenter Countdown,
        FakeTargetWindowGateway Targets,
        FakeFocusMonitor Focus,
        InputScheduler Scheduler);

    private static Harness Build()
    {
        var clock = new FakeClock();
        var waiter = new FakeWaiter(clock);
        var emitter = new RecordingKeyboardEmitter();
        var probe = new FakeInjectabilityProbe();
        var countdown = new ControllableCountdownPresenter();
        var targets = new FakeTargetWindowGateway();
        var focus = new FakeFocusMonitor();
        var scheduler = new InputScheduler(emitter, clock, waiter, new IntervalCalculator(new FixedRandomSource(0.5)));
        var controller = new InputSessionController(scheduler, emitter, probe, countdown, targets, focus);

        return new Harness(controller, clock, waiter, emitter, probe, countdown, targets, focus, scheduler);
    }

    private static LoadedDocument Document(string text)
    {
        var built = new InputUnitBuilder().Build(text, NormalizeOptions.Default);
        return new LoadedDocument(
            Path: "test.txt",
            RawText: text,
            EncodingName: "UTF-8",
            EncodingReason: "测试",
            Units: built.Units,
            Statistics: built.Statistics,
            Options: NormalizeOptions.Default,
            SuspectedBinary: false,
            Warnings: built.Warnings);
    }

    private static Task<SessionStatistics> ExpectFinished(InputSessionController controller)
    {
        var completion = new TaskCompletionSource<SessionStatistics>(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.Finished += (_, s) => completion.TrySetResult(s);
        return completion.Task;
    }

    private static Task<UserNotification> ExpectNotification(InputSessionController controller)
    {
        var completion = new TaskCompletionSource<UserNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.Notified += (_, n) => completion.TrySetResult(n);
        return completion.Task;
    }

    private static Func<SpeedProfile> Profile() => () => new SpeedProfile(Interval);

    // ── 前置校验 ────────────────────────────────────────────────────────

    [Fact]
    public async Task 文档为空_应提示且不进入倒计时()
    {
        var h = Build();
        var notification = ExpectNotification(h.Controller);

        h.Controller.Start(Document(string.Empty), 3, Profile());

        var result = await notification.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        result.Severity.Should().Be(ErrorSeverity.Warning);
        result.Title.Should().Contain("没有可输入的内容");
        h.Countdown.WasCalled.Should().BeFalse();
        h.Controller.State.Should().Be(InputSessionState.Idle);
    }

    [Fact]
    public async Task 不可注入_应提示并保持空闲()
    {
        var h = Build();
        h.Probe.Blocker = InjectabilityBlocker.SecureDesktop;
        h.Probe.Reason = "当前处于锁屏状态";
        var notification = ExpectNotification(h.Controller);

        h.Controller.Start(Document("abc"), 3, Profile());

        var result = await notification.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        result.Severity.Should().Be(ErrorSeverity.Recoverable);
        result.Message.Should().Contain("锁屏");
        h.Countdown.WasCalled.Should().BeFalse();
        h.Controller.State.Should().Be(InputSessionState.Idle);
    }

    // ── 正常流程 ────────────────────────────────────────────────────────

    [Fact]
    public async Task 正常流程_应经过倒计时与输入中并最终完成()
    {
        var h = Build();
        var states = new List<InputSessionState>();
        h.Controller.StateChanged += (_, e) => states.Add(e.Current);
        var finished = ExpectFinished(h.Controller);

        h.Controller.Start(Document("abcde"), 3, Profile());
        var statistics = await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        states.Should().ContainInOrder(
            InputSessionState.Countdown,
            InputSessionState.Typing,
            InputSessionState.Finished);

        statistics.Outcome.Should().Be(SessionOutcome.Completed);
        statistics.CompletedUnits.Should().Be(5);
        statistics.TotalUnits.Should().Be(5);
        statistics.AverageIntervalMs.Should().BeApproximately(Interval, 0.01);
        h.Emitter.Emitted.Should().HaveCount(5);
    }

    [Fact]
    public async Task 倒计时秒数_应透传给浮层()
    {
        var h = Build();
        var finished = ExpectFinished(h.Controller);

        h.Controller.Start(Document("ab"), 7, Profile());
        await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        h.Countdown.LastSeconds.Should().Be(7);
    }

    [Fact]
    public async Task 完成后_应可以重新开始()
    {
        var h = Build();

        var first = ExpectFinished(h.Controller);
        h.Controller.Start(Document("ab"), 0, Profile());
        var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var second = ExpectFinished(h.Controller);
        h.Controller.Start(Document("cd"), 0, Profile());
        var secondResult = await second.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        firstResult.Outcome.Should().Be(SessionOutcome.Completed);
        secondResult.Outcome.Should().Be(SessionOutcome.Completed);
        h.Emitter.Emitted.Should().HaveCount(4);
    }

    // ── 倒计时取消 ──────────────────────────────────────────────────────

    [Fact]
    public async Task 倒计时期间取消_应回到空闲且不注入()
    {
        var h = Build();
        h.Countdown.AutoComplete = false;
        var notification = ExpectNotification(h.Controller);

        h.Controller.Start(Document("abcdef"), 5, Profile());
        SpinWait.SpinUntil(() => h.Countdown.WasCalled, TimeSpan.FromSeconds(5)).Should().BeTrue();

        h.Controller.CancelCountdown();

        await Task.Delay(100, TestContext.Current.CancellationToken);
        h.Controller.State.Should().Be(InputSessionState.Idle);
        h.Emitter.Emitted.Should().BeEmpty();
        _ = notification;
    }

    [Fact]
    public async Task 倒计时期间急停_应等同于取消倒计时()
    {
        var h = Build();
        h.Countdown.AutoComplete = false;

        h.Controller.Start(Document("abcdef"), 5, Profile());
        SpinWait.SpinUntil(() => h.Countdown.WasCalled, TimeSpan.FromSeconds(5)).Should().BeTrue();

        h.Controller.Stop();

        await Task.Delay(100, TestContext.Current.CancellationToken);
        h.Controller.State.Should().Be(InputSessionState.Idle);
        h.Emitter.Emitted.Should().BeEmpty();
    }

    // ── 暂停 / 急停 ─────────────────────────────────────────────────────

    [Fact]
    public async Task 暂停与继续_状态应正确切换且不丢字()
    {
        var h = Build();
        var resumeScheduled = false;

        h.Waiter.BeforeWait = call =>
        {
            if (call != 3 || resumeScheduled)
            {
                return;
            }

            resumeScheduled = true;
            h.Controller.Pause();
            _ = Task.Run(async () =>
            {
                await Task.Delay(50);
                h.Controller.Resume();
            });
        };

        var finished = ExpectFinished(h.Controller);
        h.Controller.Start(Document("abcdefgh"), 0, Profile());
        var statistics = await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        statistics.Outcome.Should().Be(SessionOutcome.Completed);
        h.Emitter.Emitted.Select(u => u.SourceIndex).Should().Equal(0, 1, 2, 3, 4, 5, 6, 7);
    }

    [Fact]
    public async Task 急停_应停止注入并以停止态结束()
    {
        var h = Build();
        h.Waiter.BeforeWait = call =>
        {
            if (call == 3)
            {
                h.Controller.Stop();
            }
        };

        var finished = ExpectFinished(h.Controller);
        h.Controller.Start(Document("abcdefgh"), 0, Profile());
        var statistics = await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        statistics.Outcome.Should().Be(SessionOutcome.Stopped);
        statistics.CompletedUnits.Should().Be(2);
        h.Emitter.ReleaseAllCount.Should().Be(1);
        h.Controller.State.Should().Be(InputSessionState.Idle);
    }

    [Fact]
    public void 空闲时急停_应无副作用()
    {
        var h = Build();

        h.Controller.Stop();

        h.Controller.State.Should().Be(InputSessionState.Idle);
        h.Emitter.Emitted.Should().BeEmpty();
    }

    // ── 目标窗口选择（FR-012.4）────────────────────────────────────────

    [Fact]
    public async Task 跟随前台模式_不主动激活_直接用当前前台窗口()
    {
        var h = Build();
        h.Controller.TargetMode.Should().Be(TargetWindowMode.Foreground, "默认应保持原有行为");

        var finished = ExpectFinished(h.Controller);
        h.Controller.Start(Document("ab"), 0, Profile());
        await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        h.Targets.ActivationAttempts.Should().BeEmpty("跟随前台模式不该去抢焦点");
        h.Controller.ActiveTargetWindow.Should().Be(h.Targets.Foreground);
    }

    [Fact]
    public async Task 自动终端模式_开始前应激活终端窗口()
    {
        var h = Build();
        h.Controller.TargetMode = TargetWindowMode.AutoTerminal;

        var finished = ExpectFinished(h.Controller);
        h.Controller.Start(Document("ab"), 0, Profile());
        await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        h.Targets.ActivationAttempts.Should().ContainSingle()
            .Which.Should().Be(h.Targets.BestTerminal);
        h.Controller.ActiveTargetWindow.Should().Be(h.Targets.BestTerminal);
    }

    [Fact]
    public async Task 固定窗口模式_应激活用户选定的那个窗口()
    {
        var h = Build();
        var pinned = new IntPtr(0x3000);
        h.Controller.TargetMode = TargetWindowMode.Pinned;
        h.Controller.PinnedTargetWindow = pinned;

        var finished = ExpectFinished(h.Controller);
        h.Controller.Start(Document("ab"), 0, Profile());
        await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        h.Targets.ActivationAttempts.Should().ContainSingle().Which.Should().Be(pinned);
        h.Controller.ActiveTargetWindow.Should().Be(pinned);
    }

    [Fact]
    public async Task 目标窗口激活失败_应中止且一个字符都不注入()
    {
        var h = Build();
        h.Targets.ActivationSucceeds = false;
        h.Controller.TargetMode = TargetWindowMode.AutoTerminal;
        var notification = ExpectNotification(h.Controller);

        h.Controller.Start(Document("abcdef"), 0, Profile());

        var note = await notification.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        note.Severity.Should().Be(ErrorSeverity.Warning);
        note.Title.Should().Contain("无法切换");
        h.Controller.State.Should().Be(InputSessionState.Idle);
        h.Emitter.Emitted.Should().BeEmpty("宁可什么都不输入，也不能把字打到别的窗口上");
    }

    [Fact]
    public async Task 固定窗口已失效_应中止而不是退化成打前台窗口()
    {
        var h = Build();
        h.Controller.TargetMode = TargetWindowMode.Pinned;
        h.Controller.PinnedTargetWindow = new IntPtr(0x4000);
        h.Targets.ActivationSucceeds = false;
        var notification = ExpectNotification(h.Controller);

        h.Controller.Start(Document("abc"), 0, Profile());

        await notification.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        h.Emitter.Emitted.Should().BeEmpty();
    }

    [Fact]
    public async Task 固定窗口模式但未选窗口_应明确拒绝而不是静默打到前台窗口()
    {
        var h = Build();
        h.Controller.TargetMode = TargetWindowMode.Pinned;
        h.Controller.PinnedTargetWindow = IntPtr.Zero;
        var notification = ExpectNotification(h.Controller);

        h.Controller.Start(Document("abc"), 0, Profile());

        var note = await notification.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        note.Severity.Should().Be(ErrorSeverity.Warning);
        note.Title.Should().Contain("还没有确定目标窗口");
        h.Controller.State.Should().Be(InputSessionState.Idle);
        h.Emitter.Emitted.Should().BeEmpty("宁可什么都不输入，也不能把字打到用户没预期的窗口上");
        h.Targets.ActivationAttempts.Should().BeEmpty();
    }

    [Fact]
    public async Task 自动选终端模式但找不到终端_也应明确拒绝()
    {
        var h = Build();
        h.Targets.BestTerminal = IntPtr.Zero; // 模拟"一个终端窗口都没有"
        h.Controller.TargetMode = TargetWindowMode.AutoTerminal;
        var notification = ExpectNotification(h.Controller);

        h.Controller.Start(Document("abc"), 0, Profile());

        var note = await notification.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        note.Title.Should().Contain("还没有确定目标窗口");
        h.Emitter.Emitted.Should().BeEmpty();
    }

    // ── 焦点守护（FR-020）──────────────────────────────────────────────

    [Fact]
    public async Task 未启用焦点守护_不应启动监视()
    {
        var h = Build();
        h.Controller.FocusGuardEnabled.Should().BeFalse("默认关闭（SRS §8.5 默认值表）");

        var finished = ExpectFinished(h.Controller);
        h.Controller.Start(Document("ab"), 0, Profile());
        await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        h.Focus.StartCount.Should().Be(0);
        h.Focus.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task 启用焦点守护_应监视本次锁定的目标窗口()
    {
        var h = Build();
        h.Controller.FocusGuardEnabled = true;

        var finished = ExpectFinished(h.Controller);
        h.Controller.Start(Document("ab"), 0, Profile());
        await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        h.Focus.StartCount.Should().Be(1, "会话结束时监视器已被停止");
    }

    [Fact]
    public async Task 失去焦点_默认自动暂停_焦点回来后自动继续()
    {
        var h = Build();
        h.Controller.FocusGuardEnabled = true;

        // 在第 3 次等待之前制造"焦点跑掉"，此刻已注入 2 个字符
        h.Waiter.BeforeWait = call =>
        {
            if (call == 3)
            {
                h.Focus.RaiseFocusLost();
            }
        };

        var notifications = new List<UserNotification>();
        h.Controller.Notified += (_, n) => notifications.Add(n);

        var finished = ExpectFinished(h.Controller);
        h.Controller.Start(Document("abcdefgh"), 0, Profile());

        // 暂停后调度线程会阻塞在暂停闸门上，会话不会继续
        SpinWait.SpinUntil(() => h.Controller.State == InputSessionState.Paused, TimeSpan.FromSeconds(5))
            .Should().BeTrue("失去焦点后应自动暂停");

        notifications.Should().Contain(n => n.Title.Contains("已暂停"), "FR-020.3 要求暂停时给出提示");

        // 焦点回来 → 自动继续，且一个字都不丢
        h.Focus.RaiseFocusRegained();
        h.Controller.State.Should().Be(InputSessionState.Typing, "焦点回到目标窗口应自动继续");

        var result = await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(SessionOutcome.Completed);
        h.Emitter.Emitted.Select(u => u.SourceIndex).Should().Equal(0, 1, 2, 3, 4, 5, 6, 7);
    }

    [Fact]
    public async Task 继续输入策略_失去焦点也不暂停()
    {
        var h = Build();
        h.Controller.FocusGuardEnabled = true;
        h.Controller.FocusGuardAction = FocusGuardAction.Continue;

        h.Waiter.BeforeWait = call =>
        {
            if (call == 2)
            {
                h.Focus.RaiseFocusLost();
            }
        };

        var notifications = new List<UserNotification>();
        h.Controller.Notified += (_, n) => notifications.Add(n);

        var finished = ExpectFinished(h.Controller);
        h.Controller.Start(Document("abcd"), 0, Profile());
        var result = await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(SessionOutcome.Completed, "「继续输入」策略下不应中断");
        h.Emitter.Emitted.Should().HaveCount(4);
        notifications.Should().Contain(n => n.Message.Contains("继续输入"), "必须明确警告文本可能流入其它窗口");
    }

    [Fact]
    public async Task 自动停止策略_失去焦点立即停止()
    {
        var h = Build();
        h.Controller.FocusGuardEnabled = true;
        h.Controller.FocusGuardAction = FocusGuardAction.Stop;

        h.Waiter.BeforeWait = call =>
        {
            if (call == 2)
            {
                h.Focus.RaiseFocusLost();
            }
        };

        var finished = ExpectFinished(h.Controller);
        h.Controller.Start(Document("abcdefgh"), 0, Profile());
        var result = await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(SessionOutcome.Stopped);
        result.CompletedUnits.Should().BeLessThan(8);
    }

    [Fact]
    public async Task 用户手动暂停后_焦点回来不应自动继续()
    {
        var h = Build();
        h.Controller.FocusGuardEnabled = true;
        h.Waiter.BeforeWait = call =>
        {
            if (call == 3)
            {
                h.Controller.Pause(); // 用户自己按的暂停
            }
        };

        var finished = ExpectFinished(h.Controller);
        h.Controller.Start(Document("abcdefgh"), 0, Profile());

        SpinWait.SpinUntil(() => h.Controller.State == InputSessionState.Paused, TimeSpan.FromSeconds(5))
            .Should().BeTrue();

        // 失焦再回焦：不该把"用户主动的暂停"顶掉
        h.Focus.RaiseFocusLost();
        h.Focus.RaiseFocusRegained();

        h.Controller.State.Should().Be(InputSessionState.Paused, "只有焦点守护造成的暂停才能被自动恢复");

        h.Controller.Resume();
        var result = await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(SessionOutcome.Completed);
    }

    [Fact]
    public async Task 会话结束后_焦点监视应停止()
    {
        var h = Build();
        h.Controller.FocusGuardEnabled = true;

        var finished = ExpectFinished(h.Controller);
        h.Controller.Start(Document("ab"), 0, Profile());
        await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        h.Focus.IsRunning.Should().BeFalse();
        h.Focus.StopCount.Should().BeGreaterThan(0);

        // 会话结束后再报失焦也不该影响状态
        h.Focus.RaiseFocusLost();
        h.Controller.State.Should().Be(InputSessionState.Finished);
    }

    [Fact]
    public async Task 步进继续之后_焦点守护仍应生效()
    {
        // 回归用例：逐行步进改用 F9 继续（走 Resume）之后，
        // 若 Resume 没清掉"等待步进"标记，焦点守护会**永久失效**——字会继续打到别的窗口。
        var h = Build();
        h.Controller.FocusGuardEnabled = true;
        h.Controller.StepMode = true;

        var firstStepHandled = false;

        h.Scheduler.StepWaiting += (_, _) =>
        {
            if (!firstStepHandled)
            {
                firstStepHandled = true;
                h.Controller.Resume();       // 模拟用户按 F9 继续下一行
                h.Focus.RaiseFocusLost();    // 继续之后立刻失焦
                return;
            }

            h.Controller.Resume();           // 后续步进直接放行，避免测试挂住
        };

        var finished = ExpectFinished(h.Controller);
        h.Controller.Start(Document("aa\nbb\ncc"), 0, Profile());

        SpinWait.SpinUntil(() => h.Controller.State == InputSessionState.Paused, TimeSpan.FromSeconds(5))
            .Should().BeTrue("步进继续之后焦点守护必须仍然能暂停（标志残留会让它彻底失效）");

        h.Focus.RaiseFocusRegained();
        var result = await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(SessionOutcome.Completed);
        h.Emitter.Emitted.Should().NotBeEmpty();
    }

    // ── 统计与提示 ──────────────────────────────────────────────────────

    [Fact]
    public async Task 完成提示_应给出字符数与用时()
    {
        var h = Build();
        var finished = ExpectFinished(h.Controller);

        h.Controller.Start(Document("abcd"), 0, Profile());
        var statistics = await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        statistics.ToFinishMessage().Should().StartWith("输入完成，共 4 个字符");
        h.Controller.LastResult.Should().BeSameAs(statistics);
    }

    [Fact]
    public async Task 注入失败达阈值_应通知并进入错误态()
    {
        var h = Build();
        h.Emitter.ResultFactory = _ => EmitResult.Failed;
        var notification = ExpectNotification(h.Controller);
        var finished = ExpectFinished(h.Controller);

        h.Controller.Start(Document(new string('a', 30)), 0, Profile());
        var statistics = await finished.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        statistics.Outcome.Should().Be(SessionOutcome.Error);
        h.Controller.State.Should().Be(InputSessionState.Error);

        var note = await notification.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        note.Title.Should().Contain("中断");
    }
}
