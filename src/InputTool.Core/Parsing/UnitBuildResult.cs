namespace InputTool.Core.Parsing;

/// <summary>
/// 归一化 + 单元构建的最终产物。
/// </summary>
/// <param name="Units">按序排列的输入单元。</param>
/// <param name="Statistics">统计信息。</param>
/// <param name="Warnings">处理过程中产生的非致命警告（例如跳过了 N 个控制字符）。</param>
public sealed record UnitBuildResult(
    IReadOnlyList<InputUnit> Units,
    TextStatistics Statistics,
    IReadOnlyList<string> Warnings)
{
    /// <summary>空结果。</summary>
    public static UnitBuildResult Empty { get; } = new(Array.Empty<InputUnit>(), TextStatistics.Empty, Array.Empty<string>());
}
