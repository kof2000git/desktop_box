using System.Collections.Generic;
using System.IO;

namespace DesktopBox.Services;

public class CategorizerService : ICategorizerService
{
    // 分类稳定 key(逻辑标识 + i18n 翻译 key,资源字典里对应 cat.xxx)。
    // 旧版是中文字符串("应用程序"等),既是逻辑键又是显示名;现拆分为 key + 翻译。
    public const string Program = "cat.apps";
    public const string Document = "cat.docs";
    public const string Image = "cat.images";
    public const string Archive = "cat.archive";
    public const string Video = "cat.video";
    public const string Audio = "cat.audio";
    public const string Shortcut = "cat.shortcut";
    public const string FolderCat = "cat.folder";
    public const string Other = "cat.other";

    /// <summary>旧版中文名 → 新 key。仅用于迁移历史 boxes.json:
    /// 把旧数据里的中文标签名(应用程序/文档/…)补上 Key,使其升级后能正确按当前语言显示。</summary>
    public static readonly Dictionary<string, string> LegacyZhToKey = new()
    {
        ["应用程序"] = Program,
        ["文档"] = Document,
        ["图片"] = Image,
        ["压缩包"] = Archive,
        ["视频"] = Video,
        ["音频"] = Audio,
        ["快捷方式"] = Shortcut,
        ["文件夹"] = FolderCat,
        ["其他"] = Other,
    };

    private static readonly HashSet<string> Docs = new()
    { ".doc", ".docx", ".pdf", ".txt", ".xls", ".xlsx", ".ppt", ".pptx", ".csv", ".rtf", ".md", ".wps", ".et", ".dps", ".odt", ".ods", ".odp",
      // 代码/脚本/标记/配置文件:归「文档」。开发者桌面常见,否则会落到「其他」标签找不到。
      ".py", ".pyw", ".js", ".mjs", ".ts", ".jsx", ".tsx", ".java", ".jar", ".class",
      ".c", ".h", ".cpp", ".cc", ".cxx", ".hpp", ".cs", ".go", ".rs", ".rb", ".php",
      ".swift", ".kt", ".kts", ".scala", ".sh", ".bash", ".zsh", ".lua", ".pl", ".pm",
      ".r", ".m", ".mm", ".dart", ".vue", ".svelte",
      ".html", ".htm", ".css", ".scss", ".sass", ".less",
      ".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf", ".env",
      ".sql", ".graphql", ".proto" };

    private static readonly HashSet<string> Imgs = new()
    { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif", ".svg", ".heic" };

    private static readonly HashSet<string> Arcs = new()
    { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".iso", ".cab" };

    private static readonly HashSet<string> Vids = new()
    { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".ts", ".m4v", ".rmvb", ".rm" };

    private static readonly HashSet<string> Auds = new()
    { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a", ".ape", ".mka", ".opus", ".aiff", ".mid", ".midi" };

    private static readonly HashSet<string> Apps = new()
    { ".exe", ".msi", ".bat", ".cmd", ".ps1", ".appref-ms", ".scr", ".appx", ".msix" };

    public string Categorize(string path)
    {
        try
        {
            if (Directory.Exists(path)) return FolderCat;

            var ext = Path.GetExtension(path).ToLowerInvariant();
            // 快捷方式独立成类:只有指向文件夹的快捷方式仍归「文件夹」,其余一律归「快捷方式」
            if (ext == ".lnk")
            {
                var target = ResolveShortcut(path);
                if (!string.IsNullOrEmpty(target) && Directory.Exists(target))
                    return FolderCat;
                return Shortcut;
            }
            if (Apps.Contains(ext)) return Program;
            return ByExt(ext);
        }
        catch
        {
            return Other;
        }
    }

    private static string ByExt(string ext)
    {
        if (Docs.Contains(ext)) return Document;
        if (Imgs.Contains(ext)) return Image;
        if (Arcs.Contains(ext)) return Archive;
        if (Vids.Contains(ext)) return Video;
        if (Auds.Contains(ext)) return Audio;
        return Other;
    }

    /// <summary>通过 WScript.Shell COM 解析 .lnk 真实目标。失败返回空。</summary>
    private static string ResolveShortcut(string lnkPath)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return "";
            dynamic shell = System.Activator.CreateInstance(type)!;
            dynamic sc = shell.CreateShortcut(lnkPath);
            return (string)sc.TargetPath;
        }
        catch
        {
            return "";
        }
    }
}
