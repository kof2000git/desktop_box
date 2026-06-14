using System.IO;
using System.Text.Json;
using DesktopBox.Models;

namespace DesktopBox.Services;

/// <summary>
/// 一键整理:只引用、不移动。文件始终留在桌面原处,盒子只记录路径引用。
/// 这样其它写死桌面路径的程序/脚本不会因整理而失效。
/// </summary>
public class OrganizeService : IOrganizeService
{
    private static readonly string AppData =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private readonly IDesktopScannerService _scanner;
    private readonly ICategorizerService _categorizer;
    private readonly string _manifestPath;
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    // 生产构造:清单固定到 %AppData%\DesktopBox\organize.json
    public OrganizeService(IDesktopScannerService scanner, ICategorizerService categorizer)
        : this(scanner, categorizer, Path.Combine(AppData, "DesktopBox", "organize.json"))
    { }

    // 测试构造:可注入临时清单路径
    public OrganizeService(IDesktopScannerService scanner, ICategorizerService categorizer, string manifestPath)
    {
        _scanner = scanner;
        _categorizer = categorizer;
        _manifestPath = manifestPath;
    }

    public bool HasActiveOrganize => File.Exists(_manifestPath);
    public int CountOrganizable() => _scanner.ScanDesktop().Count;

    public OrganizeResult? Organize()
    {
        var paths = _scanner.ScanDesktop();
        if (paths.Count == 0) return null;

        var manifest = new OrganizeManifest { Timestamp = DateTime.Now };
        var result = new OrganizeResult { Manifest = manifest };

        foreach (var p in paths)
        {
            if (Path.GetFileName(p).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            var cat = _categorizer.Categorize(p);
            manifest.Moves.Add(new MoveRecord { OriginalPath = p, NewPath = p, Category = cat });
            result.Entries.Add(new OrganizeEntry { Category = cat, DisplayName = DisplayName(p), CurrentPath = p });
        }

        if (result.Entries.Count == 0) return null;
        SaveManifest(manifest);
        return result;
    }

    public void RecordBoxIds(IEnumerable<Guid> boxIds)
    {
        var manifest = LoadManifest();
        if (manifest is null) return;
        manifest.BoxIds = boxIds.ToList();
        SaveManifest(manifest);
    }

    public OrganizeResult? Restore()
    {
        var manifest = LoadManifest();
        if (manifest is null) return null;
        // 引用模式:文件从未移动,无需移回。仅清除清单记录。
        try { File.Delete(_manifestPath); } catch { }
        return new OrganizeResult { Manifest = manifest };
    }

    // ---- helpers ----
    private static string DisplayName(string path)
    {
        var n = Path.GetFileName(path);
        return n.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(n)
            : n;
    }

    private void SaveManifest(OrganizeManifest m)
    {
        var dir = Path.GetDirectoryName(_manifestPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_manifestPath, JsonSerializer.Serialize(m, _opts));
    }

    private OrganizeManifest? LoadManifest()
    {
        try
        {
            if (!File.Exists(_manifestPath)) return null;
            return JsonSerializer.Deserialize<OrganizeManifest>(File.ReadAllText(_manifestPath), _opts);
        }
        catch { return null; }
    }
}
