namespace InputTool.Core.Configuration;

/// <summary>
/// 配置读写服务。对应《软件开发设计文档》§5.7。
/// 关键行为：
/// · 定位路径：便携模式（portable.flag 优先）&gt; exe 同目录可写且有 config.json &gt; %APPDATA%\InputTool（Q-07 决议）；
/// · 损坏时备份为 config.bad.json 并用默认值启动（E-14）；
/// · 变更后 1 秒去抖落盘（FR-018.4）；
/// · 原子写：先写 .tmp 再替换，避免断电写坏配置。
/// </summary>
public interface IConfigService
{
    /// <summary>当前生效的配置。</summary>
    AppConfig Current { get; }

    /// <summary>配置文件绝对路径（界面"打开配置文件所在目录"用）。</summary>
    string ConfigFilePath { get; }

    /// <summary>是否运行在便携模式。</summary>
    bool IsPortableMode { get; }

    /// <summary>配置发生变化（去抖落盘之前即触发，供界面即时生效）。</summary>
    event EventHandler<AppConfig>? Changed;

    /// <summary>加载配置；文件不存在用默认值并立即落盘，损坏则备份后回退默认值。</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 修改配置：传入纯函数式的变换（<c>cfg =&gt; cfg with { ... }</c>），
    /// 触发 <see cref="Changed"/> 并启动 1 秒去抖保存。
    /// 之所以用 <see cref="Func{T,T}"/> 而不是 <c>Action</c>，是因为配置对象是不可变记录。
    /// </summary>
    void Update(Func<AppConfig, AppConfig> transform);

    /// <summary>立即落盘（退出前调用）。</summary>
    Task SaveNowAsync(CancellationToken cancellationToken = default);

    /// <summary>恢复默认设置（FR-018.5）。</summary>
    Task ResetToDefaultsAsync(CancellationToken cancellationToken = default);
}
