using System.Windows;
using System.Windows.Controls;
using InputTool.App.ViewModels;

namespace InputTool.App.Views;

/// <summary>
/// 设置窗口。见《软件开发设计文档》§6.5。
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>构造设置窗口。</summary>
    /// <param name="viewModel">设置 ViewModel（通常由主窗口创建并共享主 ViewModel）。</param>
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        viewModel.RequestClose += (_, _) => Close();
    }

    /// <summary>
    /// 危险选项的二次确认（§6.2：危险操作需二次确认）。
    /// 用户取消时把勾选状态回退。
    /// </summary>
    private void OnDangerousOptionChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box)
        {
            return;
        }

        var (title, message) = box.Name switch
        {
            "ClearTargetFirstBox" => (
                "确认开启「清空目标框内容」？",
                "开启后，每次开始输入前会先向目标窗口发送 Ctrl+A + Delete，\n" +
                "清空目标输入框中的现有内容。若目标框中有未保存的内容将无法恢复。\n\n" +
                "确定要开启吗？"),
            "LogContentBox" => (
                "确认在日志中记录输入内容？",
                "开启后，日志会记录每次输入内容的摘要（前 80 个字符）。\n" +
                "这可能包含身份证号、手机号、口令等敏感信息。\n\n" +
                "确定要开启吗？"),
            _ => (string.Empty, string.Empty),
        };

        if (title.Length == 0)
        {
            return;
        }

        var result = MessageBox.Show(this, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK)
        {
            box.IsChecked = false;
        }
    }
}
