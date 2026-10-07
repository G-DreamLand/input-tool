using System.Diagnostics;
using System.Runtime.InteropServices;
using InputTool.Core.Interop;

namespace InputTool.Core.Injection;

/// <summary>
/// 可注入性探测。对应《软件开发设计文档》§5.5.8（FR-010.4、E-05、E-06）。
/// </summary>
/// <remarks>
/// 检测顺序：会话是否断开 → 是否处于安全桌面/锁屏 → 前台窗口是否有效 →
/// 前台窗口进程的完整性级别是否更高。
/// 任何一步检测失败都**保守返回"不可注入"或"未发现更高完整性"**，绝不抛异常——
/// 它会被每 200 ms 的监视线程反复调用（§9.1）。
/// </remarks>
public sealed class InjectabilityProbe : IInjectabilityProbe
{
    /// <inheritdoc />
    public InjectabilityCheck Check()
    {
        // 1) 会话是否已断开（远程会话 / 已注销）
        var activeSession = NativeMethods.WTSGetActiveConsoleSessionId();
        if (activeSession != 0xFFFFFFFF)
        {
            try
            {
                if (Process.GetCurrentProcess().SessionId != (int)activeSession)
                {
                    return new InjectabilityCheck(
                        InjectabilityBlocker.SessionDisconnected,
                        "当前会话已断开（远程会话或已注销），无法注入按键。");
                }
            }
            catch (Exception)
            {
                // 取不到会话信息时跳过该项检查，不影响后续判断
            }
        }

        // 2) 是否处于锁屏 / 安全桌面 / UAC 提示：这种桌面上 OpenInputDesktop 会失败
        var desktop = NativeMethods.OpenInputDesktop(0, false, Win32Constants.DesktopSwitchDesktop);
        if (desktop == IntPtr.Zero)
        {
            return new InjectabilityCheck(
                InjectabilityBlocker.SecureDesktop,
                "当前处于锁屏 / 安全桌面 / UAC 提示状态，无法注入按键（E-06）。");
        }

        NativeMethods.CloseDesktop(desktop);

        // 3) 是否有前台窗口
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return new InjectabilityCheck(
                InjectabilityBlocker.NoForegroundWindow,
                "当前没有前台窗口，请先点击目标输入框。");
        }

        // 4) 前台窗口是否为更高完整性级别（UIPI）
        if (IsTargetProcessHigherIntegrity(foreground))
        {
            return new InjectabilityCheck(
                InjectabilityBlocker.HigherIntegrity,
                "目标窗口以管理员身份运行，普通权限无法向其注入按键。"
                + "这是 Windows 的安全机制（UIPI）限制，不是软件缺陷——请以管理员身份重启本工具。");
        }

        return new InjectabilityCheck(InjectabilityBlocker.None, "可以注入。");
    }

    /// <inheritdoc />
    public bool IsTargetProcessHigherIntegrity(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(windowHandle, out var targetProcessId);
        if (targetProcessId == 0)
        {
            return false;
        }

        if (!TryGetIntegrityLevel(targetProcessId, out var targetLevel))
        {
            return false;
        }

        if (!TryGetIntegrityLevel((uint)Environment.ProcessId, out var selfLevel))
        {
            return false;
        }

        return targetLevel > selfLevel;
    }

    /// <summary>读取进程令牌的完整性级别 RID（0x2000 Medium / 0x3000 High / 0x4000 System）。</summary>
    private static bool TryGetIntegrityLevel(uint processId, out int integrityLevel)
    {
        integrityLevel = 0;

        var process = NativeMethods.OpenProcess(Win32Constants.ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
        {
            return false;
        }

        var token = IntPtr.Zero;
        var buffer = IntPtr.Zero;

        try
        {
            if (!NativeMethods.OpenProcessToken(process, Win32Constants.TokenQuery, out token))
            {
                return false;
            }

            NativeMethods.GetTokenInformation(token, Win32Constants.TokenIntegrityLevelClass, IntPtr.Zero, 0, out var size);
            if (size == 0)
            {
                return false;
            }

            buffer = Marshal.AllocHGlobal((int)size);
            if (!NativeMethods.GetTokenInformation(token, Win32Constants.TokenIntegrityLevelClass, buffer, size, out _))
            {
                return false;
            }

            var label = Marshal.PtrToStructure<TOKEN_MANDATORY_LABEL>(buffer);
            if (label.Label.Sid == IntPtr.Zero)
            {
                return false;
            }

            var subAuthorityCount = Marshal.ReadByte(NativeMethods.GetSidSubAuthorityCount(label.Label.Sid));
            if (subAuthorityCount == 0)
            {
                return false;
            }

            var ridPointer = NativeMethods.GetSidSubAuthority(label.Label.Sid, (uint)(subAuthorityCount - 1));
            integrityLevel = Marshal.ReadInt32(ridPointer);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }

            if (token != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(token);
            }

            NativeMethods.CloseHandle(process);
        }
    }
}
