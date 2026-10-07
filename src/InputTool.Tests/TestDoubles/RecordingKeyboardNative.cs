using InputTool.Core.Abstractions;
using InputTool.Core.Hotkeys;
using InputTool.Core.Injection;

namespace InputTool.Tests.TestDoubles;

/// <summary>记录 (虚拟键, 扫描码, 标志) 三元组的假原生层，用于断言注入序列。</summary>
public sealed class RecordingKeyboardNative : IKeyboardNative
{
    private int _failedSoFar;

    /// <summary>全部调用记录。</summary>
    public List<(ushort VirtualKey, ushort ScanCode, uint Flags)> Calls { get; } = new();

    /// <summary>若为 true，则所有调用都失败。</summary>
    public bool AlwaysFail { get; set; }

    /// <summary>前 N 次调用失败（用于验证"重试一次"）。</summary>
    public int FailFirstNCalls { get; set; }

    /// <summary>失败时返回的 Win32 错误码（默认 5 = ERROR_ACCESS_DENIED）。</summary>
    public int ErrorCode { get; set; } = 5;

    /// <inheritdoc />
    public bool SendKey(ushort virtualKey, ushort scanCode, uint flags)
    {
        Calls.Add((virtualKey, scanCode, flags));

        if (AlwaysFail || _failedSoFar < FailFirstNCalls)
        {
            _failedSoFar++;
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public int GetLastError() => ErrorCode;
}

/// <summary>固定序列随机源：让抖动后的节拍可复现。</summary>
public sealed class FixedRandomSource : IRandomSource
{
    private readonly double[] _values;
    private int _index;

    /// <summary>构造固定序列随机源。</summary>
    /// <param name="values">依次返回的值（用完后循环）。</param>
    public FixedRandomSource(params double[] values) => _values = values;

    /// <inheritdoc />
    public double NextUnit() => _values.Length == 0 ? 0.5 : _values[_index++ % _values.Length];
}

/// <summary>假热键原生层：记录注册 / 注销，并可指定失败错误码。</summary>
public sealed class FakeHotkeyNative : IHotkeyNative
{
    /// <summary>成功注册的 (ID, 修饰键, 虚拟键)。</summary>
    public List<(int Id, uint Modifiers, uint VirtualKey)> Registrations { get; } = new();

    /// <summary>被注销的热键 ID。</summary>
    public List<int> Unregistrations { get; } = new();

    /// <summary>按 (修饰键, 虚拟键) 决定返回的错误码；返回 0 表示成功。</summary>
    public Func<uint, uint, int>? ResultFactory { get; set; }

    /// <inheritdoc />
    public bool Register(IntPtr windowHandle, int id, uint modifiers, uint virtualKey, out int error)
    {
        error = ResultFactory?.Invoke(modifiers, virtualKey) ?? 0;
        if (error != 0)
        {
            return false;
        }

        Registrations.Add((id, modifiers, virtualKey));
        return true;
    }

    /// <inheritdoc />
    public bool Unregister(IntPtr windowHandle, int id)
    {
        Unregistrations.Add(id);
        return true;
    }
}
