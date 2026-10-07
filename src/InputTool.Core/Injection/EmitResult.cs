namespace InputTool.Core.Injection;

/// <summary>单个单元的注入结果。</summary>
public enum EmitResult
{
    /// <summary>已成功注入（或按策略有意不注入，例如"不发送回车"策略下的换行单元）。</summary>
    Sent,

    /// <summary>跳过并计数：NUL、C0 控制字符、无法映射的字符（FR-008）。</summary>
    Skipped,

    /// <summary>注入失败（已重试 1 次后仍失败，FR-007.5 / E-12）。</summary>
    Failed,
}

/// <summary>修饰键集合（兼容模式的字符映射结果，§5.3.4）。</summary>
[Flags]
public enum ModifierKeys
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Win = 8,
}
