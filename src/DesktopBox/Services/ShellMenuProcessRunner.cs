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
                process = Process.Start(startInfo);
                if (process is null)
                    return new(ShellMenuRunStatus.StartFailed);
                _activeProcesses.Add(process);
                isolated = _job.TryAssign(process);
            }

            if (!isolated)
            {
                App.LogError(
                    new Win32Exception(Marshal.GetLastWin32Error(), "Could not assign Shell menu helper to its isolation job."),
                    "ShellMenuProcessRunner.AssignJob");
                await KillAndWaitAsync(process).ConfigureAwait(false);
                return new(ShellMenuRunStatus.IsolationUnavailable);
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
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch { }
        }
        _job.Dispose();
        GC.SuppressFinalize(this);
    }
}
