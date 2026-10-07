namespace InputTool.Core.Hotkeys;

/// <summary>热键校验结果。</summary>
/// <param name="IsValid">是否合法。</param>
/// <param name="Reason">不合法原因（直接用于界面提示）。</param>
public sealed record HotkeyValidationResult(bool IsValid, string? Reason)
{
    /// <summary>合法。</summary>
    public static HotkeyValidationResult Valid { get; } = new(true, null);

    /// <summary>非法。</summary>
    public static HotkeyValidationResult Invalid(string reason) => new(false, reason);
}

/// <summary>
/// 热键合法性校验（FR-025.3、SRS 附录 B）。规则：
/// 1. 至少含 1 个修饰键，或主键为 F1–F12；
/// 2. 不得与已注册的其它角色重复；
/// 3. 不得使用系统保留组合（Ctrl+Alt+Del、Win+L、Alt+Tab、Alt+F4、Ctrl+Shift+Esc）；
/// 4. 主键不能是纯修饰键；
/// 5. 主键不能是单 Esc（Esc 已用于取消倒计时，Q-02 决议）。
/// </summary>
public static class HotkeyValidator
{
    /// <summary>是否为系统保留组合（SRS 附录 B）。</summary>
    public static bool IsReserved(HotkeyGesture gesture)
    {
        var mods = gesture.Modifiers & ~HotkeyModifiers.NoRepeat;
        var key = gesture.Key;

        bool ctrl = mods.HasFlag(HotkeyModifiers.Control);
        bool alt = mods.HasFlag(HotkeyModifiers.Alt);
        bool shift = mods.HasFlag(HotkeyModifiers.Shift);
        bool win = mods.HasFlag(HotkeyModifiers.Win);

        // Ctrl+Alt+Del（安全注意序列，系统独占）
        if (ctrl && alt && key == Interop.VirtualKey.Delete) return true;

        // Win+L（锁屏）
        if (win && key == Interop.VirtualKey.L) return true;

        // Alt+Tab（切换窗口）
        if (alt && key == Interop.VirtualKey.Tab) return true;

        // Alt+F4（关闭窗口）
        if (alt && key == Interop.VirtualKey.F4) return true;

        // Ctrl+Shift+Esc（任务管理器）
        if (ctrl && shift && key == Interop.VirtualKey.Escape) return true;

        return false;
    }

    /// <summary>按全部规则校验一个待注册的热键。</summary>
    /// <param name="gesture">待校验组合。</param>
    /// <param name="role">所属角色（用于排除自身重复）。</param>
    /// <param name="existing">当前已注册的绑定。</param>
    public static HotkeyValidationResult Validate(
        HotkeyGesture gesture,
        HotkeyRole role,
        IReadOnlyDictionary<HotkeyRole, HotkeyBinding> existing)
    {
        // 规则 4：主键不能是纯修饰键
        if (IsModifierKey(gesture.Key))
        {
            return HotkeyValidationResult.Invalid("请按下一个主键（修饰键不能单独作为热键）。");
        }

        // 规则 5：单 Esc 已被"取消倒计时"占用（Q-02 决议）
        if (gesture.Key == Interop.VirtualKey.Escape &&
            (gesture.Modifiers & ~HotkeyModifiers.NoRepeat) == HotkeyModifiers.None)
        {
            return HotkeyValidationResult.Invalid("Esc 已用于取消倒计时，请改用其它组合。");
        }

        // 规则 3：系统保留组合
        if (IsReserved(gesture))
        {
            return HotkeyValidationResult.Invalid("该组合为系统保留（如 Ctrl+Alt+Del、Win+L、Alt+Tab、Alt+F4、Ctrl+Shift+Esc）。");
        }

        // 规则 1：至少含 1 个修饰键，或主键为 F1–F12
        var modifiers = gesture.Modifiers & ~HotkeyModifiers.NoRepeat;
        if (modifiers == HotkeyModifiers.None && !IsFunctionKey(gesture.Key))
        {
            return HotkeyValidationResult.Invalid("该组合不安全：请使用 F1–F12，或搭配 Ctrl / Alt / Shift / Win。");
        }

        // 规则 2：不得与已注册的其它角色重复
        foreach (var pair in existing)
        {
            if (pair.Key == role)
            {
                continue;
            }

            if (pair.Value.Gesture.Key == gesture.Key &&
                (pair.Value.Gesture.Modifiers & ~HotkeyModifiers.NoRepeat) == modifiers)
            {
                return HotkeyValidationResult.Invalid($"该组合已用于【{pair.Value.DisplayName}】。");
            }
        }

        return HotkeyValidationResult.Valid;
    }

    /// <summary>是否为功能键 F1–F12。</summary>
    public static bool IsFunctionKey(Interop.VirtualKey key)
        => (ushort)key is >= 0x70 and <= 0x7B;

    /// <summary>是否为修饰键本身。</summary>
    public static bool IsModifierKey(Interop.VirtualKey key)
        => key is Interop.VirtualKey.Shift or Interop.VirtualKey.Control or Interop.VirtualKey.Menu
            or Interop.VirtualKey.LeftShift or Interop.VirtualKey.RightShift
            or Interop.VirtualKey.LeftControl or Interop.VirtualKey.RightControl
            or Interop.VirtualKey.LeftMenu or Interop.VirtualKey.RightMenu
            or Interop.VirtualKey.LWin or Interop.VirtualKey.RWin;
}
