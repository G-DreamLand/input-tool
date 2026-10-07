using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using InputTool.App.Commands;
using InputTool.Core.Abstractions;
using InputTool.Core.Application;
using InputTool.Core.Configuration;
using InputTool.Core.Files;
using InputTool.Core.Parsing;
using InputTool.Core.Scheduling;
using Microsoft.Win32;

namespace InputTool.App.ViewModels;

/// <summary>
/// 主窗口 ViewModel。对应《软件开发设计文档》§6.1 / §6.2 / §4.3。
/// </summary>
/// <remarks>
/// 硬约束（§6.2 数据流约束）：本类不得直接调用 SendInput、不得直接读文件、
/// 不得直接写 JSON——所有副作用都必须经 <c>InputTool.Core</c> 的契约完成。
/// 现状：文档加载走 <c>DocumentPipeline</c>，开始/暂停/停止走 <c>IInputSessionController</c>，
/// 配置持久化由外壳的 <c>AppSettingsBinder</c> 监听本类的属性变化完成；
/// 需要应用层能力（打开日志目录、提权重启）的地方，只发事件、由外壳处理。
/// </remarks>
public sealed class MainViewModel : ViewModelBase
{
    private const int PreviewCharLimit = 500;
    private const int PreviewLineLimit = 40;

    private InputSessionState _state = InputSessionState.Idle;
    private string _filePath = string.Empty;
    private int _selectedEncodingIndex;
    private string _encodingName = "自动";
    private string _statisticsText = "字符: 0    行: 0    预估: --";
    private bool _hasPreview;
    private int _intervalMs = 200;
    private int _selectedSpeedUnitIndex;
    private double _jitterPercent;
    private int _lineExtraDelayMs;
    private int _preEnterDelayMs;
    private int _countdownSeconds = 3;
    private int _newlineStrategyIndex;
    private int _focusGuardActionIndex;
    private bool _appendTrailingEnter;
    private bool _parseEscapes;
    private bool _ignoreEmptyLines;
    private bool _skipCommentLines;
    private bool _focusGuard;
    private bool _playFinishSound = true;
    private bool _closeToTray = true;
    private string _startPauseHotkey = "F9";
    private string _emergencyStopHotkey = "F10";
    private string _speedUpHotkey = "Ctrl+Alt+Up";
    private string _speedDownHotkey = "Ctrl+Alt+Down";
    private bool _isAdminMode;
    private double _progressPercent;
    private string _progressText = "0 / 0 (0.0%)";
    private string _timingText = "已用 --    剩余 --    预计完成 --";
    private string _targetWindowText = "未确定";
    private int _selectedTargetModeIndex;
    private IntPtr _pinnedTargetWindow = IntPtr.Zero;
    private string _documentInfoText = "未加载文件";
    private string _statusText = "就绪";
    private bool _hasError;
    private string _errorTitle = string.Empty;
    private string _errorMessage = string.Empty;
    private string _errorAdvice = string.Empty;

    private readonly DocumentPipeline? _pipeline;
    private readonly IInputSessionController? _controller;

    /// <summary>只读地取窗口标题用于回显（Core 契约，不引入新的副作用）。</summary>
    private static readonly ITargetWindowGateway TargetWindows = new Win32TargetWindowGateway();

    private LoadedDocument? _document;

    /// <summary>构造主窗口 ViewModel（不带文档管线与会话控制，仅用于 XAML 设计时与界面预览渲染）。</summary>
    public MainViewModel()
        : this(null, null)
    {
    }

    /// <summary>构造主窗口 ViewModel。</summary>
    /// <param name="pipeline">文档管线；为 null 时文件相关命令只回显提示。</param>
    /// <param name="controller">输入会话控制器；为 null 时开始/暂停/停止只回显提示。</param>
    public MainViewModel(DocumentPipeline? pipeline, IInputSessionController? controller = null)
    {
        _pipeline = pipeline;
        _controller = controller;

        if (_controller is not null)
        {
            _controller.StateChanged += OnSessionStateChanged;
            _controller.ProgressChanged += OnSessionProgressChanged;
            _controller.Notified += OnSessionNotified;
            _controller.Finished += OnSessionFinished;
        }

        PreviewLines = new ObservableCollection<PreviewLine>();

        BrowseFileCommand = new RelayCommand(_ => _ = BrowseFileAsync(), _ => CanEditDocument);
        ReloadFileCommand = new RelayCommand(_ => _ = ReloadFileAsync(), _ => CanEditDocument && FilePath.Length > 0);
        OpenContainingFolderCommand = new RelayCommand(_ => OpenContainingFolder(), _ => FilePath.Length > 0);
        RefreshPreviewCommand = new RelayCommand(_ => _ = ReloadFileAsync());
        ApplyPresetCommand = new RelayCommand(ApplyPreset);
        StartCommand = new RelayCommand(_ => Start(), _ => CanStart);
        PauseResumeCommand = new RelayCommand(_ => PauseResume(), _ => State is InputSessionState.Typing or InputSessionState.Paused);
        StopCommand = new RelayCommand(_ => Stop(), _ => State is InputSessionState.Typing or InputSessionState.Paused or InputSessionState.Countdown);
        CancelCountdownCommand = new RelayCommand(_ => CancelCountdown(), _ => State == InputSessionState.Countdown);
        ResetHotkeysCommand = new RelayCommand(_ => ResetHotkeys());
        RestartElevatedCommand = new RelayCommand(_ => RestartElevated());
        ChooseTargetWindowCommand = new RelayCommand(_ => ChooseTargetWindowRequested?.Invoke(this, EventArgs.Empty));
        ShowFullTextCommand = new RelayCommand(_ => ShowFullTextRequested?.Invoke(this, EventArgs.Empty));


        OpenLogsCommand = new RelayCommand(_ => OpenLogs());
        OpenSettingsCommand = new RelayCommand(_ => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
        AboutCommand = new RelayCommand(_ => OpenAboutRequested?.Invoke(this, EventArgs.Empty));
        ExitCommand = new RelayCommand(_ => ExitRequested?.Invoke(this, EventArgs.Empty));
        StartPauseToggleCommand = new RelayCommand(_ => StartPauseToggle());
        DismissErrorCommand = new RelayCommand(_ => HasError = false);
    }

    /// <summary>请求打开设置窗口（由外壳响应，界面不直接创建窗口，见 §6.2）。</summary>
    public event EventHandler? OpenSettingsRequested;

    /// <summary>请求打开"关于"。</summary>
    public event EventHandler? OpenAboutRequested;

    /// <summary>请求退出程序（托盘菜单与主窗口共用）。</summary>
    public event EventHandler? ExitRequested;

    /// <summary>一次会话结束（供外壳弹托盘气泡，FR-017）。</summary>
    public event EventHandler<SessionStatistics>? SessionCompleted;

    /// <summary>请求以管理员身份重启（FR-027）。</summary>
    public event EventHandler? RestartElevatedRequested;

    /// <summary>请求打开日志目录（FR-021.4）。</summary>
    public event EventHandler? OpenLogsRequested;

    /// <summary>请求打开目标窗口选择器（FR-012.4）。</summary>
    public event EventHandler? ChooseTargetWindowRequested;

    /// <summary>
    /// 文档加载成功。供外壳记录 FR-021.2 要求的加载日志（路径/编码/字符数）
    /// 与最近文件列表（FR-018.3）。
    /// </summary>
    public event EventHandler<LoadedDocument>? DocumentLoaded;

    /// <summary>请求打开"查看完整内容"窗口（FR-005.1）。</summary>
    public event EventHandler? ShowFullTextRequested;

    /// <summary>
    /// 当前文档的完整文本（供"查看完整内容"窗口使用）。
    /// 未加载文档时为空串。
    /// </summary>
    public string FullText => _document?.RawText ?? string.Empty;

    // ── ① 文本文件 ──────────────────────────────────────────────────────

    /// <summary>当前文件路径（FR-001）。</summary>
    public string FilePath
    {
        get => _filePath;
        set
        {
            if (SetProperty(ref _filePath, value))
            {
                RaiseCommandStates();
            }
        }
    }

    /// <summary>编码下拉项（FR-003.6）。</summary>
    public IReadOnlyList<string> EncodingModes { get; } = new[] { "自动", "UTF-8", "UTF-16 LE", "UTF-16 BE", "GBK", "GB18030", "ANSI" };

    /// <summary>编码下拉选中项；改动后立即用新编码重新解码（FR-003.6）。</summary>
    public int SelectedEncodingIndex
    {
        get => _selectedEncodingIndex;
        set
        {
            if (SetProperty(ref _selectedEncodingIndex, value) && _document is not null)
            {
                _ = LoadDocumentAsync(isReload: true);
            }
        }
    }

    /// <summary>实际判定出的编码显示名。</summary>
    public string EncodingName
    {
        get => _encodingName;
        set => SetProperty(ref _encodingName, value);
    }

    /// <summary>统计信息：字符数 / 行数 / 预估时长（FR-005.2）。</summary>
    public string StatisticsText
    {
        get => _statisticsText;
        set => SetProperty(ref _statisticsText, value);
    }

    /// <summary>预览行集合（前 500 字符，FR-005.1）。</summary>
    public ObservableCollection<PreviewLine> PreviewLines { get; }

    /// <summary>是否有预览内容（用于空状态提示）。</summary>
    public bool HasPreview
    {
        get => _hasPreview;
        set => SetProperty(ref _hasPreview, value);
    }

    // ── ② 输入速度 ──────────────────────────────────────────────────────

    /// <summary>节拍间隔 T（毫秒，10–5000）。</summary>
    public int IntervalMs
    {
        get => _intervalMs;
        set
        {
            if (SetProperty(ref _intervalMs, value))
            {
                OnPropertyChanged(nameof(CharactersPerSecondText));
                OnPropertyChanged(nameof(ShowFastModeHint));
                // FR-005.4：修改速度后预估时长实时刷新
                UpdateStatisticsText();
            }
        }
    }

    /// <summary>字符/秒显示（FR-006.5）。</summary>
    public string CharactersPerSecondText
        => IntervalMs > 0 ? $"{1000.0 / IntervalMs:F1} 字符/秒" : "-- 字符/秒";

    /// <summary>极快模式提示的可见性（T &lt; 20 ms，SRS §5.2.5）。</summary>
    public bool ShowFastModeHint => IntervalMs < 20;

    /// <summary>速度单位下拉项。</summary>
    public IReadOnlyList<string> SpeedUnits { get; } = new[] { "毫秒/字符", "字符/秒" };

    /// <summary>速度单位选中项。</summary>
    public int SelectedSpeedUnitIndex
    {
        get => _selectedSpeedUnitIndex;
        set => SetProperty(ref _selectedSpeedUnitIndex, value);
    }

    /// <summary>抖动比例（0–0.5）。</summary>
    public double JitterPercent
    {
        get => _jitterPercent;
        set
        {
            if (SetProperty(ref _jitterPercent, value))
            {
                OnPropertyChanged(nameof(JitterPercentText));
            }
        }
    }

    /// <summary>抖动百分比显示。</summary>
    public string JitterPercentText => $"{JitterPercent * 100:F0}%";

    /// <summary>行间隔额外延时 L（毫秒）。</summary>
    public int LineExtraDelayMs
    {
        get => _lineExtraDelayMs;
        set
        {
            if (SetProperty(ref _lineExtraDelayMs, value))
            {
                UpdateStatisticsText();
            }
        }
    }

    /// <summary>换行前延时 P（毫秒）。</summary>
    public int PreEnterDelayMs
    {
        get => _preEnterDelayMs;
        set
        {
            if (SetProperty(ref _preEnterDelayMs, value))
            {
                UpdateStatisticsText();
            }
        }
    }

    /// <summary>开始倒计时（秒）。</summary>
    public int CountdownSeconds
    {
        get => _countdownSeconds;
        set => SetProperty(ref _countdownSeconds, value);
    }

    // ── ③ 行为设置 ──────────────────────────────────────────────────────

    /// <summary>换行策略下拉项（FR-009）。</summary>
    public IReadOnlyList<string> NewlineStrategies { get; } = new[]
    {
        "Enter（默认）", "Shift+Enter", "Ctrl+Enter", "仅行末尾回车", "不发送回车",
    };

    /// <summary>换行策略选中项。</summary>
    public int NewlineStrategyIndex
    {
        get => _newlineStrategyIndex;
        set => SetProperty(ref _newlineStrategyIndex, value);
    }

    /// <summary>焦点守护动作下拉项（FR-020.3）。</summary>
    public IReadOnlyList<string> FocusGuardActions { get; } = new[] { "自动暂停（推荐）", "继续输入", "自动停止" };

    /// <summary>末尾补回车（Q-01：仅作用于文本末尾本就没有换行符的场景）。</summary>
    public bool AppendTrailingEnter
    {
        get => _appendTrailingEnter;
        set
        {
            if (SetProperty(ref _appendTrailingEnter, value))
            {
                // 勾上/取消都要重新评估"最后一行会不会被执行"
                RefreshTrailingEnterWarning();
            }
        }
    }

    /// <summary>解析转义序列（默认关闭，FR-004.3）。</summary>
    public bool ParseEscapes
    {
        get => _parseEscapes;
        set => SetProperty(ref _parseEscapes, value);
    }

    /// <summary>忽略空行（默认关闭）。</summary>
    public bool IgnoreEmptyLines
    {
        get => _ignoreEmptyLines;
        set => SetProperty(ref _ignoreEmptyLines, value);
    }

    /// <summary>跳过以 # 开头的行（默认关闭）。</summary>
    public bool SkipCommentLines
    {
        get => _skipCommentLines;
        set => SetProperty(ref _skipCommentLines, value);
    }

    /// <summary>目标窗口焦点守护（默认关闭，Q-04 决议）。</summary>
    private bool _stepMode;

    /// <summary>失焦提示条是否由焦点守护产生（决定能否自动收起）。</summary>
    private bool _errorFromFocusGuard;

    /// <summary>自动收起失焦提示条的计时器（3 秒）。</summary>
    private DispatcherTimer? _focusGuardNoticeTimer;

    /// <summary>
    /// 逐行步进：每行打完就停住，按步进热键（默认 F10）才输入下一行。
    /// 空行也算一步（脚本语义）。
    /// </summary>
    public bool StepMode
    {
        get => _stepMode;
        set
        {
            if (SetProperty(ref _stepMode, value))
            {
                ApplyFocusGuard();
            }
        }
    }

    /// <summary>是否启用目标窗口焦点守护（FR-020）。</summary>
    public bool FocusGuard
    {
        get => _focusGuard;
        set
        {
            if (SetProperty(ref _focusGuard, value))
            {
                ApplyFocusGuard();
            }
        }
    }

    /// <summary>失去焦点时的动作索引（0 自动暂停 / 1 继续输入 / 2 自动停止，与枚举顺序一致）。</summary>
    public int FocusGuardActionIndex
    {
        get => _focusGuardActionIndex;
        set
        {
            if (SetProperty(ref _focusGuardActionIndex, value))
            {
                ApplyFocusGuard();
            }
        }
    }

    /// <summary>把焦点守护设置推给会话控制器（FR-020.3）。</summary>
    private void ApplyFocusGuard()
    {
        if (_controller is null)
        {
            return;
        }

        _controller.FocusGuardEnabled = _focusGuard;
        _controller.FocusGuardAction = (FocusGuardAction)_focusGuardActionIndex;
        _controller.StepMode = _stepMode;
    }

    /// <summary>完成提示音。</summary>
    public bool PlayFinishSound
    {
        get => _playFinishSound;
        set => SetProperty(ref _playFinishSound, value);
    }

    /// <summary>关闭时最小化到托盘（FR-019.1）。</summary>
    public bool CloseToTray
    {
        get => _closeToTray;
        set => SetProperty(ref _closeToTray, value);
    }

    // ── ④ 热键 ──────────────────────────────────────────────────────────

    /// <summary>开始/暂停热键显示文本（FR-013）。</summary>
    public string StartPauseHotkey
    {
        get => _startPauseHotkey;
        set => SetProperty(ref _startPauseHotkey, value);
    }

    /// <summary>紧急停止热键显示文本（FR-014）。</summary>
    public string EmergencyStopHotkey
    {
        get => _emergencyStopHotkey;
        set => SetProperty(ref _emergencyStopHotkey, value);
    }

    /// <summary>速度加快热键显示文本（FR-028）。</summary>
    public string SpeedUpHotkey
    {
        get => _speedUpHotkey;
        set => SetProperty(ref _speedUpHotkey, value);
    }

    /// <summary>速度减慢热键显示文本（FR-028）。</summary>
    public string SpeedDownHotkey
    {
        get => _speedDownHotkey;
        set => SetProperty(ref _speedDownHotkey, value);
    }

    // ── ⑤ 控制与状态 ────────────────────────────────────────────────────

    /// <summary>会话状态（驱动按钮语义与可用性，§4.3）。</summary>
    public InputSessionState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(StartButtonText));
                OnPropertyChanged(nameof(PauseButtonText));
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsDocumentEditable));
                RaiseCommandStates();
            }
        }
    }

    /// <summary>状态显示文本。</summary>
    public string StateText => State switch
    {
        InputSessionState.Idle => "空闲",
        InputSessionState.Countdown => "倒计时中",
        InputSessionState.Typing => "输入中",
        InputSessionState.Paused => "已暂停",
        InputSessionState.Finished => "已完成",
        InputSessionState.Error => "出错",
        _ => "空闲",
    };

    /// <summary>【开始】按钮文案（Finished/Error 提示重新开始，T18/T19）。</summary>
    public string StartButtonText => State is InputSessionState.Finished or InputSessionState.Error
        ? "▶ 重新开始 (F9)"
        : "▶ 开始 (F9)";

    /// <summary>【暂停/继续】按钮文案（FR-015）。</summary>
    public string PauseButtonText => State == InputSessionState.Paused ? "▶ 继续" : "⏸ 暂停";

    /// <summary>是否处于"文件与设置不可编辑"的会话中。</summary>
    public bool IsBusy => State is InputSessionState.Typing or InputSessionState.Paused or InputSessionState.Countdown;

    /// <summary>
    /// 文档与结构性行为设置是否可编辑（§4.3 可用性矩阵：
    /// Idle / Finished / Error 可编辑；Countdown / Typing / Paused 冻结）。
    /// 速度设置不受此限制——输入中改速度下一拍即生效（FR-006.3）。
    /// </summary>
    public bool IsDocumentEditable => !IsBusy;

    /// <summary>是否以管理员身份运行（FR-027 的可观测性要求）。</summary>
    public bool IsAdminMode
    {
        get => _isAdminMode;
        set => SetProperty(ref _isAdminMode, value);
    }

    /// <summary>进度百分比（0–100）。</summary>
    public double ProgressPercent
    {
        get => _progressPercent;
        set => SetProperty(ref _progressPercent, value);
    }

    /// <summary>进度文案：<c>已输入 128 / 800 字符 (16.0%)</c>（FR-016.2）。</summary>
    public string ProgressText
    {
        get => _progressText;
        set => SetProperty(ref _progressText, value);
    }

    /// <summary>已用时 / 剩余 / 预计完成时刻（FR-016.4）。</summary>
    public string TimingText
    {
        get => _timingText;
        set => SetProperty(ref _timingText, value);
    }

    /// <summary>目标窗口标题（状态栏显示）。</summary>
    public string TargetWindowText
    {
        get => _targetWindowText;
        set => SetProperty(ref _targetWindowText, value);
    }

    /// <summary>目标窗口选择方式（下拉框选项）。</summary>
    public IReadOnlyList<string> TargetWindowModes { get; } = new[] { "跟随前台窗口", "自动选终端窗口", "固定窗口" };

    /// <summary>目标窗口选择方式索引（0 跟随 / 1 自动 / 2 固定）。</summary>
    public int SelectedTargetModeIndex
    {
        get => _selectedTargetModeIndex;
        set
        {
            if (SetProperty(ref _selectedTargetModeIndex, value))
            {
                ApplyTargetMode();
            }
        }
    }

    /// <summary>用户选定的固定目标窗口句柄。</summary>
    public IntPtr PinnedTargetWindow
    {
        get => _pinnedTargetWindow;
        set
        {
            if (SetProperty(ref _pinnedTargetWindow, value))
            {
                ApplyTargetMode();
            }
        }
    }

    /// <summary>选择器返回后由外壳调用：记录句柄并切换到"固定窗口"模式。</summary>
    public void SetPickedTargetWindow(IntPtr handle, string title)
    {
        _pinnedTargetWindow = handle;
        OnPropertyChanged(nameof(PinnedTargetWindow));

        TargetWindowText = handle == IntPtr.Zero ? "未确定" : title;
        SelectedTargetModeIndex = 2;
    }

    /// <summary>把界面上的目标窗口设置推给会话控制器。</summary>
    private void ApplyTargetMode()
    {
        if (_controller is null)
        {
            return;
        }

        _controller.TargetMode = _selectedTargetModeIndex switch
        {
            1 => TargetWindowMode.AutoTerminal,
            2 => TargetWindowMode.Pinned,
            _ => TargetWindowMode.Foreground,
        };

        _controller.PinnedTargetWindow = _pinnedTargetWindow;
    }

    /// <summary>已加载文档的摘要信息。</summary>
    public string DocumentInfoText
    {
        get => _documentInfoText;
        set => SetProperty(ref _documentInfoText, value);
    }

    /// <summary>状态栏辅助文本。</summary>
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    // ── 错误条（§10.1：Recoverable 级错误的呈现）────────────────────────

    /// <summary>是否显示顶部错误条。</summary>
    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    /// <summary>错误标题。</summary>
    public string ErrorTitle
    {
        get => _errorTitle;
        set => SetProperty(ref _errorTitle, value);
    }

    /// <summary>错误正文。</summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>解决建议（NFR-008：每条错误都必须给出"怎么办"）。</summary>
    public string ErrorAdvice
    {
        get => _errorAdvice;
        set => SetProperty(ref _errorAdvice, value);
    }

    // ── 命令 ────────────────────────────────────────────────────────────

    public ICommand BrowseFileCommand { get; }

    public ICommand ReloadFileCommand { get; }

    public ICommand OpenContainingFolderCommand { get; }

    public ICommand RefreshPreviewCommand { get; }

    public ICommand ApplyPresetCommand { get; }

    public ICommand StartCommand { get; }

    public ICommand PauseResumeCommand { get; }

    public ICommand StopCommand { get; }

    public ICommand CancelCountdownCommand { get; }

    public ICommand ResetHotkeysCommand { get; }

    public ICommand RestartElevatedCommand { get; }

    /// <summary>打开目标窗口选择器（FR-012.4）。</summary>
    public ICommand ChooseTargetWindowCommand { get; }

    /// <summary>查看完整内容（FR-005.1）。</summary>
    public ICommand ShowFullTextCommand { get; }


    public ICommand OpenLogsCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand AboutCommand { get; }

    public ICommand ExitCommand { get; }

    /// <summary>托盘用的"开始 / 暂停"合并命令：空闲时开始，进行中则暂停/继续（§6.4 托盘菜单）。</summary>
    public ICommand StartPauseToggleCommand { get; }

    public ICommand DismissErrorCommand { get; }

    // ── 状态驱动（§4.3 可用性矩阵）──────────────────────────────────────

    private bool CanEditDocument => State is InputSessionState.Idle or InputSessionState.Finished or InputSessionState.Error;

    private bool CanStart => CanEditDocument;

    /// <summary>外部（控制器/热键）驱动状态迁移的唯一入口。</summary>
    public void SetState(InputSessionState state) => State = state;

    private void RaiseCommandStates()
    {
        (BrowseFileCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ReloadFileCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (OpenContainingFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (PauseResumeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (StopCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelCountdownCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    // ── 预览构建（FR-005.3：直观显示换行与 Tab）─────────────────────────

    /// <summary>按前 500 字符构建预览行（Tab 显示为 →，行尾显示 ↵）。</summary>
    public void LoadPreview(string rawText)
    {
        PreviewLines.Clear();

        if (string.IsNullOrEmpty(rawText))
        {
            HasPreview = false;
            return;
        }

        var parts = rawText.Split('\n');
        var used = 0;
        var consumed = 0;

        for (var i = 0; i < parts.Length && used < PreviewCharLimit && PreviewLines.Count < PreviewLineLimit; i++)
        {
            var text = parts[i].Replace("\r", string.Empty).Replace("\t", "→");
            if (used + text.Length > PreviewCharLimit)
            {
                text = text[..Math.Max(0, PreviewCharLimit - used)] + "…";
            }

            used += text.Length;
            // 除最后一段外，其余行都以换行结束
            PreviewLines.Add(new PreviewLine(i + 1, text, i < parts.Length - 1));
            consumed = i + 1;
        }

        // FR-005.1：超出部分必须以"…"提示。
        // 之前这里静默截断，用户会以为文件只有这么点内容——本质是需求没做到。
        if (consumed < parts.Length)
        {
            PreviewLines.Add(new PreviewLine(
                LineNumber: 0,
                Text: $"…… 已省略后续 {parts.Length - consumed} 行，点「查看完整内容」查看全部",
                HasTrailingNewline: false,
                IsTruncationMarker: true));
        }

        HasPreview = PreviewLines.Count > 0;
    }

    /// <summary>清空预览（文件加载失败时保留原内容，故只在成功加载后调用）。</summary>
    public void ClearPreview()
    {
        PreviewLines.Clear();
        HasPreview = false;
    }

    /// <summary>展示一条错误条（§10.1）。</summary>
    public void ShowError(string title, string message, string advice)
    {
        ErrorTitle = title;
        ErrorMessage = message;
        ErrorAdvice = advice;
        HasError = true;
    }

    /// <summary>
    /// 按路径加载文档（走完整管线）。供外壳、单实例激活与界面预览工具调用；
    /// 按钮命令内部也走同一条路径，避免两套逻辑。
    /// </summary>
    /// <param name="path">文件路径。</param>
    public async Task LoadDocumentFromPathAsync(string path)
    {
        FilePath = path;
        await LoadDocumentAsync(isReload: false).ConfigureAwait(true);
    }

    // ── 命令实现（接入 Core 契约的位置）─────────────────────────────────

    private async Task BrowseFileAsync()
    {
        if (_pipeline is null)
        {
            StatusText = "文件管线未接入。";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "选择文本文件",
            // 过滤器见 FR-001.1
            Filter = "文本文件 (*.txt;*.log;*.md)|*.txt;*.log;*.md|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        FilePath = dialog.FileName;
        await LoadDocumentAsync(isReload: false).ConfigureAwait(true);
    }

    private Task ReloadFileAsync() => LoadDocumentAsync(isReload: false);

    private void OpenContainingFolder()
    {
        try
        {
            // FR-002.1：在资源管理器中定位到该文件
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{FilePath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            StatusText = $"无法打开所在文件夹：{ex.Message}";
        }
    }

    /// <summary>执行"读取 → 归一化 → 单元构建"，并把结果反映到界面上。</summary>
    private async Task LoadDocumentAsync(bool isReload)
    {
        if (_pipeline is null || FilePath.Length == 0)
        {
            return;
        }

        try
        {
            StatusText = "正在读取文件…";

            var preference = new EncodingPreference(SelectedEncodingMode());
            var options = BuildNormalizeOptions();

            var outcome = isReload
                ? await _pipeline.ReloadAsync(FilePath, preference, options).ConfigureAwait(true)
                : await _pipeline.LoadAsync(FilePath, preference, options).ConfigureAwait(true);

            ApplyOutcome(outcome);
        }
        catch (Exception ex)
        {
            // 读取失败不改变已加载内容（E-03）
            ShowError("加载文件失败", ex.Message, "请确认文件存在且可访问，然后点击【重载】重试。");
            StatusText = "加载失败。";
        }
    }

    private void ApplyOutcome(DocumentLoadOutcome outcome)
    {
        var fileResult = outcome.FileResult;

        if (outcome.Document is null)
        {
            HasError = true;
            ErrorTitle = fileResult.Error switch
            {
                FileLoadError.Empty => "文件内容为空",
                FileLoadError.NotFound => "找不到文件",
                FileLoadError.InUse => "文件被占用",
                FileLoadError.AccessDenied => "没有读取权限",
                FileLoadError.IsDirectory => "选择的是文件夹",
                _ => "无法加载文件",
            };
            ErrorMessage = fileResult.Message ?? "读取失败。";
            ErrorAdvice = fileResult.Error switch
            {
                FileLoadError.Empty => "请选择内容非空的文本文件（E-02）。",
                FileLoadError.NotFound => "请确认路径是否正确，或重新选择文件。",
                FileLoadError.InUse => "请关闭占用该文件的程序后点击【重载】（E-03）。",
                FileLoadError.AccessDenied => "请检查该文件的访问权限后重试。",
                FileLoadError.IsDirectory => "请选择一个文本文件而不是文件夹。",
                _ => "请稍后重试，或改用其他文件。",
            };

            if (fileResult.Error == FileLoadError.Empty)
            {
                _document = null;
                ClearPreview();
                EncodingName = fileResult.EncodingName;
                StatisticsText = "字符: 0    行: 0    预估: --";
                DocumentInfoText = "文件内容为空";
            }

            StatusText = ErrorMessage;
            return;
        }

        _document = outcome.Document;
        HasError = false;

        EncodingName = _document.EncodingName;
        LoadPreview(_document.RawText);
        UpdateStatisticsText();

        DocumentInfoText =
            $"{Path.GetFileName(_document.Path)} · {_document.Statistics.CharCount} 字符 · {_document.Statistics.LineCount} 行 · {_document.EncodingName}";

        StatusText = _document.Warnings.Count > 0
            ? string.Join("；", _document.Warnings)
            : $"已加载，编码依据：{_document.EncodingReason}";

        // 加载成功：通知外壳（记日志 / 记最近文件），并检查"最后一行会不会被执行"
        DocumentLoaded?.Invoke(this, _document);
        RefreshTrailingEnterWarning();
    }

    private static bool EndsWithNewline(string text)
        => text.Length > 0 && (text[^1] == '\n' || text[^1] == '\r');

    /// <summary>
    /// 文件末尾没有换行、且未开启「末尾补回车」时给出警告。
    /// </summary>
    /// <remarks>
    /// 这是真实踩过的坑：两行命令的文件如果末尾没有换行，"末尾补回车"又没勾，
    /// 最后一行**永远不会被执行**——用户看到的现象是"程序没反应"，很难自己想到原因。
    /// </remarks>
    private void RefreshTrailingEnterWarning()
    {
        if (_document is null || AppendTrailingEnter || EndsWithNewline(_document.RawText))
        {
            return;
        }

        ShowError(
            "最后一行不会被执行",
            "文件末尾没有换行符，而「末尾补回车」没有勾选，因此最后一行打完不会被提交。",
            "勾选「末尾补回车」，或让文件以换行符结尾（终端场景下这一行永远不会执行）。");
    }

    /// <summary>按当前速度参数刷新"字符 / 行 / 预估"统计（FR-005.4：改速度即时刷新）。</summary>
    private void UpdateStatisticsText()
    {
        if (_document is null)
        {
            StatisticsText = "字符: 0    行: 0    预估: --";
            return;
        }

        var statistics = _document.Statistics;
        var estimate = DurationEstimator.Estimate(statistics, CurrentSpeedProfile());

        StatisticsText = $"字符: {statistics.CharCount}    行: {statistics.LineCount}    预估: {DurationEstimator.ToDisplayString(estimate)}";
    }

    private SpeedProfile CurrentSpeedProfile()
        => new(
            IntervalMs: IntervalMs,
            JitterPercent: JitterPercent,
            LineExtraDelayMs: LineExtraDelayMs,
            PreEnterDelayMs: PreEnterDelayMs,
            NewlineStrategy: SelectedNewlineStrategy());

    private NormalizeOptions BuildNormalizeOptions()
        => new(
            ParseEscapes: ParseEscapes,
            IgnoreEmptyLines: IgnoreEmptyLines,
            SkipCommentLines: SkipCommentLines,
            AppendTrailingEnter: AppendTrailingEnter);

    /// <summary>编码下拉索引 → 编码模式（顺序见 <see cref="EncodingModes"/>）。</summary>
    private EncodingMode SelectedEncodingMode() => SelectedEncodingIndex switch
    {
        1 => EncodingMode.Utf8,
        2 => EncodingMode.Utf16Le,
        3 => EncodingMode.Utf16Be,
        4 => EncodingMode.Gbk,
        5 => EncodingMode.Gb18030,
        6 => EncodingMode.Ansi,
        _ => EncodingMode.Auto,
    };

    /// <summary>换行策略下拉索引 → 策略（顺序见 <see cref="NewlineStrategies"/>）。</summary>
    /// <summary>当前换行策略的配置文本形式（写入 config.json 用）。</summary>
    public string SelectedNewlineStrategyText() => SelectedNewlineStrategy().ToString();

    private NewlineStrategy SelectedNewlineStrategy() => NewlineStrategyIndex switch
    {
        1 => NewlineStrategy.ShiftEnter,
        2 => NewlineStrategy.CtrlEnter,
        3 => NewlineStrategy.LastLineEnterOnly,
        4 => NewlineStrategy.None,
        _ => NewlineStrategy.Enter,
    };

    private void ApplyPreset(object? parameter)
    {
        // 预设按钮：20 / 50 / 100 / 200 / 500 / 1000 ms（FR-006.2）
        if (parameter is string text && int.TryParse(text, out var value))
        {
            IntervalMs = Math.Clamp(value, 10, 5000);
            StatusText = $"节拍已设为 {IntervalMs} ms";
        }
    }

    private void Start()
    {
        if (_controller is null)
        {
            StatusText = "输入会话控制器未接入。";
            return;
        }

        if (_document is null)
        {
            ShowError("请先选择文本文件", "还没有加载任何文本内容。", "点击【浏览…】选择 txt 文件后重试（E-01）。");
            return;
        }

        // 清掉上一次会话的进度显示
        ProgressPercent = 0;
        ProgressText = "0 / 0 (0.0%)";
        TimingText = "已用 --    剩余 --    预计完成 --";
        HasError = false;

        // 前置校验（文件非空、可注入性、倒计时、目标窗口）由控制器负责
        _controller.Reset();
        _controller.Start(_document, CountdownSeconds, CurrentSpeedProfile);
    }

    /// <summary>托盘菜单的"开始 / 暂停"三态切换（FR-013.3 的语义）。</summary>
    private void StartPauseToggle()
    {
        if (State is InputSessionState.Idle or InputSessionState.Finished or InputSessionState.Error)
        {
            if (CanStart)
            {
                Start();
            }
        }
        else
        {
            PauseResume();
        }
    }

    /// <summary>
    /// 打开指定文件。拖放、最近文件、提权携带文件都走这里，行为与"浏览…"一致。
    /// </summary>
    public async Task OpenFileAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        FilePath = path;
        await LoadDocumentAsync(isReload: false).ConfigureAwait(true);
    }

    /// <summary>速度热键微调（FR-028）：调整节拍并即时生效、即时刷新预估。</summary>
    public void AdjustInterval(int deltaMs)
    {
        var updated = Math.Clamp(IntervalMs + deltaMs, SpeedProfileValidator.MinIntervalMs, SpeedProfileValidator.MaxIntervalMs);
        if (updated == IntervalMs)
        {
            return;
        }

        IntervalMs = updated;
        StatusText = $"节拍已调整为 {IntervalMs} ms（下一拍生效）";
    }

    private void PauseResume() => _controller?.TogglePause();

    private void Stop() => _controller?.Stop();

    private void CancelCountdown() => _controller?.CancelCountdown();

    // ── 会话控制器事件（控制器保证在界面线程上回调）────────────────────

    private void OnSessionStateChanged(object? sender, StateChangedEventArgs e)
    {
        SetState(e.Current);
        if (!string.IsNullOrEmpty(e.Reason))
        {
            StatusText = e.Reason!;
        }

        // 自动继续（焦点回到目标窗口）后 3 秒收起失焦提示条
        if (e.Current == InputSessionState.Typing && HasError && _errorFromFocusGuard)
        {
            ArmFocusGuardNoticeDismiss();
        }

        // 进入"输入中"时把真正锁定的目标窗口回显出来，用户一眼能看出字会打到哪
        if (e.Current == InputSessionState.Typing
            && _controller?.ActiveTargetWindow is { } handle
            && handle != IntPtr.Zero)
        {
            var title = TargetWindows.GetTitle(handle);
            if (!string.IsNullOrWhiteSpace(title))
            {
                TargetWindowText = title;
            }
        }
    }

    private void OnSessionProgressChanged(object? sender, ProgressInfo progress)
    {
        ProgressPercent = progress.Percent;
        ProgressText = progress.ToDisplayString();
        TimingText =
            $"已用 {FormatDuration(progress.Elapsed)}    剩余约 {FormatDuration(progress.Remaining)}    "
            + $"预计 {progress.EstimatedEnd:HH:mm:ss} 完成";
    }

    private void OnSessionNotified(object? sender, UserNotification notification)
    {
        StatusText = notification.Message;

        if (notification.Severity == ErrorSeverity.Info)
        {
            _errorFromFocusGuard = false;
            return;
        }

        // 记下这条是不是"失焦暂停"——只有它会在自动继续 3 秒后自动收起，
        // 真正的错误（如注入失败）必须留到用户主动关闭
        _errorFromFocusGuard = notification.Title.Contains("失去焦点");

        ShowError(notification.Title, notification.Message, notification.Advice ?? "请查看日志了解详情。");
    }

    /// <summary>
    /// 焦点守护提示条：自动继续 3 秒后收起。
    /// </summary>
    /// <remarks>
    /// 用户反馈：失焦暂停的黄色提示会一直压在界面上。但**不能无差别关闭提示条**——
    /// 那会把真正的错误也一起抹掉，所以只对"失焦暂停"这一条生效。
    /// </remarks>
    private void ArmFocusGuardNoticeDismiss()
    {
        _focusGuardNoticeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _focusGuardNoticeTimer.Tick -= OnFocusGuardNoticeTick;
        _focusGuardNoticeTimer.Tick += OnFocusGuardNoticeTick;
        _focusGuardNoticeTimer.Stop();
        _focusGuardNoticeTimer.Start();
    }

    private void OnFocusGuardNoticeTick(object? sender, EventArgs e)
    {
        _focusGuardNoticeTimer?.Stop();

        if (!_errorFromFocusGuard)
        {
            return;
        }

        _errorFromFocusGuard = false;
        HasError = false;
    }

    private void OnSessionFinished(object? sender, SessionStatistics statistics)
    {
        StatusText = statistics.ToFinishMessage();
        SessionCompleted?.Invoke(this, statistics);
    }

    private static string FormatDuration(TimeSpan value)
        => value.TotalHours >= 1
            ? $"{(int)value.TotalHours} 时 {value.Minutes} 分"
            : value.TotalMinutes >= 1
                ? $"{value.Minutes} 分 {value.Seconds} 秒"
                : $"{value.Seconds} 秒";

    private void ResetHotkeys()
    {
        StartPauseHotkey = "F9";
        EmergencyStopHotkey = "F10";
        SpeedUpHotkey = "Ctrl+Alt+Up";
        SpeedDownHotkey = "Ctrl+Alt+Down";
        StatusText = "热键已恢复默认（FR-025.5）";
    }

    private void RestartElevated()
    {
        // 提权是应用层的事（需要 runas），ViewModel 只负责发起
        if (RestartElevatedRequested is null)
        {
            StatusText = "提权重启不可用。";
            return;
        }

        RestartElevatedRequested.Invoke(this, EventArgs.Empty);
    }

    private void OpenLogs()
    {
        if (OpenLogsRequested is null)
        {
            StatusText = "日志目录不可用。";
            return;
        }

        OpenLogsRequested.Invoke(this, EventArgs.Empty);
    }
}
