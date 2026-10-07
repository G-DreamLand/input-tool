using System.Text;
using InputTool.Core.Interop;
using InputTool.Core.Parsing;
using InputTool.Core.Scheduling;

namespace InputTool.Core.Injection;

/// <summary>
/// 键盘注入器。对应《软件开发设计文档》§5.5。
/// 铁律（§2.2 原则 3）：所有键盘事件必须经过本接口，禁止在别处直接调用 SendInput。
/// 线程约定（§5.5.1 / §9.3）：<see cref="AttachThread"/> 绑定注入线程，
/// 跨线程调用将立即抛出 <see cref="InvalidOperationException"/>，以便开发期早暴露错误。
/// </summary>
public interface IKeyboardEmitter
{
    /// <summary>绑定当前线程为注入线程。</summary>
    void AttachThread();

    /// <summary>解除线程绑定。</summary>
    void DetachThread();

    /// <summary>注入一个输入单元（内部保证 down/up 成对、失败重试一次）。</summary>
    EmitResult Emit(InputUnit unit, SpeedProfile profile);

    /// <summary>发送一次按键按下（必须与 <see cref="KeyUp"/> 成对）。</summary>
    void KeyDown(VirtualKey virtualKey);

    /// <summary>发送一次按键抬起。</summary>
    void KeyUp(VirtualKey virtualKey);

    /// <summary>以 Unicode 方式注入一个字符（代理对拆成 1–2 个 UTF-16 码元）。</summary>
    void SendUnicode(Rune rune);

    /// <summary>发送一组组合键，内部保证"按下顺序 → 抬起逆序"，绝不残留修饰键。</summary>
    void SendChord(params VirtualKey[] keys);

    /// <summary>
    /// 释放所有未抬起的键（逆序补发 up）并清空内部记录。
    /// 急停、异常、进程启动清理都必须调用（FR-007.8、FR-014.4、NFR-005）。
    /// </summary>
    void ReleaseAll();

    /// <summary>当前是否可注入；不可注入时给出原因（FR-010.4）。</summary>
    bool IsInjectable(out string reason);
}
