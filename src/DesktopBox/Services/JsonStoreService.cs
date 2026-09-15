using System.IO;
using System.Text.Json;
using DesktopBox.Models;

namespace DesktopBox.Services;

public class JsonStoreService : IPersistenceService
{
    private static readonly object FileGate = new();
    private readonly string _path;
    private readonly Action<string, string, string?> _replace;
    private bool _saveBlocked;
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web)
    { WriteIndented = true };

    public JsonStoreService(string path) : this(path, File.Replace) { }

    internal JsonStoreService(string path, Action<string, string, string?> replace)
    {
        _path = path;
        _replace = replace;
    }

    public AppConfig Load()
    {
        lock (FileGate)
        {
            if (!File.Exists(_path))
            {
                if (_saveBlocked && TryLoad(_path + ".bak", out var blockedBackup))
                    return blockedBackup;

                _saveBlocked = false;
                return new AppConfig();
            }

            try
            {
                if (TryLoad(_path, out var config))
                {
                    _saveBlocked = false;
                    return config;
                }

                ArchiveCorruptPrimary();
                var backupPath = _path + ".bak";
                if (!TryLoad(backupPath, out config))
                {
                    _saveBlocked = false;
                    return new AppConfig();
                }

                try
                {
                    RestoreBackup(backupPath);
                    _saveBlocked = false;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // bak 可读：读到配置先用，只是回写主文件失败才禁写，避免误伤。
                    _saveBlocked = true;
                    App.LogError(ex, "JsonStore.RestoreBackup");
                }
                return config;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // ArchiveCorruptPrimary 失败（主文件被锁）但 bak 可读时：不断言 blocked，
                // 先用 bak 顶着，下次 Save 成功再解禁。避免“明明 bak 可用却禁写”。
                if (TryLoadBackupAfterPrimaryFailure(out var backup))
                {
                    _saveBlocked = false;
                    App.LogError(ex, "JsonStore.LoadPrimaryLocked");
                    return backup;
                }
                _saveBlocked = true;
                throw;
            }
        }
    }

    public void Save(AppConfig config)
    {
        lock (FileGate)
        {
            if (_saveBlocked)
                throw new IOException("Configuration is temporarily read-only because the primary file could not be read safely.");

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            try
            {
                File.WriteAllText(tmp, JsonSerializer.Serialize(config, _opts));
                if (!File.Exists(_path))
                {
                    File.Move(tmp, _path);
                    return;
                }

                try
                {
                    _replace(tmp, _path, _path + ".bak");
                }
                catch (NotSupportedException)
                {
                    File.Copy(_path, _path + ".bak", overwrite: true);
                    File.Move(tmp, _path, overwrite: true);
                }
            }
            finally
            {
                // Delete 失败（杀软/索引锁住 .tmp）不能掩盖原保存异常。
                try { File.Delete(tmp); } catch { }
            }
        }
    }

    private static bool TryLoad(string path, out AppConfig config)
    {
        config = null!;
        if (!File.Exists(path)) return false;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), _opts)!;
            return config is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void ArchiveCorruptPrimary()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff");
        File.Move(_path, $"{_path}.{timestamp}.corrupt");
    }

    private bool TryLoadBackupAfterPrimaryFailure(out AppConfig config)
    {
        try
        {
            return TryLoad(_path + ".bak", out config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            config = null!;
            return false;
        }
    }

    private void RestoreBackup(string backupPath)
    {
        var tmp = _path + ".tmp";
        try
        {
            File.Copy(backupPath, tmp, overwrite: true);
            File.Move(tmp, _path, overwrite: true);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }
}
