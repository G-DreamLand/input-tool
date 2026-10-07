namespace InputTool.Core.Hotkeys;

/// <summary>
/// 全局热键服务。对应《软件开发设计文档》§5.6。
/// 实现要点：
/// · 宿主窗口使用主窗口的 HwndSource，通过 AddHook 捕获 WM_HOTKEY(0x0312)；
/// · 始终附加 MOD_NOREPEAT；
/// · <see cref="HotkeyRole.EmergencyStop"/> 注册失败时 <see cref="EmergencyStopAvailable"/>
///   为 false，上层必须禁用【开始】按钮（阻断式，FR-014.5）。
/// </summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>当前生效的绑定集合。</summary>
    IReadOnlyDictionary<HotkeyRole, HotkeyBinding> Bindings { get; }

    /// <summary>紧急停止热键是否已成功注册。为 false 时不允许开始输入。</summary>
    bool EmergencyStopAvailable { get; }

    /// <summary>注册（或重新注册）某个角色的热键；成功后 <see cref="Bindings"/> 立即更新。</summary>
    HotkeyRegistrationResult Register(HotkeyRole role, HotkeyGesture gesture);

    /// <summary>按默认值注册全部热键（启动时调用）。</summary>
    IReadOnlyList<HotkeyRegistrationResult> RegisterDefaults();

    /// <summary>注销全部热键（退出与移交提权前必须调用）。</summary>
    void UnregisterAll();

    /// <summary>热键被按下。</summary>
    event EventHandler<HotkeyPressedEventArgs>? Pressed;

    /// <summary>注册失败（用于界面红色提示与引导改键）。</summary>
    event EventHandler<HotkeyRegistrationFailedEventArgs>? RegistrationFailed;

    /// <summary>
    /// 宿主窗口消息的转发入口（<c>WM_HOTKEY</c>）。
    /// </summary>
    /// <remarks>
    /// Core 层不能引用 WPF，因此由表现层安装 <c>HwndSource</c> 钩子，
    /// 把收到的每条消息原样转交给这里；本服务只关心自己注册的热键 ID。
    /// </remarks>
    /// <param name="message">窗口消息 ID。</param>
    /// <param name="wParam">消息 wParam（热键 ID 在其中）。</param>
    /// <returns>消息已被本服务消费时返回 true。</returns>
    bool TryHandleWindowMessage(int message, IntPtr wParam);
}
