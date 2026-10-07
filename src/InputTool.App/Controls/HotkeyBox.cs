using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InputTool.Core.Hotkeys;
using InputTool.Core.Interop;

namespace InputTool.App.Controls;

/// <summary>
/// 热键捕获控件（FR-025.1）。点击后按下组合键即完成录入。
/// </summary>
/// <remarks>
/// 实现要点：
/// · 只按修饰键时不录入，等待用户按下主键；
/// · <c>e.Key == Key.System</c> 时必须取 <c>e.SystemKey</c>，否则 Alt 组合会被识别成 System；
/// · WPF 的 <see cref="Key"/> 通过 <see cref="KeyInterop.VirtualKeyFromKey"/> 转成 Win32 虚拟键码，
///   与 <see cref="HotkeyService"/> 注册时使用的编码保持一致；
/// · Esc 或 Delete/Backspace 清空当前热键。
/// 注意：本控件继承自 <see cref="TextBox"/>，而 WPF 的隐式样式不作用于派生类型，
/// 因此使用时需要显式指定 <c>Style="{StaticResource AppTextBoxStyle}"</c>。
/// </remarks>
public sealed class HotkeyBox : TextBox
{
    /// <summary>当前热键。</summary>
    public static readonly DependencyProperty GestureProperty = DependencyProperty.Register(
        nameof(Gesture),
        typeof(HotkeyGesture),
        typeof(HotkeyBox),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnGestureChanged));

    /// <summary>热键发生变化。</summary>
    public event EventHandler? GestureChanged;

    /// <summary>构造热键捕获框。</summary>
    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
        VerticalContentAlignment = VerticalAlignment.Center;
        ToolTip = "点击后按下组合键即可录入；Esc 或 Delete 清空（FR-025.1）";
    }

    /// <summary>当前热键；为 null 表示未设置。</summary>
    public HotkeyGesture? Gesture
    {
        get => (HotkeyGesture?)GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;

        // Alt 组合键在 WPF 中会以 Key.System 形式到达，真实主键在 SystemKey 上
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // 只按修饰键：等待主键，不录入
        if (key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
        {
            return;
        }

        // Esc / Delete / Backspace：清空
        if (key is Key.Escape or Key.Delete or Key.Back)
        {
            Gesture = null;
            return;
        }

        var virtualKey = (VirtualKey)KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == VirtualKey.None)
        {
            return;
        }

        var modifiers = HotkeyModifiers.NoRepeat;
        var pressed = Keyboard.Modifiers;
        if (pressed.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (pressed.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (pressed.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (pressed.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Win;

        Gesture = new HotkeyGesture(modifiers, virtualKey);
    }

    /// <inheritdoc />
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        SelectAll();
    }

    private static void OnGestureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not HotkeyBox box)
        {
            return;
        }

        var gesture = (HotkeyGesture?)e.NewValue;
        box.Text = gesture?.ToString() ?? string.Empty;
        box.GestureChanged?.Invoke(box, EventArgs.Empty);
    }
}
