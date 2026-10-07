using InputTool.Core.Interop;

namespace InputTool.Core.Hotkeys;

/// <summary>
/// 一个热键组合。对应《软件开发设计文档》§5.6.1。
/// 文本形式：<c>"F9"</c>、<c>"Ctrl+Alt+Up"</c>（大小写不敏感，顺序无关）。
/// </summary>
/// <param name="Modifiers">修饰键。</param>
/// <param name="Key">主键。</param>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, VirtualKey Key)
{
    /// <summary>默认【开始/暂停】热键：F9。</summary>
    public static HotkeyGesture DefaultStartPause { get; } = new(HotkeyModifiers.NoRepeat, VirtualKey.F9);

    /// <summary>默认【紧急停止】热键：F10。</summary>
    /// <summary>
    /// 紧急停止的默认热键（F10）。
    /// </summary>
    /// <remarks>
    /// 注：逐行步进**没有独立热键**——它复用「开始/暂停」键（F9）：
    /// 调度器停在行首时状态就是"已暂停"，按 F9 即继续输入下一行。
    /// 少一个热键就少一处冲突，也更好记。
    /// </remarks>
    public static HotkeyGesture DefaultEmergencyStop { get; } = new(HotkeyModifiers.NoRepeat, VirtualKey.F10);

    /// <summary>默认【速度加快】热键：Ctrl+Alt+↑。</summary>
    public static HotkeyGesture DefaultSpeedUp { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat, VirtualKey.Up);

    /// <summary>默认【速度减慢】热键：Ctrl+Alt+↓。</summary>
    public static HotkeyGesture DefaultSpeedDown { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat, VirtualKey.Down);

    /// <summary>
    /// 解析文本形式的热键（大小写不敏感、修饰键顺序无关、容忍空格）。
    /// 失败返回 false，调用方应回退默认值（配置损坏场景，E-14）。
    /// </summary>
    /// <example><c>"F9"</c>、<c>"Ctrl+Alt+Up"</c>、<c>"ctrl + shift + a"</c>。</example>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = DefaultStartPause;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        var key = VirtualKey.None;
        var hasKey = false;

        foreach (var rawPart in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (rawPart.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "alt":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "win":
                case "windows":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                default:
                    if (!TryParseKey(rawPart, out key))
                    {
                        return false;
                    }

                    hasKey = true;
                    break;
            }
        }

        if (!hasKey)
        {
            return false;
        }

        // 统一附加 MOD_NOREPEAT，避免长按连发（§5.6.2）
        gesture = new HotkeyGesture(modifiers | HotkeyModifiers.NoRepeat, key);
        return true;
    }

    /// <summary>解析文本形式的热键；失败抛出。</summary>
    public static HotkeyGesture Parse(string text)
        => TryParse(text, out var gesture) ? gesture : throw new FormatException($"无法解析热键：{text}");

    /// <summary>规范化显示文本，例如 <c>Ctrl+Alt+Up</c>。修饰键顺序固定为 Ctrl → Alt → Shift → Win。</summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyToDisplay(Key));
        return string.Join("+", parts);
    }

    /// <summary>解析单个主键：支持 "F9" / "Up" 之类的枚举名，也支持单个字母或数字。</summary>
    private static bool TryParseKey(string text, out VirtualKey key)
    {
        key = VirtualKey.None;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.Length == 1)
        {
            var c = char.ToUpperInvariant(trimmed[0]);
            if (c is >= 'A' and <= 'Z')
            {
                key = (VirtualKey)(0x41 + (c - 'A'));
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                key = (VirtualKey)(0x30 + (c - '0'));
                return true;
            }
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out key) && key != VirtualKey.None;
    }

    /// <summary>主键显示名：字母与数字还原为字符本身，其余用枚举名。</summary>
    private static string KeyToDisplay(VirtualKey key)
    {
        var value = (ushort)key;
        if (value is >= 0x30 and <= 0x39 || value is >= 0x41 and <= 0x5A)
        {
            return ((char)value).ToString();
        }

        return key.ToString();
    }
}
