namespace DesktopBox.Models;

public class AppSettings
{
    public bool AutoStart { get; set; }
    public bool FollowSystemTheme { get; set; } = true;
    public string Theme { get; set; } = "Dark";
    /// <summary>全局视图模式(所有盒子/标签共享)。默认大图标。</summary>
    public ViewMode GlobalViewMode { get; set; } = ViewMode.Large;
}
