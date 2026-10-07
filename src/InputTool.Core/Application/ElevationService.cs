using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using InputTool.Core.Injection;
using InputTool.Core.Interop;
using InputTool.Core.Logging;

namespace InputTool.Core.Application;

/// <summary>
/// 权限自检与提权重启。对应《软件开发设计文档》§5.9.2（FR-027）。
/// </summary>
/// <remarks>
/// 判断"本进程是否已提权"用 <c>GetTokenInformation(TokenElevation)</c>，
/// 而不是 <c>WindowsPrincipal</c>——后者在 UAC 开启时会给出误导性结果，
/// 且会引入额外的程序集依赖。
/// </remarks>
public sealed class ElevationService : IElevationService
{
    private readonly IInjectabilityProbe _probe;
    private readonly IAppLogger? _logger;

    /// <summary>构造提权服务。</summary>
    /// <param name="probe">可注入性探测（用于比较目标进程完整性级别）。</param>
    /// <param name="logger">日志（可选）。</param>
    public ElevationService(IInjectabilityProbe? probe = null, IAppLogger? logger = null)
    {
        _probe = probe ?? new InjectabilityProbe();
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsElevated => IsCurrentProcessElevated();

    /// <inheritdoc />
    public bool IsTargetProcessHigherIntegrity(IntPtr windowHandle)
        => _probe.IsTargetProcessHigherIntegrity(windowHandle);

    /// <inheritdoc />
    public bool RestartElevated(string? carryFilePath = null)
    {
        try
        {
            var (fileName, arguments) = BuildRestartCommand(carryFilePath);

            _logger?.Info($"请求以管理员身份重启：{fileName} {arguments}", "Elevation");

            Process.Start(new ProcessStartInfo(fileName)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = arguments,
            });

            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == Win32Constants.ErrorCancelled)
        {
            // 用户取消 UAC 不是错误（FR-027）
            _logger?.Info("用户取消了提权请求", "Elevation");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.Warn($"提权重启失败：{ex.Message}", "Elevation");
            return false;
        }
    }

    // ── 内部实现 ────────────────────────────────────────────────────────

    private static bool IsCurrentProcessElevated()
    {
        var process = NativeMethods.GetCurrentProcess();
        if (!NativeMethods.OpenProcessToken(process, Win32Constants.TokenQuery, out var token))
        {
            return false;
        }

        var buffer = IntPtr.Zero;

        try
        {
            buffer = Marshal.AllocHGlobal(sizeof(int));
            if (!NativeMethods.GetTokenInformation(
                    token,
                    Win32Constants.TokenElevationClass,
                    buffer,
                    sizeof(int),
                    out _))
            {
                return false;
            }

            return Marshal.ReadInt32(buffer) != 0;
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

            NativeMethods.CloseHandle(token);
        }
    }

    /// <summary>
    /// 拼出"以管理员身份重启"的命令行。
    /// 开发期进程是 <c>dotnet App.dll</c>，此时必须把程序集路径一起带上，否则重启的是空的 dotnet。
    /// </summary>
    private static (string FileName, string Arguments) BuildRestartCommand(string? carryFilePath)
    {
        var processPath = Environment.ProcessPath
                          ?? throw new InvalidOperationException("无法获取当前进程路径。");

        var carry = string.IsNullOrWhiteSpace(carryFilePath)
            ? string.Empty
            : $" --carry-file \"{carryFilePath}\"";

        var isDotnetHost = string.Equals(
            Path.GetFileNameWithoutExtension(processPath),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);

        if (isDotnetHost)
        {
            var assemblyPath = Assembly.GetEntryAssembly()?.Location
                               ?? throw new InvalidOperationException("无法获取入口程序集路径。");

            return (processPath, $"\"{assemblyPath}\" --elevated{carry}");
        }

        return (processPath, $"--elevated{carry}");
    }
}
