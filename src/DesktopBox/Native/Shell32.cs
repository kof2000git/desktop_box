using System;
using System.Runtime.InteropServices;

namespace DesktopBox.Native;

public static class Shell32
{
    public const uint SHGFI_ICON = 0x000000100;
    public const uint SHGFI_LARGEICON = 0x000000000;
    public const uint SHGFI_PIDL = 0x000000008;

    // 文件路径形式
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfo")]
    public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    // PIDL 形式(用于系统图标等 Shell 虚拟项)
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfo")]
    public static extern IntPtr SHGetFileInfoByPidl(IntPtr pidl, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    /// <summary>把解析名(如 ::{CLSID})解析为 PIDL。成功返回 0。</summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHParseDisplayName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszName, IntPtr pbc,
        out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    public static extern void ILFree(IntPtr pidl);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }
}
