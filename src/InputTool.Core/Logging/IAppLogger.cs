namespace InputTool.Core.Logging;

/// <summary>日志级别（FR-021）。</summary>
public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>
/// 应用日志。对应《软件开发设计文档》§5.8。
/// 隐私约束（FR-021.3 / NFR-014）：
/// 默认**不记录**输入内容，只记录数量与位置索引；
/// 仅当用户在设置中显式确认开启 <c>logging.logContent</c> 时，
/// 才允许记录内容摘要（前 80 字符）。
/// </summary>
public interface IAppLogger
{
    /// <summary>日志目录（界面"打开日志目录"用）。</summary>
    string LogDirectory { get; }

    /// <summary>指定级别是否启用（热路径应先判断，避免无谓的字符串拼接）。</summary>
    bool IsEnabled(LogLevel level);

    void Debug(string message, string category = "App");

    void Info(string message, string category = "App");

    void Warn(string message, string category = "App");

    void Error(string message, Exception? exception = null, string category = "App");

    /// <summary>切换最低记录级别（设置界面改动后调用）。</summary>
    void SetMinimumLevel(LogLevel level);

    /// <summary>把队列中的日志刷到磁盘（退出前调用）。</summary>
    void Flush();
}
