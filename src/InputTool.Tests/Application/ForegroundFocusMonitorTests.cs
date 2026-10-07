using InputTool.Core.Application;
using InputTool.Tests.TestDoubles;

namespace InputTool.Tests.Application;

/// <summary>
/// 前台焦点监视器（FR-020.2 / NFR：间隔 ≥ 200 ms）。
/// </summary>
/// <remarks>
/// 用一个"十年才触发一次"的间隔启动，把定时器排除在测试之外，
/// 再手动调用 <see cref="ForegroundFocusMonitor.PollOnce"/> 确定性驱动检查。
/// </remarks>
public sealed class ForegroundFocusMonitorTests
{
    private static readonly TimeSpan NeverInterval = TimeSpan.FromMinutes(10);

    private static readonly IntPtr Target = new(0x1000);
    private static readonly IntPtr Other = new(0x2000);

    [Fact]
    public void 检查间隔下限_应为200毫秒()
    {
        ForegroundFocusMonitor.MinimumInterval.Should().Be(TimeSpan.FromMilliseconds(200));
        ForegroundFocusMonitor.DefaultInterval.Should().Be(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void 目标句柄为零_不应启动()
    {
        var windows = new FakeTargetWindowGateway();
        using var monitor = new ForegroundFocusMonitor(windows);

        monitor.Start(IntPtr.Zero, NeverInterval);

        monitor.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void 启动后_应处于运行状态并记住目标窗口()
    {
        var windows = new FakeTargetWindowGateway { Foreground = Target };
        using var monitor = new ForegroundFocusMonitor(windows);

        monitor.Start(Target, NeverInterval);

        monitor.IsRunning.Should().BeTrue();
        monitor.TargetWindow.Should().Be(Target);
    }

    [Fact]
    public void 焦点离开目标窗口_应触发一次且不重复()
    {
        var windows = new FakeTargetWindowGateway { Foreground = Target };
        using var monitor = new ForegroundFocusMonitor(windows);
        var lost = 0;
        monitor.FocusLost += (_, _) => lost++;

        monitor.Start(Target, NeverInterval);
        windows.Foreground = Other;

        monitor.PollOnce();
        monitor.PollOnce();
        monitor.PollOnce();

        lost.Should().Be(1, "状态没变化就不该反复通知，否则托盘会被刷屏");
    }

    [Fact]
    public void 焦点回到目标窗口_应触发恢复事件()
    {
        var windows = new FakeTargetWindowGateway { Foreground = Target };
        using var monitor = new ForegroundFocusMonitor(windows);
        var lost = 0;
        var regained = 0;
        monitor.FocusLost += (_, _) => lost++;
        monitor.FocusRegained += (_, _) => regained++;

        monitor.Start(Target, NeverInterval);
        windows.Foreground = Other;
        monitor.PollOnce();

        windows.Foreground = Target;
        monitor.PollOnce();
        monitor.PollOnce();

        lost.Should().Be(1);
        regained.Should().Be(1);
    }

    [Fact]
    public void 启动时焦点已不在目标窗口_应先按基准认定而不是立刻报失焦()
    {
        // 以"开始那一刻"的真实状态为基准，避免刚启动就误报
        var windows = new FakeTargetWindowGateway { Foreground = Other };
        using var monitor = new ForegroundFocusMonitor(windows);
        var lost = 0;
        monitor.FocusLost += (_, _) => lost++;

        monitor.Start(Target, NeverInterval);
        monitor.PollOnce();

        lost.Should().Be(0);
    }

    [Fact]
    public void 停止后_不应再触发任何事件()
    {
        var windows = new FakeTargetWindowGateway { Foreground = Target };
        using var monitor = new ForegroundFocusMonitor(windows);
        var lost = 0;
        monitor.FocusLost += (_, _) => lost++;

        monitor.Start(Target, NeverInterval);
        monitor.Stop();

        windows.Foreground = Other;
        monitor.PollOnce();

        lost.Should().Be(0);
        monitor.IsRunning.Should().BeFalse();
        monitor.TargetWindow.Should().Be(IntPtr.Zero);
    }

    [Fact]
    public void 重复停止_不应抛异常()
    {
        var windows = new FakeTargetWindowGateway { Foreground = Target };
        using var monitor = new ForegroundFocusMonitor(windows);
        monitor.Start(Target, NeverInterval);

        var act = () =>
        {
            monitor.Stop();
            monitor.Stop();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void 真实定时器路径_也能被驱动()
    {
        // 这条覆盖真实 Timer：给一个很短的间隔（会被抬到 200 ms 下限），
        // 在 1 秒内应当至少触发一次失焦事件。
        var windows = new FakeTargetWindowGateway { Foreground = Target };
        using var monitor = new ForegroundFocusMonitor(windows);
        using var lost = new ManualResetEventSlim(false);
        monitor.FocusLost += (_, _) => lost.Set();

        monitor.Start(Target, TimeSpan.FromMilliseconds(1)); // 低于下限 → 应被抬到 200 ms
        windows.Foreground = Other;

        lost.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken).Should().BeTrue("真实定时器路径也要能把失焦报出来");
    }
}
