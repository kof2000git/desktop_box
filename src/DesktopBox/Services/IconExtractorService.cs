using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using DesktopBox.Native;

namespace DesktopBox.Services;

public class IconExtractorService : IIconExtractorService
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopBox", "icons");

    public string? Extract(string targetPath)
    {
        try
        {
            if (string.IsNullOrEmpty(targetPath)) return null;
            Directory.CreateDirectory(CacheDir);

            // 系统图标(Shell 虚拟项,如 ::{645FF040-...})用 PIDL 方式
            if (targetPath.StartsWith("::")) return ExtractSystemIcon(targetPath);

            if (targetPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;

            var stamp = "0";
            try { if (File.Exists(targetPath) || Directory.Exists(targetPath)) stamp = File.GetLastWriteTimeUtc(targetPath).Ticks.ToString(); } catch { }
            var key = (targetPath.ToLowerInvariant() + "|" + stamp).GetHashCode(StringComparison.Ordinal).ToString("x");
            var png = Path.Combine(CacheDir, key + ".png");
            if (File.Exists(png)) return png;

            var info = new Shell32.SHFILEINFO();
            var size = (uint)Marshal.SizeOf<Shell32.SHFILEINFO>();
            Shell32.SHGetFileInfo(targetPath, 0, ref info, size, Shell32.SHGFI_ICON | Shell32.SHGFI_LARGEICON);
            if (info.hIcon == IntPtr.Zero) return null;

            using var icon = Icon.FromHandle(info.hIcon);
            using var bmp = icon.ToBitmap();
            bmp.Save(png, ImageFormat.Png);
            User32.DestroyIcon(info.hIcon);
            return png;
        }
        catch
        {
            return null;
        }
    }

    private string? ExtractSystemIcon(string clsidPath)
    {
        var key = ("sys|" + clsidPath).GetHashCode(StringComparison.Ordinal).ToString("x");
        var png = Path.Combine(CacheDir, key + ".png");
        if (File.Exists(png)) return png;

        if (Shell32.SHParseDisplayName(clsidPath, IntPtr.Zero, out var pidl, 0, out _) != 0
            || pidl == IntPtr.Zero) return null;

        try
        {
            var info = new Shell32.SHFILEINFO();
            var size = (uint)Marshal.SizeOf<Shell32.SHFILEINFO>();
            Shell32.SHGetFileInfoByPidl(pidl, 0, ref info, size,
                Shell32.SHGFI_PIDL | Shell32.SHGFI_ICON | Shell32.SHGFI_LARGEICON);
            if (info.hIcon == IntPtr.Zero) return null;

            using var icon = Icon.FromHandle(info.hIcon);
            using var bmp = icon.ToBitmap();
            bmp.Save(png, ImageFormat.Png);
            User32.DestroyIcon(info.hIcon);
            return png;
        }
        catch { return null; }
        finally { Shell32.ILFree(pidl); }
    }
}
