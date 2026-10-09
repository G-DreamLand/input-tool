using System.Text;
using System.Text.Json;
using InputTool.Core.Logging;
using InputTool.Core.Scheduling;

namespace InputTool.Core.Configuration;

/// <summary>
/// JSON 配置服务。对应《软件开发设计文档》§5.7。
/// </summary>
/// <remarks>
/// 关键行为：
/// · 路径判定（Q-07 决议）：便携标记 &gt; exe 同目录可写且有 config.json &gt; %APPDATA%\InputTool；
/// · 损坏时备份为 config.bad.json 并回退默认值（E-14）；
/// · 变更后 1 秒去抖落盘（FR-018.4）；
/// · **原子写**：先写 .tmp 再替换，避免断电写坏配置；
/// · 加载与写入都做一次归一化（clamp），保证越界的手工编辑不会带进运行时。
/// </remarks>
public sealed class JsonConfigService : IConfigService, IDisposable
{
    /// <summary>去抖窗口（FR-018.4）。</summary>
    public static readonly TimeSpan SaveDebounce = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private readonly IAppLogger? _logger;
    private readonly Timer _saveTimer;

    private AppConfig _current = AppConfig.Default;
    private bool _pendingSave;
    private bool _disposed;

    /// <summary>构造配置服务。</summary>
    /// <param name="overrideDirectory">强制使用的目录（单测用；为 null 时按规则判定）。</param>
    /// <param name="applicationDirectory">应用程序目录（默认 exe 所在目录）。</param>
    /// <param name="logger">日志（可选）。</param>
    public JsonConfigService(
        string? overrideDirectory = null,
        string? applicationDirectory = null,
        IAppLogger? logger = null)
    {
        _logger = logger;

        var (path, portable) = ResolvePaths(overrideDirectory, applicationDirectory);
        ConfigFilePath = path;
        IsPortableMode = portable;

        _saveTimer = new Timer(_ => SavePending(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc />
    public AppConfig Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <inheritdoc />
    public string ConfigFilePath { get; }

    /// <inheritdoc />
    public bool IsPortableMode { get; }

    /// <inheritdoc />
    public event EventHandler<AppConfig>? Changed;

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(ConfigFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(ConfigFilePath))
            {
                Replace(AppConfig.Default);
                await SaveNowAsync(cancellationToken).ConfigureAwait(false);
                _logger?.Info($"配置文件不存在，已生成默认配置：{ConfigFilePath}", "Config");
                return;
            }

            var json = await File.ReadAllTextAsync(ConfigFilePath, cancellationToken).ConfigureAwait(false);
            var loaded = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig)
                         ?? throw new JsonException("反序列化结果为空");

            var migrated = Migrate(loaded, out var migrationChanged);
            Replace(Normalize(migrated));

            if (migrationChanged)
            {
                // 迁移结果必须落盘：否则每次启动都要再迁一遍，用户也无从知道值被纠正过
                await SaveNowAsync(cancellationToken).ConfigureAwait(false);
                _logger?.Info("配置迁移结果已落盘", "Config");
            }

            _logger?.Info(
                $"配置已加载：{ConfigFilePath}（便携模式={IsPortableMode}）",
                "Config");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            BackupCorrupted();
            Replace(AppConfig.Default);
            _logger?.Warn($"配置文件损坏或不可读，已备份并使用默认值：{ex.Message}", "Config");
            await SaveNowAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Update(Func<AppConfig, AppConfig> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);

        AppConfig updated;

        lock (_gate)
        {
            updated = Normalize(transform(_current) ?? _current);
            _current = updated;
            _pendingSave = true;

            if (!_disposed)
            {
                _saveTimer.Change(SaveDebounce, Timeout.InfiniteTimeSpan);
            }
        }

        Changed?.Invoke(this, updated);
    }

    /// <inheritdoc />
    public async Task SaveNowAsync(CancellationToken cancellationToken = default)
    {
        AppConfig snapshot;

        lock (_gate)
        {
            snapshot = _current;
            _pendingSave = false;
        }

        var json = JsonSerializer.Serialize(snapshot, ConfigJsonContext.Default.AppConfig);
        var directory = Path.GetDirectoryName(ConfigFilePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 原子写：先写临时文件再替换，避免写到一半掉电导致配置损坏
        var temporaryPath = ConfigFilePath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
        File.Move(temporaryPath, ConfigFilePath, overwrite: true);
    }

    /// <inheritdoc />
    public async Task ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        Replace(AppConfig.Default);
        await SaveNowAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, Current);
        _logger?.Info("已恢复默认设置（FR-018.5）", "Config");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _saveTimer.Dispose();
    }

    // ── 内部实现 ────────────────────────────────────────────────────────

    private void SavePending()
    {
        lock (_gate)
        {
            if (!_pendingSave || _disposed)
            {
                return;
            }
        }

        try
        {
            SaveNowAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.Warn($"配置落盘失败：{ex.Message}", "Config");
        }
    }

    private void Replace(AppConfig config)
    {
        lock (_gate)
        {
            _current = config;
        }
    }

    /// <summary>损坏备份：config.bad.json；已存在同名时追加时间戳（E-14）。</summary>
    private void BackupCorrupted()
    {
        try
        {
            var directory = Path.GetDirectoryName(ConfigFilePath) ?? ".";
            var backup = Path.Combine(directory, "config.bad.json");

            if (File.Exists(backup))
            {
                backup = Path.Combine(directory, $"config.bad.{DateTime.Now:yyyyMMddHHmmss}.json");
            }

            File.Copy(ConfigFilePath, backup, overwrite: true);
            _logger?.Info($"已备份损坏的配置：{backup}", "Config");
        }
        catch (Exception ex)
        {
            _logger?.Warn($"备份损坏配置失败：{ex.Message}", "Config");
        }
    }

    /// <summary>版本迁移。</summary>
    /// <param name="config">从磁盘读到的配置。</param>
    /// <param name="changed">迁移是否真的改动了内容（决定要不要立刻落盘）。</param>
    /// <remarks>
    /// 版本高于本程序时**保留字段**原样返回（向前兼容，不猜测未来语义）；
    /// 版本低于本程序时按顺序执行 v1→v2→… 的迁移。
    /// </remarks>
    private AppConfig Migrate(AppConfig config, out bool changed)
    {
        changed = false;

        if (config.Version >= AppConfig.CurrentVersion)
        {
            return config;
        }

        var migrated = config;

        // ── v1 → v2 ──
        // v1 时期【紧急停止】的出厂默认是 Ctrl+Alt+F11，后来改成 F10。
        // 热键**以配置为准**，而旧配置里存着那个旧默认值，
        // 于是每次启动都是"先注册 F10、再被配置改成 Ctrl+Alt+F11"，
        // 用户按界面写着的 F10 毫无反应（真机报障）。
        // 只纠正"恰好等于 v1 出厂默认"的值；用户自己改过的组合一律不覆盖。
        if (migrated.Version < 2 && IsLegacyEmergencyStopDefault(migrated.Hotkeys?.EmergencyStop))
        {
            var hotkeys = migrated.Hotkeys ?? new HotkeysConfig();
            migrated = migrated with
            {
                Hotkeys = hotkeys with { EmergencyStop = HotkeysConfig.DefaultEmergencyStop },
            };
            changed = true;

            _logger?.Info(
                "配置迁移 v1→v2：紧急停止热键 "
                + $"{HotkeysConfig.LegacyEmergencyStopDefaultV1} → {HotkeysConfig.DefaultEmergencyStop}"
                + "（旧值是 v1 的出厂默认，留着会让当前的 F10 失效）",
                "Config");
        }

        return migrated with { Version = AppConfig.CurrentVersion };
    }

    /// <summary>是否恰好是 v1 的急停出厂默认值（容忍大小写与空格差异）。</summary>
    private static bool IsLegacyEmergencyStopDefault(string? text)
        => !string.IsNullOrWhiteSpace(text)
           && string.Equals(
               text.Replace(" ", string.Empty, StringComparison.Ordinal),
               HotkeysConfig.LegacyEmergencyStopDefaultV1,
               StringComparison.OrdinalIgnoreCase);

    /// <summary>把越界值 clamp 回合法范围（手工编辑过的配置也要能安全吃下）。</summary>
    /// <remarks>
    /// 分节全部做非空兜底：旧版本配置文件缺少新分节、或用户把某个分节手写成 <c>null</c> 时，
    /// 反序列化会给出 null。配置问题绝不能让程序起不来（E-14）。
    /// </remarks>
    private static AppConfig Normalize(AppConfig config)
    {
        var speed = config.Speed ?? new SpeedConfig();
        var countdown = config.Countdown ?? new CountdownConfig();
        var behavior = config.Behavior ?? new BehaviorConfig();
        var file = config.File ?? new FileConfig();
        var hotkeys = config.Hotkeys ?? new HotkeysConfig();
        var ui = config.Ui ?? new UiConfig();
        var logging = config.Logging ?? new LoggingConfig();
        var target = config.Target ?? new TargetConfig();

        var normalized = SpeedProfileValidator.Normalize(new SpeedProfile(
            IntervalMs: speed.IntervalMs,
            JitterPercent: speed.JitterPercent,
            LineExtraDelayMs: speed.LineExtraDelayMs,
            PreEnterDelayMs: speed.PreEnterDelayMs,
            NewlineStrategy: Parsing.NewlineStrategy.Enter));

        return config with
        {
            Version = AppConfig.CurrentVersion,
            Speed = speed with
            {
                IntervalMs = normalized.IntervalMs,
                JitterPercent = normalized.JitterPercent,
                LineExtraDelayMs = normalized.LineExtraDelayMs,
                PreEnterDelayMs = normalized.PreEnterDelayMs,
            },
            Countdown = countdown with
            {
                Seconds = Math.Clamp(countdown.Seconds, 0, 10),
            },
            Behavior = behavior with
            {
                MaxUnitsPerRun = Math.Clamp(behavior.MaxUnitsPerRun, 1_000, 1_000_000),
                PauseAutoStopMinutes = Math.Clamp(behavior.PauseAutoStopMinutes, 0, 24 * 60),
                NewlineStrategy = string.IsNullOrWhiteSpace(behavior.NewlineStrategy)
                    ? "enter"
                    : behavior.NewlineStrategy,
            },
            File = file with
            {
                RecentFiles = (file.RecentFiles ?? Array.Empty<string>())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(10)
                    .ToArray(),
            },
            Hotkeys = hotkeys,
            Ui = ui,
            Target = target,
            Logging = logging with
            {
                RetentionDays = Math.Clamp(logging.RetentionDays, 1, 90),
                Level = string.IsNullOrWhiteSpace(logging.Level) ? "info" : logging.Level,
            },
        };
    }

    /// <summary>路径判定（Q-07 决议）。</summary>
    private static (string Path, bool Portable) ResolvePaths(string? overrideDirectory, string? applicationDirectory)
    {
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return (Path.Combine(overrideDirectory, "config.json"), true);
        }

        var appDirectory = string.IsNullOrWhiteSpace(applicationDirectory)
            ? AppContext.BaseDirectory
            : applicationDirectory;

        // 1) 显式便携标记
        if (File.Exists(Path.Combine(appDirectory, "portable.flag")))
        {
            return (Path.Combine(appDirectory, "config.json"), true);
        }

        // 2) exe 同目录已存在配置文件且目录可写
        var appConfigPath = Path.Combine(appDirectory, "config.json");
        if (File.Exists(appConfigPath) && IsDirectoryWritable(appDirectory))
        {
            return (appConfigPath, true);
        }

        // 3) 用户配置目录
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return (Path.Combine(roaming, "InputTool", "config.json"), false);
    }

    private static bool IsDirectoryWritable(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
