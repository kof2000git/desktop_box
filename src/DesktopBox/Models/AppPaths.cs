using System.IO;

namespace DesktopBox.Models;

/// <summary>所有用户数据(配置/整理清单/图标缓存)的存放位置:随可执行文件走(绿色便携)。</summary>
public static class AppPaths
{
    /// <summary>可执行文件所在目录。AppContext.BaseDirectory 对单文件发布同样指向 exe 目录。</summary>
    public static string AppDir => AppContext.BaseDirectory;

    /// <summary>盒子布局与设置配置(boxes.json)。</summary>
    public static string ConfigPath => Path.Combine(AppDir, "boxes.json");

    /// <summary>整理盒子 id 清单(organize.json)。</summary>
    public static string OrganizePath => Path.Combine(AppDir, "organize.json");

    /// <summary>图标缓存目录(可重建)。</summary>
    public static string IconCacheDir => Path.Combine(AppDir, "icons");

    /// <summary>错误日志(未处理异常等),与配置同目录:绿色便携,便于事后排查。</summary>
    public static string LogPath => Path.Combine(AppDir, "logs", "error.log");
}
