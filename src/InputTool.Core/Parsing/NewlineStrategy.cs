namespace InputTool.Core.Parsing;

/// <summary>
/// 换行策略（FR-009）。决定一个换行单元最终发出什么按键。
/// </summary>
public enum NewlineStrategy
{
    /// <summary>默认：VK_RETURN down/up。终端执行命令、聊天发送、普通换行。</summary>
    Enter,

    /// <summary>Shift+Enter：聊天软件"换行不发送"。</summary>
    ShiftEnter,

    /// <summary>Ctrl+Enter：部分软件的发送快捷键。</summary>
    CtrlEnter,

    /// <summary>
    /// 仅行末尾回车：忽略所有**非末尾位置**的换行单元，
    /// 位于单元序列末尾的换行单元正常发出（Q-01 决议）。
    /// </summary>
    LastLineEnterOnly,

    /// <summary>不发送回车：所有换行单元被忽略，行内容直接拼接。</summary>
    None,

    /// <summary>P2：用户自定义的一组虚拟键。</summary>
    Custom,
}
