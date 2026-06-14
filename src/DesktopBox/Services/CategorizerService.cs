using System.Collections.Generic;
using System.IO;

namespace DesktopBox.Services;

public class CategorizerService : ICategorizerService
{
    public const string Program = "程序";
    public const string Document = "文档";
    public const string Image = "图片";
    public const string Archive = "压缩包";
    public const string Video = "视频";
    public const string Music = "音乐";
    public const string FolderCat = "文件夹";
    public const string Other = "其他";

    private static readonly HashSet<string> Docs = new()
    { ".doc", ".docx", ".pdf", ".txt", ".xls", ".xlsx", ".ppt", ".pptx", ".csv", ".rtf", ".md", ".wps", ".et", ".dps", ".odt", ".ods", ".odp" };

    private static readonly HashSet<string> Imgs = new()
    { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif", ".svg", ".heic" };

    private static readonly HashSet<string> Arcs = new()
    { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".iso", ".cab" };

    private static readonly HashSet<string> Vids = new()
    { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".ts", ".m4v", ".rmvb", ".rm" };

    private static readonly HashSet<string> Musc = new()
    { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a" };

    private static readonly HashSet<string> Apps = new()
    { ".exe", ".msi", ".bat", ".cmd", ".ps1", ".appref-ms", ".scr" };

    public string Categorize(string path)
    {
        try
        {
            if (Directory.Exists(path)) return FolderCat;

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".lnk")
            {
                var target = ResolveShortcut(path);
                if (!string.IsNullOrEmpty(target))
                {
                    if (Directory.Exists(target)) return FolderCat;
                    var te = Path.GetExtension(target).ToLowerInvariant();
                    if (Apps.Contains(te)) return Program;
                    return ByExt(te);
                }
                return Program; // 无法解析的快捷方式默认归「程序」
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
        if (Musc.Contains(ext)) return Music;
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
