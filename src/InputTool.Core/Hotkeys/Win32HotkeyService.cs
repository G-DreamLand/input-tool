using InputTool.Core.Interop;
using InputTool.Core.Logging;

namespace InputTool.Core.Hotkeys;

/// <summary>
/// 全局热键服务。对应《软件开发设计文档》§5.6。
/// </summary>
/// <remarks>
/// 实现要点：
/// · 使用 <c>RegisterHotKey</c>，宿主窗口由表现层提供（Core 不引用 WPF）；
/// · 始终附加 <c>MOD_NOREPEAT</c>，避免长按 F9 反复切换状态；
/// · 注册失败时按 SRS 附录 B 回退到备用热键，并上报失败事件；
/// · <see cref="HotkeyRole.EmergencyStop"/> 注册失败时 <see cref="EmergencyStopAvailable"/>
///   为 false，上层必须禁用【开始】按钮（阻断式，FR-014.5）。
/// </remarks>
public sealed class Win32HotkeyService : IHotkeyService
{
    private const string StartPauseFallback = "Ctrl+Alt+P";
    private const string EmergencyStopFallback = "Ctrl+Alt+Q";

    private readonly IntPtr _windowHandle;
    private readonly IHotkeyNative _native;
    private readonly IAppLogger? _logger;

    private readonly Dictionary<HotkeyRole, HotkeyBinding> _bindings = new();
    private readonly Dictionary<int, HotkeyRole> _ids = new();

    private int _nextId = 1;
    private bool _disposed;

    /// <summary>构造热键服务。</summary>
    /// <param name="windowHandle">接收 WM_HOTKEY 的窗口句柄（通常是主窗口）。</param>
    /// <param name="native">底层注册实现；省略时使用 Win32 实现。</param>
    /// <param name="logger">日志（可选）。</param>
    public Win32HotkeyService(IntPtr windowHandle, IHotkeyNative? native = null, IAppLogger? logger = null)
    {
        _windowHandle = windowHandle;
        _native = native ?? new Win32HotkeyNative();
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<HotkeyRole, HotkeyBinding> Bindings => _bindings;

    /// <inheritdoc />
    public bool EmergencyStopAvailable => _bindings.ContainsKey(HotkeyRole.EmergencyStop);

    /// <inheritdoc />
    public event EventHandler<HotkeyPressedEventArgs>? Pressed;

    /// <inheritdoc />
    public event EventHandler<HotkeyRegistrationFailedEventArgs>? RegistrationFailed;

    /// <inheritdoc />
    public HotkeyRegistrationResult Register(HotkeyRole role, HotkeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Win32HotkeyService));
        }

        // 规则校验（§5.6.4）：非法组合直接拒绝，避免占用系统保留按键
        var validation = HotkeyValidator.Validate(gesture, role, _bindings);
        if (!validation.IsValid)
        {
            var reason = validation.Reason ?? "热键不合法。";
            RegistrationFailed?.Invoke(this, new HotkeyRegistrationFailedEventArgs(role, gesture, reason));
            return new HotkeyRegistrationResult(HotkeyRegistrationStatus.Invalid, role, gesture, 0, reason);
        }

        // 同一角色重新注册：先注销旧的，避免占用系统热键表
        UnregisterRole(role);

        var id = _nextId++;
        var modifiers = (uint)(gesture.Modifiers & ~HotkeyModifiers.NoRepeat) | Win32Constants.ModNoRepeat;

        if (!_native.Register(_windowHandle, id, modifiers, (uint)gesture.Key, out var error))
        {
            var occupied = error == Win32Constants.ErrorHotkeyAlreadyRegistered;
            var message = occupied
                ? $"“{gesture}”已被其他程序占用，请更换（FR-013.5）。"
                : $"注册热键失败（错误码 {error}）。";

            var status = occupied
                ? HotkeyRegistrationStatus.AlreadyInUse
                : HotkeyRegistrationStatus.Failed;

            _logger?.Warn($"热键注册失败 role={role} gesture={gesture} err={error}", "Hotkey");
            RegistrationFailed?.Invoke(this, new HotkeyRegistrationFailedEventArgs(role, gesture, message));
            return new HotkeyRegistrationResult(status, role, gesture, error, message);
        }

        _ids[id] = role;
        _bindings[role] = new HotkeyBinding(role, gesture, DisplayName(role));
        _logger?.Info($"热键已注册 role={role} gesture={gesture} id={id}", "Hotkey");

        return new HotkeyRegistrationResult(HotkeyRegistrationStatus.Success, role, gesture, 0, null);
    }

    /// <inheritdoc />
    public IReadOnlyList<HotkeyRegistrationResult> RegisterDefaults()
    {
        var results = new List<HotkeyRegistrationResult>(4)
        {
            RegisterWithFallback(HotkeyRole.StartPause, HotkeyGesture.DefaultStartPause, StartPauseFallback),
            RegisterWithFallback(HotkeyRole.EmergencyStop, HotkeyGesture.DefaultEmergencyStop, EmergencyStopFallback),
            RegisterWithFallback(
                HotkeyRole.StepNext,
                HotkeyGesture.DefaultStepNext,
                "Ctrl+Alt+F10"),
            Register(HotkeyRole.SpeedUp, HotkeyGesture.DefaultSpeedUp),
            Register(HotkeyRole.SpeedDown, HotkeyGesture.DefaultSpeedDown),
        };

        if (!EmergencyStopAvailable)
        {
            _logger?.Error("紧急停止热键注册失败：在成功注册之前不允许开始输入（FR-014.5）", null, "Hotkey");
        }

        return results;
    }

    /// <inheritdoc />
    public void UnregisterAll()
    {
        foreach (var id in _ids.Keys.ToList())
        {
            _native.Unregister(_windowHandle, id);
        }

        _ids.Clear();
        _bindings.Clear();
    }

    /// <inheritdoc />
    public bool TryHandleWindowMessage(int message, IntPtr wParam)
    {
        if (message != Win32Constants.WmHotkey)
        {
            return false;
        }

        if (!_ids.TryGetValue(wParam.ToInt32(), out var role))
        {
            return false;
        }

        Pressed?.Invoke(this, new HotkeyPressedEventArgs(role, DateTimeOffset.Now));
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
    }

    // ── 内部实现 ────────────────────────────────────────────────────────

    private HotkeyRegistrationResult RegisterWithFallback(
        HotkeyRole role,
        HotkeyGesture preferred,
        string fallbackText)
    {
        var result = Register(role, preferred);
        if (result.Succeeded)
        {
            return result;
        }

        // 首选被占用时回退到备用组合（SRS 附录 B）
        if (HotkeyGesture.TryParse(fallbackText, out var fallback))
        {
            _logger?.Warn($"热键 {preferred} 不可用（{result.Message}），尝试备用组合 {fallback}", "Hotkey");
            var fallbackResult = Register(role, fallback);
            if (fallbackResult.Succeeded)
            {
                return fallbackResult;
            }
        }

        return result;
    }

    private void UnregisterRole(HotkeyRole role)
    {
        if (!_bindings.TryGetValue(role, out _))
        {
            return;
        }

        var existingId = _ids.Where(pair => pair.Value == role).Select(pair => pair.Key).FirstOrDefault();
        if (existingId != 0)
        {
            _native.Unregister(_windowHandle, existingId);
            _ids.Remove(existingId);
        }

        _bindings.Remove(role);
    }

    private static string DisplayName(HotkeyRole role) => role switch
    {
        HotkeyRole.StartPause => "开始/暂停",
        HotkeyRole.EmergencyStop => "紧急停止",
        HotkeyRole.SpeedUp => "速度加快",
        HotkeyRole.SpeedDown => "速度减慢",
        HotkeyRole.StepNext => "逐行步进",
        HotkeyRole.Custom1 => "自定义 1",
        _ => "自定义 2",
    };
}
