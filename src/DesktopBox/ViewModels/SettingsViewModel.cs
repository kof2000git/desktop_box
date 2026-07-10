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
    private bool _persistedFollowSystemTheme;
    private bool _persistedIsDark;
    private string _persistedLanguage = "auto";
    private bool _isRollingBack;

    public event EventHandler<Exception>? PersistenceFailed;

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
        _persistedFollowSystemTheme = _followSystemTheme;
        _persistedIsDark = _isDark;
        _persistedLanguage = _language;
    }

    partial void OnAutoStartChanged(bool value)
    {
        if (value) _startup.Enable();
        else _startup.Disable();
    }

    partial void OnFollowSystemThemeChanged(bool value)
    {
        if (!_isRollingBack) ApplyChanges();
    }

    partial void OnIsDarkChanged(bool value)
    {
        if (!_isRollingBack && !FollowSystemTheme) ApplyChanges();
    }

    partial void OnLanguageChanged(string value)
    {
        if (_isRollingBack) return;
        try
        {
            // Persist before applying so a failed write cannot leave UI and disk disagreeing.
            var cfg = _store.Load();
            cfg.Settings.Language = value;
            _store.Save(cfg);
            _persistedLanguage = value;
            _localizer.Apply(value);
        }
        catch (Exception ex)
        {
            _isRollingBack = true;
            try { Language = _persistedLanguage; }
            finally { _isRollingBack = false; }
            OnPropertyChanged(nameof(LangIndex));
            ReportPersistenceFailure(ex);
        }
    }

    public void ApplyChanges()
    {
        try
        {
            var cfg = _store.Load();
            cfg.Settings.FollowSystemTheme = FollowSystemTheme;
            if (!FollowSystemTheme) cfg.Settings.Theme = IsDark ? "Dark" : "Light";
            _store.Save(cfg);
            _persistedFollowSystemTheme = FollowSystemTheme;
            _persistedIsDark = IsDark;
        }
        catch (Exception ex)
        {
            _isRollingBack = true;
            try
            {
                FollowSystemTheme = _persistedFollowSystemTheme;
                IsDark = _persistedIsDark;
            }
            finally { _isRollingBack = false; }
            ReportPersistenceFailure(ex);
            return;
        }

        if (FollowSystemTheme)
        {
            _theme.ApplySystem();
        }
        else
        {
            _theme.StopFollowingSystem();
            _theme.Apply(IsDark ? "Dark" : "Light");
        }
    }

    private void ReportPersistenceFailure(Exception exception)
    {
        App.LogError(exception, "SettingsViewModel.Save");
        PersistenceFailed?.Invoke(this, exception);
    }
}
