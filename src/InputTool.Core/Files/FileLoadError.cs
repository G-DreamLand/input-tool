namespace InputTool.Core.Files;

/// <summary>
/// 文件加载失败的原因分类。对应《软件开发设计文档》§5.1.1 与 §10.2（E-03）。
/// 每类都有对应的用户文案，禁止用"读取失败"一律概括（NFR-008）。
/// </summary>
public enum FileLoadError
{
    None = 0,

    /// <summary>路径不存在。</summary>
    NotFound,

    /// <summary>无访问权限。</summary>
    AccessDenied,

    /// <summary>被其他程序独占占用（ERROR_SHARING_VIOLATION / LOCK_VIOLATION）。</summary>
    InUse,

    /// <summary>路径指向目录而非文件。</summary>
    IsDirectory,

    /// <summary>文件过大（超过 50 MB 强提示阈值，§5.1.5）。</summary>
    TooLarge,

    /// <summary>文件为空（0 字节）→ 提示"文件内容为空"，禁止开始（E-02）。</summary>
    Empty,

    /// <summary>其他读取失败（IO 异常）。</summary>
    ReadFailed,

    /// <summary>疑似二进制内容（§5.1.3）。</summary>
    Binary,
}
