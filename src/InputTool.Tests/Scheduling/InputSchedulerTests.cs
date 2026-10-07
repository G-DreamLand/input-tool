using System.Text;
using InputTool.Core.Injection;
using InputTool.Core.Parsing;
using InputTool.Core.Scheduling;
using InputTool.Tests.TestDoubles;

namespace InputTool.Tests.Scheduling;

/// <summary>
/// 节拍调度（FR-007 / FR-015 / FR-016，设计文档 §5.4）。
/// 全部使用假时钟 + 假等待器 + 记录型注入器，因此是确定性的、毫秒级完成。
/// </summary>
public sealed class InputSchedulerTests
{
    private const int Interval = 200;

    private static InputUnit Char(char value) => InputUnit.Character(new Rune(value), 0, 0);

    private static InputUnit Newline() => InputUnit.NewLine(0, 0);

    private static InputUnit[] Chars(int count)
        => Enumerable.Range(0, count)
            .Select(i => InputUnit.Character(new Rune((char)('a' + (i % 26))), 0, i))
            .ToArray();

    /// <summary>同步收集进度（避免 Progress&lt;T&gt; 依赖同步上下文导致顺序不确定）。</summary>
    private sealed class SyncProgress : IProgress<ProgressInfo>
    {
        public List<ProgressInfo> Items { get; } = new();

        public void Report(ProgressInfo value)
        {
            lock (Items)
            {
                Items.Add(value);
            }
        }
    }

    private static async Task<SessionFinishedEventArgs> RunAsync(
        InputScheduler scheduler,
        IReadOnlyList<InputUnit> units,
        SpeedProfile profile,
        IProgress<ProgressInfo>? progress = null)
    {
        var completion = new TaskCompletionSource<SessionFinishedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Finished += (_, e) => completion.TrySetResult(e);
        scheduler.Start(units, () => profile, progress);
        return await completion.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    private static (FakeClock Clock, FakeWaiter Waiter, RecordingKeyboardEmitter Emitter, InputScheduler Scheduler) Build(
        List<string>? events = null)
    {
        var clock = new FakeClock();
        var waiter = new FakeWaiter(clock, events);
        var emitter = new RecordingKeyboardEmitter(events);
        var scheduler = new InputScheduler(emitter, clock, waiter, new IntervalCalculator(new FixedRandomSource(0.5)));
        return (clock, waiter, emitter, scheduler);
    }

    // ── 正常路径 ────────────────────────────────────────────────────────

    [Fact]
    public async Task 应按顺序注入全部单元_不丢不重()
    {
        var (_, _, emitter, scheduler) = Build();

        var result = await RunAsync(scheduler, Chars(5), new SpeedProfile(Interval));

        result.Outcome.Should().Be(SessionOutcome.Completed);
        result.Completed.Should().Be(5);
        emitter.Emitted.Should().HaveCount(5);
        emitter.Emitted.Should().BeInAscendingOrder(u => u.SourceIndex);
    }

    [Fact]
    public async Task 正常完成_应报告完成状态与统计()
    {
        var (_, _, _, scheduler) = Build();

        var result = await RunAsync(scheduler, Chars(3), new SpeedProfile(Interval));

        result.Outcome.Should().Be(SessionOutcome.Completed);
        result.Total.Should().Be(3);
        result.Completed.Should().Be(3);
        result.Elapsed.TotalMilliseconds.Should().Be(3 * Interval, "3 个单元 × 200 ms");
    }

    [Fact]
    public async Task 完成后_必须释放所有按键并解绑线程()
    {
        // FR-007.8 / NFR-005：任何结束路径都要补齐抬起事件
        var (_, _, emitter, scheduler) = Build();

        await RunAsync(scheduler, Chars(3), new SpeedProfile(Interval));

        emitter.ReleaseAllCount.Should().Be(1);
        emitter.AttachCount.Should().Be(1);
        emitter.DetachCount.Should().Be(1);
    }

    [Fact]
    public async Task 注入应在绑定的后台线程上_而非调用线程()
    {
        var (_, _, emitter, scheduler) = Build();
        var callerThreadId = Environment.CurrentManagedThreadId;

        await RunAsync(scheduler, Chars(3), new SpeedProfile(Interval));

        emitter.ThreadId.Should().NotBe(callerThreadId);
    }

    // ── 急停（FR-014 / NFR-006）────────────────────────────────────────

    [Fact]
    public async Task 急停_应立刻中断等待并进入停止态()
    {
        var (_, waiter, emitter, scheduler) = Build();

        // 第 3 次等待前触发急停：此时前 2 个单元已注入完成
        waiter.BeforeWait = call => { if (call == 3) scheduler.Stop(); };
        var finished = ExpectFinish(scheduler);
        scheduler.Start(Chars(10), () => new SpeedProfile(Interval));
        var result = await AwaitFinish(finished);

        result.Outcome.Should().Be(SessionOutcome.Stopped);
        result.Completed.Should().Be(2);
        emitter.Emitted.Should().HaveCount(2);
        emitter.ReleaseAllCount.Should().Be(1, "急停也必须补齐抬起事件");
    }

    [Fact]
    public async Task 急停_在停止前未完成的单元不应被注入()
    {
        var (_, waiter, emitter, scheduler) = Build();
        waiter.BeforeWait = call => { if (call == 1) scheduler.Stop(); };

        var finished = ExpectFinish(scheduler);
        scheduler.Start(Chars(5), () => new SpeedProfile(Interval));
        var result = await AwaitFinish(finished);

        result.Outcome.Should().Be(SessionOutcome.Stopped);
        emitter.Emitted.Should().BeEmpty();
    }

    // ── 暂停 / 继续（FR-015）───────────────────────────────────────────

    [Fact]
    public async Task 暂停后继续_应从当前单元接着注入()
    {
        var (_, waiter, emitter, scheduler) = Build();
        var resumeScheduled = false;

        waiter.BeforeWait = call =>
        {
            if (call != 2 || resumeScheduled)
            {
                return;
            }

            resumeScheduled = true;
            scheduler.Pause();
            _ = Task.Run(async () =>
            {
                await Task.Delay(30);
                scheduler.Resume();
            });
        };

        var result = await RunAsync(scheduler, Chars(4), new SpeedProfile(Interval));

        result.Outcome.Should().Be(SessionOutcome.Completed);
        result.Completed.Should().Be(4);

        // 不丢不重：注入序列应与输入序列完全一致
        emitter.Emitted.Select(u => u.SourceIndex).Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public async Task 暂停恢复_应重建时间基准而不补打暂停期间的欠账()
    {
        var (clock, waiter, _, scheduler) = Build();
        var resumeScheduled = false;

        waiter.BeforeWait = call =>
        {
            if (call != 2 || resumeScheduled)
            {
                return;
            }

            resumeScheduled = true;
            scheduler.Pause();
            _ = Task.Run(async () =>
            {
                await Task.Delay(30);
                // 模拟用户暂停了 60 秒（真实时钟会走掉这么多）
                clock.Advance(60_000);
                scheduler.Resume();
            });
        };

        var result = await RunAsync(scheduler, Chars(4), new SpeedProfile(Interval));

        result.Outcome.Should().Be(SessionOutcome.Completed);

        // 关键断言：恢复之后的每次等待，其"目标时刻 - 当时时刻"都恰好是一个节拍。
        // 若未重建基准，目标时刻会停留在 60 秒前，差值为巨额负数。
        var afterResume = waiter.Waits.Skip(2).ToList();
        afterResume.Should().NotBeEmpty();
        afterResume.Should().OnlyContain(w => w.Target - w.Now == Interval);
    }

    // ── 换行附加延时（FR-009）──────────────────────────────────────────

    [Fact]
    public async Task 换行单元_应先等P再注入再等L()
    {
        var events = new List<string>();
        var (_, _, _, scheduler) = Build(events);

        var profile = new SpeedProfile(
            IntervalMs: 100,
            PreEnterDelayMs: 50,
            LineExtraDelayMs: 500);

        await RunAsync(scheduler, [Newline()], profile);

        // 末尾的 release-all 来自收尾清理（FR-007.8），顺序本身正好证明"先 P、再注入、再 L"
        events.Should().Equal("delay:50", "emit:NewLine", "delay:500", "release-all");
    }

    [Fact]
    public async Task 普通字符_不应产生换行附加延时()
    {
        var (_, waiter, _, scheduler) = Build();

        await RunAsync(scheduler, Chars(3), new SpeedProfile(Interval, LineExtraDelayMs: 500, PreEnterDelayMs: 50));

        waiter.Delays.Should().BeEmpty();
    }

    [Fact]
    public async Task 仅行末尾回车_非末尾换行不注入_末尾换行注入()
    {
        // Q-01 决议：LastLineEnterOnly = 忽略非末尾位置的换行单元
        var (_, _, emitter, scheduler) = Build();
        InputUnit[] units = [Char('a'), Newline(), Char('b'), Newline()];

        var result = await RunAsync(scheduler, units, new SpeedProfile(Interval, NewlineStrategy: NewlineStrategy.LastLineEnterOnly));

        result.Outcome.Should().Be(SessionOutcome.Completed);
        emitter.Emitted.Select(u => u.Kind).Should().Equal(
            InputUnitKind.Character,
            InputUnitKind.Character,
            InputUnitKind.NewLine);
    }

    [Fact]
    public async Task 不发送回车策略_换行单元不注入但计入完成()
    {
        var (_, _, emitter, scheduler) = Build();
        InputUnit[] units = [Char('a'), Newline()];

        var result = await RunAsync(scheduler, units, new SpeedProfile(Interval, NewlineStrategy: NewlineStrategy.None));

        // 注入器负责"有意不注入"，因此仍然调用了 Emit
        result.Completed.Should().Be(2);
        emitter.Emitted.Should().HaveCount(2);
    }

    // ── 失败与追赶 ──────────────────────────────────────────────────────

    [Fact]
    public async Task 连续注入失败达阈值_应以UIPI提示进入错误态()
    {
        var (_, _, emitter, scheduler) = Build();
        emitter.ResultFactory = _ => EmitResult.Failed;

        var result = await RunAsync(scheduler, Chars(25), new SpeedProfile(Interval));

        result.Outcome.Should().Be(SessionOutcome.Error);
        result.Completed.Should().Be(20, "连续失败达到 20 次时终止");
        result.Message.Should().Contain("管理员");
        emitter.ReleaseAllCount.Should().Be(1);
    }

    [Fact]
    public async Task 偶发注入失败_应继续执行并统计失败数()
    {
        var (_, _, emitter, scheduler) = Build();

        // 前 3 个失败，之后恢复正常：不应触发阈值
        emitter.ResultFactory = unit => unit.SourceIndex < 3 ? EmitResult.Failed : EmitResult.Sent;

        var result = await RunAsync(scheduler, Chars(10), new SpeedProfile(Interval));

        result.Outcome.Should().Be(SessionOutcome.Completed);
        result.Failed.Should().Be(3);
        result.Completed.Should().Be(10);
    }

    [Fact]
    public async Task 落后超过阈值_应跳过等待并继续注入()
    {
        // 每次等待都多推进 500 ms，模拟线程被长时间抢占
        var (_, waiter, emitter, scheduler) = Build();
        waiter.OvershootMs = 500;

        var result = await RunAsync(scheduler, Chars(5), new SpeedProfile(Interval));

        result.Outcome.Should().Be(SessionOutcome.Completed);
        emitter.Emitted.Should().HaveCount(5, "追赶模式只是跳过等待，绝不丢字");
        waiter.Waits.Should().HaveCountLessThan(5, "落后时不应再调用高精度等待");
    }

    // ── 进度（FR-016）──────────────────────────────────────────────────

    [Fact]
    public async Task 进度上报_应节流到10Hz并最终报满()
    {
        var (_, _, _, scheduler) = Build();
        var progress = new SyncProgress();

        var result = await RunAsync(scheduler, Chars(100), new SpeedProfile(10), progress);

        result.Outcome.Should().Be(SessionOutcome.Completed);
        progress.Items.Should().NotBeEmpty();
        progress.Items.Should().HaveCountLessThanOrEqualTo(12, "1000 ms / 100 ms ≈ 10 次，加上收尾");

        var last = progress.Items[^1];
        last.Completed.Should().Be(100);
        last.Total.Should().Be(100);
        last.Percent.Should().BeApproximately(100.0, 0.01);
        last.Remaining.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task 进度文案_应符合界面格式()
    {
        var (_, _, _, scheduler) = Build();
        var progress = new SyncProgress();

        await RunAsync(scheduler, Chars(4), new SpeedProfile(Interval), progress);

        progress.Items[^1].ToDisplayString().Should().Be("已输入 4 / 4 字符 (100.0%)");
    }

    [Fact]
    public async Task 完成后_可以再次启动()
    {
        var (_, _, _, scheduler) = Build();

        var first = await RunAsync(scheduler, Chars(1), new SpeedProfile(Interval));
        var second = await RunAsync(scheduler, Chars(1), new SpeedProfile(Interval));

        first.Outcome.Should().Be(SessionOutcome.Completed);
        second.Outcome.Should().Be(SessionOutcome.Completed);
    }

    [Fact]
    public async Task 运行中重复启动_应抛出()
    {
        var (_, waiter, _, scheduler) = Build();
        using var block = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);

        // 卡住工作线程，让调度器稳定处于 Running
        waiter.BeforeWait = _ =>
        {
            entered.Set();
            SpinWait.SpinUntil(() => block.IsSet, TimeSpan.FromSeconds(5));
        };

        var finished = ExpectFinish(scheduler);
        scheduler.Start(Chars(3), () => new SpeedProfile(Interval));
        SpinWait.SpinUntil(() => entered.IsSet, TimeSpan.FromSeconds(5)).Should().BeTrue();

        var act = () => scheduler.Start(Chars(1), () => new SpeedProfile(Interval));
        act.Should().Throw<InvalidOperationException>();

        block.Set();
        await AwaitFinish(finished);
    }

    /// <summary>
    /// 在 Start **之前**订阅完成事件。
    /// 调度极快结束（例如第一个单元就被急停）时，若先 Start 再订阅会永远等不到事件。
    /// </summary>
    private static Task<SessionFinishedEventArgs> ExpectFinish(InputScheduler scheduler)
    {
        var completion = new TaskCompletionSource<SessionFinishedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Finished += (_, e) => completion.TrySetResult(e);
        return completion.Task;
    }

    private static async Task<SessionFinishedEventArgs> AwaitFinish(Task<SessionFinishedEventArgs> completion)
        => await completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
}
