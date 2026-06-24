using System;
using System.Threading;
using DesktopBox.Native;

namespace DesktopBox.Services;

/// <summary>基于 SHChangeNotifyRegister 的 shell 变化通知监听。
/// 注册桌面目录的 PIDL(收窄到桌面,避免 C:\Windows 等系统目录的索引/杀毒/同步事件刷屏),
/// 监听文件级事件(增删改)与图标层信号(图标列表/关联变化)。收到后按事件类型分流到两个事件,
/// 各自独立节流(400ms 合并),避免频繁删除/拷贝导致反复刷新。</summary>
public class ShellChangeNotifierService : IShellChangeNotifierService
{
    private uint _notifyId;
    private bool _registered;

    // 图标层与文件层各自独立的节流队列,互不干扰
    private Timer? _iconThrottle;
    private Timer? _fileThrottle;
    private volatile bool _iconPending;
    private volatile bool _filePending;

    public uint NotifyMessageId { get; } = User32.RegisterWindowMessage("DesktopBox_ShellNotify_v1");

    public event EventHandler? SystemIconChanged;
    public event EventHandler? DesktopFilesChanged;

    internal const uint FileChangeMask = Shell32.SHCNE_CREATE | Shell32.SHCNE_DELETE | Shell32.SHCNE_RENAMEITEM
                                       | Shell32.SHCNE_UPDATEITEM | Shell32.SHCNE_RMDIR | Shell32.SHCNE_RENAMEFOLDER;

    internal const uint SystemIconChangeMask = Shell32.SHCNE_UPDATEIMAGE | Shell32.SHCNE_ASSOCCHANGED
                                             | Shell32.SHCNE_CREATE | Shell32.SHCNE_DELETE
                                             | Shell32.SHCNE_UPDATEITEM | Shell32.SHCNE_UPDATEDIR
                                             | Shell32.SHCNE_RMDIR;

    public bool Register(IntPtr hwnd, bool force = false)
    {
        if (_registered && !force) return true;
        if (force && _registered && _notifyId != 0)
        {
            try { Shell32.SHChangeNotifyDeregister(_notifyId); } catch { }
            _notifyId = 0;
            _registered = false;
        }
        if (hwnd == IntPtr.Zero) return false;

        // 文件级事件:用于清理盒子中已失效的条目(用户在资源管理器删桌面文件后盒子同步)。
        // 图标层事件:用于刷新系统图标(回收站空满、此电脑盘符变化、关联变化)。
        const uint events = FileChangeMask | SystemIconChangeMask;
        const uint sources = Shell32.SHCNRF_ShellLevel | Shell32.SHCNRF_InterruptLevel | Shell32.SHCNRF_NewDelivery;

        // 全局监听(pidl=Zero):收窄到桌面 PIDL 时收不到任何消息(非标准桌面路径解析问题),
        // 用全局 + 事件类型过滤 + 节流来控制噪声。系统后台对其他目录的 CREATE/DELETE 会被收到,
        // 但 PruneStaleItems 只检查盒子里文件是否存在,无变化的文件不会被误删。
        var entries = new Shell32.SHChangeNotifyEntry[]
        {
            new() { pidl = IntPtr.Zero, fRecursive = true }
        };
        _notifyId = Shell32.SHChangeNotifyRegister(hwnd, sources, events, NotifyMessageId, entries.Length, ref entries);
        _registered = _notifyId != 0;
        if (!_registered)
            App.LogError(new Exception(
                $"SHChangeNotifyRegister FAILED err={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}"),
                "ShellChangeNotifier.Register");
        return _registered;
    }

    public void OnShellNotify(IntPtr wParam, IntPtr lParam)
    {
        try
        {
            // NewDelivery 模式:Lock 解析 lParam 携带的 PIDL 列表并输出实际事件标志。
            // dwProcId 按 MSDN 必须传 0(unused)——曾误传 wParam 导致 events 解析为 0,
            // 所有分流失效、DesktopFilesChanged 永不触发。
            uint events;
            var lockHandle = Shell32.SHChangeNotification_Lock(lParam, 0, out var count, out events);
            if (lockHandle != IntPtr.Zero)
            {
                try { }
                finally { Shell32.SHChangeNotification_Unlock(lockHandle); }
            }
            else
            {
                // 兜底:无 NewDelivery 句柄时,lParam 直接是事件标志。
                events = (uint)(long)lParam;
            }

            // 分流:图标层信号 → 刷系统图标;文件级变化 → 清盒子失效项。
            if (ShouldRefreshSystemIcons(events))
                ScheduleFire(isIcon: true);
            if ((events & FileChangeMask) != 0)
                ScheduleFire(isIcon: false);
        }
        catch (Exception ex) { App.LogError(ex, "ShellChangeNotifier.OnShellNotify"); }
    }

    internal static bool ShouldRefreshSystemIcons(uint events) =>
        (events & SystemIconChangeMask) != 0;

    /// <summary>节流合并:短时间内可能连发多个通知(回收站清空连发 CREATE/DELETE/UPDATE),
    /// 用 400ms 延迟合并为一次回调。单次 Timer(period=Infinite)复用时必须重新 Change 才会再次触发。</summary>
    private void ScheduleFire(bool isIcon)
    {
        if (isIcon)
        {
            if (_iconPending) return;
            _iconPending = true;
            if (_iconThrottle is null)
                _iconThrottle = new Timer(_ => FireSystemIconChanged(), null, 400, Timeout.Infinite);
            else
                _iconThrottle.Change(400, Timeout.Infinite);
        }
        else
        {
            if (_filePending) return;
            _filePending = true;
            if (_fileThrottle is null)
                _fileThrottle = new Timer(_ => FireDesktopFilesChanged(), null, 400, Timeout.Infinite);
            else
                _fileThrottle.Change(400, Timeout.Infinite);
        }
    }

    private void FireSystemIconChanged()
    {
        _iconPending = false;
        InvokeOnDispatcher(SystemIconChanged);
    }

    private void FireDesktopFilesChanged()
    {
        _filePending = false;
        InvokeOnDispatcher(DesktopFilesChanged);
    }

    /// <summary>在 UI 线程触发事件(订阅者访问 UI 集合)。</summary>
    private void InvokeOnDispatcher(EventHandler? handler)
    {
        if (handler is null) return;
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp is null || disp.HasShutdownStarted)
        {
            handler.Invoke(this, EventArgs.Empty);
            return;
        }
        disp.BeginInvoke(new Action(() => handler.Invoke(this, EventArgs.Empty)));
    }

    /// <summary>在 UI 线程触发事件(订阅者访问 UI 集合)。复位 pending 标志。</summary>
    private void InvokeOnDispatcher(EventHandler? handler, ref bool pending)
    {
        pending = false;
        if (handler is null) return;
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp is null || disp.HasShutdownStarted)
        {
            handler.Invoke(this, EventArgs.Empty);
            return;
        }
        disp.BeginInvoke(new Action(() => handler.Invoke(this, EventArgs.Empty)));
    }

    ~ShellChangeNotifierService()
    {
        if (_registered && _notifyId != 0)
        {
            try { Shell32.SHChangeNotifyDeregister(_notifyId); } catch { }
        }
        _iconThrottle?.Dispose();
        _fileThrottle?.Dispose();
    }
}
