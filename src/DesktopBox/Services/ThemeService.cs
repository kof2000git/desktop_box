using System;
using Microsoft.Win32;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace DesktopBox.Services;

public class ThemeService : IThemeService
{
    private readonly object _lifecycleLock = new();
    private readonly IThemeDispatcher _dispatcher;
    private readonly ISystemThemeEvents _systemEvents;
    private bool _isFollowingSystemTheme;
    private bool _isDisposed;

    public ThemeService()
        : this(
            new WpfThemeDispatcher(System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher),
            new SystemThemeEvents())
    {
    }

    public ThemeService(IThemeDispatcher dispatcher, ISystemThemeEvents systemEvents)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _systemEvents = systemEvents ?? throw new ArgumentNullException(nameof(systemEvents));
    }

    public void Apply(string theme)
    {
        var t = theme.Equals("Light", StringComparison.OrdinalIgnoreCase)
            ? ApplicationTheme.Light
            : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(t);
    }

    public void ApplySystem()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (!_isFollowingSystemTheme)
            {
                _systemEvents.Subscribe(OnUserPrefChanged);
                _isFollowingSystemTheme = true;
            }
        }

        ApplyCurrentSystemTheme();
    }

    public void StopFollowingSystem()
    {
        lock (_lifecycleLock)
        {
            if (!_isFollowingSystemTheme)
                return;

            _isFollowingSystemTheme = false;
            _systemEvents.Unsubscribe(OnUserPrefChanged);
        }
    }

    private void OnUserPrefChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
            return;

        lock (_lifecycleLock)
        {
            if (_isDisposed || !_isFollowingSystemTheme)
                return;
        }

        _dispatcher.Invoke(() =>
        {
            lock (_lifecycleLock)
            {
                if (_isDisposed || !_isFollowingSystemTheme)
                    return;

                ApplyCurrentSystemTheme();
            }
        });
    }

    protected virtual void ApplyCurrentSystemTheme() => Apply(IsSystemDark() ? "Dark" : "Light");

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

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            if (_isFollowingSystemTheme)
            {
                _isFollowingSystemTheme = false;
                _systemEvents.Unsubscribe(OnUserPrefChanged);
            }
        }
    }
}

internal sealed class WpfThemeDispatcher : IThemeDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfThemeDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Invoke(Action action)
    {
        if (!_dispatcher.HasShutdownStarted && !_dispatcher.HasShutdownFinished)
            _dispatcher.BeginInvoke(action);
    }
}

internal sealed class SystemThemeEvents : ISystemThemeEvents
{
    public void Subscribe(UserPreferenceChangedEventHandler handler) =>
        SystemEvents.UserPreferenceChanged += handler;

    public void Unsubscribe(UserPreferenceChangedEventHandler handler) =>
        SystemEvents.UserPreferenceChanged -= handler;
}
