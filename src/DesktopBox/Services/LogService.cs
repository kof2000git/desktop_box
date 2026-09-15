using System.Diagnostics;
using System.IO;
using System.Text;

namespace DesktopBox.Services;

/// <summary>
/// 轻量分级日志：单文件 app.log（与 error.log 同目录），按大小轮转，
/// 同步追加 + 锁保护（写入量小，UI 线程直接调也不会卡）。
/// 下次复现请附 logs/app.log + logs/error.log。
/// </summary>
public static class LogService
{
    private static readonly object Gate = new();
    private const long MaxBytes = 256 * 1024;
    private const int MaxGenerations = 5;
    private static bool _startMarkerWritten;

    public static string AppLogPath => Path.Combine(Models.AppPaths.DataDir, "logs", "app.log");

    public static void Info(string source, string message) => Write("INFO", source, message, null);

    public static void Warn(string source, string message) => Write("WARN", source, message, null);

    public static void Error(Exception? ex, string source, string? context = null)
    {
        var msg = context is null ? ex?.Message ?? "(null)" : $"{context} :: {ex?.Message}";
        Write("ERROR", source, msg, ex);
    }

    public static void WriteStartMarker()
    {
        if (_startMarkerWritten) return;
        _startMarkerWritten = true;
        try
        {
            var proc = Process.GetCurrentProcess();
            Info("App.Startup",
                $"==== start v{App.Version} pid={proc.Id} x64={Environment.Is64BitProcess} " +
                $"OS={Environment.OSVersion} DataDir={Models.AppPaths.DataDir} ====");
        }
        catch { }
    }

    public static string Truncate(string? s, int max = 260)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        return s.Length <= max ? s : s[..max] + "…";
    }

    private static void Write(string level, string source, string message, Exception? ex)
    {
        try
        {
            var path = AppLogPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tid = Environment.CurrentManagedThreadId;
            var pid = Environment.ProcessId;
            var sb = new StringBuilder();
            sb.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}][{DateTime.UtcNow:HH:mm:ss}Z]");
            sb.Append($"[pid={pid}:tid={tid}][v{App.Version}][{level}][{source}] ");
            sb.AppendLine(message);
            if (ex is not null)
                AppendException(sb, ex, 1);
            lock (Gate)
            {
                RotateIfNeeded(path);
                File.AppendAllText(path, sb.ToString());
            }
        }
        catch { /* 日志失败绝不影响程序 */ }
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxBytes) return;
            var oldest = path + "." + MaxGenerations;
            if (File.Exists(oldest)) File.Delete(oldest);
            for (var i = MaxGenerations - 1; i >= 1; i--)
            {
                var src = path + "." + i;
                if (File.Exists(src)) File.Move(src, path + "." + (i + 1), overwrite: true);
            }
            File.Move(path, path + ".1", overwrite: true);
        }
        catch { }
    }

    private static void AppendException(StringBuilder sb, Exception ex, int depth)
    {
        var indent = new string(' ', depth * 2);
        sb.AppendLine($"{indent}Type: {ex.GetType().FullName} HResult=0x{ex.HResult:X8}");
        sb.AppendLine($"{indent}Stack: {ex.StackTrace?.Trim() ?? "(无)"}");
        if (ex.InnerException is { } inner)
        {
            sb.AppendLine($"{indent}---> Inner:");
            AppendException(sb, inner, depth + 1);
        }
    }

    /// <summary>打开日志目录（托盘/设置共用）。</summary>
    public static void OpenLogDirectory()
    {
        try
        {
            var dir = Path.GetDirectoryName(AppLogPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Error(ex, "LogService.OpenLogDirectory");
        }
    }
}
