namespace InputTool.Core.Files;

/// <summary>
/// 编码选择模式。对应界面"编码"下拉（FR-003.6）。
/// </summary>
public enum EncodingMode
{
    /// <summary>自动判定：BOM → 严格 UTF-8 → GB18030 → 系统 ANSI → Latin-1（§5.1.1）。</summary>
    Auto,

    Utf8,

    Utf16Le,

    Utf16Be,

    Gbk,

    /// <summary>GBK 的超集，覆盖更多生僻字，自动判定时优先于 GBK。</summary>
    Gb18030,

    /// <summary>系统 ANSI 代码页（中文 Windows 为 936）。</summary>
    Ansi,
}

/// <summary>用户的编码偏好。</summary>
/// <param name="Mode">编码模式；<see cref="EncodingMode.Auto"/> 表示走自动判定。</param>
public sealed record EncodingPreference(EncodingMode Mode)
{
    /// <summary>默认：自动判定。</summary>
    public static EncodingPreference Auto { get; } = new(EncodingMode.Auto);
}
