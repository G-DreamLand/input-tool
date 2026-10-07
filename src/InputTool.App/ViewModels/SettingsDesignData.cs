using InputTool.Core.Application;

namespace InputTool.App.ViewModels;

/// <summary>
/// 设置窗口的设计时示例数据（XAML 设计器用）。不参与运行时逻辑。
/// </summary>
public static class SettingsDesignData
{
    /// <summary>设计时使用的设置 ViewModel。</summary>
    public static SettingsViewModel Instance { get; } = Create();

    /// <summary>创建一份演示数据。</summary>
    public static SettingsViewModel Create()
    {
        var viewModel = new SettingsViewModel(DesignData.Create())
        {
            KeyboardModeIndex = 0,
            LogLevelIndex = 1,
            LogRetentionDays = 7,
            MaxUnitsPerRun = 100_000,
        };

        return viewModel;
    }
}
