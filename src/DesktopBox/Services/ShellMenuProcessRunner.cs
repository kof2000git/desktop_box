using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace DesktopBox.Services;

public sealed class ShellMenuProcessRunner : IShellMenuRunner
{
    public const int RemoveFromBoxExitCode = 0x7000;
    public const int TargetDeletedExitCode = 0x7002;

    private readonly string _helperPath;
    private readonly TimeSpan _timeout;
    private readonly IReadOnlyList<string> _prefixArguments;
    private readonly object _processGate = new();
    private readonly HashSet<Process> _activeProcesses = new();
    private readonly ShellMenuJob _job = new();
    private bool _disposed;

    public ShellMenuProcessRunner()
        : this(Path.Combine(AppContext.BaseDirectory, "DesktopBox.ShellMenu.exe"))
    {
    }

    public ShellMenuProcessRunner(
        string helperPath,
        TimeSpan? timeout = null,
        IReadOnlyList<string>? prefixArguments = null)
    {
        _helperPath = helperPath;
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
        _prefixArguments = prefixArguments ?? [];
    }

    public async Task<ShellMenuRunResult> ShowAsync(
        string path,
        int screenX,
        int screenY,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _helperPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in _prefixArguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add(path);
        startInfo.ArgumentList.Add(screenX.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(screenY.ToString(CultureInfo.InvariantCulture));

        Process? process = null;
        try
        {
            bool isolated;
            lock (_processGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                try
                {
                    process = Process.Start(startInfo);
                }
                catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or DirectoryNotFoundException)
                {
                    // helper 缺失（没跑 build_dll.bat / publish 未拷贝）：回 StartFailed 走回退菜单，别弹错。
                    LogService.Warn("ShellMenu.MissingHelper",
                        $"helper 启动失败，回退到精简菜单 path={LogService.Truncate(_helperPath)} err={ex.Message}");
                    return new(ShellMenuRunStatus.StartFailed);
                }
                if (process is null)
                    return new(ShellMenuRunStatus.StartFailed);
                _activeProcesses.Add(process);
                isolated = _job.TryAssign(process);
            }

            if (!isolated)
            {
                // 宿主已在 Job 中（VS 调试/dotnet run/企业沙箱，单进程单 Job）时 Assign 恒失败。
                // 旧逻辑直接判菜单不可用（100% 右键失败）；现降级为无 Job 运行（helper 本身已是独立进程），只记日志。
                LogService.Warn("ShellMenu.NoJob",
                    $"helper 未进隔离 Job（宿主可能已在 Job 中），降级运行 path={LogService.Truncate(path)}");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await KillAndWaitAsync(process).ConfigureAwait(false);
                return new(ShellMenuRunStatus.TimedOut);
            }

            return process.ExitCode switch
            {
                0 => new(ShellMenuRunStatus.Completed, process.ExitCode),
                RemoveFromBoxExitCode => new(ShellMenuRunStatus.RemoveFromBox, process.ExitCode),
                TargetDeletedExitCode => new(ShellMenuRunStatus.TargetDeleted, process.ExitCode),
                _ => new(ShellMenuRunStatus.Crashed, process.ExitCode)
            };
        }
        catch (OperationCanceledException)
        {
            if (process is not null) await KillAndWaitAsync(process).ConfigureAwait(false);
            throw;
        }
        catch
        {
            if (process is null)
                return new(ShellMenuRunStatus.StartFailed);
            await KillAndWaitAsync(process).ConfigureAwait(false);
            return new(ShellMenuRunStatus.Crashed);
        }
        finally
        {
            if (process is not null)
            {
                lock (_processGate) _activeProcesses.Remove(process);
                process.Dispose();
            }
        }
    }

    private static async Task KillAndWaitAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch
        {
            // The helper may have exited between checks or already been killed by the job object.
        }
    }

    public void Dispose()
    {
        Process[] active;
        lock (_processGate)
        {
            if (_disposed) return;
            _disposed = true;
            active = _activeProcesses.ToArray();
        }

        foreach (var process in active)
        {
            try
            {
                // 退出时别在调用线程（常为 UI）同步等 5s，缩到 500ms，避免关机卡死观感。
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.WaitForExit(500);
            }
            catch { }
        }
        _job.Dispose();
        GC.SuppressFinalize(this);
    }
}
