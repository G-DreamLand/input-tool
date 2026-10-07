namespace InputTool.Core.Interop;

/// <summary>
/// 屏幕与光标度量。对应《软件开发设计文档》§6.3（浮层定位到"鼠标附近"）。
/// </summary>
/// <remarks>
/// 光标坐标是**物理像素**；WPF 的 Left/Top 是设备无关单位（DIP）。
/// 高 DPI 下调用方需要用 <c>VisualTreeHelper.GetDpi(this)</c> 的缩放比换算。
/// </remarks>
public static class ScreenMetrics
{
    /// <summary>获取当前鼠标光标位置（物理像素，屏幕坐标系）。</summary>
    /// <returns>成功返回 true。</returns>
    public static bool TryGetCursorPosition(out int x, out int y)
    {
        x = 0;
        y = 0;

        if (!NativeMethods.GetCursorPos(out var point))
        {
            return false;
        }

        x = point.X;
        y = point.Y;
        return true;
    }
}
