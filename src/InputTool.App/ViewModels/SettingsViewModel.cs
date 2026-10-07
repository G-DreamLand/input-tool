using System.Reflection;
using System.Windows.Input;
using InputTool.App.Commands;
using InputTool.App.Themes;
using InputTool.Core.Hotkeys;

namespace InputTool.App.ViewModels;

/// <summary>
/// 设置窗口 ViewModel。对应《软件开发设计文档》§6.5。
/// </summary>
/// <remarks>
/// 与主窗口共用的设置项（文本行为、倒计时、热键显示、提示音等）**直接委托**给
/// <see cref="MainViewModel"/>，避免同一份设置在两个 ViewModel 里各存一份而失同步。
/// 仅设置窗口独有的项（主题、日志、键盘模式、输入上限）在这里持有。
/// 待 <c>IConfigService</c> 落地后，两者都改为读写 <c>AppConfig</c>。
/// </remarks>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    private int _themeIndex;
    private int _keyboardModeIndex;
    private bool _clearTargetFirst;
    private int _logLevelIndex = 1;
    private int _logRetentionDays = 7;
    private bool _logContent;
    private int _maxUnitsPerRun = 100_000;
    private int _languageIndex;

    private HotkeyGesture? _startPauseGesture;
    private HotkeyGesture? _emergencyStopGesture;
    private HotkeyGesture? _speedUpGesture;
    private HotkeyGesture? _speedDownGesture;
    private bool _hasHotkeyError;
    private string _hotkeyMessage = string.Empty;

    /// <summary>构造设置窗口 ViewModel。</summary>
    /// <param name="main">主窗口 ViewModel（共享设置项的来源）。</param>
    public SettingsViewModel(MainViewModel main)
    {
        _main = main ?? throw new ArgumentNullException(nameof(main));

        // 从主窗口现有设置初始化热键（配置服务落地后改为读 AppConfig）
        _startPauseGesture = HotkeyGesture.TryParse(_main.StartPauseHotkey, out var g1) ? g1 : HotkeyGesture.DefaultStartPause;
        _emergencyStopGesture = HotkeyGesture.TryParse(_main.EmergencyStopHotkey, out var g2) ? g2 : HotkeyGesture.DefaultEmergencyStop;
        _speedUpGesture = HotkeyGesture.TryParse(_main.SpeedUpHotkey, out var g3) ? g3 : HotkeyGesture.DefaultSpeedUp;
        _speedDownGesture = HotkeyGesture.TryParse(_main.SpeedDownHotkey, out var g4) ? g4 : HotkeyGesture.DefaultSpeedDown;

        ResetHotkeysCommand = new RelayCommand(_ => ResetHotkeys());
        ResetAllCommand = new RelayCommand(_ => ResetAllSettings());
        OpenLogsCommand = new RelayCommand(_ => OpenLogs());
        CloseCommand = new RelayCommand(_ => RequestClose?.Invoke(this, EventArgs.Empty));

        ValidateHotkeys();
    }

    /// <summary>请求关闭窗口。</summary>
    public event EventHandler? RequestClose;

    /// <summary>设置已变更（用于主窗口即时刷新）。</summary>
    public event EventHandler? SettingsChanged;

    // ── 常规 ────────────────────────────────────────────────────────────

    /// <summary>主题下拉项（§6.6）。</summary>
    public IReadOnlyList<string> Themes { get; } = new[] { "跟随系统", "浅色", "深色" };

    /// <summary>主题选中项；变更后立即应用。</summary>
    public int ThemeIndex
    {
        get => _themeIndex;
        set
        {
            if (SetProperty(ref _themeIndex, value))
            {
                ThemeManager.Apply(value switch
                {
                    1 => AppTheme.Light,
                    2 => AppTheme.Dark,
                    _ => AppTheme.System,
                });
            }
        }
    }

    /// <summary>界面语言（en-US 为预留，见 SRS §6.2）。</summary>
    public IReadOnlyList<string> Languages { get; } = new[] { "简体中文（zh-CN）", "English（en-US，预留）" };

    /// <summary>语言选中项。</summary>
    public int LanguageIndex
    {
        get => _languageIndex;
        set => SetProperty(ref _languageIndex, value);
    }

    /// <summary>关闭时最小化到托盘（FR-019.1）。</summary>
    public bool CloseToTray
    {
        get => _main.CloseToTray;
        set
        {
            if (_main.CloseToTray != value)
            {
                _main.CloseToTray = value;
                OnPropertyChanged();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>完成提示音（FR-017）。</summary>
    public bool PlayFinishSound
    {
        get => _main.PlayFinishSound;
        set
        {
            if (_main.PlayFinishSound != value)
            {
                _main.PlayFinishSound = value;
                OnPropertyChanged();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    // ── 热键（FR-025）──────────────────────────────────────────────────

    /// <summary>开始/暂停热键。</summary>
    public HotkeyGesture? StartPauseGesture
    {
        get => _startPauseGesture;
        set
        {
            if (SetProperty(ref _startPauseGesture, value))
            {
                ValidateHotkeys();
            }
        }
    }

    /// <summary>紧急停止热键。</summary>
    public HotkeyGesture? EmergencyStopGesture
    {
        get => _emergencyStopGesture;
        set
        {
            if (SetProperty(ref _emergencyStopGesture, value))
            {
                ValidateHotkeys();
            }
        }
    }

    /// <summary>速度加快热键。</summary>
    public HotkeyGesture? SpeedUpGesture
    {
        get => _speedUpGesture;
        set
        {
            if (SetProperty(ref _speedUpGesture, value))
            {
                ValidateHotkeys();
            }
        }
    }

    /// <summary>速度减慢热键。</summary>
    public HotkeyGesture? SpeedDownGesture
    {
        get => _speedDownGesture;
        set
        {
            if (SetProperty(ref _speedDownGesture, value))
            {
                ValidateHotkeys();
            }
        }
    }

    /// <summary>热键校验是否失败（失败时界面显示红色提示，且不允许生效）。</summary>
    public bool HasHotkeyError
    {
        get => _hasHotkeyError;
        private set => SetProperty(ref _hasHotkeyError, value);
    }

    /// <summary>热键校验提示文本。</summary>
    public string HotkeyMessage
    {
        get => _hotkeyMessage;
        private set => SetProperty(ref _hotkeyMessage, value);
    }

    // ── 文本（FR-004 / SRS §5.1.6）─────────────────────────────────────

    /// <summary>默认编码下拉（与主窗口一致）。</summary>
    public IReadOnlyList<string> EncodingModes => _main.EncodingModes;

    /// <summary>默认编码选中项。</summary>
    public int DefaultEncodingIndex
    {
        get => _main.SelectedEncodingIndex;
        set
        {
            if (_main.SelectedEncodingIndex != value)
            {
                _main.SelectedEncodingIndex = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>解析转义序列（默认关闭）。</summary>
    public bool ParseEscapes
    {
        get => _main.ParseEscapes;
        set
        {
            if (_main.ParseEscapes != value)
            {
                _main.ParseEscapes = value;
                OnPropertyChanged();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>忽略空行。</summary>
    public bool IgnoreEmptyLines
    {
        get => _main.IgnoreEmptyLines;
        set
        {
            if (_main.IgnoreEmptyLines != value)
            {
                _main.IgnoreEmptyLines = value;
                OnPropertyChanged();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>跳过以 # 开头的行。</summary>
    public bool SkipCommentLines
    {
        get => _main.SkipCommentLines;
        set
        {
            if (_main.SkipCommentLines != value)
            {
                _main.SkipCommentLines = value;
                OnPropertyChanged();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>末尾补回车（Q-01）。</summary>
    public bool AppendTrailingEnter
    {
        get => _main.AppendTrailingEnter;
        set
        {
            if (_main.AppendTrailingEnter != value)
            {
                _main.AppendTrailingEnter = value;
                OnPropertyChanged();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>单次输入单元上限（SRS §5.1.6）。</summary>
    public int MaxUnitsPerRun
    {
        get => _maxUnitsPerRun;
        set => SetProperty(ref _maxUnitsPerRun, value);
    }

    // ── 键盘（FR-011.4 / FR-026）──────────────────────────────────────

    /// <summary>键盘注入模式下拉。</summary>
    public IReadOnlyList<string> KeyboardModes { get; } = new[] { "Unicode 注入（推荐）", "兼容模式（虚拟键 + 修饰键）" };

    /// <summary>键盘模式选中项。</summary>
    public int KeyboardModeIndex
    {
        get => _keyboardModeIndex;
        set => SetProperty(ref _keyboardModeIndex, value);
    }

    /// <summary>输入前清理目标框内容（危险操作，默认关闭，FR-026）。</summary>
    public bool ClearTargetFirst
    {
        get => _clearTargetFirst;
        set => SetProperty(ref _clearTargetFirst, value);
    }

    // ── 高级（FR-021）──────────────────────────────────────────────────

    /// <summary>日志级别下拉。</summary>
    public IReadOnlyList<string> LogLevels { get; } = new[] { "Debug", "Info", "Warn", "Error" };

    /// <summary>日志级别选中项。</summary>
    public int LogLevelIndex
    {
        get => _logLevelIndex;
        set => SetProperty(ref _logLevelIndex, value);
    }

    /// <summary>日志保留天数（1–90）。</summary>
    public int LogRetentionDays
    {
        get => _logRetentionDays;
        set => SetProperty(ref _logRetentionDays, Math.Clamp(value, 1, 90));
    }

    /// <summary>是否记录输入内容摘要（需二次确认，FR-021.3 / Q-06）。</summary>
    public bool LogContent
    {
        get => _logContent;
        set => SetProperty(ref _logContent, value);
    }

    // ── 关于 ────────────────────────────────────────────────────────────

    /// <summary>版本号文本。</summary>
    public string VersionText
    {
        get
        {
            var assembly = typeof(SettingsViewModel).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return informational ?? assembly.GetName().Version?.ToString() ?? "1.0.0";
        }
    }

    /// <summary>关于信息。</summary>
    public string AboutText =>
        "键盘文本自动输入工具（Input Tool）\n" +
        "把文本文件的内容按可调节奏逐字输入到当前前台窗口。\n\n" +
        "• 完全离线运行，不联网、不采集任何数据（NFR-013 / NFR-014）\n" +
        "• 只读你显式选择的文件，不扫描磁盘、不修改源文件（C-02）\n" +
        "• 不记录输入内容；日志仅记录数量与位置索引\n" +
        "• 目标窗口以管理员身份运行时，需以管理员身份运行本工具（UIPI 限制）";

    // ── 命令 ────────────────────────────────────────────────────────────

    public ICommand ResetHotkeysCommand { get; }

    public ICommand ResetAllCommand { get; }

    public ICommand OpenLogsCommand { get; }

    public ICommand CloseCommand { get; }

    // ── 实现 ────────────────────────────────────────────────────────────

    /// <summary>校验四个热键：合法性 + 互不重复；失败时给出具体原因（FR-025.3）。</summary>
    public void ValidateHotkeys()
    {
        var entries = new (HotkeyRole Role, HotkeyGesture? Gesture, string Display)[]
        {
            (HotkeyRole.StartPause, _startPauseGesture, "开始/暂停"),
            (HotkeyRole.EmergencyStop, _emergencyStopGesture, "紧急停止"),
            (HotkeyRole.SpeedUp, _speedUpGesture, "速度加快"),
            (HotkeyRole.SpeedDown, _speedDownGesture, "速度减慢"),
        };

        foreach (var entry in entries)
        {
            if (entry.Gesture is null)
            {
                HasHotkeyError = true;
                HotkeyMessage = $"【{entry.Display}】尚未设置热键：请点击输入框后按下组合键。";
                return;
            }
        }

        var bindings = entries.ToDictionary(
            e => e.Role,
            e => new HotkeyBinding(e.Role, e.Gesture!, e.Display));

        foreach (var pair in bindings)
        {
            var result = HotkeyValidator.Validate(pair.Value.Gesture, pair.Key, bindings);
            if (!result.IsValid)
            {
                HasHotkeyError = true;
                HotkeyMessage = $"【{pair.Value.DisplayName}】{result.Reason}";
                return;
            }
        }

        HasHotkeyError = false;
        HotkeyMessage = "热键有效，冲突校验通过。";
        ApplyHotkeysToMain();
    }

    private void ApplyHotkeysToMain()
    {
        if (_startPauseGesture is not null) _main.StartPauseHotkey = _startPauseGesture.ToString();
        if (_emergencyStopGesture is not null) _main.EmergencyStopHotkey = _emergencyStopGesture.ToString();
        if (_speedUpGesture is not null) _main.SpeedUpHotkey = _speedUpGesture.ToString();
        if (_speedDownGesture is not null) _main.SpeedDownHotkey = _speedDownGesture.ToString();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ResetHotkeys()
    {
        StartPauseGesture = HotkeyGesture.DefaultStartPause;
        EmergencyStopGesture = HotkeyGesture.DefaultEmergencyStop;
        SpeedUpGesture = HotkeyGesture.DefaultSpeedUp;
        SpeedDownGesture = HotkeyGesture.DefaultSpeedDown;
    }

    private void ResetAllSettings()
    {
        // TODO(M3): 接入 IConfigService.ResetToDefaultsAsync() 后改为真正的持久化重置（FR-018.5）
        ResetHotkeys();
        ThemeIndex = 0;
        KeyboardModeIndex = 0;
        ClearTargetFirst = false;
        LogLevelIndex = 1;
        LogRetentionDays = 7;
        LogContent = false;
        MaxUnitsPerRun = 100_000;
        ParseEscapes = false;
        IgnoreEmptyLines = false;
        SkipCommentLines = false;
        AppendTrailingEnter = false;
        CloseToTray = true;
        PlayFinishSound = true;
    }

    private void OpenLogs()
    {
        // 统一走主 ViewModel 的那条路：它会抛 OpenLogsRequested，由外壳用真实的日志目录打开（FR-021.4）
        _main.OpenLogsCommand.Execute(null);
    }
}
