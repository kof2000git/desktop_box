using System;
using System.IO;
using Microsoft.Win32;

namespace DesktopBox.Services;

public class StartupService : IStartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopBox";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        var value = key?.GetValue(ValueName) as string;
        return !string.IsNullOrWhiteSpace(value);
    }

    public void Enable()
    {
        var exe = ResolveExecutablePath();
        if (string.IsNullOrEmpty(exe))
            throw new InvalidOperationException("Unable to resolve DesktopBox.exe path for startup registration.");

        using var key = Registry.CurrentUser.CreateSubKey(RunKey)
            ?? throw new InvalidOperationException("Unable to open HKCU Run key for startup registration.");
        // Quoted path so spaces under LocalAppData/user folders do not break the Run entry.
        key.SetValue(ValueName, $"\"{exe}\"");
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, false);
    }

    /// <summary>Prefer the real process image path (correct for single-file publish), then fall back to exe beside BaseDirectory.</summary>
    internal static string? ResolveExecutablePath()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
                return Path.GetFullPath(processPath);
        }
        catch
        {
            // ProcessPath can throw on exotic hosts; fall through.
        }

        try
        {
            var besideBase = Path.Combine(AppContext.BaseDirectory, "DesktopBox.exe");
            if (File.Exists(besideBase))
                return Path.GetFullPath(besideBase);
        }
        catch
        {
            // BaseDirectory may be unavailable in some test hosts.
        }

        return null;
    }
}
