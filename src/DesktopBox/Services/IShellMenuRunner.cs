namespace DesktopBox.Services;

public enum ShellMenuRunStatus
{
    Completed,
    RemoveFromBox,
    StartFailed,
    IsolationUnavailable,
    Crashed,
    TimedOut
}

public readonly record struct ShellMenuRunResult(ShellMenuRunStatus Status, int? ExitCode = null);

public interface IShellMenuRunner : IDisposable
{
    Task<ShellMenuRunResult> ShowAsync(
        string path,
        int screenX,
        int screenY,
        CancellationToken cancellationToken = default);
}
