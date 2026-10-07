using InputTool.Core.Interop;

namespace InputTool.Tests;

/// <summary>
/// 冒烟测试：验证测试宿主、断言库与本机 .NET 运行时链路可用。
/// 真正的模块单测按《软件开发设计文档》§12.1 的覆盖清单逐项补齐。
/// </summary>
public sealed class SmokeTests
{
    [Fact]
    public void 测试宿主应能运行并通过断言()
    {
        (1 + 1).Should().Be(2);
    }

    [Fact]
    public void 虚拟键码常量应与_Win32_定义一致()
    {
        ((ushort)VirtualKey.Return).Should().Be(0x0D);
        ((ushort)VirtualKey.Tab).Should().Be(0x09);
        ((ushort)VirtualKey.Space).Should().Be(0x20);
        ((ushort)VirtualKey.Escape).Should().Be(0x1B);
    }

    [Fact]
    public void 替代框架应可用于打桩()
    {
        var clock = Substitute.For<InputTool.Core.Abstractions.IClock>();
        clock.GetTimestamp().Returns(12345L);

        clock.GetTimestamp().Should().Be(12345L);
    }
}
