namespace InputTool.Core.Abstractions;

/// <summary>
/// 随机源抽象，用于节拍抖动（FR-006.4）。
/// 抽成接口是为了让单测能注入固定序列，断言"抖动后的间隔"可复现（§12.1）。
/// </summary>
public interface IRandomSource
{
    /// <summary>返回 [0, 1) 区间的均匀随机数。</summary>
    double NextUnit();
}

/// <summary>默认实现：线程安全的共享随机源。</summary>
public sealed class SystemRandomSource : IRandomSource
{
    /// <inheritdoc />
    public double NextUnit() => Random.Shared.NextDouble();
}
