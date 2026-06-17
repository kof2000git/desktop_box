using System.IO;

namespace DesktopBox.Models;

/// <summary>所有用户数据(配置/整理清单/图标缓存)的存放位置:随可执行文件走(绿色便携)。</summary>
public static class AppPaths
{
    /// <summary>可执行文件所在目录。AppContext.BaseDirectory 对单文件发布同样指向 exe 目录。</summary>
    public static string AppDir => AppContext.BaseDirectory;

    /// <summary>实际可写数据目录。优先 exe 同目录；若不可写则退回 LocalAppData，避免安装到 Program Files 后配置静默丢失。</summary>
    public static string DataDir { get; } = ResolveDataDir();

    /// <summary>盒子布局与设置配置(boxes.json)。</summary>
    public static string ConfigPath => Path.Combine(DataDir, "boxes.json");

    /// <summary>整理盒子 id 清单(organize.json)。</summary>
    public static string OrganizePath => Path.Combine(DataDir, "organize.json");

    /// <summary>图标缓存目录(可重建)。</summary>
    public static string IconCacheDir => Path.Combine(DataDir, "icons");

    /// <summary>错误日志(未处理异常等),与配置同目录:绿色便携,便于事后排查。</summary>
    public static string LogPath => Path.Combine(DataDir, "logs", "error.log");

    private static string ResolveDataDir()
    {
        if (CanWriteDirectory(AppDir)) return AppDir;

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopBox");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private static bool CanWriteDirectory(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".desktopbox-write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
