using System;
using System.IO;

namespace DesktopBox.Native;

/// <summary>解析 .lnk 快捷方式指向的目标路径(WScript.Shell)。
/// 用于"在资源管理器中显示"定位到快捷方式目标本身,而非 .lnk 所在的桌面。</summary>
public static class ShellLinkResolver
{
    public static string? ResolveTarget(string lnkPath)
    {
        try
        {
            if (!File.Exists(lnkPath)) return null;
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic sc = shell.CreateShortcut(lnkPath);
            var target = (string?)sc.TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch { return null; }
    }

    public static (string? iconPath, int iconIndex) ResolveIconLocation(string lnkPath)
    {
        try
        {
            if (!File.Exists(lnkPath)) return (null, -1);
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return (null, -1);
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic sc = shell.CreateShortcut(lnkPath);
            var raw = (string?)sc.IconLocation;
            if (string.IsNullOrWhiteSpace(raw)) return (null, -1);

            var parts = raw.Split(',', 2, StringSplitOptions.TrimEntries);
            var iconPath = Environment.ExpandEnvironmentVariables(parts[0].Trim().Trim('"'));
            var iconIndex = parts.Length > 1 && int.TryParse(parts[1], out var idx) ? idx : 0;
            return (iconPath, iconIndex);
        }
        catch { return (null, -1); }
    }
}
