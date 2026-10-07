namespace InputTool.Core.Hotkeys;

/// <summary>角色 → 热键的绑定关系。</summary>
/// <param name="Role">角色。</param>
/// <param name="Gesture">热键组合。</param>
/// <param name="DisplayName">界面显示名（如"开始/暂停"）。</param>
public sealed record HotkeyBinding(HotkeyRole Role, HotkeyGesture Gesture, string DisplayName);

/// <summary>热键注册结果状态。</summary>
public enum HotkeyRegistrationStatus
{
    Success,

    /// <summary>被其它程序占用（RegisterHotKey 失败，FR-013.5 / FR-025.4）。</summary>
    AlreadyInUse,

    /// <summary>组合本身非法（未通过 <see cref="HotkeyValidator"/>）。</summary>
    Invalid,

    /// <summary>其它 Win32 失败。</summary>
    Failed,
}

/// <summary>热键注册结果。</summary>
/// <param name="Status">状态。</param>
/// <param name="Role">角色。</param>
/// <param name="Gesture">尝试注册的组合。</param>
/// <param name="Win32Error">RegisterHotKey 返回的 Win32 错误码。</param>
/// <param name="Message">面向用户的说明。</param>
public sealed record HotkeyRegistrationResult(
    HotkeyRegistrationStatus Status,
    HotkeyRole Role,
    HotkeyGesture? Gesture,
    int Win32Error,
    string? Message)
{
    /// <summary>是否注册成功。</summary>
    public bool Succeeded => Status == HotkeyRegistrationStatus.Success;
}

/// <summary>热键被按下的事件参数。</summary>
/// <param name="Role">被触发的角色。</param>
/// <param name="PressedAt">触发时刻。</param>
public sealed record HotkeyPressedEventArgs(HotkeyRole Role, DateTimeOffset PressedAt);

/// <summary>热键注册失败的事件参数。</summary>
/// <param name="Role">角色。</param>
/// <param name="Gesture">失败的组合。</param>
/// <param name="Reason">失败原因（用于界面提示）。</param>
public sealed record HotkeyRegistrationFailedEventArgs(HotkeyRole Role, HotkeyGesture Gesture, string Reason);
