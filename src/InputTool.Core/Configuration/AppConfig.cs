namespace InputTool.Core.Configuration;

/// <summary>速度单位（FR-006：用户可切换两种表达）。</summary>
public enum SpeedUnit
{
    /// <summary>毫秒/字符（默认）。</summary>
    MsPerChar,

    /// <summary>字符/秒（CPS）。</summary>
    CharsPerSecond,
}

/// <summary>倒计时浮层位置（§6.3）。</summary>
public enum OverlayPosition
{
    /// <summary>主显示器右下角（默认）。</summary>
    BottomRight,

    /// <summary>鼠标附近。</summary>
    NearCursor,
}

/// <summary>界面主题（SRS §6.2）。</summary>
public enum ThemeMode
{
    /// <summary>跟随系统。</summary>
    System,

    Light,

    Dark,
}

/// <summary>焦点守护在丢失焦点时的动作（FR-020.3）。</summary>
public enum FocusGuardAction
{
    /// <summary>自动暂停（默认）。</summary>
    Pause,

    /// <summary>继续输入（仅记录警告）。</summary>
    Continue,

    /// <summary>自动停止。示例：</summary>
    Stop,
}

/// <summary>键盘注入模式（FR-011.4）。</summary>
public enum KeyboardMode
{
    /// <summary>Unicode 注入（默认，与键盘布局和输入法无关）。</summary>
    Unicode,

    /// <summary>兼容模式：ASCII 用"虚拟键 + 修饰键"，非 ASCII 仍尝试 Unicode 注入。</summary>
    Compatibility,
}

/// <summary>窗口位置与大小（FR-018.2）。</summary>
public sealed record WindowPlacement(int X = 400, int Y = 200, int Width = 760, int Height = 620);

/// <summary>文件相关配置。</summary>
public sealed record FileConfig(
    string? Path = null,
    string Encoding = "auto",
    IReadOnlyList<string>? RecentFiles = null);

/// <summary>速度相关配置（对应 config.json 的 speed 节点）。</summary>
public sealed record SpeedConfig(
    int IntervalMs = 200,
    SpeedUnit Unit = SpeedUnit.MsPerChar,
    double JitterPercent = 0,
    int LineExtraDelayMs = 0,
    int PreEnterDelayMs = 0);

/// <summary>行为相关配置（对应 config.json 的 behavior 节点）。</summary>
public sealed record BehaviorConfig(
    string NewlineStrategy = "enter",
    bool AppendTrailingEnter = false,
    bool ParseEscapes = false,
    bool IgnoreEmptyLines = false,
    bool SkipCommentLines = false,
    bool ClearTargetFirst = false,
    bool FocusGuard = false,
    bool StepMode = false,
    FocusGuardAction FocusGuardAction = FocusGuardAction.Pause,
    KeyboardMode KeyboardMode = KeyboardMode.Unicode,
    int PauseAutoStopMinutes = 0,
    int MaxUnitsPerRun = 100_000);

/// <summary>倒计时配置。</summary>
public sealed record CountdownConfig(
    int Seconds = 3,
    OverlayPosition OverlayPosition = OverlayPosition.BottomRight);

/// <summary>热键配置（文本形式，便于用户手工编辑）。</summary>
public sealed record HotkeysConfig(
    string StartPause = "F9",
    // 注：主构造参数默认值取不到下面那个常量（不在同一作用域），只能写死字面量；
    // 二者必须一致，已有用例 `默认急停热键_应与常量一致` 守住。
    string EmergencyStop = "F10",
    string SpeedUp = "Ctrl+Alt+Up",
    string SpeedDown = "Ctrl+Alt+Down")
{
    /// <summary>【紧急停止】当前出厂默认。</summary>
    public const string DefaultEmergencyStop = "F10";

    /// <summary>
    /// v1 时期【紧急停止】的出厂默认值。
    /// </summary>
    /// <remarks>
    /// v1→v2 迁移据此识别"这个值只是旧出厂默认、不是用户自己改的"，从而安全纠正。
    /// 背景：热键以配置为准，而配置一旦落盘就永远带着旧默认值——
    /// 改了默认值却不迁移，老用户按界面上写着的 F10 会毫无反应（真机报障）。
    /// </remarks>
    public const string LegacyEmergencyStopDefaultV1 = "Ctrl+Alt+F11";
}

/// <summary>界面配置。</summary>
public sealed record UiConfig(
    ThemeMode Theme = ThemeMode.System,
    WindowPlacement? Window = null,
    bool CloseToTray = true,
    bool PlayFinishSound = true,
    string Language = "zh-CN");

/// <summary>日志配置（FR-021）。</summary>
public sealed record LoggingConfig(
    bool Enabled = true,
    string Level = "info",
    int RetentionDays = 7,
    bool LogContent = false);

/// <summary>
/// 目标窗口配置（§6.3 / FR-012.4）。
/// 注意：固定窗口的**句柄不持久化**——窗口句柄每次运行都会变，存下来只会误导。
/// 这里只记住"用哪种选择方式"。
/// </summary>
public sealed record TargetConfig(
    Application.TargetWindowMode Mode = Application.TargetWindowMode.Foreground);

/// <summary>
/// 应用配置根对象。与 SRS §8.1 的 config.json 一一对应，字段全部带默认值，
/// 保证新增字段时旧配置文件反序列化后自动取默认值（向前兼容，§5.7.1）。
/// </summary>
public sealed record AppConfig
{
    /// <summary>
    /// 配置结构版本，用于迁移。
    /// 缺省值刻意保持 1：老配置或手写配置里没有 <c>version</c> 字段时按**最老版本**处理，
    /// 迁移链才会跑起来（<see cref="AppConfig.Default"/> 另行显式带上当前版本）。
    /// </summary>
    public int Version { get; init; } = 1;

    public FileConfig File { get; init; } = new();
    public SpeedConfig Speed { get; init; } = new();
    public BehaviorConfig Behavior { get; init; } = new();
    public CountdownConfig Countdown { get; init; } = new();
    public HotkeysConfig Hotkeys { get; init; } = new();
    public UiConfig Ui { get; init; } = new();
    public LoggingConfig Logging { get; init; } = new();
    public TargetConfig Target { get; init; } = new();

    /// <summary>默认配置（SRS 附录 A）。</summary>
    public static AppConfig Default { get; } = new() { Version = CurrentVersion };

    /// <summary>
    /// 当前支持的配置结构版本。
    /// v2：把 v1 时期落盘的旧急停默认值 Ctrl+Alt+F11 迁移为 F10。
    /// </summary>
    public const int CurrentVersion = 2;
}
