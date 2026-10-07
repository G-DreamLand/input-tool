using System.ComponentModel;
using InputTool.App.ViewModels;
using InputTool.Core.Application;
using InputTool.Core.Configuration;
using InputTool.Core.Logging;
using InputTool.Core.Parsing;
using InputTool.Core.Scheduling;

namespace InputTool.App.Services;

/// <summary>
/// 把界面与配置文件双向绑定起来（FR-018.2 / FR-018.4）。
/// </summary>
/// <remarks>
/// · <see cref="Apply"/>：配置 → 界面（启动时、恢复默认后调用）；
/// · 之后监听界面属性变化 → 配置（去抖落盘由 <see cref="IConfigService"/> 负责）；
/// · <c>_applying</c> 标志用于切断"应用配置又触发保存"的回环。
/// <para>
/// 只映射**需要持久化**的属性；纯运行时状态（进度、状态机、预览）不写进配置。
/// </para>
/// </remarks>
public sealed class AppSettingsBinder : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly IConfigService _config;
    private readonly IAppLogger? _logger;

    private bool _applying;
    private bool _disposed;

    /// <summary>构造绑定器。</summary>
    /// <param name="viewModel">主窗口 ViewModel。</param>
    /// <param name="config">配置服务。</param>
    /// <param name="logger">日志（可选）。</param>
    public AppSettingsBinder(MainViewModel viewModel, IConfigService config, IAppLogger? logger = null)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    /// <summary>把当前配置应用到界面，并开始监听界面变化。</summary>
    public void Apply()
    {
        var config = _config.Current;

        _applying = true;

        try
        {
            _viewModel.IntervalMs = config.Speed.IntervalMs;
            _viewModel.JitterPercent = config.Speed.JitterPercent;
            _viewModel.LineExtraDelayMs = config.Speed.LineExtraDelayMs;
            _viewModel.PreEnterDelayMs = config.Speed.PreEnterDelayMs;
            _viewModel.SelectedSpeedUnitIndex = config.Speed.Unit == SpeedUnit.CharsPerSecond ? 1 : 0;

            _viewModel.NewlineStrategyIndex = ToIndex(ParseNewlineStrategy(config.Behavior.NewlineStrategy));
            _viewModel.AppendTrailingEnter = config.Behavior.AppendTrailingEnter;
            _viewModel.ParseEscapes = config.Behavior.ParseEscapes;
            _viewModel.IgnoreEmptyLines = config.Behavior.IgnoreEmptyLines;
            _viewModel.SkipCommentLines = config.Behavior.SkipCommentLines;
            _viewModel.FocusGuard = config.Behavior.FocusGuard;
            _viewModel.StepMode = config.Behavior.StepMode;
            _viewModel.FocusGuardActionIndex = (int)config.Behavior.FocusGuardAction;

            _viewModel.CountdownSeconds = config.Countdown.Seconds;
            _viewModel.SelectedTargetModeIndex = (int)config.Target.Mode;

            _viewModel.StartPauseHotkey = config.Hotkeys.StartPause;
            _viewModel.EmergencyStopHotkey = config.Hotkeys.EmergencyStop;
            _viewModel.SpeedUpHotkey = config.Hotkeys.SpeedUp;
            _viewModel.SpeedDownHotkey = config.Hotkeys.SpeedDown;

            _viewModel.CloseToTray = config.Ui.CloseToTray;
            _viewModel.PlayFinishSound = config.Ui.PlayFinishSound;
        }
        finally
        {
            _applying = false;
        }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _logger?.Info("界面已应用配置", "Config");
    }

    /// <summary>把界面当前值全量写回配置（恢复默认时用）。</summary>
    public void Capture()
    {
        _config.Update(config => config with
        {
            Speed = config.Speed with
            {
                IntervalMs = _viewModel.IntervalMs,
                Unit = _viewModel.SelectedSpeedUnitIndex == 1 ? SpeedUnit.CharsPerSecond : SpeedUnit.MsPerChar,
                JitterPercent = _viewModel.JitterPercent,
                LineExtraDelayMs = _viewModel.LineExtraDelayMs,
                PreEnterDelayMs = _viewModel.PreEnterDelayMs,
            },
            Behavior = config.Behavior with
            {
                NewlineStrategy = _viewModel.SelectedNewlineStrategyText(),
                AppendTrailingEnter = _viewModel.AppendTrailingEnter,
                ParseEscapes = _viewModel.ParseEscapes,
                IgnoreEmptyLines = _viewModel.IgnoreEmptyLines,
                SkipCommentLines = _viewModel.SkipCommentLines,
                FocusGuard = _viewModel.FocusGuard,
                StepMode = _viewModel.StepMode,
                FocusGuardAction = (FocusGuardAction)_viewModel.FocusGuardActionIndex,
            },
            Countdown = config.Countdown with { Seconds = _viewModel.CountdownSeconds },
            Target = config.Target with { Mode = (TargetWindowMode)_viewModel.SelectedTargetModeIndex },
            Hotkeys = config.Hotkeys with
            {
                StartPause = _viewModel.StartPauseHotkey,
                EmergencyStop = _viewModel.EmergencyStopHotkey,
                SpeedUp = _viewModel.SpeedUpHotkey,
                SpeedDown = _viewModel.SpeedDownHotkey,
            },
            Ui = config.Ui with
            {
                CloseToTray = _viewModel.CloseToTray,
                PlayFinishSound = _viewModel.PlayFinishSound,
            },
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    // ── 界面 → 配置 ─────────────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_applying || _disposed || e.PropertyName is null)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IntervalMs):
                Update(config => config with { Speed = config.Speed with { IntervalMs = _viewModel.IntervalMs } });
                break;

            case nameof(MainViewModel.JitterPercent):
                Update(config => config with { Speed = config.Speed with { JitterPercent = _viewModel.JitterPercent } });
                break;

            case nameof(MainViewModel.LineExtraDelayMs):
                Update(config => config with { Speed = config.Speed with { LineExtraDelayMs = _viewModel.LineExtraDelayMs } });
                break;

            case nameof(MainViewModel.PreEnterDelayMs):
                Update(config => config with { Speed = config.Speed with { PreEnterDelayMs = _viewModel.PreEnterDelayMs } });
                break;

            case nameof(MainViewModel.SelectedSpeedUnitIndex):
                Update(config => config with
                {
                    Speed = config.Speed with
                    {
                        Unit = _viewModel.SelectedSpeedUnitIndex == 1 ? SpeedUnit.CharsPerSecond : SpeedUnit.MsPerChar,
                    },
                });
                break;

            case nameof(MainViewModel.NewlineStrategyIndex):
                Update(config => config with
                {
                    Behavior = config.Behavior with { NewlineStrategy = _viewModel.SelectedNewlineStrategyText() },
                });
                break;

            case nameof(MainViewModel.AppendTrailingEnter):
                Update(config => config with { Behavior = config.Behavior with { AppendTrailingEnter = _viewModel.AppendTrailingEnter } });
                break;

            case nameof(MainViewModel.ParseEscapes):
                Update(config => config with { Behavior = config.Behavior with { ParseEscapes = _viewModel.ParseEscapes } });
                break;

            case nameof(MainViewModel.IgnoreEmptyLines):
                Update(config => config with { Behavior = config.Behavior with { IgnoreEmptyLines = _viewModel.IgnoreEmptyLines } });
                break;

            case nameof(MainViewModel.SkipCommentLines):
                Update(config => config with { Behavior = config.Behavior with { SkipCommentLines = _viewModel.SkipCommentLines } });
                break;

            case nameof(MainViewModel.FocusGuard):
                Update(config => config with { Behavior = config.Behavior with { FocusGuard = _viewModel.FocusGuard } });
                break;

            case nameof(MainViewModel.StepMode):
                Update(config => config with { Behavior = config.Behavior with { StepMode = _viewModel.StepMode } });
                break;

            case nameof(MainViewModel.FocusGuardActionIndex):
                Update(config => config with
                {
                    Behavior = config.Behavior with { FocusGuardAction = (FocusGuardAction)_viewModel.FocusGuardActionIndex },
                });
                break;

            case nameof(MainViewModel.CountdownSeconds):
                Update(config => config with { Countdown = config.Countdown with { Seconds = _viewModel.CountdownSeconds } });
                break;

            case nameof(MainViewModel.SelectedTargetModeIndex):
                Update(config => config with { Target = config.Target with { Mode = (TargetWindowMode)_viewModel.SelectedTargetModeIndex } });
                break;

            case nameof(MainViewModel.StartPauseHotkey):
                Update(config => config with { Hotkeys = config.Hotkeys with { StartPause = _viewModel.StartPauseHotkey } });
                break;

            case nameof(MainViewModel.EmergencyStopHotkey):
                Update(config => config with { Hotkeys = config.Hotkeys with { EmergencyStop = _viewModel.EmergencyStopHotkey } });
                break;

            case nameof(MainViewModel.SpeedUpHotkey):
                Update(config => config with { Hotkeys = config.Hotkeys with { SpeedUp = _viewModel.SpeedUpHotkey } });
                break;

            case nameof(MainViewModel.SpeedDownHotkey):
                Update(config => config with { Hotkeys = config.Hotkeys with { SpeedDown = _viewModel.SpeedDownHotkey } });
                break;

            case nameof(MainViewModel.CloseToTray):
                Update(config => config with { Ui = config.Ui with { CloseToTray = _viewModel.CloseToTray } });
                break;

            case nameof(MainViewModel.PlayFinishSound):
                Update(config => config with { Ui = config.Ui with { PlayFinishSound = _viewModel.PlayFinishSound } });
                break;

            default:
                break;
        }
    }

    /// <summary>记录最近打开的文件（FR-018.3）。</summary>
    public void RememberRecentFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _config.Update(config => config with
        {
            File = config.File with
            {
                Path = path,
                RecentFiles = new[] { path }
                    .Concat(config.File.RecentFiles ?? Array.Empty<string>())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(10)
                    .ToArray(),
            },
        });
    }

    private void Update(Func<AppConfig, AppConfig> transform)
    {
        try
        {
            _config.Update(transform);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"界面改动写入配置失败：{ex.Message}", "Config");
        }
    }

    // ── 索引映射 ────────────────────────────────────────────────────────

    /// <summary>配置文件里的换行策略文本 → 枚举（非法值回退 Enter）。</summary>
    private static NewlineStrategy ParseNewlineStrategy(string? text)
        => Enum.TryParse<NewlineStrategy>(text, ignoreCase: true, out var strategy)
            ? strategy
            : NewlineStrategy.Enter;

    /// <summary>枚举 → 界面的下拉索引（与 MainViewModel.SelectedNewlineStrategy 的顺序一致）。</summary>
    private static int ToIndex(NewlineStrategy strategy) => strategy switch
    {
        NewlineStrategy.ShiftEnter => 1,
        NewlineStrategy.CtrlEnter => 2,
        NewlineStrategy.LastLineEnterOnly => 3,
        NewlineStrategy.None => 4,
        _ => 0,
    };
}
