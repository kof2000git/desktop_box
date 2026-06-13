namespace DesktopBox.Services;

public interface IThemeService
{
    void Apply(string theme);   // "Dark" | "Light"
    void ApplySystem();         // 跟随系统,并监听变化
    bool IsSystemDark();
}
