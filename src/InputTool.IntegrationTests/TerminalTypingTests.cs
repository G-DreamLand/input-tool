using System.Diagnostics;
using InputTool.Core.Injection;
using InputTool.Core.Interop;
using InputTool.Core.Parsing;
using InputTool.Core.Scheduling;

namespace InputTool.IntegrationTests;

/// <summary>
/// 终端场景的真实验证（SRS 场景 B、AC-07 / AC-08）：
/// 启动真实的 cmd.exe / PowerShell 窗口，注入"命令行 + 回车"，
/// 再通过命令自身的副作用（生成标记文件）判断**它到底有没有被执行**。
/// </summary>
/// <remarks>
/// 为什么用"标记文件"而不是回读终端窗口内容：
/// · 现代终端（Windows Terminal / conhost）的文本区无法可靠地跨进程读取；
/// · 命令的副作用是"真的执行了"的确凿证据，且与终端实现无关。
/// <para>
/// 打 <c>[Trait("Category","Desktop")]</c>：会打开真实控制台窗口并抢占前台焦点，CI 默认跳过。
/// </para>
/// </remarks>
// 桌面用例共享同一集合 → 串行执行；并行时它们会互相抢走前台焦点
[Trait("Category", "Desktop")]
[Collection("Desktop")]
public sealed class TerminalTypingTests
{
    /// <summary>逐字节拍（测试用，快一些）。</summary>
    private const int IntervalMs = 40;

    /// <summary>WSL 发行版名（openEuler 22.03 LTS）。</summary>
    private const string OpenEulerDistro = "openEuler-22.03";

    [Fact]
    public async Task CMD终端_单行命令_回车应真的执行()
    {
        var directory = NewTempDirectory();
        var marker = Path.Combine(directory, "cmd-ok.txt");
        var command = $"echo INPUTTOOL_CMD_OK> \"{marker}\"\n";

        await RunInTerminalAsync(
            "cmd.exe",
            string.Empty,
            command,
            async () =>
            {
                (await WaitUntilAsync(() => File.Exists(marker), TimeSpan.FromSeconds(15)))
                    .Should().BeTrue($"命令没有被执行：标记文件未生成 {marker}");

                File.ReadAllText(marker).Trim().Should().Be("INPUTTOOL_CMD_OK");
            });
    }

    [Fact]
    public async Task CMD终端_多行命令_应逐行执行()
    {
        // SRS 场景 B：向终端灌入多行命令，每行回车执行，行间隔留出执行时间
        var directory = NewTempDirectory();
        var first = Path.Combine(directory, "line1.txt");
        var second = Path.Combine(directory, "line2.txt");
        var command = $"echo LINE1> \"{first}\"\necho LINE2> \"{second}\"\n";

        await RunInTerminalAsync(
            "cmd.exe",
            string.Empty,
            command,
            async () =>
            {
                (await WaitUntilAsync(
                        () => File.Exists(first) && File.Exists(second),
                        TimeSpan.FromSeconds(20)))
                    .Should().BeTrue("两行命令都应被逐行执行");

                File.ReadAllText(first).Trim().Should().Be("LINE1");
                File.ReadAllText(second).Trim().Should().Be("LINE2");
            },
            lineExtraDelayMs: 800);
    }

    [Fact]
    public async Task PowerShell终端_单行命令_回车应真的执行()
    {
        var directory = NewTempDirectory();
        var marker = Path.Combine(directory, "ps-ok.txt");
        var command = $"Set-Content -LiteralPath '{marker}' -Value 'INPUTTOOL_PS_OK'\n";

        await RunInTerminalAsync(
            "powershell.exe",
            "-NoExit -NoProfile",
            command,
            async () =>
            {
                (await WaitUntilAsync(() => File.Exists(marker), TimeSpan.FromSeconds(20)))
                    .Should().BeTrue($"命令没有被执行：标记文件未生成 {marker}");

                File.ReadAllText(marker).Trim().Should().Be("INPUTTOOL_PS_OK");
            },
            readyDelayMs: 2000);
    }

    [Fact]
    public async Task PowerShell终端_多行命令_应逐行执行()
    {
        var directory = NewTempDirectory();
        var first = Path.Combine(directory, "ps1.txt");
        var second = Path.Combine(directory, "ps2.txt");
        var command =
            $"Set-Content -LiteralPath '{first}' -Value 'PS_LINE1'\n"
            + $"Set-Content -LiteralPath '{second}' -Value 'PS_LINE2'\n";

        await RunInTerminalAsync(
            "powershell.exe",
            "-NoExit -NoProfile",
            command,
            async () =>
            {
                (await WaitUntilAsync(
                        () => File.Exists(first) && File.Exists(second),
                        TimeSpan.FromSeconds(25)))
                    .Should().BeTrue("两行 PowerShell 命令都应被逐行执行");

                File.ReadAllText(first).Trim().Should().Be("PS_LINE1");
                File.ReadAllText(second).Trim().Should().Be("PS_LINE2");
            },
            readyDelayMs: 2000,
            lineExtraDelayMs: 1200);
    }

    // ── WSL（openEuler 22.03 LTS）──────────────────────────────────────

    [Fact]
    public async Task WSL_openEuler终端_单行命令_回车应真的执行()
    {
        var marker = $"/tmp/inputtool-wsl-{Guid.NewGuid():N}.txt";
        var command = $"echo INPUTTOOL_WSL_OK > {marker}\n";

        await RunInTerminalAsync(
            "wsl.exe",
            $"-d {OpenEulerDistro}",
            command,
            async () =>
            {
                var content = await WaitForWslFileAsync(marker, TimeSpan.FromSeconds(25));

                content.Should().NotBeNull($"命令没有被执行：发行版内未生成文件 {marker}");
                content!.Trim().Should().Be("INPUTTOOL_WSL_OK");

                await DeleteWslFileAsync(marker);
            },
            readyDelayMs: 3000);
    }

    [Fact]
    public async Task WSL_openEuler终端_多行命令_应逐行执行()
    {
        var first = $"/tmp/inputtool-wsl1-{Guid.NewGuid():N}.txt";
        var second = $"/tmp/inputtool-wsl2-{Guid.NewGuid():N}.txt";
        var command = $"echo WSL_LINE1 > {first}\necho WSL_LINE2 > {second}\n";

        await RunInTerminalAsync(
            "wsl.exe",
            $"-d {OpenEulerDistro}",
            command,
            async () =>
            {
                var content1 = await WaitForWslFileAsync(first, TimeSpan.FromSeconds(30));
                var content2 = await WaitForWslFileAsync(second, TimeSpan.FromSeconds(15));

                content1.Should().NotBeNull("第一行命令应被执行");
                content2.Should().NotBeNull("第二行命令应被执行（逐行执行）");
                content1!.Trim().Should().Be("WSL_LINE1");
                content2!.Trim().Should().Be("WSL_LINE2");

                await DeleteWslFileAsync(first);
                await DeleteWslFileAsync(second);
            },
            readyDelayMs: 3000,
            lineExtraDelayMs: 800);
    }

    // ── 脚手架 ──────────────────────────────────────────────────────────

    private static string NewTempDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task RunInTerminalAsync(
        string fileName,
        string arguments,
        string commandText,
        Func<Task> verify,
        int readyDelayMs = 700,
        int lineExtraDelayMs = 500)
    {
        // 记录启动前已有的窗口：控制台窗口的归属进程是 conhost / Windows Terminal，
        // 拿不到 MainWindowHandle，只能靠"启动后新出现的窗口"来定位。
        var existing = WindowEnumerator.GetVisibleTopLevelWindows().ToHashSet();

        var startInfo = new ProcessStartInfo(fileName, arguments) { UseShellExecute = true };
        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException($"无法启动 {fileName}");

        try
        {
            (await FocusNewWindowAsync(existing, TimeSpan.FromSeconds(30)))
                .Should().BeTrue($"{fileName} 的新窗口未能在 30 秒内获得前台焦点，无法验证注入");

            // 控制台/Shell 初始化需要一点时间，过早注入会被丢弃
            await Task.Delay(readyDelayMs, TestContext.Current.CancellationToken);

            await TypeAsync(commandText, IntervalMs, lineExtraDelayMs);

            await verify();
        }
        finally
        {
            await TryExitAsync(process);
        }
    }

    /// <summary>用真实的注入器把文本打出去（命令行末尾已含换行单元）。</summary>
    private static async Task TypeAsync(string text, int intervalMs, int lineExtraDelayMs)
    {
        var built = new InputUnitBuilder().Build(text, NormalizeOptions.Default);
        var scheduler = new InputScheduler(new Win32KeyboardEmitter());

        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Finished += (_, _) => finished.TrySetResult();

        scheduler.Start(
            built.Units,
            () => new SpeedProfile(intervalMs, LineExtraDelayMs: lineExtraDelayMs));

        await finished.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// 找出启动后**新出现**的可见窗口并把它置为前台（并验证成功）。
    /// 控制台窗口属于 conhost / Windows Terminal，因此不能按进程找。
    /// </summary>
    private static async Task<bool> FocusNewWindowAsync(IReadOnlySet<IntPtr> existing, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            foreach (var handle in WindowEnumerator.GetVisibleTopLevelWindows())
            {
                if (existing.Contains(handle) || WindowEnumerator.GetTitle(handle).Length == 0)
                {
                    continue;
                }

                if (WindowEnumerator.TryActivate(handle))
                {
                    return true;
                }
            }

            await Task.Delay(300, TestContext.Current.CancellationToken);
        }

        return false;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        return condition();
    }

    // ── WSL 侧的验证辅助 ────────────────────────────────────────────────
    // 用 wsl.exe 直接读发行版内的文件，作为"命令真的在 Linux 侧执行了"的证据；
    // 比读取 \\wsl$ UNC 或终端画面都更稳定。

    private static async Task<string?> WaitForWslFileAsync(string linuxPath, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var content = await TryReadWslFileAsync(linuxPath);
            if (content is not null)
            {
                return content;
            }

            await Task.Delay(300, TestContext.Current.CancellationToken);
        }

        return await TryReadWslFileAsync(linuxPath);
    }

    private static async Task<string?> TryReadWslFileAsync(string linuxPath)
    {
        var startInfo = new ProcessStartInfo("wsl.exe", $"-d {OpenEulerDistro} -- cat {linuxPath}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);

        // stderr 必须一并读掉，否则 WSL 的代理警告写满管道会导致死锁
        _ = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        var output = await stdout;
        return process.ExitCode == 0 && output.Length > 0 ? output : null;
    }

    private static async Task DeleteWslFileAsync(string linuxPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo("wsl.exe", $"-d {OpenEulerDistro} -- rm -f {linuxPath}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return;
            }

            _ = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            _ = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

            using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (Exception)
        {
            // 清理失败不影响验证结论
        }
    }

    private static async Task TryExitAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                // 试着优雅退出；失败就直接结束进程树
                await TypeAsync("exit\n", 30, 100);
                process.WaitForExit(3000);
            }
        }
        catch (Exception)
        {
            // 退出失败不影响验证结论
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
}
