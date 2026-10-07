using System.Windows;
using InputTool.App.ViewModels;

namespace InputTool.App.Views;

/// <summary>
/// 主窗口。见《软件开发设计文档》§6.1。
/// 代码隐藏只做界面装配，业务逻辑一律留在 ViewModel 与 Core 契约中（§6.2）。
/// DataContext 由外壳（<c>AppShell</c>）或预览渲染器注入，窗口自身不创建 ViewModel。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>构造主窗口。</summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>构造主窗口并绑定指定的 ViewModel。</summary>
    /// <param name="viewModel">主窗口 ViewModel。</param>
    public MainWindow(MainViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }
}