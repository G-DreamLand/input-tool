using System.Text;
using InputTool.Core.Parsing;
using InputTool.Core.Scheduling;
using InputTool.Tests.TestDoubles;

namespace InputTool.Tests.Scheduling;

/// <summary>
/// 节拍计算（FR-006.4、SRS §5.2.2）。
/// </summary>
public sealed class IntervalCalculatorTests
{
    private static InputUnit Char() => InputUnit.Character(new Rune('a'), 0, 0);

    private static InputUnit Newline() => InputUnit.NewLine(0, 0);

    [Fact]
    public void 无抖动_应精确等于节拍()
    {
        var calculator = new IntervalCalculator(new FixedRandomSource(0.9));

        calculator.NextIntervalMs(Char(), new SpeedProfile(IntervalMs: 200)).Should().Be(200);
    }

    [Theory]
    // NextUnit() = 1.0 时 r = +1；0.0 时 r = -1；0.5 时 r = 0
    [InlineData(1.0, 150)]
    [InlineData(0.5, 100)]
    [InlineData(0.0, 50)]
    public void 抖动_应按正负区间缩放(double random, int expected)
    {
        var calculator = new IntervalCalculator(new FixedRandomSource(random));

        calculator.NextIntervalMs(Char(), new SpeedProfile(IntervalMs: 100, JitterPercent: 0.5))
            .Should().Be(expected);
    }

    [Fact]
    public void 抖动后的间隔_四舍五入到整数毫秒()
    {
        // 100 × (1 + 0.1 × 0.33) = 103.3 → 103
        var calculator = new IntervalCalculator(new FixedRandomSource(0.665));

        calculator.NextIntervalMs(Char(), new SpeedProfile(IntervalMs: 100, JitterPercent: 0.1))
            .Should().Be(103);
    }

    [Fact]
    public void 抖动后低于下限_应钳到5毫秒()
    {
        // 10 × (1 - 0.5) = 5 → 恰好下限
        var calculator = new IntervalCalculator(new FixedRandomSource(0.0));

        calculator.NextIntervalMs(Char(), new SpeedProfile(IntervalMs: 10, JitterPercent: 0.5))
            .Should().Be(5);
    }

    [Fact]
    public void 换行单元_不应在此处附加延时()
    {
        // 换行附加延时（P / L）由调度器在注入前后分别等待，这里必须只返回基础节拍
        var calculator = new IntervalCalculator(new FixedRandomSource(0.5));
        var profile = new SpeedProfile(IntervalMs: 100, PreEnterDelayMs: 50, LineExtraDelayMs: 500);

        calculator.NextIntervalMs(Newline(), profile).Should().Be(100);
    }

    [Fact]
    public void 多个单元_应依次消耗随机序列()
    {
        var calculator = new IntervalCalculator(new FixedRandomSource(1.0, 0.0));
        var profile = new SpeedProfile(IntervalMs: 100, JitterPercent: 0.5);

        calculator.NextIntervalMs(Char(), profile).Should().Be(150);
        calculator.NextIntervalMs(Char(), profile).Should().Be(50);
        // 序列用完后循环
        calculator.NextIntervalMs(Char(), profile).Should().Be(150);
    }
}
