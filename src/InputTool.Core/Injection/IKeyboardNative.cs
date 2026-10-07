namespace InputTool.Core.Injection;

/// <summary>
/// 对 <c>SendInput</c> 的最小封装（一次调用 = 一个按键事件）。
/// </summary>
/// <remarks>
/// 抽出这层的目的（对应设计文档 §12.1 可测性设计）：
/// 让 <c>Win32KeyboardEmitter</c> 的"按键序列"逻辑可以在没有真实桌面会话的
/// 单元测试里断言——测试替身只需记录 (虚拟键, 扫描码, 标志) 三元组，
/// 无需构造 Win32 结构体，也无需真的注入按键。
/// </remarks>
public interface IKeyboardNative
{
    /// <summary>
    /// 发送一个键盘事件。
    /// </summary>
    /// <param name="virtualKey">虚拟键码；Unicode 模式必须为 0。</param>
    /// <param name="scanCode">Unicode 模式下为 UTF-16 码元，否则为扫描码。</param>
    /// <param name="flags">KEYEVENTF_* 组合。</param>
    /// <returns>成功返回 true；失败返回 false，错误码用 <see cref="GetLastError"/> 读取。</returns>
    bool SendKey(ushort virtualKey, ushort scanCode, uint flags);

    /// <summary>最近一次 <see cref="SendKey"/> 失败时的 Win32 错误码（§5.5.7）。</summary>
    int GetLastError();
}
