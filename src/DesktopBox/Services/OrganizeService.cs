using System.IO;
using System.Text.Json;
using DesktopBox.Models;

namespace DesktopBox.Services;

public class OrganizeService : IOrganizeService
{
    private static readonly string AppData =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private readonly IDesktopScannerService _scanner;
    private readonly ICategorizerService _categorizer;
    private readonly string _organizedRoot;
    private readonly string _manifestPath;
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    // 生产用:路径固定到 %AppData%\DesktopBox\...
    public OrganizeService(IDesktopScannerService scanner, ICategorizerService categorizer)
        : this(scanner, categorizer,
               Path.Combine(AppData, "DesktopBox", "Organized"),
               Path.Combine(AppData, "DesktopBox", "organize.json"))
    { }

    // 测试用:可注入临时路径
    public OrganizeService(IDesktopScannerService scanner, ICategorizerService categorizer,
                           string organizedRoot, string manifestPath)
    {
        _scanner = scanner;
        _categorizer = categorizer;
        _organizedRoot = organizedRoot;
        _manifestPath = manifestPath;
    }

    public string OrganizedRoot => _organizedRoot;

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
            var cat = _categorizer.Categorize(p);
            var name = DisplayName(p);
            var destDir = Path.Combine(_organizedRoot, cat);
            try { Directory.CreateDirectory(destDir); }
            catch { continue; }

            var dest = UniquePath(Path.Combine(destDir, name));
            try
            {
                if (Directory.Exists(p)) Directory.Move(p, dest);
                else File.Move(p, dest);
            }
            catch
            {
                continue; // 文件占用/权限问题:跳过,不中断
            }

            manifest.Moves.Add(new MoveRecord { OriginalPath = p, NewPath = dest, Category = cat });
            result.Entries.Add(new OrganizeEntry { Category = cat, DisplayName = name, CurrentPath = dest });
        }

        if (manifest.Moves.Count == 0) return null;
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

        foreach (var m in manifest.Moves)
        {
            try
            {
                if (Directory.Exists(m.NewPath))
                {
                    if (!Directory.Exists(m.OriginalPath)) Directory.Move(m.NewPath, m.OriginalPath);
                }
                else if (File.Exists(m.NewPath))
                {
                    if (!File.Exists(m.OriginalPath)) File.Move(m.NewPath, m.OriginalPath);
                }
            }
            catch { /* 单条失败不中断还原 */ }
        }

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

    private static string UniquePath(string desired)
    {
        if (!File.Exists(desired) && !Directory.Exists(desired)) return desired;
        var dir = Path.GetDirectoryName(desired) ?? "";
        var name = Path.GetFileNameWithoutExtension(desired);
        var ext = Path.GetExtension(desired);
        for (int i = 2; i < 1000; i++)
        {
            var cand = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(cand) && !Directory.Exists(cand)) return cand;
        }
        return desired + ".dup";
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
