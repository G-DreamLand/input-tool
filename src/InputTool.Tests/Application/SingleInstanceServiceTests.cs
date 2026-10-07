using InputTool.Core.Application;
using InputTool.Core.Injection;
using InputTool.Core.Interop;

namespace InputTool.Tests.Application;

/// <summary>
/// 单实例运行（FR-022、设计文档 §5.9.3）。
/// </summary>
public sealed class SingleInstanceServiceTests
{
    private static string UniqueMutexName() => $@"Local\InputTool.Tests.{Guid.NewGuid():N}";

    [Fact]
    public void 首个实例_应被识别为第一实例()
    {
        using var service = new SingleInstanceService(UniqueMutexName());

        service.IsFirstInstance.Should().BeTrue();
    }

    [Fact]
    public void 同名互斥体的第二个实例_不应被识别为第一实例()
    {
        var name = UniqueMutexName();

        using var first = new SingleInstanceService(name);
        using var second = new SingleInstanceService(name);

        first.IsFirstInstance.Should().BeTrue();
        second.IsFirstInstance.Should().BeFalse("第二个实例应被识别出来并去激活已有窗口");
    }

    [Fact]
    public void 不同名的实例_互不影响()
    {
        using var first = new SingleInstanceService(UniqueMutexName());
        using var second = new SingleInstanceService(UniqueMutexName());

        first.IsFirstInstance.Should().BeTrue();
        second.IsFirstInstance.Should().BeTrue();
    }

    [Fact]
    public void 释放第一实例后_新的同名实例应重新成为第一实例()
    {
        var name = UniqueMutexName();

        using (var first = new SingleInstanceService(name))
        {
            first.IsFirstInstance.Should().BeTrue();
        }

        using var again = new SingleInstanceService(name);
        again.IsFirstInstance.Should().BeTrue("上一个实例已退出，互斥体应随之销毁");
    }

    [Fact]
    public void 注册激活消息_应得到非零ID()
    {
        using var service = new SingleInstanceService(UniqueMutexName());

        service.ActivationMessageId.Should().NotBe(0, "RegisterWindowMessage 成功时应返回非零 ID");
    }

    [Fact]
    public void 收到激活消息_应触发激活请求()
    {
        using var service = new SingleInstanceService(UniqueMutexName());
        var raised = false;
        service.ActivationRequested += (_, _) => raised = true;

        var handled = service.TryHandleWindowMessage(service.ActivationMessageId, IntPtr.Zero);

        handled.Should().BeTrue();
        raised.Should().BeTrue();
    }

    [Fact]
    public void 非激活消息_应返回未消费()
    {
        using var service = new SingleInstanceService(UniqueMutexName());

        service.TryHandleWindowMessage(0x0001, IntPtr.Zero).Should().BeFalse();
        service.TryHandleWindowMessage(0x0312, IntPtr.Zero).Should().BeFalse();
    }

    [Fact]
    public void 重复释放_不应抛异常()
    {
        var service = new SingleInstanceService(UniqueMutexName());

        var act = () =>
        {
            service.Dispose();
            service.Dispose();
        };

        act.Should().NotThrow();
    }
}

/// <summary>
/// 权限自检（FR-027、设计文档 §5.9.2）。
/// 真正的提权重启需要 UAC 交互，属于 AC-18 的人工验收项；这里只覆盖可自动化的部分。
/// </summary>
public sealed class ElevationServiceTests
{
    [Fact]
    public void 查询当前进程是否提权_应可安全调用()
    {
        var service = new ElevationService();

        var act = () => service.IsElevated;
        act.Should().NotThrow();
    }

    [Fact]
    public void 空句柄_不应被判定为更高完整性()
    {
        var service = new ElevationService(new InjectabilityProbe());

        service.IsTargetProcessHigherIntegrity(IntPtr.Zero).Should().BeFalse();
    }

    [Fact]
    public void 当前前台窗口_通常不应高于本进程完整性()
    {
        var service = new ElevationService(new InjectabilityProbe());
        var foreground = WindowEnumerator.GetForegroundWindow();

        // 前台窗口是本进程或同级进程时必然为 false；若为管理员窗口则可能为 true，
        // 因此这里只断言"可安全调用且不抛异常"。
        var act = () => service.IsTargetProcessHigherIntegrity(foreground);
        act.Should().NotThrow();
    }

    [Fact]
    public void 使用注入的探测器_应转发完整性判断()
    {
        var probe = new TestDoubles.FakeInjectabilityProbe();
        var service = new ElevationService(probe);

        service.IsTargetProcessHigherIntegrity(new IntPtr(1)).Should().BeFalse();
    }
}
