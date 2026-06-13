using System;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace DesktopBox.Services;

public class ThemeService : IThemeService
{
    public void Apply(string theme)
    {
        var t = theme.Equals("Light", StringComparison.OrdinalIgnoreCase)
            ? ApplicationTheme.Light
            : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(t);
    }

    public void ApplySystem()
    {
        Apply(IsSystemDark() ? "Dark" : "Light");
        SystemEvents.UserPreferenceChanged -= OnUserPrefChanged; // 避免重复订阅
        SystemEvents.UserPreferenceChanged += OnUserPrefChanged;
    }

    private void OnUserPrefChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
            Apply(IsSystemDark() ? "Dark" : "Light");
    }

    public bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return true; // 默认深色
        }
    }
}
