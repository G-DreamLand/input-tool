using System.Runtime.InteropServices;

namespace InputTool.Core.Interop;

/// <summary>
/// SendInput 使用的 Win32 结构体。
/// 依据《软件开发设计文档》§5.5.2：<c>INPUT</c> 在 x64 下必须是 40 字节，
/// 因此联合体中必须保留 <see cref="MOUSEINPUT"/>（它是最大的成员，
/// 决定联合体尺寸）。<see cref="NativeMethods.InputSize"/> 会做运行时校验。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    internal uint type;
    internal InputUnion U;
}

[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)] internal MOUSEINPUT mi;
    [FieldOffset(0)] internal KEYBDINPUT ki;
    [FieldOffset(0)] internal HARDWAREINPUT hi;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    internal int dx;
    internal int dy;
    internal uint mouseData;
    internal uint dwFlags;
    internal uint time;
    internal IntPtr dwExtraInfo;
}

/// <summary>
/// 键盘输入事件。
/// Unicode 注入时 <see cref="wVk"/> = 0、<see cref="wScan"/> = UTF-16 码元、
/// <see cref="dwFlags"/> 含 <c>KEYEVENTF_UNICODE</c>（§5.3.3）。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    internal ushort wVk;
    internal ushort wScan;
    internal uint dwFlags;

    /// <summary>保持为 0，由系统填充，不伪造时间戳（FR-010.7）。</summary>
    internal uint time;

    /// <summary>保持为 <see cref="IntPtr.Zero"/>，不伪造其他进程的输入来源。</summary>
    internal IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HARDWAREINPUT
{
    internal uint uMsg;
    internal ushort wParamL;
    internal ushort wParamH;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    internal int X;
    internal int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SID_AND_ATTRIBUTES
{
    internal IntPtr Sid;
    internal uint Attributes;
}

/// <summary>用于 GetTokenInformation(TokenIntegrityLevel) 的强制标签结构（FR-027）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TOKEN_MANDATORY_LABEL
{
    internal SID_AND_ATTRIBUTES Label;
}
