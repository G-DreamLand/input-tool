using System.Windows;
using InputTool.App.ViewModels;

namespace InputTool.App.Views;

/// <summary>
/// "查看完整内容"窗口（FR-005.1）。
/// </summary>
/// <remarks>
/// 主界面预览按规格只显示前 500 字符 / 40 行，超出部分以"…"提示；
/// 本窗口用来核对**全文**。
/// <para>
/// 两个实现要点：
/// · 用 <c>ListBox</c> 且开启**虚拟化**，几万行也只创建可见的元素，不会拖慢界面
///   （所以这里不是把所有文本塞进一个 TextBox——那样大文件会明显卡顿）；
/// · **只读**，不提供编辑：SRS 约束 C-02 明确"不修改源文件"。
/// </para>
/// </remarks>
public partial class FullTextViewWindow : Window
{
    private readonly string _text;

    /// <summary>构造完整内容窗口。</summary>
    /// <param name="text">完整原文。</param>
    public FullTextViewWindow(string text)
    {
        InitializeComponent();

        _text = text ?? string.Empty;

        var lines = BuildLines(_text);
        LineList.ItemsSource = lines;

        SummaryText.Text =
            $"共 {lines.Count} 行 / {_text.Length} 字符（Tab 显示为 →，行尾 ↵ 表示换行）。"
            + "本窗口只读，不会修改源文件。";
    }

    /// <summary>把原文切成带行号的预览行（与主界面预览同一套显示约定）。</summary>
    private static List<PreviewLine> BuildLines(string text)
    {
        var lines = new List<PreviewLine>();

        if (text.Length == 0)
        {
            return lines;
        }

        var parts = text.Split('\n');

        for (var i = 0; i < parts.Length; i++)
        {
            lines.Add(new PreviewLine(
                LineNumber: i + 1,
                Text: parts[i].Replace("\r", string.Empty).Replace("\t", "→"),
                HasTrailingNewline: i < parts.Length - 1));
        }

        return lines;
    }

    private void OnCopyAll(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_text);
            SummaryText.Text = $"已复制全文（{_text.Length} 字符）到剪贴板。";
        }
        catch (Exception ex)
        {
            SummaryText.Text = $"复制失败：{ex.Message}";
        }
    }
}
