using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using InputTool.Core.Interop;

namespace InputTool.App.Views;

/// <summary>
/// 目标窗口选择器（FR-012.4）。
/// </summary>
/// <remarks>
/// 只做"选"这一件事：把当前可见顶层窗口列出来（默认只列终端类），
/// 由调用方把选中的句柄交给会话控制器。真正的"切前台并校验"发生在每次开始输入之前，
/// 因为窗口句柄会随用户开关窗口而失效。
/// </remarks>
public partial class TargetWindowPickerWindow : Window
{
    private readonly ObservableCollection<TargetWindowInfo> _targets = new();

    /// <summary>构造选择器。</summary>
    public TargetWindowPickerWindow()
    {
        InitializeComponent();

        TargetList.ItemsSource = _targets;
        Loaded += (_, _) => Reload();
    }

    /// <summary>用户选定的窗口；取消时为 null。</summary>
    public TargetWindowInfo? SelectedTarget { get; private set; }

    private void Reload()
    {
        var terminalsOnly = TerminalsOnlyCheckBox.IsChecked == true;

        // EnumerateTargets 返回的顺序就是 Z 序，保持原序即可让"最近用过的"排在最上面
        var windows = WindowEnumerator.EnumerateTargets()
            .Where(window => !terminalsOnly || window.IsTerminal)
            .ToList();

        _targets.Clear();
        foreach (var window in windows)
        {
            _targets.Add(window);
        }

        if (_targets.Count > 0)
        {
            TargetList.SelectedIndex = 0;
        }

        CountText.Text = terminalsOnly
            ? $"共 {_targets.Count} 个终端窗口"
            : $"共 {_targets.Count} 个窗口";
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => Reload();

    private void OnRefresh(object sender, RoutedEventArgs e) => Reload();

    private void OnConfirm(object sender, RoutedEventArgs e) => Confirm();

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e) => Confirm();

    private void Confirm()
    {
        if (TargetList.SelectedItem is not TargetWindowInfo info)
        {
            return;
        }

        SelectedTarget = info;
        DialogResult = true;
    }
}
