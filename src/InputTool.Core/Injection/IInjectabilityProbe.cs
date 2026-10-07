namespace InputTool.Core.Injection;

/// <summary>
/// 可注入性探测（FR-010.4/.5、E-05、E-06）。
/// 检测顺序（§5.5.8）：会话是否断开 → 是否处于安全桌面/锁屏 →
/// 前台窗口是否有效 → 前台窗口进程的完整性级别是否更高。
/// </summary>
public interface IInjectabilityProbe
{
    /// <summary>执行一次完整检测。</summary>
    InjectabilityCheck Check();

    /// <summary>判断指定窗口所属进程的完整性级别是否高于本进程（FR-027）。</summary>
    bool IsTargetProcessHigherIntegrity(IntPtr windowHandle);
}
