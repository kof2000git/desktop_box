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

    // 系统图像列表(比 SHGFI_ICON 对虚拟文件夹更可靠:"网络"等 CLSID 在 SHGFI_ICON 下常返回空图标)
    public const uint SHGFI_SYSICONINDEX = 0x000004000;
    public const uint SHIL_LARGE = 0x000000000;
    public const uint ILD_NORMAL = 0x000000000;

    [DllImport("shell32.dll", EntryPoint = "SHGetImageList")]
    public static extern int SHGetImageList(uint iImageList, [In] ref Guid riid, out IntPtr ppv);

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

/// <summary>系统图像列表 COM 接口(IImageList)。只声明到 GetIcon(vtable 前6个方法占位以对齐槽位)。
/// 必须在 STA 线程调用(见 IconExtractorService.ExtractSystemIcon)。</summary>
[ComImport, Guid("46EB5926-582E-4017-9FDF-E899822AA095"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IImageList
{
    void Add(IntPtr hbmImage, IntPtr hbmMask, out int pi);
    void ReplaceIcon(int i, IntPtr hicon, out int pi);
    void SetOverlayImage(int iImage, int iOverlay);
    void Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
    void AddMasked(IntPtr hbmImage, uint crMask, out int pi);
    void Draw(IntPtr pimldp);
    // PreserveSig:直接拿 HRESULT,失败(如 iIcon 越界)不抛 COM 异常,便于记录真实 hr
    [PreserveSig]
    int GetIcon(int i, uint flags, out IntPtr picon);
}
