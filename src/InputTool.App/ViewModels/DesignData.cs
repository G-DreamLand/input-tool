using InputTool.Core.Application;

namespace InputTool.App.ViewModels;

/// <summary>
/// 设计时/预览用的示例数据。
/// </summary>
/// <remarks>
/// 两个用途：
/// 1. XAML 设计器通过 <c>d:DataContext="{x:Static vm:DesignData.Main}"</c> 显示真实布局；
/// 2. 无头预览渲染器（<c>--render-preview</c>）用它生成界面截图，便于 UI 走查与回归。
/// 它不参与运行时逻辑，正式数据一律由 Core 契约提供。
/// </remarks>
public static class DesignData
{
    /// <summary>填充好的主窗口 ViewModel（处于"输入中 38.4%"的演示状态）。</summary>
    public static MainViewModel Main { get; } = Create();

    /// <summary>创建一份演示数据。</summary>
    public static MainViewModel Create()
    {
        var viewModel = new MainViewModel
        {
            FilePath = @"D:\话术\开场白.txt",
            EncodingName = "UTF-8",
            StatisticsText = "字符: 812    行: 16    预估: 2 分 43 秒",
            IntervalMs = 200,
            JitterPercent = 0.2,
            LineExtraDelayMs = 0,
            PreEnterDelayMs = 0,
            CountdownSeconds = 3,
            NewlineStrategyIndex = 0,
            FocusGuard = false,
            FocusGuardActionIndex = 0,
            PlayFinishSound = true,
            CloseToTray = true,
            StartPauseHotkey = "F9",
            EmergencyStopHotkey = "F10",
            SpeedUpHotkey = "Ctrl+Alt+Up",
            SpeedDownHotkey = "Ctrl+Alt+Down",
            DocumentInfoText = "开场白.txt · 812 字符 · 16 行 · UTF-8",
            TargetWindowText = "记事本",
            StatusText = "正在向目标窗口输入…",
        };

        viewModel.LoadPreview(SampleText);
        viewModel.SetState(InputSessionState.Typing);
        viewModel.ProgressPercent = 38.4;
        viewModel.ProgressText = "已输入 312 / 812 字符 (38.4%)";
        viewModel.TimingText = "已用 1 分 02 秒    剩余约 1 分 41 秒    预计 14:32:10 完成";
        return viewModel;
    }

    /// <summary>演示文本：含中文、Tab、空行，用于展示预览区的 ↵ / → 标记。</summary>
    public const string SampleText =
        "尊敬的用户您好，" +
        "\n感谢您选择我们的产品。接下来为您介绍三项核心能力：" +
        "\n" +
        "\n1. 文本读取：自动识别 UTF-8 / GB18030 / UTF-16 编码，中文不乱码；" +
        "\n2. 节奏可控：节拍间隔 10–5000 ms 可调，支持抖动模拟真人手感；" +
        "\n3. 随时可停：F9 暂停、F10 紧急停止，200 ms 内彻底停下且不残留按键。" +
        "\n" +
        "\n\t如需批量导入，请在配置文件中追加 fileList 节点。" +
        "\n\t如需停止，请按 F10。" +
        "\n\n祝您使用愉快。";
}
