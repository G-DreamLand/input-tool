namespace InputTool.Core.Interop;

/// <summary>
/// Win32 虚拟键码（Virtual-Key Codes）。
/// 依据《软件开发设计文档》§5.5.3 与附录 A：功能键一律用虚拟键注入，
/// 文本字符走 Unicode 注入，两者不混用。
/// </summary>
public enum VirtualKey : ushort
{
    None = 0x00,

    /// <summary>Backspace，对应文本中的 \b（FR-008）。</summary>
    Back = 0x08,

    /// <summary>Tab，对应文本中的 \t（FR-008）。</summary>
    Tab = 0x09,

    Clear = 0x0C,

    /// <summary>Enter，换行策略的核心按键，必须是真实的 VK_RETURN（FR-008）。</summary>
    Return = 0x0D,

    Shift = 0x10,
    Control = 0x11,

    /// <summary>Alt 键（VK_MENU）。</summary>
    Menu = 0x12,

    Pause = 0x13,
    Capital = 0x14,

    /// <summary>Esc，需"解析转义序列"开关打开时才会出现（FR-008）。</summary>
    Escape = 0x1B,

    /// <summary>空格，命令行中为参数分隔符（FR-008）。</summary>
    Space = 0x20,

    /// <summary>Page Up。</summary>
    Prior = 0x21,

    /// <summary>Page Down。</summary>
    Next = 0x22,

    End = 0x23,
    Home = 0x24,
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,

    Insert = 0x2D,

    /// <summary>Delete，P2 预留（FR-026 清理目标框内容）。</summary>
    Delete = 0x2E,

    LWin = 0x5B,
    RWin = 0x5C,

    NumPad0 = 0x60,
    NumPad1 = 0x61,
    NumPad2 = 0x62,
    NumPad3 = 0x63,
    NumPad4 = 0x64,
    NumPad5 = 0x65,
    NumPad6 = 0x66,
    NumPad7 = 0x67,
    NumPad8 = 0x68,
    NumPad9 = 0x69,
    Multiply = 0x6A,
    Add = 0x6B,
    Subtract = 0x6D,
    Decimal = 0x6E,
    Divide = 0x6F,

    /// <summary>F1，热键候选主键范围 F1–F12（FR-025）。</summary>
    F1 = 0x70,
    F2 = 0x71,
    F3 = 0x72,
    F4 = 0x73,
    F5 = 0x74,
    F6 = 0x75,
    F7 = 0x76,
    F8 = 0x77,

    /// <summary>F9，默认【开始/暂停】热键（FR-013）。</summary>
    F9 = 0x78,

    /// <summary>F10，默认【紧急停止】热键（FR-014）。</summary>
    F10 = 0x79,

    F11 = 0x7A,
    F12 = 0x7B,

    NumLock = 0x90,
    ScrollLock = 0x91,

    LeftShift = 0xA0,
    RightShift = 0xA1,
    LeftControl = 0xA2,
    RightControl = 0xA3,
    LeftMenu = 0xA4,
    RightMenu = 0xA5,

    Semicolon = 0xBA,
    Plus = 0xBB,
    Comma = 0xBC,
    Minus = 0xBD,
    Period = 0xBE,
    Question = 0xBF,
    Tilde = 0xC0,
    OpenBracket = 0xDB,
    Backslash = 0xDC,
    CloseBracket = 0xDD,
    Quote = 0xDE,

    // ── 字母与数字主键（热键主键用，FR-025.2）───────────────────────────
    // 命名以 D 开头是因为 C# 枚举成员不能以数字开头；显示时由
    // HotkeyGesture.ToString() 还原为 "1".."9"。

    D0 = 0x30,
    D1 = 0x31,
    D2 = 0x32,
    D3 = 0x33,
    D4 = 0x34,
    D5 = 0x35,
    D6 = 0x36,
    D7 = 0x37,
    D8 = 0x38,
    D9 = 0x39,

    A = 0x41,
    B = 0x42,
    C = 0x43,
    D = 0x44,
    E = 0x45,
    F = 0x46,
    G = 0x47,
    H = 0x48,
    I = 0x49,
    J = 0x4A,
    K = 0x4B,
    L = 0x4C,
    M = 0x4D,
    N = 0x4E,
    O = 0x4F,
    P = 0x50,
    Q = 0x51,
    R = 0x52,
    S = 0x53,
    T = 0x54,
    U = 0x55,
    V = 0x56,
    W = 0x57,
    X = 0x58,
    Y = 0x59,
    Z = 0x5A,
}
