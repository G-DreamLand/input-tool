using InputTool.Core.Injection;

namespace InputTool.Tests.Injection;

/// <summary>
/// 字符 → 虚拟键映射（兼容模式，FR-011.4/.5、SRS §5.3.4）。
/// 扫描函数注入假实现，因此断言与真实键盘布局无关。
/// </summary>
public sealed class KeyMapperTests
{
    /// <summary>构造一个返回固定扫描码的映射器；低字节为虚拟键，高字节为修饰键位。</summary>
    private static KeyMapper WithScan(short scan) => new(_ => scan);

    [Fact]
    public void 扫描返回负值_应判定为无法映射()
    {
        var mapper = WithScan(-1);

        mapper.TryMapChar('中', out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void 低字节为0xFF_应判定为无法映射()
    {
        var mapper = WithScan(unchecked((short)0x00FF));

        mapper.TryMapChar('中', out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void 低字节为0_应判定为无法映射()
    {
        var mapper = WithScan(0x0100);

        mapper.TryMapChar('a', out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void 小写字母_应映射为无修饰键的虚拟键()
    {
        // 0x41 = VK_A，高字节 0 表示不需要修饰键
        var mapper = WithScan(0x0041);

        mapper.TryMapChar('a', out var key, out var modifiers, out var extended).Should().BeTrue();

        key.Should().Be(InputTool.Core.Interop.VirtualKey.A);
        modifiers.Should().Be(ModifierKeys.None);
        extended.Should().BeFalse();
    }

    [Fact]
    public void 大写字母_应要求Shift修饰键()
    {
        // 高字节 bit0 = Shift
        var mapper = WithScan(0x0141);

        mapper.TryMapChar('A', out var key, out var modifiers, out _).Should().BeTrue();

        key.Should().Be(InputTool.Core.Interop.VirtualKey.A);
        modifiers.Should().HaveFlag(ModifierKeys.Shift);
    }

    [Fact]
    public void 高字节包含Ctrl与Alt_应全部解析出来()
    {
        // bit1 = Ctrl（2）、bit2 = Alt（4）→ 高字节 6
        var mapper = WithScan(0x0641);

        mapper.TryMapChar('a', out _, out var modifiers, out _).Should().BeTrue();

        modifiers.Should().HaveFlag(ModifierKeys.Control);
        modifiers.Should().HaveFlag(ModifierKeys.Alt);
        modifiers.Should().NotHaveFlag(ModifierKeys.Shift);
    }

    [Theory]
    [InlineData(0x25)] // VK_LEFT
    [InlineData(0x26)] // VK_UP
    [InlineData(0x27)] // VK_RIGHT
    [InlineData(0x28)] // VK_DOWN
    [InlineData(0x2E)] // VK_DELETE
    public void 扩展键_应被标记为扩展(int virtualKey)
    {
        var mapper = WithScan((short)virtualKey);

        mapper.TryMapChar('x', out _, out _, out var extended).Should().BeTrue();

        extended.Should().BeTrue();
    }

    [Fact]
    public void 普通字母_不应标记为扩展键()
    {
        var mapper = WithScan(0x0041);

        mapper.TryMapChar('a', out _, out _, out var extended).Should().BeTrue();

        extended.Should().BeFalse();
    }
}
