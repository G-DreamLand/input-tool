using InputTool.Core.Abstractions;
using InputTool.Core.Injection;
using InputTool.Core.Logging;
using InputTool.Core.Parsing;

namespace InputTool.Core.Scheduling;

/// <summary>
/// 节拍调度器。对应《软件开发设计文档》§5.4。
/// </summary>
/// <remarks>
/// 核心约定：
/// · **绝对时间基准**：目标时刻 = 基准 + Σ 间隔，杜绝长时间运行漂移（FR-007.1）；
/// · 输入在**专用后台线程**上串行执行，UI 卡顿不影响节奏（FR-007.6）；
/// · 落后超过 200 ms 时进入追赶模式（跳过等待、时间债清零），防止雪崩式追赶；
/// · 暂停恢复时**重建时间基准**，暂停时长不计入节拍（FR-015）；
/// · 停止响应 ≤ 200 ms：等待循环每一轮都检查停止与暂停标志（FR-007.7 / NFR-006）；
/// · 无论何种路径结束，都会在 finally 中调用 <c>ReleaseAll()</c>，保证不留按下未抬起的键（FR-007.8 / NFR-005）。
/// </remarks>
public sealed class InputScheduler : IInputScheduler
{
    private const int CatchUpThresholdMs = 200;
    private const int ProgressReportIntervalMs = 100;
    private const int ConsecutiveFailureLimit = 20;

    private readonly IClock _clock;
    private readonly IPrecisionWaiter _waiter;
    private readonly IKeyboardEmitter _emitter;
    private readonly IIntervalCalculator _calculator;
    private readonly IAppLogger? _logger;

    // 初始为"已放行"：Start 前没有暂停
    private readonly ManualResetEventSlim _pauseGate = new(initialState: true);

    /// <summary>逐行步进开关（跨行即停，等 Resume 再走）。</summary>
    private volatile bool _stepMode;

    /// <summary>上一个已处理单元的行号；-1 表示尚未处理任何单元（第一行不触发步进）。</summary>
    private int _lastLineIndex = -1;

    /// <inheritdoc />
    public bool StepMode
    {
        get => _stepMode;
        set => _stepMode = value;
    }

    /// <inheritdoc />
    /// <remarks>载荷给出**下一行**的行号，供界面预览该行内容。</remarks>
    public event EventHandler<StepWaitingEventArgs>? StepWaiting;

    private volatile bool _stopRequested;
    private volatile SchedulerState _state = SchedulerState.Idle;
    private Thread? _worker;
    private int _completed;

    /// <summary>构造调度器。</summary>
    /// <param name="emitter">键盘注入器（必需）。</param>
    /// <param name="clock">时钟；省略时使用 <see cref="StopwatchClock"/>。</param>
    /// <param name="waiter">高精度等待；省略时使用 <see cref="PrecisionWaiter"/>。</param>
    /// <param name="calculator">节拍计算；省略时使用 <see cref="Scheduling.IntervalCalculator"/>。</param>
    /// <param name="logger">日志（可选）。</param>
    public InputScheduler(
        IKeyboardEmitter emitter,
        IClock? clock = null,
        IPrecisionWaiter? waiter = null,
        IIntervalCalculator? calculator = null,
        IAppLogger? logger = null)
    {
        _emitter = emitter ?? throw new ArgumentNullException(nameof(emitter));
        _clock = clock ?? new StopwatchClock();
        _waiter = waiter ?? new PrecisionWaiter();
        _calculator = calculator ?? new IntervalCalculator();
        _logger = logger;
    }

    /// <inheritdoc />
    public SchedulerState State => _state;

    /// <inheritdoc />
    public event EventHandler<SessionFinishedEventArgs>? Finished;

    /// <summary>已完成的单元数（供外部查询进度）。</summary>
    public int CompletedUnits => _completed;

    /// <inheritdoc />
    public void Start(
        IReadOnlyList<InputUnit> units,
        Func<SpeedProfile> profileProvider,
        IProgress<ProgressInfo>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(profileProvider);

        if (_state != SchedulerState.Idle)
        {
            throw new InvalidOperationException("调度器已在运行，不能重复启动。");
        }

        _stopRequested = false;
        _completed = 0;
        _pauseGate.Set();
        _state = SchedulerState.Running;

        _worker = new Thread(() => RunLoop(units, profileProvider, progress))
        {
            IsBackground = true,
            Name = "InputTool.Typing",
            // 略高于普通线程，减少被 UI 抢占造成的节拍抖动
            Priority = ThreadPriority.AboveNormal,
        };

        _logger?.Info($"开始输入 units={units.Count}", "Input");
        _worker.Start();
    }

    /// <inheritdoc />
    public void Pause()
    {
        if (_state != SchedulerState.Running)
        {
            return;
        }

        _pauseGate.Reset();
        _state = SchedulerState.Paused;
        _logger?.Info($"已暂停于 {_completed} 个单元", "Input");
    }

    /// <inheritdoc />
    public void Resume()
    {
        if (_state != SchedulerState.Paused)
        {
            return;
        }

        _state = SchedulerState.Running;
        _pauseGate.Set();
        _logger?.Info($"继续，从第 {_completed} 个单元起", "Input");
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (_state == SchedulerState.Idle)
        {
            return;
        }

        _stopRequested = true;
        _state = SchedulerState.Stopping;

        // 唤醒可能正阻塞在暂停门上的输入线程（双保险：标志位 + 门）
        _pauseGate.Set();
    }

    // ── 输入线程主循环 ──────────────────────────────────────────────────

    private void RunLoop(
        IReadOnlyList<InputUnit> units,
        Func<SpeedProfile> profileProvider,
        IProgress<ProgressInfo>? progress)
    {
        var frequency = _clock.Frequency;
        var startedTimestamp = _clock.GetTimestamp();
        var targetTicks = startedTimestamp;

        var index = 0;
        var skipped = 0;
        var failed = 0;
        var consecutiveFailed = 0;
        var lastReportMs = 0.0;
        var outcome = SessionOutcome.Completed;
        string? message = null;

        using var timerScope = TimerResolutionScope.Begin();
        _emitter.AttachThread();

        try
        {
            while (index < units.Count)
            {
                if (_stopRequested)
                {
                    outcome = SessionOutcome.Stopped;
                    break;
                }

                // 逐行步进（新增）：一旦要跨到新的一行，就先停住等步进热键。
                // 放在下面那个暂停门检查之前，就能把"等待"、"急停"、
                // "恢复后重建时间基准"全部复用现成逻辑。
                // 空行同样是一行，因此连续空行会要求连续按键（脚本语义）。
                if (_stepMode && _lastLineIndex >= 0 && units[index].LineIndex != _lastLineIndex)
                {
                    Pause();
                    StepWaiting?.Invoke(this, new StepWaitingEventArgs(units[index].LineIndex));
                }

                _lastLineIndex = units[index].LineIndex;

                // 暂停门：恢复时重建时间基准（FR-015）
                if (!_pauseGate.IsSet)
                {
                    _state = SchedulerState.Paused;
                    _pauseGate.Wait();

                    if (_stopRequested)
                    {
                        outcome = SessionOutcome.Stopped;
                        break;
                    }

                    targetTicks = _clock.GetTimestamp();
                    _state = SchedulerState.Running;
                }

                var profile = profileProvider() ?? SpeedProfile.Default;
                var unit = units[index];
                var isNewline = unit.Kind == InputUnitKind.NewLine
                                && profile.NewlineStrategy != NewlineStrategy.None;

                var interval = _calculator.NextIntervalMs(unit, profile);
                var target = targetTicks + ToTicks(interval, frequency);

                // 换行前延时 P：必须发生在回车之前
                if (isNewline && profile.PreEnterDelayMs > 0
                    && !_waiter.Delay(profile.PreEnterDelayMs, IsInterrupted))
                {
                    continue;
                }

                var now = _clock.GetTimestamp();
                if (now > target + ToTicks(CatchUpThresholdMs, frequency))
                {
                    _logger?.Warn(
                        $"调度落后 {FromTicks(now - target, frequency):F0} ms，跳过等待以免雪崩式追赶",
                        "Input");
                    target = now;
                }
                else if (!_waiter.WaitUntil(target, IsInterrupted))
                {
                    continue;
                }

                // 仅行末尾回车：非末尾位置的换行单元不注入（Q-01 决议）。
                // 该判断需要知道"是否最后一个单元"，因此放在调度器而非注入器里。
                var suppressNewline = isNewline
                                      && profile.NewlineStrategy == NewlineStrategy.LastLineEnterOnly
                                      && index != units.Count - 1;

                var emitResult = suppressNewline ? EmitResult.Sent : _emitter.Emit(unit, profile);
                switch (emitResult)
                {
                    case EmitResult.Skipped:
                        skipped++;
                        consecutiveFailed = 0;
                        break;
                    case EmitResult.Failed:
                        failed++;
                        consecutiveFailed++;
                        break;
                    default:
                        consecutiveFailed = 0;
                        break;
                }

                targetTicks = target;
                index++;
                _completed = index;

                if (consecutiveFailed >= ConsecutiveFailureLimit)
                {
                    outcome = SessionOutcome.Error;
                    message = "连续注入失败：目标窗口可能以管理员身份运行（Windows UIPI 限制），"
                              + "请以管理员身份重启本工具后重试。";
                    _logger?.Error($"连续 {consecutiveFailed} 次注入失败，终止会话", null, "Input");
                    break;
                }

                // 行间隔额外延时 L：发生在回车之后，给目标程序执行 / 渲染留时间
                if (isNewline && profile.LineExtraDelayMs > 0)
                {
                    if (!_waiter.Delay(profile.LineExtraDelayMs, IsInterrupted))
                    {
                        continue;
                    }

                    targetTicks += ToTicks(profile.LineExtraDelayMs, frequency);
                }

                // 进度上报 ≤ 10 次/秒（FR-016.5）
                var elapsedMs = _clock.GetElapsedTime(startedTimestamp).TotalMilliseconds;
                if (elapsedMs - lastReportMs >= ProgressReportIntervalMs || index == units.Count)
                {
                    lastReportMs = elapsedMs;
                    progress?.Report(BuildProgress(
                        index,
                        units.Count,
                        _clock.GetElapsedTime(startedTimestamp),
                        skipped,
                        failed,
                        profile));
                }
            }
        }
        catch (Exception ex)
        {
            outcome = SessionOutcome.Error;
            message = ex.Message;
            _logger?.Error("输入线程异常终止", ex, "Input");
        }
        finally
        {
            // 无论正常结束、停止还是异常，都必须补齐抬起事件（FR-007.8 / NFR-005）
            try
            {
                _emitter.ReleaseAll();
            }
            catch (Exception ex)
            {
                _logger?.Warn($"清理按键状态失败：{ex.Message}", "Input");
            }

            _emitter.DetachThread();
            _state = SchedulerState.Idle;

            var totalElapsed = _clock.GetElapsedTime(startedTimestamp);
            _logger?.Info(
                $"输入结束 outcome={outcome} completed={index}/{units.Count} "
                + $"用时={totalElapsed.TotalSeconds:F1}s 跳过={skipped} 失败={failed}",
                "Input");

            Finished?.Invoke(
                this,
                new SessionFinishedEventArgs(outcome, index, units.Count, totalElapsed, skipped, failed, message));
        }
    }

    /// <summary>等待是否需要中断（停止或暂停）；返回 true 表示应当中止当前等待。</summary>
    private bool IsInterrupted() => _stopRequested || !_pauseGate.IsSet;

    private static ProgressInfo BuildProgress(
        int completed,
        int total,
        TimeSpan elapsed,
        int skipped,
        int failed,
        SpeedProfile profile)
    {
        var remainingUnits = Math.Max(0, total - completed);

        // 用"已用时间 / 已完成单元数"作为有效节拍：抖动与换行附加延时都被自然摊进去了，
        // 比"剩余单元 × 标称节拍"更贴近实际，随时间收敛（FR-016.3 / AC-16）。
        var averageMs = completed > 0
            ? elapsed.TotalMilliseconds / completed
            : profile.IntervalMs;

        var remaining = TimeSpan.FromMilliseconds(averageMs * remainingUnits);

        return new ProgressInfo(completed, total, elapsed, remaining, DateTimeOffset.Now + remaining, skipped, failed);
    }

    private static long ToTicks(double milliseconds, long frequency) => (long)(milliseconds / 1000.0 * frequency);

    private static double FromTicks(long ticks, long frequency) => ticks * 1000.0 / frequency;
}
