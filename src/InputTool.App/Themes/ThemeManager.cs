using System.Windows;
using Microsoft.Win32;

namespace InputTool.App.Themes;

/// <summary>主题模式（与 Core 的 <c>ThemeMode</c> 对应，界面层独立一份避免 Core 依赖 WPF）。</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// 主题管理：切换浅色/深色、跟随系统。对应《软件开发设计文档》§6.6。
/// </summary>
/// <remarks>
/// 实现要点：
/// · 主题字典固定位于 <c>Application.Resources.MergedDictionaries[0]</c>，
///   切换时整体替换该字典；控件样式对这些画刷使用 DynamicResource，因此立即生效；
/// · 跟随系统时读取注册表 AppsUseLightTheme，并监听
///   <see cref="SystemParameters.StaticPropertyChanged"/> 以响应用户在系统设置里的切换。
/// </remarks>
public static class ThemeManager
{
    private const string LightSource = "pack://application:,,,/Themes/Theme.Light.xaml";
    private const string DarkSource = "pack://application:,,,/Themes/Theme.Dark.xaml";

    private static bool _hooked;

    /// <summary>当前选择的主题模式。</summary>
    public static AppTheme Current { get; private set; } = AppTheme.System;

    /// <summary>当前实际生效的是否为深色。</summary>
    public static bool IsDarkEffective { get; private set; }

    /// <summary>应用主题。传入 <see cref="AppTheme.System"/> 时按系统设置解析。</summary>
    public static void Apply(AppTheme theme)
    {
        Current = theme;

        bool dark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => IsSystemDark(),
        };

        IsDarkEffective = dark;
        SwapThemeDictionary(dark ? DarkSource : LightSource);

        if (theme == AppTheme.System)
        {
            HookSystemChanges();
        }
    }

    /// <summary>读取系统"应用模式"是否为深色。</summary>
    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int light)
            {
                return light == 0;
            }
        }
        catch (Exception)
        {
            // 读不到注册表就按浅色处理，不影响功能。
        }

        return false;
    }

    private static void SwapThemeDictionary(string source)
    {
        if (Application.Current is null)
        {
            return;
        }

        var themeDictionary = new ResourceDictionary { Source = new Uri(source, UriKind.Absolute) };
        var merged = Application.Current.Resources.MergedDictionaries;

        if (merged.Count == 0)
        {
            merged.Add(themeDictionary);
        }
        else
        {
            merged[0] = themeDictionary;
        }
    }

    private static void HookSystemChanges()
    {
        if (_hooked)
        {
            return;
        }

        _hooked = true;
        SystemParameters.StaticPropertyChanged += (_, _) =>
        {
            if (Current != AppTheme.System)
            {
                return;
            }

            bool dark = IsSystemDark();
            if (dark != IsDarkEffective)
            {
                IsDarkEffective = dark;
                SwapThemeDictionary(dark ? DarkSource : LightSource);
            }
        };
    }
}
