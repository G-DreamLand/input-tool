namespace InputTool.Core.Application;

/// <summary>
/// 权限自检与提权重启。对应《软件开发设计文档》§5.9.2（FR-027）。
/// </summary>
/// <remarks>
/// 必须向用户明确传达（C-06）：目标窗口为管理员权限时无法注入，
/// 这是 Windows 的安全机制（UIPI）限制，不是软件缺陷。
/// </remarks>
public interface IElevationService
{
    /// <summary>本进程是否以管理员身份运行。</summary>
    bool IsElevated { get; }

    /// <summary>指定窗口所属进程的完整性级别是否高于本进程。</summary>
    bool IsTargetProcessHigherIntegrity(IntPtr windowHandle);

    /// <summary>
    /// 以管理员身份重启本工具（ShellExecute + runas，携带 --elevated 参数）。
    /// 用户取消 UAC 时返回 false，调用方按"非错误"处理。
    /// </summary>
    bool RestartElevated(string? carryFilePath = null);
}
