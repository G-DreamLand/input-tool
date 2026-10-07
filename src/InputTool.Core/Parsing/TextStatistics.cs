namespace InputTool.Core.Parsing;

/// <summary>
/// 文本统计信息（FR-005.2）。
/// 注意：预估时长不放在此处——它需要 <c>SpeedProfile</c>，为避免
/// 解析层反向依赖调度层，预估由 <c>Scheduling.DurationEstimator</c> 承担（§5.2.4 的细化）。
/// </summary>
/// <param name="TotalUnits">单元总数。</param>
/// <param name="CharacterUnits">普通字符单元数。</param>
/// <param name="NewlineUnits">换行单元数。</param>
/// <param name="FunctionKeyUnits">功能性按键单元数。</param>
/// <param name="SkippedUnits">被跳过并计数的单元数（C0 控制字符、NUL、无法表示的字符）。</param>
/// <param name="LineCount">行数。</param>
/// <param name="CharCount">字符数（不含换行）。</param>
/// <param name="TruncatedByLimit">是否因超过 <see cref="NormalizeOptions.MaxUnits"/> 被截断。</param>
public sealed record TextStatistics(
    int TotalUnits,
    int CharacterUnits,
    int NewlineUnits,
    int FunctionKeyUnits,
    int SkippedUnits,
    int LineCount,
    int CharCount,
    bool TruncatedByLimit)
{
    /// <summary>空统计（未加载文件）。</summary>
    public static TextStatistics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, false);
}
