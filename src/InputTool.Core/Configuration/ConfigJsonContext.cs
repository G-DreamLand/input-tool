using System.Text.Json;
using System.Text.Json.Serialization;

namespace InputTool.Core.Configuration;

/// <summary>
/// System.Text.Json 源生成上下文（§5.7.1）。
/// 使用源生成而非反射是为了：裁剪友好、启动更快、无运行时反射开销。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppConfig))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext
{
}
