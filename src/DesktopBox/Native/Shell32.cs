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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")]
    public static extern uint ExtractIconEx(string lpszFile, int nIconIndex,
        out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

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

    // ===== SHChangeNotifyRegister:接收 shell 变化通知(图标更新/关联变化/回收站变更等)=====
    // 用于系统图标自动刷新:回收站清空后图标从"满"变"空"、"此电脑"盘符变化等都会发通知。

    public const uint SHCNE_RENAMEITEM = 0x00000001;
    public const uint SHCNE_CREATE = 0x00000002;        // 文件创建(删文件进回收站触发)
    public const uint SHCNE_DELETE = 0x00000004;        // 文件删除(回收站清空触发)
    public const uint SHCNE_UPDATEITEM = 0x00000800;    // 项目更新
    public const uint SHCNE_UPDATEDIR = 0x00001000;     // 目录更新(回收站内容变化)
    public const uint SHCNE_UPDATEIMAGE = 0x00008000;   // 系统图标列表更新
    public const uint SHCNE_ASSOCCHANGED = 0x08000000;  // 文件关联变化(图标可能变)
    public const uint SHCNE_RENAMEFOLDER = 0x00020000;
    public const uint SHCNE_RMDIR = 0x00000010;

    public const uint SHCNRF_InterruptLevel = 0x0001;
    public const uint SHCNRF_ShellLevel = 0x0002;
    public const uint SHCNRF_NewDelivery = 0x8000;       // 接收指针形式的 PIDL 列表(必须配合此标志)

    /// <summary>注册一个窗口接收 shell 变化通知。成功返回注册 ID(>0),失败返回 0。</summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint SHChangeNotifyRegister(
        IntPtr hwnd,            // 接收通知消息的窗口
        uint fSources,          // SHCNRF_*
        uint fEvents,           // 关注的事件 SHCNE_*
        uint wMsg,              // shell 把通知作为此消息号发送给 hwnd
        int cEntries,
        ref SHChangeNotifyEntry[] pfsne);

    /// <summary>取消注册。</summary>
    [DllImport("shell32.dll")]
    public static extern bool SHChangeNotifyDeregister(uint ulID);

    /// <summary>通知条目。pidl 为 IntPtr.Zero 表示"全局/所有"。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SHChangeNotifyEntry
    {
        public IntPtr pidl;
        public bool fRecursive;
    }

    /// <summary>解析通知携带的 PIDL 列表(NewDelivery 模式:WPARAM=数量,LPARAM=PIDL 数组指针)。</summary>
    [DllImport("shell32.dll")]
    public static extern IntPtr SHChangeNotification_Lock(IntPtr hChange, uint dwProcId,
        out uint ppnl, out uint ppsne);
    [DllImport("shell32.dll")]
    public static extern bool SHChangeNotification_Unlock(IntPtr hLock);
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
