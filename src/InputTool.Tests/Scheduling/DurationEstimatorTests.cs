using InputTool.Core.Parsing;
using InputTool.Core.Scheduling;

namespace InputTool.Tests.Scheduling;

/// <summary>
/// 时长预估（FR-023、设计文档 §5.2.4）。
/// </summary>
public sealed class DurationEstimatorTests
{
    private static TextStatistics Statistics(int characters, int newlines, int functionKeys = 0)
        => new(
            TotalUnits: characters + newlines + functionKeys,
            CharacterUnits: characters,
            NewlineUnits: newlines,
            FunctionKeyUnits: functionKeys,
            SkippedUnits: 0,
            LineCount: 1,
            CharCount: characters,
            TruncatedByLimit: false);

    [Fact]
    public void 纯字符_应等于字符数乘节拍()
    {
        // 800 字 × 200 ms ≈ 2 分 40 秒（SRS FR-023 示例）
        var duration = DurationEstimator.Estimate(Statistics(800, 0), new SpeedProfile(IntervalMs: 200));

        duration.Should().Be(TimeSpan.FromSeconds(160));
        DurationEstimator.ToDisplayString(duration).Should().Be("约 2 分 40 秒");
    }

    [Fact]
    public void 换行单元_应计入换行前延时与行间隔延时()
    {
        var statistics = Statistics(characters: 10, newlines: 1);
        var profile = new SpeedProfile(IntervalMs: 100, LineExtraDelayMs: 500, PreEnterDelayMs: 50);

        // 10 × 100 + (50 + 100 + 500) = 1650 ms
        DurationEstimator.Estimate(statistics, profile).Should().Be(TimeSpan.FromMilliseconds(1650));
    }

    [Fact]
    public void 功能键单元_应按基础节拍计入()
    {
        var duration = DurationEstimator.Estimate(Statistics(0, 0, functionKeys: 3), new SpeedProfile(IntervalMs: 200));

        duration.Should().Be(TimeSpan.FromMilliseconds(600));
    }

    [Fact]
    public void 空文本_应为零时长()
    {
        DurationEstimator.Estimate(TextStatistics.Empty, SpeedProfile.Default).Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(0, "--")]
    [InlineData(1, "约 1 秒")]
    [InlineData(45, "约 45 秒")]
    [InlineData(60, "约 1 分 0 秒")]
    [InlineData(163, "约 2 分 43 秒")]
    [InlineData(3900, "约 1 小时 5 分")]
    public void ToDisplayString_应按量级格式化(int seconds, string expected)
    {
        DurationEstimator.ToDisplayString(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }

    [Fact]
    public void 负时长_应显示占位符()
    {
        DurationEstimator.ToDisplayString(TimeSpan.FromSeconds(-1)).Should().Be("--");
    }

    [Fact]
    public void 预估应随节拍线性变化()
    {
        var statistics = Statistics(100, 0);

        var fast = DurationEstimator.Estimate(statistics, new SpeedProfile(IntervalMs: 100));
        var slow = DurationEstimator.Estimate(statistics, new SpeedProfile(IntervalMs: 200));

        slow.Should().Be(fast * 2);
    }

    [Fact]
    public void 大文本预估_不应溢出()
    {
        var statistics = Statistics(5_000_000, 100_000);

        var duration = DurationEstimator.Estimate(statistics, new SpeedProfile(IntervalMs: 5000));

        duration.Should().BeGreaterThan(TimeSpan.FromDays(1));
    }
}
