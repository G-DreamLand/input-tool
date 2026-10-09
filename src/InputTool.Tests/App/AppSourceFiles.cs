namespace InputTool.Tests.App;

/// <summary>
/// 读取 App 层源码 / XAML，供"界面硬约束与文案契约"类用例做断言。
/// </summary>
/// <remarks>
/// WPF 层没法在单测里跑起来（用户也要求不自动启动程序），
/// 于是退一步：把"不许写死、必须绑定"这类契约直接钉在源码文本上，
/// 谁违反测试立刻变红。
/// </remarks>
internal static class AppSourceFiles
{
    /// <summary>仓库根目录（含 <c>src/InputTool.sln</c> 的那一级）。</summary>
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "InputTool.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"从 {AppContext.BaseDirectory} 向上找不到仓库根目录（应包含 src 下的 InputTool.sln）");
    }

    public static string Read(params string[] relativeParts)
    {
        var path = Path.Combine(new[] { RepoRoot() }.Concat(relativeParts).ToArray());

        File.Exists(path).Should().BeTrue($"契约用例需要读到源码：{path}");

        return File.ReadAllText(path);
    }

    public static string View(string fileName) => Read("src", "InputTool.App", "Views", fileName);

    public static string ViewModel(string fileName) => Read("src", "InputTool.App", "ViewModels", fileName);
}