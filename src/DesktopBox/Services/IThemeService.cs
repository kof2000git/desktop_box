using System;
using Microsoft.Win32;

namespace DesktopBox.Services;

public interface IThemeService : IDisposable
{
    void Apply(string theme);   // "Dark" | "Light"
    void ApplySystem();         // 跟随系统,并监听变化
    void StopFollowingSystem();
    bool IsSystemDark();
}

public interface IThemeDispatcher
{
    void Invoke(Action action);
}

public interface ISystemThemeEvents
{
    void Subscribe(UserPreferenceChangedEventHandler handler);
    void Unsubscribe(UserPreferenceChangedEventHandler handler);
}
