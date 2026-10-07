using System.Text;

namespace InputTool.Core.Files;

/// <summary>
/// 编码判定与解码。对应《软件开发设计文档》§5.1.2 / §5.1.3。
/// 实现要点（留给实现阶段）：
/// · UTF-32 的 BOM 判定必须先于 UTF-16，否则 FF FE 00 00 会被误判为 UTF-16 LE；
/// · 严格 UTF-8 使用 new UTF8Encoding(false, throwOnInvalidBytes: true)；
/// · GB18030/936 需要 CodePagesEncodingProvider（csproj 已引入 CodePages 包）；
/// · 二进制采样：NUL 计 3 分、其他控制字符计 1 分，占比 &gt; 5% 即判为疑似二进制。
/// </summary>
public interface IEncodingDetector
{
    /// <summary>对文件头部字节做编码判定。</summary>
    EncodingDetectionResult Detect(ReadOnlySpan<byte> head);

    /// <summary>按用户偏好解码整段字节；<see cref="EncodingMode.Auto"/> 时复用 <see cref="Detect"/>。</summary>
    DecodedText Decode(ReadOnlySpan<byte> bytes, EncodingPreference preference);

    /// <summary>把显示名（如 "GB18030"）解析为 <see cref="Encoding"/>，用于配置恢复。</summary>
    bool TryGetEncoding(string encodingName, out Encoding encoding);
}
