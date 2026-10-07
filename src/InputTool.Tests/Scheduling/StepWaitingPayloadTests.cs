using InputTool.Core.Injection;
using InputTool.Core.Parsing;
using InputTool.Core.Scheduling;
using InputTool.Tests.TestDoubles;

namespace InputTool.Tests.Scheduling;

/// <summary>
/// 步进等待事件的行号载荷（FR 新增：步进时显示下一行）。
/// </summary>
/// <remarks>
/// 载荷必须是**即将输入的那一行**——浮层显示的就是它，错一行整个功能就错了，
/// 而且错行不会有任何报错，只会安安静静地显示成另一行内容。
/// 用假时钟 + 假等待器确定性驱动，在回调里"代按"步进热键。
/// </remarks>
public sealed class StepWaitingPayloadTests
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
    public async Task 步进等待_载荷应是即将输入的那一行的行号()
    {
        var scheduler = Build(out _);
        scheduler.StepMode = true;

        var seen = new List<int>();
        scheduler.StepWaiting += (_, e) =>
        {
            seen.Add(e.LineIndex);
            scheduler.Resume(); // 代按 F9
        };

        var finished = ExpectFinish(scheduler);
        scheduler.Start(Units("aa\nbb\ncc"), () => new SpeedProfile(50));
        var result = await finished.WaitAsync(Never, Ct);

        result.Outcome.Should().Be(SessionOutcome.Completed);
        seen.Should().Equal(new[] { 1, 2 }, "进入第 2、3 行时各停一次，载荷就是即将输入的那一行（0 基）");
    }

    [Fact]
    public async Task 第一行不停_因此载荷永远不会是第0行()
    {
        var scheduler = Build(out _);
        scheduler.StepMode = true;

        var seen = new List<int>();
        scheduler.StepWaiting += (_, e) =>
        {
            seen.Add(e.LineIndex);
            scheduler.Resume();
        };

        var finished = ExpectFinish(scheduler);
        scheduler.Start(Units("aa\nbb\ncc"), () => new SpeedProfile(50));
        await finished.WaitAsync(Never, Ct);

        seen.Should().NotContain(0, "第一行开始时没有「上一行」可交代，调度器对第一行不停");
    }

    [Fact]
    public async Task 连续空行_每行都停且载荷逐个递进()
    {
        var scheduler = Build(out _);
        scheduler.StepMode = true;

        var seen = new List<int>();
        scheduler.StepWaiting += (_, e) =>
        {
            seen.Add(e.LineIndex);
            scheduler.Resume();
        };

        var finished = ExpectFinish(scheduler);
        // 第 2、3 行都是纯空行，按脚本语义各算一步
        scheduler.Start(Units("aa\n\n\nbb"), () => new SpeedProfile(50));
        var result = await finished.WaitAsync(Never, Ct);

        result.Outcome.Should().Be(SessionOutcome.Completed);
        seen.Should().Equal(new[] { 1, 2, 3 }, "连续空行要连续停，行号不能跳");
    }

    [Fact]
    public async Task 载荷中的行号_应对应源文本里真实存在的行()
    {
        var scheduler = Build(out _);
        scheduler.StepMode = true;

        const string Text = "aa\nbb\n\ncc";
        var lines = InputTool.Core.Application.SourceLineText.SplitLines(Text);

        var seen = new List<int>();
        scheduler.StepWaiting += (_, e) =>
        {
            seen.Add(e.LineIndex);
            scheduler.Resume();
        };

        var finished = ExpectFinish(scheduler);
        scheduler.Start(Units(Text), () => new SpeedProfile(50));
        await finished.WaitAsync(Never, Ct);

        seen.Should().NotBeEmpty();

        foreach (var lineIndex in seen)
        {
            lineIndex.Should().BeInRange(0, lines.Count - 1, "载荷行号必须能在源文本里定位到");
        }
    }
}