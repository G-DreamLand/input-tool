# Input Tool — 键盘文本自动输入工具

把文本文件的内容，以可调的固定节奏**逐字模拟真实键盘输入**到当前前台窗口。
面向"禁止粘贴 / 需要拟人化输入 / 需要在输入过程中观察对方响应"的场景。

- 需求依据：[需求规格说明书.md](需求规格说明书.md)（IT-SRS-001，28 条功能需求 + 20 条非功能需求 + 25 条验收项）
- 设计依据：[软件开发设计文档.md](软件开发设计文档.md)（IT-SDD-001，架构 / 状态机 / 模块详细设计 / 追溯矩阵）

> **状态：M2 设计完成，正在搭建骨架。** 当前仓库只包含工程结构、接口契约与数据模型，
> 各服务实现为 `NotImplementedException` 桩，等待 M3/M4 按设计文档 §15 的任务分解填充。

## 技术栈

| 项 | 选择 |
| --- | --- |
| 运行时 | .NET 8（LTS） |
| 目标框架 | `net8.0-windows10.0.17763.0`（最低 Windows 10 1809） |
| 界面 | WPF + MVVM |
| 键盘注入 | P/Invoke `SendInput`（`KEYEVENTF_UNICODE`） |
| 热键 | `RegisterHotKey` + `WM_HOTKEY` |
| 托盘 | Hardcodet.NotifyIcon.Wpf（ADR-09） |
| 配置 | System.Text.Json（源生成） |
| 测试 | xUnit v3 + Microsoft.Testing.Platform + FluentAssertions 6.x + NSubstitute |

## 目录结构

```
input tool/
├─ 需求规格说明书.md
├─ 软件开发设计文档.md
├─ build/
│  ├─ common.ps1            # dotnet 定位与残留进程清理
│  ├─ build.ps1             # 构建
│  └─ test.ps1              # 运行测试
├─ src/
│  ├─ InputTool.sln
│  ├─ Directory.Build.props # 全解决方案统一编译属性
│  ├─ InputTool.Core/       # 领域服务层（禁引 WPF）
│  │  ├─ Abstractions/      # IClock / IRandomSource / UserNotification
│  │  ├─ Application/       # 状态机契约、会话控制器契约、提权契约
│  │  ├─ Configuration/     # AppConfig 与配置服务契约
│  │  ├─ Files/             # 文件读取与编码判定
│  │  ├─ Hotkeys/           # 全局热键
│  │  ├─ Injection/         # 键盘注入与字符映射
│  │  ├─ Interop/           # 全部 P/Invoke 声明
│  │  ├─ Logging/           # 日志契约
│  │  ├─ Parsing/           # 归一化与输入单元构建
│  │  └─ Scheduling/        # 节拍调度与速度模型
│  ├─ InputTool.App/        # WPF 表现层
│  │  ├─ Views/             # MainWindow / SettingsWindow / OverlayWindow
│  │  ├─ ViewModels/        # MainViewModel / SettingsViewModel / PreviewLine / 设计时示例数据
│  │  ├─ Controls/          # HotkeyBox：热键捕获控件（FR-025.1）
│  │  ├─ Services/          # AppShell（窗口装配）、TrayService（托盘，FR-019）
│  │  ├─ Commands/          # RelayCommand
│  │  ├─ Themes/            # 设计令牌、浅色/深色主题字典、控件样式、ThemeManager
│  │  ├─ Diagnostics/       # UiPreviewRenderer：无头界面截图（UI 走查与回归）
│  │  ├─ Assets/            # InputTool.ico（由 tools/gen-icon.ps1 生成）
│  │  └─ app.manifest       # PerMonitorV2 DPI + asInvoker
│  ├─ InputTool.Tests/      # 单元测试
│  └─ InputTool.IntegrationTests/  # 桌面集成测试（默认跳过）
└─ tools/                   # 辅助脚本与自测目标程序（M5 阶段补充）
```

## 构建与测试

```powershell
# 构建（默认 Debug）
pwsh -File build/build.ps1

# Release 构建
pwsh -File build/build.ps1 -Configuration Release

# 运行全部测试
pwsh -File build/test.ps1
```

也可以直接使用 dotnet CLI：

```powershell
dotnet build src/InputTool.sln -c Debug
dotnet run --project src/InputTool.Tests -c Debug
```

### 为什么测试用 `dotnet run` 而不是 `dotnet test`

测试栈是 **xUnit v3 + Microsoft.Testing.Platform**，测试工程本身就是一个可执行程序。
`dotnet test` 会让测试进程通过**命名管道**回连 MSBuild，而本机开发环境禁用了命名管道，
该路径会以 `NamedPipeClientStream.TryConnect` 失败告终。`dotnet run` 让测试在该进程内直接执行，
链路更短，也不受该限制影响。

同样由于历史原因，vstest 的 testhost 会以 `Win32Exception (5)` 启动失败，
因此本项目不使用 vstest（`Microsoft.NET.Test.Sdk` / `xunit.runner.visualstudio` 均已移除）。

## 界面（UI）

主窗口按设计文档 §6.1 分为五个分区，其中 ①②③④ 可滚动、**⑤ 控制区与状态栏固定在底部**，
保证任何窗口高度下【开始 / 暂停 / 停止】都可见。

| 分层 | 位置 | 说明 |
| --- | --- | --- |
| 设计令牌 | [Themes/Tokens.xaml](src/InputTool.App/Themes/Tokens.xaml) | 尺寸、字号、圆角、等宽字体，与主题无关 |
| 主题字典 | [Themes/Theme.Light.xaml](src/InputTool.App/Themes/Theme.Light.xaml)、[Theme.Dark.xaml](src/InputTool.App/Themes/Theme.Dark.xaml) | 只含颜色与画刷，两套键名完全一致 |
| 控件样式 | [Themes/Controls.xaml](src/InputTool.App/Themes/Controls.xaml) | Button / TextBox / ComboBox / CheckBox / Slider / ProgressBar / GroupBox / ScrollBar 自绘模板 |
| 主题切换 | [Themes/ThemeManager.cs](src/InputTool.App/Themes/ThemeManager.cs) | 替换 `MergedDictionaries[0]`；跟随系统时读注册表 `AppsUseLightTheme` 并监听系统变化 |

样式里对画刷一律使用 `DynamicResource`，因此切换主题立即生效、无需重启。

### 窗口与控件清单

| 界面 | 位置 | 对应需求 |
| --- | --- | --- |
| 主窗口（五分区 + 顶部错误条 + 状态栏） | [Views/MainWindow.xaml](src/InputTool.App/Views/MainWindow.xaml) | FR-001~FR-011、FR-015、FR-016、FR-023、FR-025 |
| 设置窗口（常规 / 热键 / 文本 / 键盘 / 高级 / 关于） | [Views/SettingsWindow.xaml](src/InputTool.App/Views/SettingsWindow.xaml) | FR-018、FR-021、FR-025、FR-026 |
| 倒计时浮层 / 输入进度浮层 | [Views/OverlayWindow.xaml](src/InputTool.App/Views/OverlayWindow.xaml) | FR-012、FR-016（`WS_EX_NOACTIVATE` 不抢焦点） |
| 系统托盘（菜单 / 双击 / 气泡 / 状态提示） | [Services/TrayService.cs](src/InputTool.App/Services/TrayService.cs) | FR-019、FR-017 |
| 热键捕获控件 | [Controls/HotkeyBox.cs](src/InputTool.App/Controls/HotkeyBox.cs) | FR-025.1 |
| 窗口装配与关闭到托盘 | [Services/AppShell.cs](src/InputTool.App/Services/AppShell.cs) | FR-019.1、E-09 |
| 应用图标（7 个尺寸，脚本可重复生成） | [tools/gen-icon.ps1](tools/gen-icon.ps1) → `Assets/InputTool.ico` | — |

危险操作（清空目标框、日志记录内容）在设置窗口里都会弹二次确认（§6.2）。

### 界面截图（无头渲染）

[Diagnostics/UiPreviewRenderer.cs](src/InputTool.App/Diagnostics/UiPreviewRenderer.cs) 提供无头截图能力：
把窗口显示在屏幕外再渲染为 PNG，**不需要人工截图**，适合 UI 走查与视觉回归。

```powershell
$dll = "src/InputTool.App/bin/Debug/net8.0-windows10.0.17763.0/InputTool.dll"
dotnet $dll --render-preview docs/prototype/ui-main-light.png     --theme light --view main
dotnet $dll --render-preview docs/prototype/ui-main-dark.png      --theme dark  --view main
dotnet $dll --render-preview docs/prototype/ui-settings-light.png --theme light --view settings
dotnet $dll --render-preview docs/prototype/ui-overlay-countdown.png --view overlay-countdown
dotnet $dll --render-preview docs/prototype/ui-overlay-progress.png  --view overlay-progress

# 用真实文件跑完整管线后渲染（端到端验证 / 文档截图）
dotnet $dll --render-preview docs/prototype/ui-main-loaded-light.png --view main --file docs/prototype/sample-text.txt
```

`--view` 可选 `main` / `settings` / `overlay-countdown` / `overlay-progress`；
`--file` 让主界面走真实文档管线。半透明浮层会自动合成到中性底色上以便查看。
产物见 [docs/prototype/](docs/prototype/)。

## 文本管线（M3 第一轮）

设计文档 §5.1 + §5.2 的完整链路已落地：

```
文件 → EncodingDetector → FileLoader → TextNormalizer → InputUnitBuilder → TextStatistics
       编码判定            错误分类      换行归一       单元构建           统计与预估
```

| 模块 | 能力 |
| --- | --- |
| [EncodingDetector](src/InputTool.Core/Files/EncodingDetector.cs) | BOM（UTF-8 / 16LE / 16BE / 32LE / 32BE）→ 严格 UTF-8 → GB18030 → ANSI(936) → Latin-1 兜底；疑似二进制探测；手动指定编码；自动注册代码页提供程序 |
| [FileLoader](src/InputTool.Core/Files/FileLoader.cs) | 流式读取；错误分类（不存在 / 目录 / 被占用 / 无权限 / 空文件）；≤8 MB 字节缓存，切换编码时免重复读盘；大文件强提示 |
| [TextNormalizer](src/InputTool.Core/Parsing/TextNormalizer.cs) | CRLF / CR / LF 统一为 LF；去除开头 BOM 残留 |
| [InputUnitBuilder](src/InputTool.Core/Parsing/InputUnitBuilder.cs) | Rune 折叠（Emoji 代理对）、Tab / 退格 / Esc 映射、NUL 与 C0 跳过计数、转义序列解析、忽略空行、跳过 `#` 行、末尾补回车、单元上限截断 |
| [DurationEstimator](src/InputTool.Core/Scheduling/DurationEstimator.cs) | `(字符 + 功能键) × T + 换行 × (P + T + L)` |
| [DocumentPipeline](src/InputTool.Core/Application/DocumentPipeline.cs) | 上述各层的编排入口；界面与将来的会话控制器共用同一条路径 |

**端到端实测**：加载 [sample-text.txt](docs/prototype/sample-text.txt)（208 字符 / 11 行 / UTF-8）
→ 自动判定 UTF-8、生成 218 个输入单元、预估约 44 秒；界面预览显示真实行号与 `↵` / `→` 标记。

当前已接入界面的命令：选文件、重载、打开所在文件夹、切换编码即时重解码、修改速度即时刷新预估。

## 配置、日志与运行环境（M3 第三轮）

| 能力 | 说明 |
| --- | --- |
| [JsonConfigService](src/InputTool.Core/Configuration/JsonConfigService.cs) | 路径判定：`portable.flag` → exe 同目录可写 → `%APPDATA%\InputTool`；**原子写**（写 `.tmp` 再替换）；1 秒**去抖**落盘；损坏时备份 `config.bad.json` 并回退默认；加载与修改都做一次 clamp，手工编辑越界也不会带进运行时 |
| [FileLogger](src/InputTool.Core/Logging/FileLogger.cs) | `%APPDATA%\InputTool\logs\inputtool-yyyyMMdd.log`，格式 `时间 [级别] [分类] 消息`；**有界队列 + 后台线程**，队列满只丢日志绝不阻塞输入；超 5 MB 换文件；启动清理超保留期日志；**写完即关句柄**，因此随时可以用记事本打开 |
| [SingleInstanceService](src/InputTool.Core/Application/SingleInstanceService.cs) | `Local\` 互斥体（每用户会话一个实例）；第二个实例用 `RegisterWindowMessage` + `PostMessage(HWND_BROADCAST)` 把已有窗口叫到前面。**不用命名管道**，因为本机开发环境禁用了管道（见 §21.3） |
| [ElevationService](src/InputTool.Core/Application/ElevationService.cs) | 用 `GetTokenInformation(TokenElevation)` 判断自身是否提权；`runas` 重启并带回当前文件；用户取消 UAC 不算失败，程序恢复可用 |
| [AppSettingsBinder](src/InputTool.App/Services/AppSettingsBinder.cs) | 配置 ↔ 界面**双向**绑定；只持久化需要保存的项，纯运行时状态（进度、状态机）不写入 |

启动顺序（[App.xaml.cs](src/InputTool.App/App.xaml.cs)）：日志 → 修饰键残留清理 → 单实例 → 配置 → 主题 → 全局异常处理 → 外壳。
退出顺序（[AppShell.Exit](src/InputTool.App/Services/AppShell.cs)）：停会话 → 注销热键 → 保存配置 → 刷日志 → 释放单实例。

### 实测证据（日志摘录）

```text
[SingleInstance] 单实例检查完成：IsFirstInstance=True, 消息 ID=50073
[Config] 配置文件不存在，已生成默认配置：C:\Users\...\AppData\Roaming\InputTool\config.json
[Hotkey] 热键已注册 role=StartPause gesture=F9 id=1
[Hotkey] 热键已注册 role=EmergencyStop gesture=F10 id=2
[Startup] 启动完成：便携模式=False，配置=...\config.json，管理员=False
── 第二次启动 ──
[SingleInstance] 单实例检查完成：IsFirstInstance=False, 消息 ID=50073
[Startup] 已有实例在运行，已请求激活该实例并退出
```

## 输入引擎（M3 第二轮）

从"文本 + 配置"到"真的敲出按键"的完整链路：

| 模块 | 能力 |
| --- | --- |
| [InputScheduler](src/InputTool.Core/Scheduling/InputScheduler.cs) | **绝对时间基准**（目标时刻 = 基准 + Σ 间隔，杜绝漂移）；专用后台线程串行注入；落后 >200 ms 进入追赶模式；暂停恢复重建基准；停止 ≤200 ms 生效；任何结束路径都 `ReleaseAll()` 补发抬起 |
| [IntervalCalculator](src/InputTool.Core/Scheduling/IntervalCalculator.cs) | 抖动 `T × (1 + J·r)`、5 ms 下限保护；换行附加延时交由调度器在注入前后分别等待（P 在回车前、L 在回车后） |
| [PrecisionWaiter](src/InputTool.Core/Scheduling/PrecisionWaiter.cs) | 粗睡 → 让出时间片 → 忙等收尾；每轮都检查取消标志，长延时也能被急停打断 |
| [Win32KeyboardEmitter](src/InputTool.Core/Injection/Win32KeyboardEmitter.cs) | Unicode 注入（代理对拆两码元）、功能键虚拟码、四种换行策略、兼容模式（`VkKeyScanEx` + 修饰键）、失败重试一次、按键栈逆序补齐 |
| [KeyMapper](src/InputTool.Core/Injection/KeyMapper.cs) / [InjectabilityProbe](src/InputTool.Core/Injection/InjectabilityProbe.cs) | 字符 → 虚拟键 + 修饰键；锁屏/安全桌面/会话断开/更高完整性级别检测（UIPI） |
| [Win32HotkeyService](src/InputTool.Core/Hotkeys/Win32HotkeyService.cs) | `RegisterHotKey` + `MOD_NOREPEAT`；注册失败按 SRS 附录 B 回退备用组合；急停热键失败标记为不可用 |
| [InputSessionController](src/InputTool.Core/Application/InputSessionController.cs) | 状态机（§4.2）、前置校验、倒计时、会话统计；所有事件切回界面线程回调 |

### 真实打字验证

[docs/…/RealTypingTests.cs](src/InputTool.IntegrationTests/RealTypingTests.cs) 开一个真实 WinForms 窗口，
用真实的 `SendInput` 把文本打进去再回读比对 —— 覆盖 ASCII、中文、中英混排、全角符号、Tab 与多行空行：

```powershell
dotnet src/InputTool.IntegrationTests/bin/Debug/net8.0-windows10.0.17763.0/InputTool.IntegrationTests.dll
```

> 该用例打 `[Trait("Category","Desktop")]`：需要真实桌面会话且会短暂抢占前台焦点，CI 默认跳过。

### 终端验证（cmd / PowerShell）

[TerminalTypingTests.cs](src/InputTool.IntegrationTests/TerminalTypingTests.cs) 启动**真实的** cmd.exe 与 PowerShell 窗口，
注入「命令行 + 回车」，再通过命令自身的副作用（生成标记文件）判断它**到底有没有被执行**：

| 用例 | 验证内容 |
| --- | --- |
| CMD 单行命令 | `echo ...> marker.txt` + 回车 → 标记文件生成且内容正确 |
| CMD 多行命令 | 两行 `echo` 逐行执行，行间隔 800 ms（SRS 场景 B） |
| PowerShell 单行命令 | `Set-Content -LiteralPath ... -Value ...` + 回车 → 文件正确 |
| PowerShell 多行命令 | 两行命令逐行执行，行间隔 1200 ms |
| WSL（openEuler 22.03）单行命令 | 启动 `wsl.exe -d openEuler-22.03`，注入 `echo ... > /tmp/marker` + 回车 |
| WSL（openEuler 22.03）多行命令 | 两行命令逐行执行，行间隔 800 ms |

这条链路证明的是 **`VK_RETURN` 确实是真回车**——终端把它当成"执行该行命令"，而不是插入一个换行符。

> WSL 用例的验证方式：从 Windows 侧执行 `wsl.exe -d openEuler-22.03 -- cat <文件>` 回读 Linux 侧的文件内容，
> 作为"命令确实在发行版内执行了"的证据（比读 `\\wsl$` UNC 或终端画面都稳定）。
> 测试会在结束时删除 Linux 侧与宿主侧的临时文件。

> 为什么用"标记文件"而不是回读终端画面：现代终端（Windows Terminal / conhost）的文本区无法可靠地跨进程读取，
> 而命令的副作用是与终端实现无关的确凿证据。

### 完成度

| 项 | 状态 |
| --- | --- |
| 设计令牌 + 浅色/深色主题 + 控件样式 | ✅ |
| 主窗口五分区布局、预览区（↵ / → 标记）、进度与状态栏 | ✅ |
| 状态驱动交互（§4.3 可用性矩阵：会话中冻结文档与行为设置） | ✅ |
| 设置窗口（六组设置 + 热键捕获 + 冲突校验 + 危险操作二次确认） | ✅ |
| 倒计时浮层 / 进度浮层（不抢焦点、屏幕外可截图） | ✅ |
| 系统托盘（菜单 / 双击 / 气泡 / 状态提示 / 关闭到托盘） | ✅ |
| 应用图标与无头截图工具 | ✅ |
| 热键解析与校验逻辑（`HotkeyGesture.TryParse` / `HotkeyValidator`） | ✅ |
| **文本管线**：编码判定 + 文件读取 + 归一化 + 单元构建 + 时长预估 | ✅ |
| **输入引擎**：节拍调度 + 键盘注入 + 全局热键 + 会话状态机 | ✅ |
| 界面命令接入：选文件 / 重载 / 所在文件夹 / 编码切换重解码 / 预估实时刷新 | ✅ |
| 真正的逐字键入（真实窗口端到端回读验证） | ✅ 6 个桌面用例 |
| 终端场景：cmd / PowerShell 的"回车即执行命令" | ✅ 4 个桌面用例 |
| WSL 场景：openEuler 22.03 内的"回车即执行命令" | ✅ 2 个桌面用例 |
| **配置持久化**（便携模式 / 原子写 / 去抖 / 损坏备份 / 版本迁移） | ✅ |
| **运行日志**（按天滚动 / 保留期清理 / 有界队列不阻塞输入） | ✅ |
| **单实例**（第二个实例广播激活已有窗口） | ✅ |
| **权限自检与提权重启**（UIPI 检测 + `runas` 重启并带回文件） | ✅ |
| **目标窗口选择**（跟随前台 / 自动选终端窗口 / 固定窗口，激活失败即中止） | ✅ |
| **焦点守护 FR-020**（每 200 ms 检查前台窗口；失焦自动暂停 + 托盘提示，回来自动继续） | ✅ |
| 测试规模 | ✅ 321 个单元测试 + 14 个桌面集成用例（12 通过 / 2 待补：Windows Terminal 自动化、UIPI 提权） |

## 本机开发环境说明

| 事项 | 说明 |
| --- | --- |
| .NET SDK 位置 | `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`（免管理员安装，已加入用户 PATH 与 `DOTNET_ROOT`）。`build/*.ps1` 会自动定位，无需依赖 PATH |
| 引用程序集 | `Directory.Build.props` 中关闭了 `ProduceReferenceAssembly`。原因：WPF 标记编译派生的 `_wpftmp` 临时工程与主工程图并行构建时会争用 `obj/**/ref` 下的程序集，触发 `CS0009`/`CS2012`「文件正被另一进程使用」错误 |
| 构建并行度 | 统一使用 `-m:1`（串行）。同一原因：`_wpftmp` 工程会与主工程图争用被引用项目的输出 `Core.dll`，触发 `MSB3248`/`CS0009`。`build/build.ps1` 与 `build/test.ps1` 已内置该参数 |
| 测试宿主残留 | 测试进程偶有未退出，会锁住 `bin` 下的 exe 导致下一次生成报 `MSB3021`。两个脚本都会在开始前清理残留进程，并且**直接运行测试程序集**（`dotnet <测试dll>`）而不是 `dotnet run`，避开 apphost 复制步骤 |
| 测试临时目录 | 测试不写系统 `%TEMP%`（在本机对子进程不可写），而是写测试程序集目录下的 `test-tmp/`，自包含且不污染系统 |
| 脚本编码 | `build/*.ps1` **必须是 UTF-8 with BOM**：Windows PowerShell 5.1 会把无 BOM 的文件按 ANSI 读取，导致脚本内中文乱码并报「缺少字符串终止符」。用编辑器改动过这些脚本后请确认 BOM 仍在 |
| 构建顺序 | `build/build.ps1` **先单独构建 `InputTool.Core`，再以下游工程 `-p:BuildProjectReferences=false` 逐个构建**。这是本机稳定构建的关键：否则 WPF 为 App 派生的 `InputTool.App_<hash>_wpftmp` 内层工程会与主工程图**各自去构建 Core**，两边同时读写同一个 `InputTool.Core.dll`，必然报 `MC1000 / CS0009 / CS2012：文件正被另一进程使用`。实测 `-m:1`、`-nodeReuse:false`、`-p:UseSharedCompilation=false` 都不足以消除。`build/test.ps1` 复用同一流程（它曾自己走 `dotnet build <sln>` 把程序集写坏，导致测试宿主偶发以 `0xE0434352` 崩溃） |
| 杀软干扰 | 本机装有 **360 安全卫士**（`360rp.exe`），实时扫描会短暂持有新生成的 DLL，可能叠加出同类占用错误。已将工作目录加入 360 信任区；`build/build.ps1` 另外保留"等解锁 + 清理中间产物后重试 + 自动重新还原"的自愈逻辑作为兜底 |
| 僵尸测试宿主 | 偶发会有 `InputTool.Tests` 进程残留且**无法结束**（Access denied）。它不影响构建（空闲时并不占用 DLL），遇到时忽略即可 |

## 开发约定

- 依赖方向严格单向：UI → 应用 → 领域服务 → 平台封装；`InputTool.Core` 不得引用 WPF。
- 所有键盘事件必须经 `IKeyboardEmitter`，禁止在别处直接调用 `SendInput`。
- 所有 `P/Invoke` 声明集中在 `Interop/NativeMethods.cs`。
- 代码注释与提交信息使用中文；提交遵循约定式提交（`feat/fix/refactor/test/docs/chore`）。
- 详见设计文档 §16 工程规范与协作约定。
