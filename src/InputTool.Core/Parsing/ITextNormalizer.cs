namespace InputTool.Core.Parsing;

/// <summary>
/// 文本归一化：换行统一、BOM 去除、可选转义解析与空行/注释处理（FR-004、SRS §5.1）。
/// </summary>
public interface ITextNormalizer
{
    /// <summary>把原始文本归一化为"以 \n 为唯一换行符"的中间形式。</summary>
    string Normalize(string rawText, NormalizeOptions options);
}

/// <summary>
/// 输入单元构建：把归一化后的文本展开为 <see cref="InputUnit"/> 序列（FR-004.7、SRS §5.1.3）。
/// </summary>
public interface IInputUnitBuilder
{
    /// <summary>解析转义序列（仅在 <see cref="NormalizeOptions.ParseEscapes"/> 开启时调用）。</summary>
    string ParseEscapes(string text);

    /// <summary>构建单元序列与统计信息。</summary>
    UnitBuildResult Build(string normalizedText, NormalizeOptions options);
}
