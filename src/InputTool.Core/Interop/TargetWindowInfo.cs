namespace InputTool.Core.Interop;

/// <summary>
/// 可作为输入目标的一个顶层窗口。
/// </summary>
/// <param name="Handle">窗口句柄。</param>
/// <param name="Title">窗口标题。</param>
/// <param name="ProcessName">所属进程名（取不到时为空串）。</param>
/// <param name="ClassName">窗口类名（取不到时为空串）。</param>
/// <param name="ProcessId">所属进程 ID。</param>
public sealed record TargetWindowInfo(
    IntPtr Handle,
    string Title,
    string ProcessName,
    string ClassName,
    uint ProcessId)
{
    /// <summary>
    /// 是否是"终端类"窗口（控制台 / 终端模拟器）。
    /// </summary>
    /// <remarks>
    /// 判断依据是窗口类名与进程名，覆盖：
    /// 经典 conhost（`ConsoleWindowClass`）、Windows Terminal（`CASCADIA_HOSTING_WINDOW_CLASS`）、
    /// WSL（`wsl.exe` / `wslhost.exe` / `OpenConsole.exe`）以及常见第三方终端。
    /// </remarks>
    public bool IsTerminal => IsTerminalClass(ClassName) || IsTerminalProcess(ProcessName);

    /// <summary>界面展示用的描述，例如 <c>wsl.exe — Ubuntu（ConsoleWindowClass）</c>。</summary>
    public string DisplayText
    {
        get
        {
            var title = string.IsNullOrWhiteSpace(Title) ? "(无标题)" : Title.Trim();
            var process = string.IsNullOrWhiteSpace(ProcessName) ? "?" : ProcessName + ".exe";

            return $"{process} — {title}";
        }
    }

    private static bool IsTerminalClass(string className) => className switch
    {
        "ConsoleWindowClass" => true,
        "CASCADIA_HOSTING_WINDOW_CLASS" => true,
        "VirtualConsoleClass" => true,
        "mintty" => true,
        _ => false,
    };

    private static bool IsTerminalProcess(string processName) => processName.ToLowerInvariant() switch
    {
        "wsl" or "wslhost" or "wslservice" => true,
        "windowsterminal" or "openconsole" or "conhost" => true,
        "cmd" or "powershell" or "pwsh" => true,
        "bash" or "mintty" or "cygwin" or "wt" => true,
        _ => false,
    };
}
