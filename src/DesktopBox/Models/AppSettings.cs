namespace DesktopBox.Models;

public class AppSettings
{
    public bool AutoStart { get; set; }
    public bool FollowSystemTheme { get; set; } = true;
    public string Theme { get; set; } = "Dark";
}
