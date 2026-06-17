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
    private IntPtr _desktopPidl = IntPtr.Zero;

    // 图标层与文件层各自独立的节流队列,互不干扰
    private Timer? _iconThrottle;
    private Timer? _fileThrottle;
    private volatile bool _iconPending;
    private volatile bool _filePending;

    public uint NotifyMessageId { get; } = User32.RegisterWindowMessage("DesktopBox_ShellNotify_v1");

    public event EventHandler? SystemIconChanged;
    public event EventHandler? DesktopFilesChanged;

    public bool Register(IntPtr hwnd)
    {
        if (_registered) return true;
        if (hwnd == IntPtr.Zero) return false;

        // 文件级事件:用于清理盒子中已失效的条目(用户在资源管理器删桌面文件后盒子同步)。
        // 图标层事件:用于刷新系统图标(回收站空满、此电脑盘符变化、关联变化)。
        const uint fileEvents = Shell32.SHCNE_CREATE | Shell32.SHCNE_DELETE | Shell32.SHCNE_RENAMEITEM
                              | Shell32.SHCNE_UPDATEITEM | Shell32.SHCNE_RMDIR | Shell32.SHCNE_RENAMEFOLDER;
        const uint iconEvents = Shell32.SHCNE_UPDATEIMAGE | Shell32.SHCNE_ASSOCCHANGED;
        const uint events = fileEvents | iconEvents;
        const uint sources = Shell32.SHCNRF_ShellLevel | Shell32.SHCNRF_InterruptLevel | Shell32.SHCNRF_NewDelivery;

        // 桌面目录 PIDL:只收桌面(含子目录)的变化,过滤掉系统后台对其他目录的扫描噪声。
        // 解析失败时退化为零 PIDL(全局)——仍能收到事件,只是范围更大。
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrEmpty(desktopPath))
            Shell32.SHParseDisplayName(desktopPath, IntPtr.Zero, out _desktopPidl, 0, out _);

        var entries = new Shell32.SHChangeNotifyEntry[]
        {
            new() { pidl = _desktopPidl, fRecursive = true }
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
            // dwProcId 传 wParam(注册ID);Lock 对该参数不严格校验,0 亦可,但传 wParam 更规范。
            uint events;
            var lockHandle = Shell32.SHChangeNotification_Lock(lParam, (uint)(wParam.ToInt64() & 0xFFFFFFFFu),
                out var count, out events);
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
            const uint fileMask = Shell32.SHCNE_CREATE | Shell32.SHCNE_DELETE | Shell32.SHCNE_RENAMEITEM
                                | Shell32.SHCNE_UPDATEITEM | Shell32.SHCNE_RMDIR | Shell32.SHCNE_RENAMEFOLDER;
            if ((events & (Shell32.SHCNE_UPDATEIMAGE | Shell32.SHCNE_ASSOCCHANGED)) != 0)
                ScheduleFire(isIcon: true);
            if ((events & fileMask) != 0)
                ScheduleFire(isIcon: false);
        }
        catch (Exception ex) { App.LogError(ex, "ShellChangeNotifier.OnShellNotify"); }
    }

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
        if (_desktopPidl != IntPtr.Zero)
        {
            try { Shell32.ILFree(_desktopPidl); } catch { }
        }
        _iconThrottle?.Dispose();
        _fileThrottle?.Dispose();
    }
}
