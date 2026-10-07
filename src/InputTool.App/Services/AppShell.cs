using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using InputTool.App.Commands;
using InputTool.App.ViewModels;
using InputTool.App.Views;
using InputTool.Core.Application;
using InputTool.Core.Configuration;
using InputTool.Core.Hotkeys;
using InputTool.Core.Injection;
using InputTool.Core.Logging;
using InputTool.Core.Scheduling;

namespace InputTool.App.Services;

/// <summary>
/// 应用外壳：装配主窗口、托盘、浮层、设置窗口、会话控制器、全局热键、
/// 配置持久化、日志、单实例与提权入口。
/// </summary>
public sealed class AppShell : IDisposable
{
    private readonly MainViewModel _main;
    private readonly MainWindow _mainWindow;
    private readonly OverlayWindow _overlay;
    private readonly IStepLinePresenter _stepLinePresenter;
    private readonly TrayService _tray;
    private readonly InputSessionController _controller;
    private readonly Win32KeyboardEmitter _emitter;
    private readonly IConfigService _config;
    private readonly IElevationService _elevation;
    private readonly ISingleInstanceService _singleInstance;
    private readonly IAppLogger _logger;
    private readonly AppSettingsBinder _binder;
    private readonly string? _carryFilePath;

    private Win32HotkeyService? _hotkeys;
    private SettingsWindow? _settingsWindow;
    private FullTextViewWindow? _fullTextWindow;
    private bool _exiting;

    /// <summary>装配整个应用界面。</summary>
    /// <param name="config">配置服务。</param>
    /// <param name="logger">日志。</param>
    /// <param name="singleInstance">单实例服务。</param>
    /// <param name="elevation">提权服务；省略时自建。</param>
    /// <param name="carryFilePath">提权重启时携带的文件路径（FR-027）。</param>
    public AppShell(
        IConfigService config,
        IAppLogger logger,
        ISingleInstanceService singleInstance,
        IElevationService? elevation = null,
        string? carryFilePath = null)
    {
        _config = config;
        _logger = logger;
        _singleInstance = singleInstance;
        _elevation = elevation ?? new ElevationService(logger: logger);
        _carryFilePath = carryFilePath;

        _overlay = new OverlayWindow();
        _emitter = new Win32KeyboardEmitter();

        // 步进等待时把「下一行」显示到浮层上（与倒计时共用同一个浮层窗口，靠模式隔离）
        _stepLinePresenter = new OverlayStepLinePresenter(_overlay);

        _controller = new InputSessionController(
            emitter: _emitter,
            probe: new InjectabilityProbe(),
            countdown: new OverlayCountdownPresenter(_overlay),
            logger: logger);

        // 控制器只管"下一行是什么"，怎么画由呈现器决定
        _controller.StepLineChanged += OnStepLineChanged;

        _main = new MainViewModel(new DocumentPipeline(), _controller)
        {
            IsAdminMode = _elevation.IsElevated,
        };

        _mainWindow = new MainWindow(_main);

        _tray = new TrayService(new TrayMenuActions(
            StartPause: _main.StartPauseToggleCommand,
            EmergencyStop: _main.StopCommand,
            ShowMainWindow: new RelayCommand(_ => ShowMainWindow()),
            OpenSettings: _main.OpenSettingsCommand,
            OpenLogs: _main.OpenLogsCommand,
            About: _main.AboutCommand,
            Exit: new RelayCommand(_ => Exit())));

        _main.OpenSettingsRequested += (_, _) => ShowSettings();
        _main.OpenAboutRequested += (_, _) => ShowSettings();
        _main.ExitRequested += (_, _) => Exit();
        _main.RestartElevatedRequested += (_, _) => RestartElevated();
        _main.OpenLogsRequested += (_, _) => OpenLogDirectory();
        _main.ChooseTargetWindowRequested += (_, _) => ShowTargetWindowPicker();
        _main.DocumentLoaded += OnDocumentLoaded;
        _main.ShowFullTextRequested += (_, _) => ShowFullTextWindow();
        _main.SessionCompleted += OnSessionCompleted;
        _main.PropertyChanged += OnMainPropertyChanged;
        _mainWindow.Closing += OnMainWindowClosing;
        _mainWindow.SourceInitialized += (_, _) => InitializeWindowIntegration();

        // 配置 → 界面；之后界面改动会自动写回配置（1 秒去抖）
        _binder = new AppSettingsBinder(_main, _config, _logger);
        _binder.Apply();

        _singleInstance.ActivationRequested += (_, _) => ShowMainWindow();

        UpdateTrayStatus();
    }

    /// <summary>显示主窗口并完成启动收尾（含提权携带文件）。</summary>
    public void Start()
    {
        ShowMainWindow();
        UpdateTrayStatus();

        _logger.Info(
            $"启动完成：便携模式={_config.IsPortableMode}，配置={_config.ConfigFilePath}，"
            + $"管理员={_elevation.IsElevated}",
            "Startup");

        // 提权重启时把用户原来在看的文件带回来（FR-027）；否则恢复上次打开的文件（FR-018.2）
        if (!string.IsNullOrWhiteSpace(_carryFilePath))
        {
            _ = _main.OpenFileAsync(_carryFilePath);
        }
        else if (!string.IsNullOrWhiteSpace(_config.Current.File.Path)
                 && File.Exists(_config.Current.File.Path))
        {
            _ = _main.OpenFileAsync(_config.Current.File.Path);
        }
    }

    /// <summary>显示并激活主窗口（托盘双击、第二实例激活请求都走这里）。</summary>
    public void ShowMainWindow()
    {
        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
    }

    /// <summary>退出程序（§9.3 约定 6 的顺序）。</summary>
    public void Exit()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _logger.Info("开始退出流程", "Shutdown");

        // 1) 停止会话（补发抬起）2) 注销热键
        _controller.Stop();
        _hotkeys?.Dispose();

        // 3) 刷日志 4) 保存配置 5) 释放单实例
        try
        {
            _binder.Capture();
            _config.SaveNowAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.Warn($"退出时保存配置失败：{ex.Message}", "Shutdown");
        }

        _logger.Info("退出完成", "Shutdown");
        _logger.Flush();

        _singleInstance.Dispose();

        _overlay.HideOverlay();
        _overlay.Close();
        _tray.Dispose();
        _mainWindow.Close();

        Application.Current?.Shutdown();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _binder.Dispose();
        _hotkeys?.Dispose();
        _tray.Dispose();
        _overlay.Close();
        _logger.Flush();
    }

    // ── 窗口消息：全局热键 + 第二实例激活（§5.6 / §5.9.3）─────────────

    private void InitializeWindowIntegration()
    {
        var handle = new WindowInteropHelper(_mainWindow).Handle;
        if (handle == IntPtr.Zero)
        {
            _logger.Warn("拿不到主窗口句柄，全局热键与单实例激活将不可用", "Startup");
            return;
        }

        // Core 不引用 WPF，因此由这里安装消息钩子，把消息转交给各个服务
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);

        _hotkeys = new Win32HotkeyService(handle, logger: _logger);
        _hotkeys.Pressed += OnHotkeyPressed;
        _hotkeys.RegisterDefaults();

        // 配置里的热键优先于内置默认值（原先只调 RegisterDefaults，
        // 导致设置里改的热键永远不生效——既有缺陷）
        ApplyConfiguredHotkeys();

        // 回显实际生效的热键（可能因冲突回退到备用组合，SRS 附录 B）
        _main.StartPauseHotkey = DisplayGesture(HotkeyRole.StartPause, "F9");
        _main.EmergencyStopHotkey = DisplayGesture(HotkeyRole.EmergencyStop, "F10");
        _main.SpeedUpHotkey = DisplayGesture(HotkeyRole.SpeedUp, "Ctrl+Alt+Up");
        _main.SpeedDownHotkey = DisplayGesture(HotkeyRole.SpeedDown, "Ctrl+Alt+Down");

        // 急停热键注册失败 → 阻断式警告：在成功注册前不允许开始输入（FR-014.5）
        if (!_hotkeys.EmergencyStopAvailable)
        {
            _logger.Warn("紧急停止热键注册失败", "Hotkey");
            _main.ShowError(
                "紧急停止热键不可用",
                "紧急停止热键注册失败（可能被其他程序占用）。在成功注册之前不允许开始输入，这是安全要求。",
                "请在设置中为「紧急停止」更换一个热键后重试（FR-014.5）。");
        }
    }

    /// <summary>
    /// 用配置里的热键覆盖内置默认值。
    /// </summary>
    /// <remarks>
    /// 关键安全约束：**任何一步失败都必须把原来的绑定恢复回去**。
    /// 尤其是急停键——它一旦被注销而新键又注册不上，用户就失去了随时停止的能力，
    /// 而按 FR-014.5 程序还会拒绝开始输入，等于把人卡死。
    /// 所以这里的策略是"先注册成功才生效，失败就回滚"。
    /// </remarks>
    private void ApplyConfiguredHotkeys()
    {
        if (_hotkeys is null)
        {
            return;
        }

        var configured = _config.Current.Hotkeys;

        ApplyConfiguredHotkey(HotkeyRole.StartPause, configured.StartPause);
        ApplyConfiguredHotkey(HotkeyRole.EmergencyStop, configured.EmergencyStop);
        ApplyConfiguredHotkey(HotkeyRole.SpeedUp, configured.SpeedUp);
        ApplyConfiguredHotkey(HotkeyRole.SpeedDown, configured.SpeedDown);
    }

    private void ApplyConfiguredHotkey(HotkeyRole role, string configuredText)
    {
        if (_hotkeys is null
            || !_hotkeys.Bindings.TryGetValue(role, out var current)
            || !HotkeyGesture.TryParse(configuredText, out var desired)
            || desired == current.Gesture)
        {
            // 没配、解析不了、或与当前一致：保持现状（默认值可用，绝不动它）
            return;
        }

        var previous = current.Gesture;
        var result = _hotkeys.Register(role, desired);

        if (result.Status == HotkeyRegistrationStatus.Success)
        {
            _logger.Info($"热键已按配置生效：{role} → {desired}", "Hotkey");
            return;
        }

        // 回滚：宁可回到能用的默认键，也不留一个没有热键的角色
        _hotkeys.Register(role, previous);
        _logger.Warn(
            $"配置的热键 {role}={configuredText} 注册失败（{result.Message}），已保留 {previous}",
            "Hotkey");

        if (role == HotkeyRole.EmergencyStop)
        {
            _main.ShowError(
                "紧急停止热键未能按配置生效",
                $"你配置的「{configuredText}」注册失败，当前仍使用「{previous}」。",
                "请在设置里换一个组合，或点「恢复默认热键」。");
        }
    }

    private string DisplayGesture(HotkeyRole role, string fallback)
        => _hotkeys is not null && _hotkeys.Bindings.TryGetValue(role, out var binding)
            ? binding.Gesture.ToString()
            : fallback;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // 第二实例请求激活：把窗口带到前面来
        if (_singleInstance.TryHandleWindowMessage(msg, wParam))
        {
            handled = true;
            return IntPtr.Zero;
        }

        if (_hotkeys?.TryHandleWindowMessage(msg, wParam) == true)
        {
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void OnHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
    {
        switch (e.Role)
        {
            case HotkeyRole.StartPause:
                _main.StartPauseToggleCommand.Execute(null);
                break;

            case HotkeyRole.EmergencyStop:
                // 急停必须最短路径：直接打到控制器，不等界面
                _controller.Stop();
                break;

            case HotkeyRole.SpeedUp:
                _main.AdjustInterval(+10);
                break;

            case HotkeyRole.SpeedDown:
                _main.AdjustInterval(-10);
                break;
        }
    }

    // ── 步进时显示下一行（FR 新增）──────────────────────────────────────

    /// <summary>
    /// 步进等待状态变化：把「下一行」显示到浮层上，离开等待时收起。
    /// </summary>
    /// <remarks>
    /// 控制器保证该事件回到界面线程，因此这里可以直接碰 WPF 元素。
    /// </remarks>
    private void OnStepLineChanged(object? sender, StepLineChangedEventArgs e)
    {
        if (e.IsWaiting)
        {
            _stepLinePresenter.Show(e.LineNumber, e.DisplayText);
        }
        else
        {
            _stepLinePresenter.Hide();
        }
    }

    // ── 会话结果提示（FR-017）──────────────────────────────────────────

    private void OnSessionCompleted(object? sender, SessionStatistics statistics)
    {
        var title = statistics.Outcome switch
        {
            SessionOutcome.Completed => "输入完成",
            SessionOutcome.Stopped => "已停止",
            _ => "输入中断",
        };

        _logger.Info($"{title}：{statistics.ToFinishMessage()}", "Session");
        _tray.ShowBalloon(title, statistics.ToFinishMessage());
    }

    // ── 查看完整内容（FR-005.1）───────────────────────────────────────

    private void ShowFullTextWindow()
    {
        if (_fullTextWindow is { IsVisible: true })
        {
            _fullTextWindow.Activate();
            return;
        }

        var text = _main.FullText;

        if (text.Length == 0)
        {
            _main.StatusText = "还没有加载文件。";
            return;
        }

        _fullTextWindow = new FullTextViewWindow(text) { Owner = _mainWindow };
        _fullTextWindow.Closed += (_, _) => _fullTextWindow = null;
        _fullTextWindow.Show();

        _logger.Info($"打开完整内容窗口：{text.Length} 字符", "File");
    }

    // ── 文件加载（FR-021.2 日志 / FR-018.3 最近文件）──────────────────

    private void OnDocumentLoaded(object? sender, LoadedDocument document)
    {
        var statistics = document.Statistics;

        // FR-021.2 要求记录：路径、编码、字符数
        _logger.Info(
            $"文件已加载：{document.Path}"
            + $"（编码={document.EncodingName}，依据={document.EncodingReason}，"
            + $"字符={statistics.CharCount}，行={statistics.LineCount}，"
            + $"单元={document.Units.Count}，跳过={statistics.SkippedUnits}）",
            "File");

        if (document.Warnings.Count > 0)
        {
            _logger.Warn($"加载告警：{string.Join("；", document.Warnings)}", "File");
        }

        // FR-018.3：每次加载成功都记入最近文件
        _binder.RememberRecentFile(document.Path);
    }

    // ── 目标窗口选择（FR-012.4）───────────────────────────────────────

    private void ShowTargetWindowPicker()
    {
        var picker = new TargetWindowPickerWindow { Owner = _mainWindow };

        if (picker.ShowDialog() != true || picker.SelectedTarget is not { } target)
        {
            return;
        }

        _main.SetPickedTargetWindow(target.Handle, target.DisplayText);
        _logger.Info(
            $"目标窗口已锁定：{target.DisplayText}（handle=0x{target.Handle.ToInt64():X}，类={target.ClassName}）",
            "Session");
    }

    // ── 日志与提权（FR-021.4 / FR-027）─────────────────────────────────

    private void OpenLogDirectory()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_logger.LogDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Warn($"打开日志目录失败：{ex.Message}", "Log");
            _main.ShowError("无法打开日志目录", ex.Message, $"日志目录：{_logger.LogDirectory}");
        }
    }

    private void RestartElevated()
    {
        if (_elevation.IsElevated)
        {
            _main.StatusText = "当前已经是管理员权限，无需重启。";
            return;
        }

        _logger.Info("用户请求以管理员身份重启", "Elevation");

        // 先停会话并释放热键，避免两个进程抢同一组热键
        _controller.Stop();
        _hotkeys?.Dispose();
        _exiting = true;

        try
        {
            _binder.Capture();
            _config.SaveNowAsync().GetAwaiter().GetResult();
            _logger.Flush();
        }
        catch (Exception ex)
        {
            _logger.Warn($"提权前保存配置失败：{ex.Message}", "Elevation");
        }

        if (_elevation.RestartElevated(_main.FilePath))
        {
            _singleInstance.Dispose();
            _tray.Dispose();
            _overlay.Close();
            Application.Current?.Shutdown();
        }
        else
        {
            // 用户取消了 UAC：把程序恢复成可用状态（FR-027）
            _exiting = false;
            _main.StatusText = "已取消提权；紧急停止热键需要在重启后重新生效。";
            _main.ShowError(
                "已取消以管理员身份重启",
                "安全操作已取消，程序继续以当前权限运行。",
                "如果目标窗口以管理员身份运行，需要以管理员身份重启本工具才能注入。");

            if (_mainWindow.IsVisible)
            {
                InitializeWindowIntegration();
            }
        }
    }

    // ── 设置窗口（FR-018 / FR-025）────────────────────────────────────

    private void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(new SettingsViewModel(_main))
        {
            Owner = _mainWindow,
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    // ── 关闭到托盘（FR-019.1 / E-09）─────────────────────────────────

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        if (!_main.CloseToTray)
        {
            Exit();
            return;
        }

        e.Cancel = true;
        _mainWindow.Hide();
        _tray.ShowBalloon("Input Tool 仍在运行", "程序已最小化到托盘：双击托盘图标可重新打开，右键菜单可退出。");
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.State) or nameof(MainViewModel.ProgressPercent))
        {
            UpdateTrayStatus();
        }
    }

    private void UpdateTrayStatus()
    {
        var status = _main.State switch
        {
            InputSessionState.Typing => $"输入中 {_main.ProgressPercent:F0}%",
            InputSessionState.Paused => "已暂停",
            InputSessionState.Countdown => "倒计时中",
            _ => "空闲",
        };

        _tray.UpdateStatus(status);
    }
}
