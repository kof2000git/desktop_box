using System;
using CommunityToolkit.Mvvm.ComponentModel;
using DesktopBox.Services;

namespace DesktopBox.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IStartupService _startup;
    private readonly IThemeService _theme;
    private readonly IPersistenceService _store;

    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private bool _followSystemTheme;
    [ObservableProperty] private bool _isDark;

    public SettingsViewModel(IStartupService startup, IThemeService theme, IPersistenceService store)
    {
        _startup = startup;
        _theme = theme;
        _store = store;

        var cfg = store.Load();
        _autoStart = startup.IsEnabled();
        _followSystemTheme = cfg.Settings.FollowSystemTheme;
        _isDark = _followSystemTheme ? _theme.IsSystemDark()
            : cfg.Settings.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase);
    }

    partial void OnAutoStartChanged(bool value)
    {
        if (value) _startup.Enable();
        else _startup.Disable();
    }

    partial void OnFollowSystemThemeChanged(bool value) => ApplyChanges();

    partial void OnIsDarkChanged(bool value)
    {
        if (!FollowSystemTheme) ApplyChanges();
    }

    public void ApplyChanges()
    {
        var cfg = _store.Load();
        cfg.Settings.FollowSystemTheme = FollowSystemTheme;
        if (!FollowSystemTheme) cfg.Settings.Theme = IsDark ? "Dark" : "Light";
        _store.Save(cfg);

        if (FollowSystemTheme) _theme.ApplySystem();
        else _theme.Apply(IsDark ? "Dark" : "Light");
    }
}
