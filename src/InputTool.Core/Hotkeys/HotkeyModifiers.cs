namespace InputTool.Core.Hotkeys;

/// <summary>
/// 热键修饰键。取值与 Win32 的 MOD_* 一致，可直接转换（§5.6.2）。
/// </summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,

    /// <summary>MOD_NOREPEAT：抑制长按连发，避免长按 F9 反复切换状态。</summary>
    NoRepeat = 0x4000,
}

/// <summary>热键角色。热键 ID 与角色的对应关系集中在热键层实现（§5.6.2）。</summary>
public enum HotkeyRole
{
    /// <summary>开始 / 暂停 / 继续（默认 F9，FR-013）。</summary>
    StartPause,

    /// <summary>紧急停止（默认 F10，FR-014）。注册失败会阻断开始输入。</summary>
    EmergencyStop,

    /// <summary>速度加快（默认 Ctrl+Alt+↑，FR-028）。</summary>
    SpeedUp,

    /// <summary>速度减慢（默认 Ctrl+Alt+↓，FR-028）。</summary>
    SpeedDown,

    /// <summary>P2 预留。</summary>
    Custom1,

    /// <summary>P2 预留。</summary>
    Custom2,
}
