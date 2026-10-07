using System.Text;
using InputTool.Core.Configuration;
using InputTool.Core.Interop;
using InputTool.Core.Logging;
using InputTool.Core.Parsing;
using InputTool.Core.Scheduling;

namespace InputTool.Core.Injection;

/// <summary>
/// 键盘注入器。对应《软件开发设计文档》§5.5。
/// </summary>
/// <remarks>
/// 铁律与约定：
/// · 所有键盘事件都经此实现，其它模块不得直接调用 <c>SendInput</c>（§2.2 原则 3）；
/// · 注入线程亲和：<see cref="AttachThread"/> 后跨线程调用立即抛异常，让错误在开发期暴露（§9.3 约定 1）；
/// · 每次按键都成对（down/up），并在 <see cref="ReleaseAll"/> 中逆序补齐（FR-007.8 / NFR-005）；
/// · 单次失败重试 1 次，仍失败则记录并返回 Failed，由调度器计数后继续（FR-007.5 / E-12）。
/// </remarks>
public sealed class Win32KeyboardEmitter : IKeyboardEmitter
{
    private const int MaxRetries = 1;

    private readonly IKeyboardNative _native;
    private readonly IKeyMapper _keyMapper;
    private readonly IInjectabilityProbe _probe;
    private readonly IAppLogger? _logger;

    /// <summary>"已按下未抬起"的键栈：逆序补发 up 的依据。</summary>
    private readonly List<VirtualKey> _pressed = new();

    private int _ownerThreadId;
    private KeyboardMode _mode = KeyboardMode.Unicode;
    private int _sendFailures;
    private int _lastErrorCode;
    private bool _loggedAccessDenied;

    /// <summary>构造注入器。</summary>
    /// <param name="native">底层发送实现；省略时使用 Win32 <c>SendInput</c>。</param>
    /// <param name="keyMapper">兼容模式字符映射；省略时使用当前键盘布局。</param>
    /// <param name="probe">可注入性探测；省略时使用系统实现。</param>
    /// <param name="logger">日志（可选）。</param>
    public Win32KeyboardEmitter(
        IKeyboardNative? native = null,
        IKeyMapper? keyMapper = null,
        IInjectabilityProbe? probe = null,
        IAppLogger? logger = null)
    {
        _native = native ?? new Win32KeyboardNative();
        _keyMapper = keyMapper ?? new KeyMapper();
        _probe = probe ?? new InjectabilityProbe();
        _logger = logger;
    }

    /// <summary>键盘注入模式：Unicode（默认）或兼容模式（FR-011.4）。</summary>
    public KeyboardMode Mode
    {
        get => _mode;
        set => _mode = value;
    }

    /// <summary>最近一次注入失败的 Win32 错误码。</summary>
    public int LastErrorCode => _lastErrorCode;

    /// <summary>累计注入失败次数（供诊断）。</summary>
    public int SendFailures => _sendFailures;

    /// <summary>是否出现过 ERROR_ACCESS_DENIED（5）——通常意味着 UIPI 限制（FR-010.6）。</summary>
    public bool HasAccessDenied => _lastErrorCode == Win32Constants.ErrorAccessDenied;

    /// <inheritdoc />
    public void AttachThread() => _ownerThreadId = Environment.CurrentManagedThreadId;

    /// <inheritdoc />
    public void DetachThread() => _ownerThreadId = 0;

    /// <inheritdoc />
    public EmitResult Emit(InputUnit unit, SpeedProfile profile)
    {
        EnsureThread();

        return unit.Kind switch
        {
            InputUnitKind.Character => EmitCharacter(unit.Rune),
            InputUnitKind.FunctionKey => TapKey(unit.Key),
            InputUnitKind.NewLine => EmitNewLine(profile),
            _ => EmitResult.Skipped,
        };
    }

    /// <inheritdoc />
    public void KeyDown(VirtualKey virtualKey)
    {
        EnsureThread();
        TryKeyDown(virtualKey);
    }

    /// <inheritdoc />
    public void KeyUp(VirtualKey virtualKey)
    {
        EnsureThread();
        TryKeyUp(virtualKey);
    }

    /// <inheritdoc />
    public void SendUnicode(Rune rune)
    {
        EnsureThread();
        SendUnicodeCore(rune);
    }

    /// <inheritdoc />
    public void SendChord(params VirtualKey[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        EnsureThread();
        TapChord(keys);
    }

    /// <inheritdoc />
    public void ReleaseAll()
    {
        // 注意：本方法不做线程亲和校验——急停与崩溃清理可能来自其它线程，
        // 它必须永远可用（FR-014.4 / NFR-005）。
        if (_pressed.Count == 0)
        {
            return;
        }

        // 逆序补发 up：先按下的后释放，避免修饰键残留导致用户键盘"被按住"
        for (var i = _pressed.Count - 1; i >= 0; i--)
        {
            SendRaw((ushort)_pressed[i], 0, Win32Constants.KeyeventfKeyUp);
        }

        _pressed.Clear();
    }

    /// <summary>
    /// 无条件释放所有修饰键（启动时清理上次非正常退出留下的按键状态，§5.9.6）。
    /// </summary>
    public void ReleaseModifiers()
    {
        var modifiers = new[]
        {
            VirtualKey.Shift, VirtualKey.Control, VirtualKey.Menu,
            VirtualKey.LeftShift, VirtualKey.RightShift,
            VirtualKey.LeftControl, VirtualKey.RightControl,
            VirtualKey.LeftMenu, VirtualKey.RightMenu,
            VirtualKey.LWin, VirtualKey.RWin,
        };

        foreach (var key in modifiers)
        {
            _native.SendKey((ushort)key, 0, Win32Constants.KeyeventfKeyUp);
        }
    }

    /// <inheritdoc />
    public bool IsInjectable(out string reason)
    {
        var check = _probe.Check();
        reason = check.Reason;
        return check.CanInject;
    }

    // ── 单元级注入 ──────────────────────────────────────────────────────

    private EmitResult EmitCharacter(Rune rune)
    {
        // NUL 不注入（SRS §5.3.3）
        if (rune.Value == 0)
        {
            return EmitResult.Skipped;
        }

        // 兼容模式：仅对 ASCII 可打印字符走"虚拟键 + 修饰键"，其余仍走 Unicode（FR-011.4/.5）
        if (_mode == KeyboardMode.Compatibility
            && rune.IsAscii
            && !Rune.IsControl(rune)
            && _keyMapper.TryMapChar((char)rune.Value, out var key, out var modifiers, out var isExtended))
        {
            return TapWithModifiers(key, modifiers, isExtended);
        }

        return SendUnicodeCore(rune);
    }

    private EmitResult EmitNewLine(SpeedProfile profile)
    {
        // 注意：LastLineEnterOnly 的"忽略非末尾换行"由调度器在列表层面处理，
        // 走到这里的换行单元都应真实发出。
        return profile.NewlineStrategy switch
        {
            NewlineStrategy.None => EmitResult.Sent,                       // 有意不注入
            NewlineStrategy.ShiftEnter => TapChord([VirtualKey.Shift, VirtualKey.Return]),
            NewlineStrategy.CtrlEnter => TapChord([VirtualKey.Control, VirtualKey.Return]),
            NewlineStrategy.Custom => EmitResult.Skipped,                  // P2：自定义键序列未实现
            _ => TapKey(VirtualKey.Return),
        };
    }

    /// <summary>
    /// Unicode 注入：把 Rune 拆成 1–2 个 UTF-16 码元，每个码元独立成对发送。
    /// 代理对的两码元连续发送，间隔远小于 1 ms（SRS §5.3.3）。
    /// </summary>
    private EmitResult SendUnicodeCore(Rune rune)
    {
        Span<char> buffer = stackalloc char[2];
        var length = rune.EncodeToUtf16(buffer);
        var ok = true;

        for (var i = 0; i < length; i++)
        {
            ok &= SendRaw(0, buffer[i], Win32Constants.KeyeventfUnicode);
            ok &= SendRaw(0, buffer[i], Win32Constants.KeyeventfUnicode | Win32Constants.KeyeventfKeyUp);
        }

        return ok ? EmitResult.Sent : EmitResult.Failed;
    }

    private EmitResult TapKey(VirtualKey key)
    {
        // down → up 连续发送：不主动 sleep，避免侵蚀节拍精度（间隔 0 ms 落在 FR-010.2 的 0–2 ms 内）
        var down = TryKeyDown(key);
        var up = TryKeyUp(key);
        return down && up ? EmitResult.Sent : EmitResult.Failed;
    }

    private EmitResult TapChord(VirtualKey[] keys)
    {
        var pressed = new List<VirtualKey>(keys.Length);
        var ok = true;

        try
        {
            foreach (var key in keys)
            {
                if (TryKeyDown(key))
                {
                    pressed.Add(key);
                }
                else
                {
                    ok = false;
                }
            }
        }
        finally
        {
            // 抬起顺序与按下相反
            for (var i = pressed.Count - 1; i >= 0; i--)
            {
                ok &= TryKeyUp(pressed[i]);
            }
        }

        return ok ? EmitResult.Sent : EmitResult.Failed;
    }

    private EmitResult TapWithModifiers(VirtualKey key, ModifierKeys modifiers, bool isExtended)
    {
        var pressed = new List<VirtualKey>(3);
        var ok = true;

        try
        {
            if (modifiers.HasFlag(ModifierKeys.Shift) && TryKeyDown(VirtualKey.Shift))
            {
                pressed.Add(VirtualKey.Shift);
            }

            if (modifiers.HasFlag(ModifierKeys.Control) && TryKeyDown(VirtualKey.Control))
            {
                pressed.Add(VirtualKey.Control);
            }

            if (modifiers.HasFlag(ModifierKeys.Alt) && TryKeyDown(VirtualKey.Menu))
            {
                pressed.Add(VirtualKey.Menu);
            }

            ok &= TryKeyDown(key, isExtended);
            ok &= TryKeyUp(key, isExtended);
        }
        finally
        {
            for (var i = pressed.Count - 1; i >= 0; i--)
            {
                ok &= TryKeyUp(pressed[i]);
            }
        }

        return ok ? EmitResult.Sent : EmitResult.Failed;
    }

    // ── 底层发送 ────────────────────────────────────────────────────────

    private bool TryKeyDown(VirtualKey key, bool extended = false)
    {
        var flags = extended ? Win32Constants.KeyeventfExtendedKey : 0u;
        if (!SendRaw((ushort)key, 0, flags))
        {
            return false;
        }

        _pressed.Add(key);
        return true;
    }

    private bool TryKeyUp(VirtualKey key, bool extended = false)
    {
        var flags = Win32Constants.KeyeventfKeyUp
                    | (extended ? Win32Constants.KeyeventfExtendedKey : 0u);

        var ok = SendRaw((ushort)key, 0, flags);

        // 即使抬起失败也从栈里移除：它已经不代表"系统认为按下的键"
        _pressed.Remove(key);
        return ok;
    }

    /// <summary>发送一个按键事件，失败时重试一次（FR-007.5）。</summary>
    private bool SendRaw(ushort virtualKey, ushort scanCode, uint flags)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (_native.SendKey(virtualKey, scanCode, flags))
            {
                return true;
            }

            _lastErrorCode = _native.GetLastError();

            if (_lastErrorCode == Win32Constants.ErrorAccessDenied && !_loggedAccessDenied)
            {
                _loggedAccessDenied = true;
                _logger?.Warn(
                    "注入被拒绝（ERROR_ACCESS_DENIED）：目标窗口可能以管理员身份运行，"
                    + "这是 Windows UIPI 安全机制限制（FR-010.6 / C-06）",
                    "Input");
            }
        }

        _sendFailures++;
        _logger?.Warn(
            $"注入失败 vk=0x{virtualKey:X2} scan=0x{scanCode:X4} flags=0x{flags:X4} err={_lastErrorCode}",
            "Input");

        return false;
    }

    private void EnsureThread()
    {
        var owner = _ownerThreadId;
        if (owner != 0 && owner != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException(
                "键盘注入器只能在其绑定的输入线程上使用（设计文档 §9.3 约定 1）。");
        }
    }
}
