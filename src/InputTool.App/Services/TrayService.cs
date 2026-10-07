using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;

namespace InputTool.App.Services;

/// <summary>托盘菜单用到的命令集合（由外壳注入，避免托盘直接依赖 ViewModel）。</summary>
public sealed record TrayMenuActions(
    ICommand StartPause,
    ICommand EmergencyStop,
    ICommand ShowMainWindow,
    ICommand OpenSettings,
    ICommand OpenLogs,
    ICommand About,
    ICommand Exit);

/// <summary>
/// 系统托盘驻留。对应《软件开发设计文档》§6.4、FR-019。
/// </summary>
/// <remarks>
/// 使用 Hardcodet.NotifyIcon.Wpf（ADR-09 / Q-09）。
/// 注意：托盘菜单沿用系统默认外观（未套用应用主题）——它浮在系统托盘上，
/// Win11 下会跟随系统主题；自定义菜单模板列为后续界面打磨项。
/// </remarks>
public sealed class TrayService : IDisposable
{
    private readonly TaskbarIcon _icon;
    private bool _disposed;

    /// <summary>构造托盘服务。</summary>
    /// <param name="actions">菜单命令集合。</param>
    public TrayService(TrayMenuActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        _icon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/InputTool.ico")),
            ToolTipText = "Input Tool — 空闲",
            ContextMenu = BuildMenu(actions),
        };

        // 双击托盘图标显示主窗口（FR-019.3）
        _icon.TrayMouseDoubleClick += (_, _) => actions.ShowMainWindow.Execute(null);
    }

    /// <summary>更新托盘悬浮提示（FR-019.4：显示当前状态，例如"输入中 35%"）。</summary>
    public void UpdateStatus(string status)
        => _icon.ToolTipText = $"Input Tool — {status}";

    /// <summary>弹出气泡提示（FR-017：完成 / 异常提示）。</summary>
    public void ShowBalloon(string title, string message)
        => _icon.ShowBalloonTip(title, message, BalloonIcon.Info);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.Dispose();
    }

    private static ContextMenu BuildMenu(TrayMenuActions actions)
    {
        var menu = new ContextMenu();

        menu.Items.Add(CreateItem("开始 / 暂停", actions.StartPause));
        menu.Items.Add(CreateItem("紧急停止", actions.EmergencyStop));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("打开主窗口", actions.ShowMainWindow));
        menu.Items.Add(CreateItem("设置…", actions.OpenSettings));
        menu.Items.Add(CreateItem("打开日志目录", actions.OpenLogs));
        menu.Items.Add(CreateItem("关于", actions.About));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("退出", actions.Exit));

        return menu;
    }

    private static MenuItem CreateItem(string header, ICommand command)
        => new() { Header = header, Command = command };
}
