using InputTool.Core.Injection;
using InputTool.Core.Interop;
using InputTool.Core.Parsing;
using InputTool.Core.Scheduling;

namespace InputTool.IntegrationTests;

/// <summary>
/// 真实桌面打字验证（AC-06 / AC-09 的核心环节）：
/// 开一个真实的 WinForms 窗口 + 多行文本框，让 <see cref="Win32KeyboardEmitter"/>
/// 通过 <c>SendInput</c> 把文本打进去，再把文本框内容读回来比对。
/// </summary>
/// <remarks>
/// 打 <c>[Trait("Category","Desktop")]</c>：需要真实桌面会话且会短暂抢占前台焦点，
/// CI 默认跳过，由 M5 阶段的受控执行项手动运行。
/// </remarks>
// 桌面用例共享同一集合 → 串行执行；并行时它们会互相抢走前台焦点
[Trait("Category", "Desktop")]
[Collection("Desktop")]
public sealed class RealTypingTests
{
    /// <summary>节拍：测试用 30 ms，跑得快一些。</summary>
    private const int IntervalMs = 30;

    [Theory]
    [InlineData("Hello 123")]
    [InlineData("中文字符测试")]
    [InlineData("Mixed 中英 mixed 123")]
    [InlineData("符号：！@#￥%……&*（）")]
    [InlineData("Tab\t分隔\t内容")]
    public async Task 应能把文本真实键入到前台窗口(string text)
    {
        var received = await TypeIntoRealWindowAsync(text);

        // WinForms 文本框把换行存成 CRLF，断言前统一成 LF 再比较
        Normalize(received).Should().Be(Normalize(text));
    }

    [Fact]
    public async Task 空行与多行文本_应正确换行()
    {
        const string text = "第一行\n第二行\n\n第四行";

        var received = await TypeIntoRealWindowAsync(text);

        Normalize(received).Should().Be(Normalize(text));
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n");

    // ── 测试脚手架 ──────────────────────────────────────────────────────

    private static async Task<string> TypeIntoRealWindowAsync(string text)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        // WinForms 需要 STA 线程与消息泵
        var thread = new Thread(() =>
        {
            using var form = new System.Windows.Forms.Form
            {
                Text = "Input Tool — 打字验证目标",
                Width = 640,
                Height = 220,
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                TopMost = true,
            };

            var box = new System.Windows.Forms.TextBox
            {
                Multiline = true,
                Dock = System.Windows.Forms.DockStyle.Fill,
                Font = new System.Drawing.Font("Microsoft YaHei UI", 14F),
                WordWrap = true,
                // 必须开启：否则 Tab 会被当作"切换焦点"，测不到 VK_TAB 注入
                AcceptsTab = true,
            };

            form.Controls.Add(box);

            form.Shown += (_, _) =>
            {
                box.Focus();
                box.Select();

                // 留一点时间让窗口真正拿到前台焦点，再开始注入
                var timer = new System.Windows.Forms.Timer { Interval = 400 };
                timer.Tick += async (_, _) =>
                {
                    timer.Stop();
                    try
                    {
                        // 必须**确认**本窗口已是前台窗口，否则注入会打到别的窗口上
                        var activated = false;
                        for (var attempt = 0; attempt < 25 && !activated; attempt++)
                        {
                            activated = WindowEnumerator.TryActivate(form.Handle);
                            if (!activated)
                            {
                                await Task.Delay(200, TestContext.Current.CancellationToken);
                            }
                        }

                        activated.Should().BeTrue("验证窗口未能获得前台焦点，注入会打到别的窗口");

                        box.Focus();
                        await Task.Delay(200, TestContext.Current.CancellationToken);

                        await TypeAsync(text);
                        await Task.Delay(300, TestContext.Current.CancellationToken);
                        completion.TrySetResult(box.Text);
                    }
                    catch (Exception ex)
                    {
                        completion.TrySetException(ex);
                    }
                    finally
                    {
                        form.Close();
                    }
                };

                timer.Start();
            };

            System.Windows.Forms.Application.Run(form);
        })
        {
            IsBackground = true,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return await completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }

    /// <summary>用真实的注入器与调度器把文本打出去。</summary>
    private static async Task TypeAsync(string text)
    {
        var unitBuilder = new InputUnitBuilder();
        var built = unitBuilder.Build(text, NormalizeOptions.Default);

        var keyboard = new Win32KeyboardEmitter();
        var scheduler = new InputScheduler(keyboard);

        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Finished += (_, _) => finished.TrySetResult();

        scheduler.Start(built.Units, () => new SpeedProfile(IntervalMs));
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
