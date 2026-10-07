using InputTool.Core.Application;
using InputTool.Core.Injection;
using InputTool.Core.Parsing;
using InputTool.Core.Scheduling;
using InputTool.Tests.TestDoubles;

namespace InputTool.Tests.Scheduling;

/// <summary>
/// 逐行步进（跨行即停，按热键才输入下一行）。
/// </summary>
/// <remarks>
/// 用假时钟 + 假等待器确定性驱动：真实调度器会真的等待热键，测试里
/// 直接在 <see cref="IInputScheduler.StepWaiting"/> 回调里"代按"热键。
/// </remarks>
public sealed class StepModeTests
{
    private static readonly TimeSpan Never = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IReadOnlyList<InputUnit> Units(string text)
        => new InputUnitBuilder().Build(text, NormalizeOptions.Default).Units;

    private static InputScheduler Build(out RecordingKeyboardEmitter emitter)
    {
        var clock = new FakeClock();
        var waiter = new FakeWaiter(clock);
        emitter = new RecordingKeyboardEmitter();
        return new InputScheduler(emitter, clock, waiter, new IntervalCalculator(new FixedRandomSource(0.5)));
    }

    private static Task<SessionFinishedEventArgs> ExpectFinish(InputScheduler scheduler)
    {
        var completion = new TaskCompletionSource<SessionFinishedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Finished += (_, e) => completion.TrySetResult(e);
        return completion.Task;
    }

    [Fact]
    public async Task 未启用步进_应一次跑完且不产生等待()
    {
        var scheduler = Build(out var emitter);

        var stepWaits = 0;
        scheduler.StepWaiting += (_, _) => stepWaits++;

        var finished = ExpectFinish(scheduler);
        scheduler.Start(Units("aa\nbb\ncc"), () => new SpeedProfile(50));

        var result = await finished.WaitAsync(Never, Ct);

        stepWaits.Should().Be(0, "开关关闭时行为必须与以前完全一致");
        result.Outcome.Should().Be(SessionOutcome.Completed);
        emitter.Emitted.Should().HaveCount(8, "aa + 换行 + bb + 换行 + cc");
    }

    [Fact]
    public async Task 启用步进_每跨一行就停一次()
    {
        var scheduler = Build(out var emitter);
        scheduler.StepMode = true;

        var stepWaits = 0;
        scheduler.StepWaiting += (_, _) =>
        {
            stepWaits++;
            scheduler.Resume(); // 代按步进热键
        };

        var finished = ExpectFinish(scheduler);
        scheduler.Start(Units("aa\nbb\ncc"), () => new SpeedProfile(50));

        var result = await finished.WaitAsync(Never, Ct);

        stepWaits.Should().Be(2, "第 1 行不停；进入第 2、3 行各停一次");
        result.Outcome.Should().Be(SessionOutcome.Completed);
        emitter.Emitted.Should().HaveCount(8, "步进不得丢字");
    }

    [Fact]
    public async Task 空行也算一步()
    {
        var scheduler = Build(out _);
        scheduler.StepMode = true;

        var stepWaits = 0;
        scheduler.StepWaiting += (_, _) =>
        {
            stepWaits++;
            scheduler.Resume();
        };

        var finished = ExpectFinish(scheduler);
        // 第 2 行是纯空行，按脚本语义同样算一步
        scheduler.Start(Units("aa\n\nbb"), () => new SpeedProfile(50));

        var result = await finished.WaitAsync(Never, Ct);

        stepWaits.Should().Be(2, "空行同样要停一次（脚本语义）");
        result.Outcome.Should().Be(SessionOutcome.Completed);
    }

    [Fact]
    public async Task 停在行首时急停仍然生效()
    {
        var scheduler = Build(out _);
        scheduler.StepMode = true;

        var stepWaits = 0;
        scheduler.StepWaiting += (_, _) =>
        {
            stepWaits++;
            scheduler.Stop(); // 等待期间按急停，而不是继续
        };

        var finished = ExpectFinish(scheduler);
        scheduler.Start(Units("aa\nbb\ncc"), () => new SpeedProfile(50));

        var result = await finished.WaitAsync(Never, Ct);

        stepWaits.Should().Be(1, "急停后不该再有步进等待");
        result.Outcome.Should().Be(SessionOutcome.Stopped, "步进等待期间急停必须能立刻停下");
    }

    [Fact]
    public async Task 单行文本_不应产生步进等待()
    {
        var scheduler = Build(out _);
        scheduler.StepMode = true;

        var stepWaits = 0;
        scheduler.StepWaiting += (_, _) => stepWaits++;

        var finished = ExpectFinish(scheduler);
        scheduler.Start(Units("只有一行"), () => new SpeedProfile(50));

        await finished.WaitAsync(Never, Ct);

        stepWaits.Should().Be(0, "只有一行时没有可跨的行，不该停");
    }
}
