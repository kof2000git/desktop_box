using System;
using CommunityToolkit.Mvvm.ComponentModel;
using DesktopBox.Services;

namespace DesktopBox.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IStartupService _startup;
    private readonly IThemeService _theme;
    private readonly IPersistenceService _store;
    private readonly ILocalizerService _localizer;

    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private bool _followSystemTheme;
    [ObservableProperty] private bool _isDark;
    [ObservableProperty] private string _language = "auto";

    /// <summary>语言下拉选中索引:0=auto(跟随系统), 1=简体中文, 2=English。</summary>
    public int LangIndex
    {
        get => Language switch { "zh-CN" => 1, "en-US" => 2, _ => 0 };
        set => Language = value switch { 1 => "zh-CN", 2 => "en-US", _ => "auto" };
    }

    public SettingsViewModel(IStartupService startup, IThemeService theme, IPersistenceService store, ILocalizerService localizer)
    {
        _startup = startup;
        _theme = theme;
        _store = store;
        _localizer = localizer;

        var cfg = store.Load();
        _autoStart = startup.IsEnabled();
        _followSystemTheme = cfg.Settings.FollowSystemTheme;
        _isDark = _followSystemTheme ? _theme.IsSystemDark()
            : cfg.Settings.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase);
        _language = string.IsNullOrEmpty(cfg.Settings.Language) ? "auto" : cfg.Settings.Language;
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

    partial void OnLanguageChanged(string value)
    {
        // 持久化 + 即时切换:DynamicResource 自动刷新,托盘菜单/分类标签 Header 由事件刷新
        var cfg = _store.Load();
        cfg.Settings.Language = value;
        _store.Save(cfg);
        _localizer.Apply(value);
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
