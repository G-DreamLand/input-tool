namespace InputTool.Core.Injection;

/// <summary>
/// 阻止注入的原因。对应《软件开发设计文档》§5.5.8 与 E-05 / E-06。
/// </summary>
public enum InjectabilityBlocker
{
    /// <summary>可以注入。</summary>
    None,

    /// <summary>会话已断开（WTSGetActiveConsoleSessionId 与当前会话不符）。</summary>
    SessionDisconnected,

    /// <summary>处于锁屏 / 安全桌面 / UAC 提示（OpenInputDesktop 失败）。</summary>
    SecureDesktop,

    /// <summary>没有前台窗口（GetForegroundWindow 返回 0）。</summary>
    NoForegroundWindow,

    /// <summary>前台窗口属于更高完整性级别（UIPI 限制，非致命，提示提权即可）。</summary>
    HigherIntegrity,
}

/// <summary>可注入性检查结果。</summary>
/// <param name="Blocker">阻止原因；<see cref="InjectabilityBlocker.None"/> 表示可注入。</param>
/// <param name="Reason">可读说明，写入日志并用于用户提示。</param>
public sealed record InjectabilityCheck(InjectabilityBlocker Blocker, string Reason)
{
    /// <summary>是否可注入。</summary>
    public bool CanInject => Blocker == InjectabilityBlocker.None;
}
