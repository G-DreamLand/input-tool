using InputTool.Core.Parsing;
using InputTool.Core.Scheduling;

namespace InputTool.Tests.Scheduling;

/// <summary>
/// 速度参数校验（FR-006 参数表、设计文档 §5.3.1）。
/// </summary>
public sealed class SpeedProfileValidatorTests
{
    [Theory]
    [InlineData(0, 1)]       // T = 0 非法（SRS §5.2.5）→ 抬到下限 1 ms
    [InlineData(1, 1)]       // 1 ms 必须能真正取到（测试提速用）
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(200, 200)]
    [InlineData(2000, 2000)]
    [InlineData(5001, 5000)]
    [InlineData(99999, 5000)]
    public void 节拍间隔_应clamp到1到5000(int input, int expected)
    {
        SpeedProfileValidator.Normalize(new SpeedProfile(IntervalMs: input))
            .IntervalMs.Should().Be(expected);
    }

    [Fact]
    public void 节拍间隔_步进为1毫秒()
    {
        // 步进必须是 1，否则界面填 1 ms 会被对齐到 0（再抬回下限），看着像"设置没生效"
        SpeedProfileValidator.IntervalStepMs.Should().Be(1);

        SpeedProfileValidator.Normalize(new SpeedProfile(IntervalMs: 237))
            .IntervalMs.Should().Be(237);
    }

    [Fact]
    public void 解析后的单步下限_应与界面下限一致()
    {
        SpeedProfileValidator.MinResolvedIntervalMs.Should().Be(SpeedProfileValidator.MinIntervalMs);
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.9, 0.5)]
    public void 抖动比例_应clamp到0到50百分比(double input, double expected)
    {
        SpeedProfileValidator.Normalize(new SpeedProfile(JitterPercent: input))
            .JitterPercent.Should().Be(expected);
    }

    [Fact]
    public void 抖动比例_应对齐到5百分比步进()
    {
        SpeedProfileValidator.Normalize(new SpeedProfile(JitterPercent: 0.23))
            .JitterPercent.Should().Be(0.25);
    }

    [Fact]
    public void 抖动比例为NaN_应按下限处理()
    {
        SpeedProfileValidator.Normalize(new SpeedProfile(JitterPercent: double.NaN))
            .JitterPercent.Should().Be(0);
    }

    [Theory]
    [InlineData(33, 50)]
    [InlineData(10001, 10000)]
    [InlineData(-5, 0)]
    public void 行间隔延时_应clamp并步进对齐(int input, int expected)
    {
        SpeedProfileValidator.Normalize(new SpeedProfile(LineExtraDelayMs: input))
            .LineExtraDelayMs.Should().Be(expected);
    }

    [Theory]
    [InlineData(5010, 5000)]
    [InlineData(-1, 0)]
    public void 换行前延时_应clamp并步进对齐(int input, int expected)
    {
        SpeedProfileValidator.Normalize(new SpeedProfile(PreEnterDelayMs: input))
            .PreEnterDelayMs.Should().Be(expected);
    }

    [Fact]
    public void 归一化_不应改动换行策略()
    {
        var profile = new SpeedProfile(NewlineStrategy: NewlineStrategy.ShiftEnter);

        SpeedProfileValidator.Normalize(profile).NewlineStrategy.Should().Be(NewlineStrategy.ShiftEnter);
    }

    [Fact]
    public void 合法参数_不需要修正()
    {
        SpeedProfileValidator.NeedsCorrection(SpeedProfile.Default).Should().BeFalse();
    }

    [Fact]
    public void 越界参数_需要修正()
    {
        SpeedProfileValidator.NeedsCorrection(new SpeedProfile(IntervalMs: 99999)).Should().BeTrue();
    }

    [Fact]
    public void 由CPS换算节拍_应四舍五入()
    {
        SpeedProfile.IntervalFromCps(5).Should().Be(200);
        SpeedProfile.IntervalFromCps(0).Should().Be(SpeedProfileValidator.MaxIntervalMs);
    }

    [Fact]
    public void 由节拍换算CPS_应保留一位小数()
    {
        new SpeedProfile(IntervalMs: 200).CharactersPerSecond.Should().BeApproximately(5.0, 0.01);
        new SpeedProfile(IntervalMs: 150).CharactersPerSecond.Should().BeApproximately(6.67, 0.01);
    }
}
