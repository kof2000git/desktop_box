namespace DesktopBox.Models;

public class AppSettings
{
    public bool AutoStart { get; set; }
    public bool FollowSystemTheme { get; set; } = true;
    public string Theme { get; set; } = "Dark";
    /// <summary>界面语言:"auto"(跟随系统)/ "zh-CN" / "en-US"。默认跟随系统。</summary>
    public string Language { get; set; } = "auto";
    /// <summary>全局视图模式(所有盒子/标签共享)。默认大图标。</summary>
    public ViewMode GlobalViewMode { get; set; } = ViewMode.Large;
}
