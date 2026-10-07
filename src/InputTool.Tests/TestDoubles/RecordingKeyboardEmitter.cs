using System.Text;
using InputTool.Core.Injection;
using InputTool.Core.Interop;
using InputTool.Core.Parsing;
using InputTool.Core.Scheduling;

namespace InputTool.Tests.TestDoubles;

/// <summary>
/// 记录型注入器：记录"注入了哪些单元、顺序如何"，并可按需返回跳过 / 失败。
/// </summary>
public sealed class RecordingKeyboardEmitter : IKeyboardEmitter
{
    /// <summary>构造记录型注入器。</summary>
    /// <param name="events">可选事件日志（与假等待器共用，用于断言 P / 注入 / L 的先后顺序）。</param>
    public RecordingKeyboardEmitter(List<string>? events = null)
    {
        Events = events ?? new List<string>();
    }

    /// <summary>共享事件日志。</summary>
    public List<string> Events { get; }

    /// <summary>已注入的单元（按注入顺序）。</summary>
    public List<InputUnit> Emitted { get; } = new();

    /// <summary>按单元决定返回值；未设置时恒为 Sent。</summary>
    public Func<InputUnit, EmitResult>? ResultFactory { get; set; }

    /// <summary>AttachThread 调用次数。</summary>
    public int AttachCount { get; private set; }

    /// <summary>DetachThread 调用次数。</summary>
    public int DetachCount { get; private set; }

    /// <summary>ReleaseAll 调用次数（急停安全的关键断言）。</summary>
    public int ReleaseAllCount { get; private set; }

    /// <summary>记录到的注入线程 ID。</summary>
    public int ThreadId { get; private set; }

    /// <summary>IsInjectable 的返回值。</summary>
    public bool InjectableResult { get; set; } = true;

    /// <inheritdoc />
    public void AttachThread()
    {
        AttachCount++;
        ThreadId = Environment.CurrentManagedThreadId;
    }

    /// <inheritdoc />
    public void DetachThread() => DetachCount++;

    /// <inheritdoc />
    public EmitResult Emit(InputUnit unit, SpeedProfile profile)
    {
        Emitted.Add(unit);
        Events.Add($"emit:{unit.Kind}");

        return ResultFactory?.Invoke(unit) ?? EmitResult.Sent;
    }

    /// <inheritdoc />
    public void KeyDown(VirtualKey virtualKey) => Events.Add($"down:{virtualKey}");

    /// <inheritdoc />
    public void KeyUp(VirtualKey virtualKey) => Events.Add($"up:{virtualKey}");

    /// <inheritdoc />
    public void SendUnicode(Rune rune) => Events.Add($"unicode:{rune.Value:X4}");

    /// <inheritdoc />
    public void SendChord(params VirtualKey[] keys) => Events.Add($"chord:{string.Join("+", keys)}");

    /// <inheritdoc />
    public void ReleaseAll()
    {
        ReleaseAllCount++;
        Events.Add("release-all");
    }

    /// <inheritdoc />
    public bool IsInjectable(out string reason)
    {
        reason = InjectableResult ? "测试：可注入" : "测试：不可注入";
        return InjectableResult;
    }
}
