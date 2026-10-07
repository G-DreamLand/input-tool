using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using InputTool.App.Themes;
using InputTool.App.ViewModels;
using InputTool.App.Views;
using InputTool.Core.Application;
using InputTool.Core.Configuration;

namespace InputTool.App.Diagnostics;

/// <summary>可渲染的界面视图。</summary>
internal enum PreviewView
{
    /// <summary>主窗口（默认）。</summary>
    Main,

    /// <summary>设置窗口。</summary>
    Settings,

    /// <summary>倒计时浮层。</summary>
    OverlayCountdown,

    /// <summary>输入进度浮层。</summary>
    OverlayProgress,
}

/// <summary>
/// UI 无头预览渲染器：把界面渲染成 PNG，供 UI 走查与视觉回归使用。
/// </summary>
/// <remarks>
/// 用法：
/// <code>
/// InputTool.exe --render-preview &lt;输出路径&gt; [--theme light|dark] [--view main|settings|overlay-countdown|overlay-progress]
/// </code>
/// 原理：把窗口显示在屏幕外（Left/Top = -30000）后渲染为位图，因此在自动化环境里
/// 也能稳定产出截图，不需要人工截图。透明窗口（浮层）会合成到中性底色上以便查看。
/// 该开关只用于开发与评审，不参与正常运行流程。
/// </remarks>
internal static class UiPreviewRenderer
{
    private const string PreviewSwitch = "--render-preview";
    private const string ThemeSwitch = "--theme";
    private const string ViewSwitch = "--view";
    private const string FileSwitch = "--file";
    private const string DefaultOutput = "ui-preview.png";

    /// <summary>命令行是否请求了预览渲染。</summary>
    internal static bool IsPreviewRequested(string[] args)
        => args.Any(a => string.Equals(a, PreviewSwitch, StringComparison.OrdinalIgnoreCase));

    /// <summary>解析 --theme 参数。</summary>
    internal static AppTheme ParseTheme(string[] args)
    {
        var value = GetOptionValue(args, ThemeSwitch);
        return value?.ToLowerInvariant() switch
        {
            "dark" => AppTheme.Dark,
            "light" => AppTheme.Light,
            _ => AppTheme.System,
        };
    }

    /// <summary>解析 --view 参数。</summary>
    internal static PreviewView ParseView(string[] args)
    {
        var value = GetOptionValue(args, ViewSwitch);
        return value?.ToLowerInvariant() switch
        {
            "settings" => PreviewView.Settings,
            "overlay-countdown" => PreviewView.OverlayCountdown,
            "overlay-progress" => PreviewView.OverlayProgress,
            _ => PreviewView.Main,
        };
    }

    /// <summary>解析 --render-preview 的输出路径。</summary>
    internal static string GetOutputPath(string[] args)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, PreviewSwitch, StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return args[index + 1];
        }

        return DefaultOutput;
    }

    /// <summary>解析 --file 参数（按真实文件渲染主界面，用于端到端验证与文档截图）。</summary>
    internal static string? GetInputFile(string[] args)
    {
        var value = GetOptionValue(args, FileSwitch);
        return !string.IsNullOrEmpty(value) && File.Exists(value) ? value : null;
    }

    /// <summary>渲染指定视图到 PNG 文件。</summary>
    internal static void RenderToFile(string outputPath, PreviewView view, string? inputFile = null)
    {
        switch (view)
        {
            case PreviewView.Settings:
                // 高度取完整内容高度，保证六个分组与底部按钮栏同时入镜
                RenderWindow(new SettingsWindow(SettingsDesignData.Create()), outputPath, 780, 1020);
                break;

            case PreviewView.OverlayCountdown:
                RenderOverlay(outputPath, countdown: true);
                break;

            case PreviewView.OverlayProgress:
                RenderOverlay(outputPath, countdown: false);
                break;

            default:
                RenderWindow(new MainWindow(CreateMainViewModel(inputFile)), outputPath, 860, 1060);
                break;
        }
    }

    /// <summary>
    /// 主界面 ViewModel：给了 --file 就用真实文件跑完整管线（读取 → 编码判定 → 归一化 → 单元构建），
    /// 否则退回示例数据。
    /// </summary>
    private static MainViewModel CreateMainViewModel(string? inputFile)
    {
        if (inputFile is null)
        {
            return DesignData.Create();
        }

        var viewModel = new MainViewModel(new DocumentPipeline());
        PumpUntilComplete(viewModel.LoadDocumentFromPathAsync(inputFile));
        return viewModel;
    }

    /// <summary>在 UI 线程上等待一个异步任务完成（推一个嵌套消息循环，避免 .Result 死锁）。</summary>
    private static void PumpUntilComplete(Task task)
    {
        if (task.IsCompleted)
        {
            task.GetAwaiter().GetResult();
            return;
        }

        var frame = new DispatcherFrame();
        _ = task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    private static void RenderWindow(Window window, string outputPath, double width, double height)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.ShowInTaskbar = false;
        window.Left = -30000;
        window.Top = -30000;
        window.Width = width;
        window.Height = height;

        window.Show();
        Flush(window);

        Save(window, outputPath, (int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), backdrop: null);
        window.Close();
    }

    private static void RenderOverlay(string outputPath, bool countdown)
    {
        var overlay = new OverlayWindow();

        if (countdown)
        {
            overlay.StartCountdown(5, OverlayPosition.BottomRight);
        }
        else
        {
            overlay.ShowProgress("输入中…", 38.4, "312 / 812 字符 (38.4%)", OverlayPosition.BottomRight);
        }

        // 浮层尺寸由内容决定，需要先完成布局
        overlay.UpdateLayout();
        Flush(overlay);

        var width = (int)Math.Ceiling(overlay.ActualWidth);
        var height = (int)Math.Ceiling(overlay.ActualHeight);

        // 浮层是半透明窗口，合成到中性底色上才看得清
        Save(overlay, outputPath, width, height, new SolidColorBrush(Color.FromRgb(0x6E, 0x74, 0x7C)));

        overlay.HideOverlay();
        overlay.Close();
    }

    private static void Flush(Window window)
        => window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static void Save(Visual visual, string outputPath, int width, int height, Brush? backdrop)
    {
        var content = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        content.Render(visual);

        BitmapSource result = content;

        if (backdrop is not null)
        {
            var composed = new DrawingVisual();
            using (var dc = composed.RenderOpen())
            {
                dc.DrawRectangle(backdrop, null, new Rect(0, 0, width, height));
                dc.DrawImage(content, new Rect(0, 0, width, height));
            }

            var composedBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            composedBitmap.Render(composed);
            result = composedBitmap;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(result));

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(fullPath);
        encoder.Save(stream);
    }

    private static string? GetOptionValue(string[] args, string option)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, option, StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return args[index + 1];
        }

        return null;
    }
}
