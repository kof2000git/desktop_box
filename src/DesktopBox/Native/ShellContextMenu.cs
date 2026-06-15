using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;

namespace DesktopBox.Native;

/// <summary>
/// 显示文件/系统图标的原生 Shell 右键菜单(与资源管理器一致)。
/// 实现遵循 Raymond Chen "How to host an IContextMenu" 系列(WndProc 把消息转给 HandleMenuMsg2)。
/// 关键点:① 菜单显示期间 child pidl 必须存活(DefContextMenu 延迟访问它),不可提前释放;
///        ② WndProc 要把"所有"消息交给 Shell 处理(非仅 4 种),否则自绘项/子菜单会访问违规。
/// </summary>
public static class ShellContextMenu
{
    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint MF_STRING = 0x0000;
    private const uint CMIC_MASK_UNICODE = 0x4000;
    private const int SW_SHOWNORMAL = 1;
    private const uint CMF_NORMAL = 0x00000000;

    // 系统/Shell 命令 id 取值范围:CMD_FIRST..CMD_LAST;0x7000 留给"从盒子移除"
    private const uint CMD_FIRST = 1;
    private const uint CMD_LAST = 0x6FFF;
    private const uint ID_CMD_REMOVE = 0x7000;

    /// <summary>在屏幕坐标 screen 处显示 path 的系统右键菜单;末尾追加"从盒子移除"。</summary>
    public static void Show(string path, System.Drawing.Point screen, Action? removeFromBox)
    {
        if (string.IsNullOrEmpty(path)) return;

        // 关键:在独立 STA 线程托管原生 Shell 菜单,完全隔离于主窗口(reparent 到 WorkerW)的
        // Dispatcher/消息状态。主线程下 COM 菜单自绘回调会触发 CSE(AV)直接崩进程;独立线程有
        // 自己的 HwndSource + 消息循环,不继承主窗口异常态,从而规避 AV。
        Exception? caught = null;
        var t = new Thread(() =>
        {
            try { ShowCore(path, screen, removeFromBox); }
            catch (Exception ex) { caught = ex; }
        });
        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();   // 主线程等菜单关闭(菜单 modal 期间在前台,主线程冻结可接受)
        if (caught is not null)
            try { App.LogError(caught, "ShellContextMenu.Show(threaded)"); } catch { }
    }

    private static void ShowCore(string path, System.Drawing.Point screen, Action? removeFromBox)
    {
        IShellFolder? parent = null;
        object? cmObj = null;
        IntPtr childPidl = IntPtr.Zero;
        try
        {
            if (Shell32.SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
                return;
            try
            {
                var sfIid = typeof(IShellFolder).GUID;
                if (SHBindToParent(pidl, ref sfIid, out parent, out childPidl) != 0 || parent is null)
                    return;

                var cmIid = typeof(IContextMenu).GUID;
                parent.GetUIObjectOf(IntPtr.Zero, 1, new[] { childPidl }, ref cmIid, IntPtr.Zero, out cmObj);
                // childPidl 是 SHBindToParent 返回的 pidl 内部指针(MSDN:"指向 pidl 的内存,不要单独释放")。
                // 整个菜单期间它都有效(pidl 在下面 finally 才释放);之后随 pidl 失效,永远不要单独 ILFree。
                if (cmObj is not IContextMenu cm) return;
                var cm2 = cmObj as IContextMenu2;
                var cm3 = cmObj as IContextMenu3;

                var hMenu = CreatePopupMenu();
                if (hMenu == IntPtr.Zero) return;
                try
                {
                    cm.QueryContextMenu(hMenu, 0, CMD_FIRST, CMD_LAST, CMF_NORMAL);

                    if (removeFromBox is not null)
                    {
                        AppendMenu(hMenu, MF_SEPARATOR, 0, null);
                        AppendMenu(hMenu, MF_STRING, ID_CMD_REMOVE, "从盒子移除");
                    }

                    // 隐藏消息窗口:菜单 modal 期间把窗口消息转给 Shell(IContextMenu2/3)
                    using var hook = new MenuHook(cm2, cm3);
                    SetForegroundWindow(hook.Hwnd);
                    var cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_LEFTALIGN | TPM_RIGHTBUTTON,
                        screen.X, screen.Y, hook.Hwnd, IntPtr.Zero);

                    if (cmd == 0) return;             // 用户未选择(取消)
                    if (cmd == ID_CMD_REMOVE) { removeFromBox?.Invoke(); return; }
                    if (cmd < CMD_FIRST) return;      // 非法 id,忽略

                    var verb = (IntPtr)(cmd - CMD_FIRST);
                    var info = new CMINVOKECOMMANDINFOEX
                    {
                        cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                        fMask = CMIC_MASK_UNICODE,
                        lpVerb = verb,
                        lpVerbW = verb,
                        nShow = SW_SHOWNORMAL
                    };
                    cm.InvokeCommand(ref info);
                }
                finally { DestroyMenu(hMenu); }
            }
            finally { Shell32.ILFree(pidl); }
        }
        catch { /* 菜单失败不应崩溃,静默忽略 */ }
        finally
        {
            // 不要 ILFree(childPidl)! SHBindToParent 返回的 ppidlLast 是 pidl 的内部指针(MSDN 明确
            // "指向 pidl 的内存,不要单独释放")。上面的 ILFree(pidl) 已释放整块内存,childPidl 随之失效。
            // 对已释放内存的内部指针再调 ILFree 会触发 0xc0000005 访问违规(曾导致右键菜单后崩溃,
            // 且因异常在最外层 finally 抛出、逃逸了本方法的 catch,直达全局 Dispatcher 异常处理器)。
            if (cmObj is not null) Marshal.ReleaseComObject(cmObj);
            if (parent is not null) Marshal.ReleaseComObject(parent);
        }
    }

    /// <summary>临时隐藏窗口,菜单显示期间把 Shell 需要的消息转给 IContextMenu2/3。</summary>
    private sealed class MenuHook : IDisposable
    {
        private readonly HwndSource _source;
        private readonly IContextMenu2? _cm2;
        private readonly IContextMenu3? _cm3;
        public IntPtr Hwnd => _source.Handle;

        public MenuHook(IContextMenu2? cm2, IContextMenu3? cm3)
        {
            _cm2 = cm2; _cm3 = cm3;
            _source = new HwndSource(new HwndSourceParameters("dbx_cmhook")
            { Width = 0, Height = 0, PositionX = -32000, PositionY = -32000 });
            _source.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 按 Raymond Chen 的做法:菜单期间把消息交给 Shell 处理。SUCCEEDED 时用其返回值;
            // 不能限定仅 4 种消息,否则延迟生成的子菜单/自绘项会访问违规(0xc0000005)。
            if (_cm3 is not null)
            {
                var hr = _cm3.HandleMenuMsg2((uint)msg, wParam, lParam, out var result);
                if (hr >= 0)             // SUCCEEDED
                {
                    handled = true;
                    return result;
                }
            }
            else if (_cm2 is not null)
            {
                var hr = _cm2.HandleMenuMsg((uint)msg, wParam, lParam);
                if (hr >= 0)             // SUCCEEDED
                {
                    handled = true;
                    return IntPtr.Zero;  // IContextMenu2 无返回值,固定返回 0
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose() => _source.Dispose();
    }

    // ---- P/Invoke ----
    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, [In] ref Guid riid, out IShellFolder? ppv, out IntPtr ppidlLast);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}

// ---- Shell COM 接口(vtable 顺序必须与 SDK 一致)----

[ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellFolder
{
    [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out IntPtr ppidl, uint rgfInOut, out uint pdwAttributes);
    [PreserveSig] int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);
    [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);
    [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);
    [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
    [PreserveSig] int CreateViewObject(IntPtr hwndOwner, [In] ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetAttributesOf(uint cidl, [In] IntPtr[] apidl, ref uint rgfInOut);
    [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [In] IntPtr[] apidl, [In] ref Guid riid, IntPtr rgfReserved, [MarshalAs(UnmanagedType.Interface)] out object? ppv);
    [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint uFlags, out IntPtr pName);
    [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
}

[ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IContextMenu
{
    [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
    [PreserveSig] int GetCommandString(IntPtr idCmd, uint uType, IntPtr pwReserved, [MarshalAs(UnmanagedType.LPStr)] System.Text.StringBuilder pszName, uint cchMax);
}

[ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IContextMenu2 : IContextMenu
{
    [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
}

[ComImport, Guid("000214F5-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IContextMenu3 : IContextMenu2
{
    [PreserveSig] int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct CMINVOKECOMMANDINFOEX
{
    public int cbSize;
    public uint fMask;
    public IntPtr hwnd;
    public IntPtr lpVerb;          // MAKEINTRESOURCE 命令偏移
    [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
    [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
    public int nShow;
    public uint dwHotKey;
    public IntPtr hIcon;
    [MarshalAs(UnmanagedType.LPStr)] public string? lpTitle;
    public IntPtr lpVerbW;         // Unicode 命令偏移(fMask 含 CMIC_MASK_UNICODE 时用)
    [MarshalAs(UnmanagedType.LPWStr)] public string? lpParametersW;
    [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectoryW;
    [MarshalAs(UnmanagedType.LPWStr)] public string? lpTitleW;
    public System.Drawing.Point ptInvoke;
}
