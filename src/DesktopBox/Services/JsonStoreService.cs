using System.IO;
using System.Text.Json;
using DesktopBox.Models;

namespace DesktopBox.Services;

public class JsonStoreService : IPersistenceService
{
    private readonly string _path;
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web)
    { WriteIndented = true };

    public JsonStoreService(string path) => _path = path;

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppConfig();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppConfig>(json, _opts) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig(); // 损坏则降级为空配置,不崩
        }
    }

    public void Save(AppConfig config)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, _opts));
        if (File.Exists(_path)) File.Replace(tmp, _path, null); // 原子替换
        else File.Move(tmp, _path);
    }
}
