using System.Windows;
using System.Windows.Threading;
using InputTool.App.Diagnostics;
using InputTool.App.Services;
using InputTool.App.Themes;
using InputTool.Core.Application;
using InputTool.Core.Configuration;
using InputTool.Core.Injection;
using InputTool.Core.Logging;

namespace InputTool.App;

/// <summary>
/// 应用入口。启动序列对应《软件开发设计文档》§5.9.4。
/// </summary>
/// <remarks>
/// 顺序是有讲究的：
/// 1. 预览模式最先判断——它不该碰配置、不该占单实例；
/// 2. **日志最先就绪**：后面每一步都可能需要留痕；
/// 3. 崩溃残留清理：补发一次修饰键抬起，防止上次异常退出把 Ctrl/Shift 卡住（FR-007.7）；
/// 4. 单实例检查（FR-022）：不是第一个实例就广播激活并立刻退出；
/// 5. 配置（FR-018）：用配置里的级别重置日志、用配置里的主题覆盖默认；
/// 6. 全局异常处理（FR-024）；
/// 7. 装配外壳。
/// </remarks>
public partial class App : Application
{
    private AppShell? _shell;
    private FileLogger? _logger;
    private JsonConfigService? _config;
    private SingleInstanceService? _singleInstance;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── 1. 无头预览模式：渲染界面截图后立即退出（UI 走查与视觉回归）──
        if (UiPreviewRenderer.IsPreviewRequested(e.Args))
        {
            ThemeManager.Apply(UiPreviewRenderer.ParseTheme(e.Args));
            UiPreviewRenderer.RenderToFile(
                UiPreviewRenderer.GetOutputPath(e.Args),
                UiPreviewRenderer.ParseView(e.Args),
                UiPreviewRenderer.GetInputFile(e.Args));
            Shutdown();
            return;
        }

        // ── 2. 日志 ──
        _logger = new FileLogger();
        _logger.Info($"应用启动：{Environment.ProcessPath}", "Startup");

        // ── 3. 崩溃残留清理：补发一次修饰键抬起（FR-007.7）──
        try
        {
            new Win32KeyboardEmitter().ReleaseModifiers();
        }
        catch (Exception ex)
        {
            _logger.Warn($"启动时清理修饰键失败：{ex.Message}", "Startup");
        }

        // ── 4. 单实例（FR-022）──
        _singleInstance = new SingleInstanceService(logger: _logger);

        if (!_singleInstance.IsFirstInstance)
        {
            _singleInstance.TryRequestActivation();
            _logger.Info("已有实例在运行，已请求激活该实例并退出", "Startup");
            _logger.Flush();
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        // ── 5. 配置（FR-018）──
        _config = new JsonConfigService(
            overrideDirectory: HasFlag(e.Args, "--portable") ? AppContext.BaseDirectory : null,
            logger: _logger);

        try
        {
            _config.LoadAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // 配置再重要也不能挡住启动
            _logger.Error("加载配置失败，本次以默认值运行", ex, "Config");
        }

        ApplyLoggingConfig(_config.Current.Logging);

        var theme = HasFlag(e.Args, "--theme")
            ? UiPreviewRenderer.ParseTheme(e.Args)
            : ToAppTheme(_config.Current.Ui.Theme);
        ThemeManager.Apply(theme);

        // ── 6. 全局异常处理（FR-024）──
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        // ── 7. 外壳 ──
        // 装配失败必须立刻退出：此时既没有主窗口也没有托盘图标，
        // 若只把异常标记为已处理，会留下一个"没有界面却还在运行"的僵尸进程
        // （还会占着自己的 exe/dll，导致用户之后双击打不开程序）。
        try
        {
            _shell = new AppShell(
                _config,
                _logger,
                _singleInstance,
                elevation: new ElevationService(logger: _logger),
                carryFilePath: GetOptionValue(e.Args, "--carry-file"));

            _shell.Start();
        }
        catch (Exception ex)
        {
            _logger.Error("启动装配失败，程序将退出", ex, "Startup");
            _logger.Flush();
            MessageBox.Show(
                $"程序启动失败，即将退出。\n\n{ex.Message}\n\n日志：{_logger.LogDirectory}",
                "Input Tool 启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _shell?.Dispose();

        _logger?.Info($"应用退出，退出码 {e.ApplicationExitCode}", "Shutdown");
        _logger?.Flush();
        _logger?.Dispose();

        _config?.Dispose();
        _singleInstance?.Dispose();

        base.OnExit(e);
    }

    // ── 内部实现 ────────────────────────────────────────────────────────

    private void ApplyLoggingConfig(LoggingConfig config)
    {
        if (_logger is null)
        {
            return;
        }

        if (!config.Enabled)
        {
            // 关闭日志：只留 Error，避免完全无声
            _logger.SetMinimumLevel(LogLevel.Error);
            return;
        }

        _logger.SetMinimumLevel(Enum.TryParse<LogLevel>(config.Level, ignoreCase: true, out var level)
            ? level
            : LogLevel.Info);
        _logger.SetRetentionDays(config.RetentionDays);
    }

    private static AppTheme ToAppTheme(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => AppTheme.Light,
        ThemeMode.Dark => AppTheme.Dark,
        _ => AppTheme.System,
    };

    private static bool HasFlag(string[] args, string flag)
        => Array.Exists(args, arg => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase));

    /// <summary>取 <c>--name value</c> 形式的参数值。</summary>
    private static string? GetOptionValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error("界面线程发生未处理异常", e.Exception, "Crash");

        // 收尾：把修饰键放开，避免用户被卡在按下状态（FR-007.7）
        try
        {
            new Win32KeyboardEmitter().ReleaseModifiers();
        }
        catch (Exception ex)
        {
            _logger?.Warn($"异常收尾时释放修饰键失败：{ex.Message}", "Crash");
        }

        _logger?.Flush();

        if (_shell is null)
        {
            // 外壳还没装配好（甚至还没创建）就崩了：直接退出，别留僵尸进程
            _logger?.Error("启动阶段发生未处理异常，程序退出", e.Exception, "Crash");
            Shutdown(1);
            return;
        }

        _shell.Exit();
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _logger?.Error("进程发生未处理异常", e.ExceptionObject as Exception, "Crash");
        _logger?.Flush();
    }
}
