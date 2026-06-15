using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using DesktopBox.Models;
using DesktopBox.Native;

namespace DesktopBox.Services;

public class IconExtractorService : IIconExtractorService
{
    private static readonly string CacheDir = AppPaths.IconCacheDir;

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

        // SHGetImageList 返回系统图像列表的 COM 接口,必须在 STA 单元调用。
        // 本方法由 Task.Run 线程池调用,线程池是 MTA 且无法用 CoInitializeEx(STA) 重初始化
        // (RPC_E_CHANGED_MODE),MTA 下 SHGetImageList 直接返回 E_NOINTERFACE → 图标提取全失败。
        // 解决:起一个专用 STA 线程跑提取,阻塞等待结果(CLR 会自动为它初始化 STA)。
        string? result = null;
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { result = ExtractSystemIconCore(clsidPath, png); }
            catch (Exception ex) { error = ex; }
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) App.LogError(error, "ExtractSystemIcon.STA");
        return result;
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
            App.LogError(new Exception($"sysicon clsid={clsidPath} hIcon={(long)info.hIcon} iIcon={info.iIcon}"), "ExtractSystemIcon.HICON");
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
}
