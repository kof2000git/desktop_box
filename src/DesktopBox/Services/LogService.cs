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
    private const long MaxBytes = 512 * 1024;
    private const int MaxGenerations = 5;
    private const int MaxQueueLength = 2000;
    private static bool _startMarkerWritten;

    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> Queue = new();
    private static readonly AutoResetEvent Signal = new(false);
    private static readonly Thread WriterThread;
    private static volatile bool _running = true;

    static LogService()
    {
        WriterThread = new Thread(ProcessLogQueue)
        {
            IsBackground = true,
            Name = "DesktopBox.LogWriter",
            Priority = ThreadPriority.BelowNormal
        };
        WriterThread.Start();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
    }

    public static string AppLogPath => Path.Combine(Models.AppPaths.DataDir, "logs", "app.log");

    public static void Info(string source, string message) => Enqueue("INFO", source, message, null);

    public static void Warn(string source, string message) => Enqueue("WARN", source, message, null);

    public static void Error(Exception? ex, string source, string? context = null)
    {
        var msg = context is null ? ex?.Message ?? "(null)" : $"{context} :: {ex?.Message}";
        Enqueue("ERROR", source, msg, ex);
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

    private static void Enqueue(string level, string source, string message, Exception? ex)
    {
        try
        {
            if (Queue.Count > MaxQueueLength) return;

            var tid = Environment.CurrentManagedThreadId;
            var pid = Environment.ProcessId;
            var sb = new StringBuilder();
            sb.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}][{DateTime.UtcNow:HH:mm:ss}Z]");
            sb.Append($"[pid={pid}:tid={tid}][v{App.Version}][{level}][{source}] ");
            sb.AppendLine(message);
            if (ex is not null)
                AppendException(sb, ex, 1);

            Queue.Enqueue(sb.ToString());
            Signal.Set();
        }
        catch { /* 日志格式化失败绝不影响程序 */ }
    }

    private static void ProcessLogQueue()
    {
        var batch = new List<string>(64);
        while (_running)
        {
            try
            {
                Signal.WaitOne(500);
                FlushBatch(batch);
            }
            catch { }
        }
        FlushBatch(batch);
    }

    private static void FlushBatch(List<string> buffer)
    {
        buffer.Clear();
        while (Queue.TryDequeue(out var line))
        {
            buffer.Add(line);
            if (buffer.Count >= 100) break;
        }
        if (buffer.Count == 0) return;

        try
        {
            var path = AppLogPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            RotateIfNeeded(path);

            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            foreach (var item in buffer)
            {
                writer.Write(item);
            }
            writer.Flush();
        }
        catch { /* 写入失败静默跳过 */ }
    }

    /// <summary>同步冲刷待写队列（退出或崩溃前调用）。</summary>
    public static void Flush()
    {
        _running = false;
        try { Signal.Set(); } catch { }
        try
        {
            var batch = new List<string>();
            for (int i = 0; i < 10 && !Queue.IsEmpty; i++)
            {
                FlushBatch(batch);
            }
        }
        catch { }
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxBytes) return;
            var oldest = path + "." + MaxGenerations;
            if (File.Exists(oldest))
            {
                try { File.Delete(oldest); } catch { }
            }
            for (var i = MaxGenerations - 1; i >= 1; i--)
            {
                var src = path + "." + i;
                if (File.Exists(src))
                {
                    try { File.Move(src, path + "." + (i + 1), overwrite: true); } catch { }
                }
            }
            try { File.Move(path, path + ".1", overwrite: true); } catch { }
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
