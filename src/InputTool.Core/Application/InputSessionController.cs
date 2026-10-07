using InputTool.Core.Abstractions;
using InputTool.Core.Configuration;
using InputTool.Core.Injection;
using InputTool.Core.Logging;
using InputTool.Core.Scheduling;

namespace InputTool.Core.Application;

/// <summary>
/// 输入会话控制器。对应《软件开发设计文档》§4.2 状态机与 §5.9.1。
/// </summary>
/// <remarks>
/// 关键约定：
/// · 所有对外事件都在**界面线程**上回调（构造/Start 时捕获 <see cref="SynchronizationContext"/>），
///   界面订阅方无需自己 marshal；
/// · <see cref="Stop"/> 是幂等的，且对倒计时与注入两种状态分别处理；
/// · 输入结束后无论结果如何都会产出 <see cref="SessionStatistics"/> 并触发 <see cref="Finished"/>。
/// </remarks>
public sealed class InputSessionController : IInputSessionController
{
    private readonly IInputScheduler _scheduler;
    private readonly IKeyboardEmitter _emitter;
    private readonly IInjectabilityProbe _probe;
    private readonly ICountdownPresenter _countdown;
    private readonly ITargetWindowGateway _targetWindows;
    private readonly IFocusMonitor _focusMonitor;
    private readonly IAppLogger? _logger;

    /// <summary>本次暂停是否由焦点守护触发（用于"焦点回来后自动继续"）。</summary>
    private bool _pausedByFocusGuard;

    private InputSessionState _state = InputSessionState.Idle;
    private LoadedDocument? _document;
    private SessionStatistics? _lastResult;
    private Func<SpeedProfile>? _profileProvider;
    private CancellationTokenSource? _countdownCts;
    private SynchronizationContext? _uiContext;

    private DateTimeOffset _startedAt;
    private IntPtr _targetWindow;

    /// <summary>构造会话控制器。</summary>
    /// <param name="scheduler">调度器；省略时基于 <paramref name="emitter"/> 创建。</param>
    /// <param name="emitter">键盘注入器；省略时使用 Win32 实现。</param>
    /// <param name="probe">可注入性探测；省略时使用系统实现。</param>
    /// <param name="countdown">倒计时浮层；省略时不等待（无头场景）。</param>
    /// <param name="targetWindows">目标窗口系统能力；省略时使用 Win32 实现。</param>
    /// <param name="focusMonitor">焦点监视器；省略时使用轮询实现（FR-020）。</param>
    /// <param name="logger">日志（可选）。</param>
    public InputSessionController(
        IInputScheduler? scheduler = null,
        IKeyboardEmitter? emitter = null,
        IInjectabilityProbe? probe = null,
        ICountdownPresenter? countdown = null,
        ITargetWindowGateway? targetWindows = null,
        IFocusMonitor? focusMonitor = null,
        IAppLogger? logger = null)
    {
        _emitter = emitter ?? new Win32KeyboardEmitter();
        _scheduler = scheduler ?? new InputScheduler(_emitter);
        _probe = probe ?? new InjectabilityProbe();
        _countdown = countdown ?? new ImmediateCountdownPresenter();
        _targetWindows = targetWindows ?? new Win32TargetWindowGateway();
        _focusMonitor = focusMonitor ?? new ForegroundFocusMonitor(_targetWindows, logger);
        _logger = logger;

        _scheduler.Finished += OnSchedulerFinished;
        _scheduler.StepWaiting += OnStepWaiting;
    }

    /// <inheritdoc />
    public InputSessionState State => _state;

    /// <inheritdoc />
    public LoadedDocument? Document => _document;

    /// <inheritdoc />
    public SessionStatistics? LastResult => _lastResult;

    /// <inheritdoc />
    public TargetWindowMode TargetMode { get; set; } = TargetWindowMode.Foreground;

    /// <inheritdoc />
    public IntPtr PinnedTargetWindow { get; set; } = IntPtr.Zero;

    /// <inheritdoc />
    public IntPtr ActiveTargetWindow { get; private set; } = IntPtr.Zero;

    /// <inheritdoc />
    public bool FocusGuardEnabled { get; set; }

    /// <inheritdoc />
    public FocusGuardAction FocusGuardAction { get; set; } = FocusGuardAction.Pause;

    /// <inheritdoc />
    public bool StepMode { get; set; }

    /// <summary>是否正停在某一行开头等待步进热键。</summary>
    private bool _waitingForStep;

    /// <inheritdoc />
    public event EventHandler<StateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<ProgressInfo>? ProgressChanged;

    /// <inheritdoc />
    public event EventHandler<UserNotification>? Notified;

    /// <inheritdoc />
    public event EventHandler<SessionStatistics>? Finished;

    /// <inheritdoc />
    public void Start(LoadedDocument document, int countdownSeconds, Func<SpeedProfile> profileProvider)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(profileProvider);

        if (_state is not (InputSessionState.Idle or InputSessionState.Finished or InputSessionState.Error))
        {
            return;
        }

        // 在界面线程上捕获同步上下文，之后所有事件都回到这里
        _uiContext = SynchronizationContext.Current ?? _uiContext;
        _ = StartAsync(document, countdownSeconds, profileProvider);
    }

    /// <inheritdoc />
    public void Pause()
    {
        if (_state != InputSessionState.Typing)
        {
            return;
        }

        _scheduler.Pause();
        SetState(InputSessionState.Paused, "已暂停，可自由操作目标窗口");
    }

    /// <inheritdoc />
    public void Resume()
    {
        if (_state != InputSessionState.Paused)
        {
            return;
        }

        // 用户手动继续：清掉"焦点守护造成的暂停"标记，避免焦点回来时重复恢复。
        // 同时清掉"等待步进"——步进继续现在就走这条路（F9），
        // 若不清，焦点守护会因为 _waitingForStep 一直为 true 而**永久失效**。
        _pausedByFocusGuard = false;
        _waitingForStep = false;
        _scheduler.Resume();
        SetState(InputSessionState.Typing, "继续输入");
    }

    /// <inheritdoc />
    public void TogglePause()
    {
        if (_state == InputSessionState.Typing)
        {
            Pause();
        }
        else if (_state == InputSessionState.Paused)
        {
            Resume();
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        switch (_state)
        {
            case InputSessionState.Idle:
                return;

            case InputSessionState.Countdown:
                CancelCountdown();
                return;

            default:
                _logger?.Info("收到停止请求", "Session");
                _scheduler.Stop();
                return;
        }
    }

    /// <inheritdoc />
    public void CancelCountdown()
    {
        if (_state != InputSessionState.Countdown)
        {
            return;
        }

        _countdownCts?.Cancel();
        SetState(InputSessionState.Idle, "已取消倒计时");
    }

    /// <inheritdoc />
    public void Reset()
    {
        if (_state is InputSessionState.Typing or InputSessionState.Paused or InputSessionState.Countdown)
        {
            return;
        }

        SetState(InputSessionState.Idle, "已就绪");
    }

    // ── 内部实现 ────────────────────────────────────────────────────────

    private async Task StartAsync(LoadedDocument document, int countdownSeconds, Func<SpeedProfile> profileProvider)
    {
        try
        {
            // 前置校验（§5.9.1 Preflight）
            if (document.IsEmpty)
            {
                Notify(new UserNotification(
                    ErrorSeverity.Warning,
                    "没有可输入的内容",
                    "文档为空，或所有单元都被「跳过」类开关过滤掉了。",
                    "请更换文件，或检查「忽略空行 / 跳过以 # 开头的行」等开关。"));
                return;
            }

            var injectability = _probe.Check();
            if (!injectability.CanInject)
            {
                Notify(new UserNotification(
                    ErrorSeverity.Recoverable,
                    "当前无法注入按键",
                    injectability.Reason,
                    injectability.Blocker == InjectabilityBlocker.HigherIntegrity
                        ? "目标窗口以管理员身份运行（Windows UIPI 限制）。请以管理员身份重启本工具后重试。"
                        : "请解锁屏幕、确保目标窗口获得焦点后重试。"));
                return;
            }

            _document = document;
            _profileProvider = profileProvider;

            var seconds = Math.Max(0, countdownSeconds);

            // 倒计时（T1 → T5）
            SetState(InputSessionState.Countdown, seconds > 0 ? $"倒计时 {seconds} 秒：请切换到目标输入框" : "立即开始");

            using var cts = new CancellationTokenSource();
            _countdownCts = cts;

            var completed = await _countdown.RunAsync(seconds, cts.Token).ConfigureAwait(true);
            _countdownCts = null;

            if (!completed || cts.IsCancellationRequested)
            {
                if (_state == InputSessionState.Countdown)
                {
                    SetState(InputSessionState.Idle, "已取消倒计时");
                }

                return;
            }

            // 解析目标窗口（§6.3 / FR-012.4）：
            // · 跟随前台：用倒计时结束那一刻的前台窗口（用户自己切过去）；
            // · 自动终端 / 固定窗口：这里主动切前台，并**回读校验**是否真的成功。
            var target = ResolveTargetWindow(out var targetFailure);

            if (targetFailure is not null)
            {
                _logger?.Warn($"目标窗口无法确定：{targetFailure}", "Session");

                Notify(new UserNotification(
                    ErrorSeverity.Warning,
                    "还没有确定目标窗口",
                    targetFailure,
                    "请点主界面的「选择…」挑一个目标窗口；或把「目标窗口」改为「跟随前台窗口」，然后先切到目标窗口再开始。"));
                SetState(InputSessionState.Idle, "未确定目标窗口，未开始输入");
                return;
            }

            if (target != IntPtr.Zero && !_targetWindows.TryActivate(target))
            {
                _logger?.Warn($"无法把目标窗口切到前台（handle=0x{target.ToInt64():X}）", "Session");

                Notify(new UserNotification(
                    ErrorSeverity.Warning,
                    "无法切换到目标窗口",
                    "Windows 限制前台切换：只有当前前台进程才能无条件抢焦点。",
                    "请手动点击目标窗口后重试，或把「目标窗口」改为「跟随前台窗口」，在倒计时内自己切过去。"));
                SetState(InputSessionState.Idle, "目标窗口激活失败，未开始输入");
                return;
            }

            _targetWindow = target != IntPtr.Zero ? target : _targetWindows.GetForegroundWindow();
            ActiveTargetWindow = _targetWindow;
            _startedAt = DateTimeOffset.Now;

            // 焦点守护（FR-020）：锁定目标窗口后开始周期性检查前台窗口
            StartFocusGuard();

            SetState(
                InputSessionState.Typing,
                _targetWindows.GetTitle(_targetWindow) is { Length: > 0 } title
                    ? $"正在向「{title}」输入…"
                    : "正在向目标窗口输入…");

            _scheduler.StepMode = StepMode;
            _scheduler.Start(document.Units, profileProvider, new ProgressRelay(this));
        }
        catch (Exception ex)
        {
            _logger?.Error("启动输入会话失败", ex, "Session");
            SetState(InputSessionState.Error, ex.Message);
            Notify(new UserNotification(ErrorSeverity.Fatal, "启动输入失败", ex.Message, "请查看日志了解详情。"));
        }
    }

    private void OnSchedulerFinished(object? sender, SessionFinishedEventArgs e)
    {
        // 会话结束（无论什么原因）：先停掉焦点监视，别让它继续跑
        StopFocusGuard();

        var averageInterval = e.Completed > 0 ? e.Elapsed.TotalMilliseconds / e.Completed : 0;

        var statistics = new SessionStatistics(
            SessionId: Guid.NewGuid(),
            TotalUnits: e.Total,
            CompletedUnits: e.Completed,
            SkippedUnits: e.Skipped,
            FailedUnits: e.Failed,
            Elapsed: e.Elapsed,
            AverageIntervalMs: averageInterval,
            MaxDeviationMs: 0,
            TargetWindow: _targetWindow,
            StartedAt: _startedAt,
            EndedAt: DateTimeOffset.Now,
            Outcome: e.Outcome);

        _lastResult = statistics;

        RaiseOnUi(() =>
        {
            var state = e.Outcome switch
            {
                SessionOutcome.Completed => InputSessionState.Finished,
                SessionOutcome.Stopped => InputSessionState.Idle,
                _ => InputSessionState.Error,
            };

            SetState(state, statistics.ToFinishMessage());

            if (e.Outcome == SessionOutcome.Error)
            {
                Notify(new UserNotification(
                    ErrorSeverity.Recoverable,
                    "输入已中断",
                    e.Message ?? "注入过程中发生错误。",
                    "请查看日志；若目标窗口以管理员身份运行，请以管理员身份重启本工具。"));
            }

            Finished?.Invoke(this, statistics);
        });
    }

    /// <summary>
    /// 按当前模式解析目标窗口。
    /// </summary>
    /// <param name="failureReason">
    /// 无法确定时给出**面向用户**的原因；为 null 表示要么已确定，要么是"跟随前台窗口"。
    /// </param>
    /// <remarks>
    /// 设计原则：**绝不静默退化**。「固定窗口」没选窗口、或「自动选终端」找不到终端时，
    /// 必须明确报错并中止，而不是悄悄退回"跟随前台窗口"——
    /// 那会把字打到用户完全没预期的窗口里（真实踩过：字被打了程序自己的窗口）。
    /// </remarks>
    private IntPtr ResolveTargetWindow(out string? failureReason)
    {
        failureReason = null;

        switch (TargetMode)
        {
            case TargetWindowMode.Pinned when PinnedTargetWindow != IntPtr.Zero:
                return PinnedTargetWindow;

            case TargetWindowMode.Pinned:
                failureReason = "当前是「固定窗口」模式，但还没有选定目标窗口。";
                return IntPtr.Zero;

            case TargetWindowMode.AutoTerminal:
                var terminal = _targetWindows.FindBestTerminalWindow();
                if (terminal == IntPtr.Zero)
                {
                    failureReason = "当前是「自动选终端窗口」模式，但没有找到任何终端窗口。";
                }

                return terminal;

            default:
                // 跟随前台窗口：合法策略，由用户在倒计时内自己切过去
                return IntPtr.Zero;
        }
    }

    // ── 焦点守护（FR-020）──────────────────────────────────────────────

    private void StartFocusGuard()
    {
        if (!FocusGuardEnabled || _targetWindow == IntPtr.Zero)
        {
            return;
        }

        _pausedByFocusGuard = false;
        _focusMonitor.FocusLost += OnFocusLost;
        _focusMonitor.FocusRegained += OnFocusRegained;
        _focusMonitor.Start(_targetWindow, ForegroundFocusMonitor.DefaultInterval);
    }

    private void StopFocusGuard()
    {
        _focusMonitor.FocusLost -= OnFocusLost;
        _focusMonitor.FocusRegained -= OnFocusRegained;
        _focusMonitor.Stop();
        _pausedByFocusGuard = false;
    }

    private void OnFocusLost(object? sender, EventArgs e) => RaiseOnUi(HandleFocusLost);

    private void OnFocusRegained(object? sender, EventArgs e) => RaiseOnUi(HandleFocusRegained);

    /// <summary>调度器停在某一行开头（FR 新增：逐行步进）。</summary>
    private void OnStepWaiting(object? sender, EventArgs e)
        => RaiseOnUi(() =>
        {
            _waitingForStep = true;
            SetState(InputSessionState.Paused, "已完成本行，等待下一步（步进热键）");
        });

    /// <summary>失去焦点：按 FR-020.3 的策略处理。</summary>
    private void HandleFocusLost()
    {
        if (_state is not (InputSessionState.Typing or InputSessionState.Paused))
        {
            return;
        }

        // 停在行首等步进时并不是"焦点跑掉了"，不该弹失焦提示。
        // 这也是与焦点守护的**关键互锁**：因为 _pausedByFocusGuard 此时为 false，
        // 焦点回来时 HandleFocusRegained 的自动继续不会触发 —— 不会替你跳步。
        if (_waitingForStep)
        {
            return;
        }

        switch (FocusGuardAction)
        {
            case FocusGuardAction.Continue:
                _logger?.Warn("目标窗口失去焦点，按设置继续输入（FR-020.3）", "FocusGuard");
                Notify(new UserNotification(
                    ErrorSeverity.Recoverable,
                    "目标窗口已失去焦点",
                    "当前策略是「继续输入」，文本可能流入其它窗口。",
                    "建议把焦点守护策略改为「自动暂停」，或立即按 F10 紧急停止。"));
                break;

            case FocusGuardAction.Stop:
                _logger?.Warn("目标窗口失去焦点，按设置自动停止（FR-020.3）", "FocusGuard");
                Notify(new UserNotification(
                    ErrorSeverity.Warning,
                    "目标窗口已失去焦点，输入已停止",
                    "按当前设置，丢失焦点时会自动停止本次输入。",
                    "重新开始前请先点击目标窗口，再按 F9。"));
                _scheduler.Stop();
                break;

            default:
                // Pause（默认策略）
                if (_state == InputSessionState.Typing)
                {
                    _scheduler.Pause();
                    _pausedByFocusGuard = true;
                    SetState(InputSessionState.Paused, "目标窗口失去焦点，已自动暂停");
                }

                Notify(new UserNotification(
                    ErrorSeverity.Warning,
                    "目标窗口已失去焦点，输入已暂停",
                    "为避免文本流入其它窗口，输入已自动暂停。",
                    "切回目标窗口即自动继续；按 F10 可彻底停止。"));
                break;
        }
    }

    /// <summary>
    /// 焦点回到目标窗口：只对"由焦点守护造成的暂停"自动继续。
    /// 用户自己按的暂停不会被这里覆盖。
    /// </summary>
    private void HandleFocusRegained()
    {
        if (!_pausedByFocusGuard || _state != InputSessionState.Paused)
        {
            return;
        }

        _pausedByFocusGuard = false;
        _scheduler.Resume();
        SetState(InputSessionState.Typing, "焦点已回到目标窗口，继续输入");

        Notify(new UserNotification(
            ErrorSeverity.Info,
            "已继续输入",
            "目标窗口重新获得焦点，输入已自动恢复。"));
    }

    private void SetState(InputSessionState state, string reason)
    {
        var previous = _state;
        if (previous == state)
        {
            return;
        }

        _state = state;
        _logger?.Info($"状态迁移 {previous} → {state}（{reason}）", "Session");
        StateChanged?.Invoke(this, new StateChangedEventArgs(previous, state, reason));
    }

    private void Notify(UserNotification notification)
        => RaiseOnUi(() => Notified?.Invoke(this, notification));

    /// <summary>把回调切回界面线程；没有同步上下文时（单测、无头场景）直接执行。</summary>
    private void RaiseOnUi(Action action)
    {
        var context = _uiContext;
        if (context is null || ReferenceEquals(context, SynchronizationContext.Current))
        {
            action();
            return;
        }

        context.Post(_ => action(), null);
    }

    /// <summary>把调度器的进度回调转发到 <see cref="ProgressChanged"/>（并切回界面线程）。</summary>
    private sealed class ProgressRelay : IProgress<ProgressInfo>
    {
        private readonly InputSessionController _owner;

        public ProgressRelay(InputSessionController owner) => _owner = owner;

        public void Report(ProgressInfo value)
            => _owner.RaiseOnUi(() => _owner.ProgressChanged?.Invoke(_owner, value));
    }
}
