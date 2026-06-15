using System.IO;
using System.Text.Json;
using DesktopBox.Models;

namespace DesktopBox.Services;

/// <summary>
/// 桌面扫描 + 分类 + 整理盒子标识。引用模式:文件始终留在桌面原处,只记录路径引用。
/// 增量整理:本服务只负责"扫描+分类"与"整理盒子 id 管理";"哪些是新文件"的判断
/// 由 MainViewModel 对照整理盒子已有条目完成。
/// </summary>
public class OrganizeService : IOrganizeService
{
    private readonly IDesktopScannerService _scanner;
    private readonly ICategorizerService _categorizer;
    private readonly string _manifestPath;
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    // 生产构造:清单随程序走(便携,放可执行文件同目录)
    public OrganizeService(IDesktopScannerService scanner, ICategorizerService categorizer)
        : this(scanner, categorizer, AppPaths.OrganizePath)
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

    public List<(string Path, string Category)> ScanAndCategorize()
    {
        var result = new List<(string, string)>();
        foreach (var p in _scanner.ScanDesktop())
        {
            if (Path.GetFileName(p).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            result.Add((p, _categorizer.Categorize(p)));
        }
        return result;
    }

    public void RecordBoxIds(IEnumerable<Guid> boxIds)
    {
        var manifest = LoadManifest() ?? new OrganizeManifest { Timestamp = DateTime.Now };
        manifest.BoxIds = boxIds.ToList();
        SaveManifest(manifest);
    }

    public Guid? GetOrganizeBoxId()
    {
        var id = LoadManifest()?.BoxIds.FirstOrDefault();
        return id is { } g && g != Guid.Empty ? g : null;
    }

    // ---- helpers ----
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
