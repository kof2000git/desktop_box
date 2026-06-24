using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DesktopBox.Models;
using DesktopBox.Native;

namespace DesktopBox.Services;

public class IconExtractorService : IIconExtractorService
{
    private static readonly string CacheDir = AppPaths.IconCacheDir;

    public string? Extract(string targetPath) => Extract(targetPath, forceRefresh: false);

    /// <param name="forceRefresh">true 时删除系统图标的旧缓存,重新提取当前状态(回收站空/满切换后用)。</param>
    public string? Extract(string targetPath, bool forceRefresh)
    {
        try
        {
            if (string.IsNullOrEmpty(targetPath)) return null;
            Directory.CreateDirectory(CacheDir);

            // 系统图标(Shell 虚拟项,如 ::{645FF040-...})用 PIDL 方式
            if (targetPath.StartsWith("::")) return ExtractSystemIcon(targetPath, forceRefresh);

            if (targetPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;

            if (targetPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var shortcutIcon = ExtractShortcutIcon(targetPath, forceRefresh);
                if (!string.IsNullOrEmpty(shortcutIcon))
                    return shortcutIcon;
            }

            return ExtractPathIcon(targetPath, forceRefresh);
        }
        catch
        {
            return null;
        }
    }

    private string? ExtractPathIcon(string targetPath, bool forceRefresh)
    {
        var stamp = "0";
        try { if (File.Exists(targetPath) || Directory.Exists(targetPath)) stamp = File.GetLastWriteTimeUtc(targetPath).Ticks.ToString(); } catch { }
        var key = StableKey(targetPath.ToLowerInvariant() + "|" + stamp);
        var png = Path.Combine(CacheDir, key + ".png");
        if (forceRefresh)
        {
            try { if (File.Exists(png)) File.Delete(png); } catch { }
        }
        if (File.Exists(png)) return png;

        var info = new Shell32.SHFILEINFO();
        var size = (uint)Marshal.SizeOf<Shell32.SHFILEINFO>();
        Shell32.SHGetFileInfo(targetPath, 0, ref info, size, Shell32.SHGFI_ICON | Shell32.SHGFI_LARGEICON);
        if (info.hIcon == IntPtr.Zero) return null;

        try
        {
            using var icon = Icon.FromHandle(info.hIcon);
            using var bmp = icon.ToBitmap();
            bmp.Save(png, ImageFormat.Png);
        }
        finally
        {
            User32.DestroyIcon(info.hIcon);
        }
        return png;
    }

    private string? ExtractShortcutIcon(string lnkPath, bool forceRefresh = false)
    {
        try
        {
            var (iconPath, iconIndex) = ShellLinkResolver.ResolveIconLocation(lnkPath);
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                iconPath = Environment.ExpandEnvironmentVariables(iconPath.Trim().Trim('"'));
                if (!iconPath.StartsWith("::", StringComparison.Ordinal) && !Path.IsPathRooted(iconPath))
                {
                    var shortcutDir = Path.GetDirectoryName(lnkPath);
                    if (!string.IsNullOrWhiteSpace(shortcutDir))
                        iconPath = Path.GetFullPath(Path.Combine(shortcutDir, iconPath));
                }

                if (iconPath.StartsWith("::", StringComparison.Ordinal))
                {
                    var systemIcon = Extract(iconPath, forceRefresh);
                    if (!string.IsNullOrEmpty(systemIcon))
                        return systemIcon;
                }
                else
                {
                    var resourceIcon = ExtractIconResource(iconPath, iconIndex, forceRefresh);
                    if (!string.IsNullOrEmpty(resourceIcon))
                        return resourceIcon;
                }
            }

            var targetPath = ShellLinkResolver.ResolveTarget(lnkPath);
            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                var targetIcon = ExtractPathIcon(targetPath, forceRefresh);
                if (!string.IsNullOrEmpty(targetIcon))
                    return targetIcon;
            }

            return ExtractPathIcon(lnkPath, forceRefresh);
        }
        catch
        {
            return null;
        }
    }

    private string? ExtractIconResource(string iconPath, int iconIndex, bool forceRefresh)
    {
        try
        {
            if (!File.Exists(iconPath))
                return null;

            var iconStamp = "0";
            try { iconStamp = File.GetLastWriteTimeUtc(iconPath).Ticks.ToString(); } catch { }
            var key = StableKey("lnk|" + iconPath.ToLowerInvariant() + "|" + iconIndex + "|" + iconStamp);
            var png = Path.Combine(CacheDir, key + ".png");
            if (forceRefresh)
            {
                try { if (File.Exists(png)) File.Delete(png); } catch { }
            }
            if (File.Exists(png) && !forceRefresh) return png;

            var large = IntPtr.Zero;
            var small = IntPtr.Zero;
            try
            {
                var count = Shell32.ExtractIconEx(iconPath, iconIndex, out large, out small, 1);
                var hIcon = large != IntPtr.Zero ? large : small;
                if (count == 0 || hIcon == IntPtr.Zero)
                    return null;
                using var icon = Icon.FromHandle(hIcon);
                using var bmp = icon.ToBitmap();
                bmp.Save(png, ImageFormat.Png);
                return png;
            }
            finally
            {
                if (large != IntPtr.Zero) User32.DestroyIcon(large);
                if (small != IntPtr.Zero) User32.DestroyIcon(small);
            }
        }
        catch
        {
            return null;
        }
    }

    private string? ExtractSystemIcon(string clsidPath, bool forceRefresh = false)
    {
        var key = StableKey("sys|" + clsidPath);
        var png = Path.Combine(CacheDir, key + ".png");
        // 系统图标(回收站空/满、此电脑盘符)的状态会变化,但缓存 key 不含时间戳,
        // 默认命中缓存即返回旧图。forceRefresh=true 时删旧缓存强制重新提取当前状态。
        if (forceRefresh)
        {
            // 删掉这个 key 的所有旧缓存(基础名 + 历次时间戳名),避免累积
            try { foreach (var f in Directory.GetFiles(CacheDir, key + "*.png")) File.Delete(f); } catch { }
        }
        if (File.Exists(png) && !forceRefresh) return png;

        // forceRefresh 路径:重新提取后用带时间戳的文件名返回,确保 IconCachePath 值变化——
        // 否则 png 文件内容变了但路径不变,WPF Image 会复用缓存的 BitmapImage 不重绘(回收站图标不更新)。
        var stamp = DateTime.UtcNow.Ticks;
        var pngRefresh = forceRefresh ? Path.Combine(CacheDir, key + "_" + stamp + ".png") : png;
        var target = forceRefresh ? pngRefresh : png;

        // SHGetImageList 返回系统图像列表的 COM 接口,必须在 STA 单元调用。
        // 本方法由 Task.Run 线程池调用,线程池是 MTA 且无法用 CoInitializeEx(STA) 重初始化
        // (RPC_E_CHANGED_MODE),MTA 下 SHGetImageList 直接返回 E_NOINTERFACE → 图标提取全失败。
        // 解决:起一个专用 STA 线程跑提取,阻塞等待结果(CLR 会自动为它初始化 STA)。
        try
        {
            return StaTaskRunner.RunSync(() => ExtractSystemIconCore(clsidPath, target));
        }
        catch (Exception ex)
        {
            App.LogError(ex, "ExtractSystemIcon.STA");
            return null;
        }
    }

    /// <summary>在 STA 线程上执行真正的图标提取。用 SHGFI_ICON 直接拿 hIcon(不走 COM 系统图像列表——
    /// 后者在托管环境无论 MTA/STA 都返回 E_NOINTERFACE)。系统虚拟项 CLSID 已在 Load 迁移为当前有效值,
    /// 故 SHGFI_ICON 对此电脑/回收站/控制面板/网络均能返回真实图标。</summary>
    private static string? ExtractSystemIconCore(string clsidPath, string png)
    {
        if (Shell32.SHParseDisplayName(clsidPath, IntPtr.Zero, out var pidl, 0, out _) != 0
            || pidl == IntPtr.Zero) return null;

        try
        {
            var info = new Shell32.SHFILEINFO();
            var size = (uint)Marshal.SizeOf<Shell32.SHFILEINFO>();
            Shell32.SHGetFileInfoByPidl(pidl, 0, ref info, size,
                Shell32.SHGFI_PIDL | Shell32.SHGFI_ICON | Shell32.SHGFI_LARGEICON);
            if (info.hIcon == IntPtr.Zero) return null;
            try
            {
                using var icon = Icon.FromHandle(info.hIcon);
                using var bmp = icon.ToBitmap();
                bmp.Save(png, ImageFormat.Png);
                return png;
            }
            finally { User32.DestroyIcon(info.hIcon); }
        }
        finally { Shell32.ILFree(pidl); }
    }

    private static string StableKey(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes, 0, 12).ToLowerInvariant();
    }
}
