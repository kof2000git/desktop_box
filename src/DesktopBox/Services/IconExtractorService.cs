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
            if (targetPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null; // 网址无本地图标

            Directory.CreateDirectory(CacheDir);

            // 缓存键:路径 + 修改时间,避免旧图标残留
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
            return null; // 任何失败降级为无图标,不崩
        }
    }
}
