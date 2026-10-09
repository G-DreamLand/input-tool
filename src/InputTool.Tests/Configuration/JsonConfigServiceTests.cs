using System.Text;
using System.Text.Json;
using InputTool.Core.Configuration;

namespace InputTool.Tests.Configuration;

/// <summary>
/// 配置持久化（FR-018、E-14、设计文档 §5.7）。
/// </summary>
public sealed class JsonConfigServiceTests : IDisposable
{
    private readonly string _directory;

    public JsonConfigServiceTests()
    {
        _directory = Path.Combine(AppContext.BaseDirectory, "test-tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败不影响测试结论
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private JsonConfigService CreateService() => new(overrideDirectory: _directory);

    // ── 加载 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task 配置文件不存在_应使用默认值并生成文件()
    {
        using var service = CreateService();

        await service.LoadAsync(Ct);

        service.Current.Should().Be(AppConfig.Default);
        File.Exists(service.ConfigFilePath).Should().BeTrue("默认配置应立即落盘，便于用户手工编辑");
    }

    [Fact]
    public async Task 指定目录时_应视为便携模式()
    {
        using var service = CreateService();

        await service.LoadAsync(Ct);

        service.IsPortableMode.Should().BeTrue();
    }

    [Fact]
    public async Task 配置文件损坏_应备份并回退默认值()
    {
        var path = Path.Combine(_directory, "config.json");
        await File.WriteAllTextAsync(path, "{ 这不是合法 JSON", Ct);

        using var service = CreateService();
        await service.LoadAsync(Ct);

        service.Current.Should().Be(AppConfig.Default);
        File.Exists(Path.Combine(_directory, "config.bad.json")).Should().BeTrue("损坏文件必须留档（E-14）");
    }

    [Fact]
    public async Task 已有备份时_损坏文件应另存为带时间戳的备份()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "config.bad.json"), "旧的备份", Ct);
        await File.WriteAllTextAsync(Path.Combine(_directory, "config.json"), "坏内容", Ct);

        using var service = CreateService();
        await service.LoadAsync(Ct);

        Directory.EnumerateFiles(_directory, "config.bad.*.json").Should().NotBeEmpty();
    }

    [Fact]
    public async Task 缺少新分节的旧配置_应取默认值而不是崩溃()
    {
        var path = Path.Combine(_directory, "config.json");
        await File.WriteAllTextAsync(path, """{ "version": 1 }""", Ct);

        using var service = CreateService();
        await service.LoadAsync(Ct);

        service.Current.Speed.Should().Be(new SpeedConfig());
        service.Current.Behavior.Should().Be(new BehaviorConfig());
        service.Current.Countdown.Should().Be(new CountdownConfig());
        service.Current.Hotkeys.Should().Be(new HotkeysConfig());
        service.Current.Ui.Should().Be(new UiConfig());
        service.Current.Logging.Should().Be(new LoggingConfig());
        service.Current.File.Encoding.Should().Be("auto");
        service.Current.File.RecentFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task 分节被手写成null_也不应崩溃()
    {
        var path = Path.Combine(_directory, "config.json");
        await File.WriteAllTextAsync(
            path,
            """{ "version": 1, "speed": null, "behavior": null, "logging": null, "ui": null }""",
            Ct);

        using var service = CreateService();

        var act = async () => await service.LoadAsync(Ct);

        await act.Should().NotThrowAsync("配置写坏不能让程序起不来（E-14）");
        service.Current.Speed.Should().Be(new SpeedConfig());
        service.Current.Behavior.Should().Be(new BehaviorConfig());
    }

    [Fact]
    public async Task 空的新行策略_应回退为enter()
    {
        var path = Path.Combine(_directory, "config.json");
        await File.WriteAllTextAsync(path, """{ "version": 1, "behavior": { "newlineStrategy": "   " } }""", Ct);

        using var service = CreateService();
        await service.LoadAsync(Ct);

        service.Current.Behavior.NewlineStrategy.Should().Be("enter");
    }

    [Fact]
    public async Task 越界的手工配置_应被归一化到合法范围()
    {
        var path = Path.Combine(_directory, "config.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "version": 1,
              "speed": { "intervalMs": 999999, "jitterPercent": 9, "lineExtraDelayMs": -5 },
              "countdown": { "seconds": 99 },
              "behavior": { "maxUnitsPerRun": 10 },
              "logging": { "retentionDays": 999 }
            }
            """,
            Ct);

        using var service = CreateService();
        await service.LoadAsync(Ct);

        service.Current.Speed.IntervalMs.Should().Be(5000);
        service.Current.Speed.JitterPercent.Should().Be(0.5);
        service.Current.Speed.LineExtraDelayMs.Should().Be(0);
        service.Current.Countdown.Seconds.Should().Be(10);
        service.Current.Behavior.MaxUnitsPerRun.Should().Be(1000);
        service.Current.Logging.RetentionDays.Should().Be(90);
    }

    [Fact]
    public void 默认急停热键_应与常量一致()
    {
        // 主构造参数的默认值只能是字面量，无法直接引用常量；
        // 这条用例守住这处重复，避免将来只改一边。
        new HotkeysConfig().EmergencyStop.Should().Be(HotkeysConfig.DefaultEmergencyStop);
        AppConfig.Default.Hotkeys.EmergencyStop.Should().Be(HotkeysConfig.DefaultEmergencyStop);
    }
    // ── 版本迁移（v1 → v2）──────────────────────────────────────────────

    /// <summary>写一个 v1 配置（急停为指定值）到配置文件。</summary>
    private async Task WriteV1ConfigAsync(string emergencyStop, int version = 1)
    {
        var path = Path.Combine(_directory, "config.json");
        await File.WriteAllTextAsync(
            path,
            $$"""{ "version": {{version}}, "hotkeys": { "emergencyStop": "{{emergencyStop}}" } }""",
            Ct);
    }

    [Fact]
    public async Task 旧配置里的急停出厂默认值_应迁移为当前的F10()
    {
        // 回归用例（真机报障：按 F10 毫无反应）：
        // v1 的出厂默认是 Ctrl+Alt+F11，后来改成 F10；但热键"以配置为准"，
        // 旧配置里存着旧默认值 → 每次启动都是先注册 F10、再被配置改成 Ctrl+Alt+F11。
        await WriteV1ConfigAsync("Ctrl+Alt+F11");

        using var service = CreateService();
        await service.LoadAsync(Ct);

        service.Current.Hotkeys.EmergencyStop.Should().Be(
            HotkeysConfig.DefaultEmergencyStop,
            "旧出厂默认值必须迁移，否则界面上的 F10 永远不生效");
        service.Current.Version.Should().Be(AppConfig.CurrentVersion);
    }

    [Fact]
    public async Task 旧默认值的大小写与空格差异_也应被识别并迁移()
    {
        await WriteV1ConfigAsync("ctrl + alt + f11");

        using var service = CreateService();
        await service.LoadAsync(Ct);

        service.Current.Hotkeys.EmergencyStop.Should().Be(HotkeysConfig.DefaultEmergencyStop);
    }

    [Fact]
    public async Task 用户自己改过的急停热键_迁移不得覆盖()
    {
        await WriteV1ConfigAsync("Ctrl+Shift+F12");

        using var service = CreateService();
        await service.LoadAsync(Ct);

        service.Current.Hotkeys.EmergencyStop.Should().Be(
            "Ctrl+Shift+F12",
            "迁移只纠正 v1 的出厂默认值，不能动用户自己选的组合");
    }

    [Fact]
    public async Task 已是当前版本的配置_不再迁移()
    {
        await WriteV1ConfigAsync("Ctrl+Alt+F11", version: AppConfig.CurrentVersion);

        using var service = CreateService();
        await service.LoadAsync(Ct);

        service.Current.Hotkeys.EmergencyStop.Should().Be(
            "Ctrl+Alt+F11",
            "版本已是当前，文件怎么写就怎么用（迁移不得反复生效）");
    }

    [Fact]
    public async Task 迁移结果应立刻落盘()
    {
        await WriteV1ConfigAsync("Ctrl+Alt+F11");

        using (var service = CreateService())
        {
            await service.LoadAsync(Ct);
        }

        var json = await File.ReadAllTextAsync(Path.Combine(_directory, "config.json"), Ct);
        json.Should().Contain("F10");
        json.Should().NotContain("Ctrl+Alt+F11", "迁移后不该再残留旧默认值");
    }

    [Fact]
    public async Task 未迁移的配置_不应因为加载而改写文件()
    {
        await WriteV1ConfigAsync("Ctrl+Shift+F12");

        var path = Path.Combine(_directory, "config.json");
        var before = await File.ReadAllTextAsync(path, Ct);

        using (var service = CreateService())
        {
            await service.LoadAsync(Ct);
        }

        (await File.ReadAllTextAsync(path, Ct)).Should().Be(before, "没有可迁移的内容时不该多写一次盘");
    }
    // ── 保存 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task 修改后落盘_重新加载应得到相同结果()
    {
        using (var first = CreateService())
        {
            await first.LoadAsync(Ct);
            first.Update(config => config with
            {
                Speed = config.Speed with { IntervalMs = 150, JitterPercent = 0.2 },
                Behavior = config.Behavior with { AppendTrailingEnter = true, KeyboardMode = KeyboardMode.Compatibility },
            });
            await first.SaveNowAsync(Ct);
        }

        using var second = CreateService();
        await second.LoadAsync(Ct);

        second.Current.Speed.IntervalMs.Should().Be(150);
        second.Current.Speed.JitterPercent.Should().Be(0.2);
        second.Current.Behavior.AppendTrailingEnter.Should().BeTrue();
        second.Current.Behavior.KeyboardMode.Should().Be(KeyboardMode.Compatibility);
    }

    [Fact]
    public async Task 保存应使用原子写_不残留临时文件()
    {
        using var service = CreateService();
        await service.LoadAsync(Ct);

        service.Update(config => config with { Speed = config.Speed with { IntervalMs = 300 } });
        await service.SaveNowAsync(Ct);

        File.Exists(service.ConfigFilePath + ".tmp").Should().BeFalse("原子写完成后不应留下临时文件");
    }

    [Fact]
    public async Task 保存的是UTF8无BOM_便于用户手工编辑()
    {
        using var service = CreateService();
        await service.LoadAsync(Ct);

        var bytes = await File.ReadAllBytesAsync(service.ConfigFilePath, Ct);

        bytes.Length.Should().BeGreaterThan(3);
        (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF).Should().BeFalse();
    }

    [Fact]
    public async Task 更新应触发Changed事件()
    {
        using var service = CreateService();
        await service.LoadAsync(Ct);

        AppConfig? received = null;
        service.Changed += (_, config) => received = config;

        service.Update(config => config with { Countdown = config.Countdown with { Seconds = 5 } });

        received.Should().NotBeNull();
        received!.Countdown.Seconds.Should().Be(5);
    }

    [Fact]
    public async Task 去抖保存_应在一秒后才落盘()
    {
        using var service = CreateService();
        await service.LoadAsync(Ct);

        var original = await File.ReadAllTextAsync(service.ConfigFilePath, Ct);

        service.Update(config => config with { Speed = config.Speed with { IntervalMs = 900 } });

        // 立即读：还没落盘
        (await File.ReadAllTextAsync(service.ConfigFilePath, Ct)).Should().Be(original);

        // 等过去抖窗口
        await Task.Delay(JsonConfigService.SaveDebounce + TimeSpan.FromMilliseconds(600), Ct);

        var updated = await File.ReadAllTextAsync(service.ConfigFilePath, Ct);
        updated.Should().Contain("900");
    }

    [Fact]
    public async Task 恢复默认设置_应清空自定义项并落盘()
    {
        using var service = CreateService();
        await service.LoadAsync(Ct);
        service.Update(config => config with { Speed = config.Speed with { IntervalMs = 456 } });
        await service.SaveNowAsync(Ct);

        await service.ResetToDefaultsAsync(Ct);

        service.Current.Should().Be(AppConfig.Default);
        (await File.ReadAllTextAsync(service.ConfigFilePath, Ct)).Should().Contain("200");
    }

    [Fact]
    public async Task 最近文件列表_应去重并截断到十条()
    {
        var path = Path.Combine(_directory, "config.json");
        var recent = string.Join(",", Enumerable.Range(0, 15).Select(i => $"\"C:\\\\a\\\\{i}.txt\""));
        await File.WriteAllTextAsync(path, $$"""{ "version": 1, "file": { "recentFiles": [{{recent}}, "C:\\\\a\\\\0.txt"] } }""", Ct);

        using var service = CreateService();
        await service.LoadAsync(Ct);

        service.Current.File.RecentFiles.Should().HaveCount(10);
        service.Current.File.RecentFiles!.Distinct(StringComparer.OrdinalIgnoreCase).Should().HaveCount(10);
    }

    [Fact]
    public async Task 序列化字段名_应为驼峰式且与设计文档一致()
    {
        using var service = CreateService();
        await service.LoadAsync(Ct);

        var json = await File.ReadAllTextAsync(service.ConfigFilePath, Ct);

        json.Should().Contain("\"intervalMs\"");
        json.Should().Contain("\"newlineStrategy\"");
        json.Should().Contain("\"retentionDays\"");
        json.Should().Contain("\"closeToTray\"");
    }

    [Fact]
    public void 配置对象应可通过JSON往返()
    {
        var original = AppConfig.Default with
        {
            Speed = AppConfig.Default.Speed with { IntervalMs = 137, JitterPercent = 0.15 },
        };

        var json = JsonSerializer.Serialize(original, ConfigJsonContext.Default.AppConfig);
        var roundTrip = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig);

        roundTrip.Should().Be(original);
    }
}
