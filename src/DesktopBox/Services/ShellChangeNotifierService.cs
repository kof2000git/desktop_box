using System;
using System.Threading;
using DesktopBox.Native;

namespace DesktopBox.Services;

/// <summary>基于 SHChangeNotifyRegister 的系统图标变化监听。
/// 全局范围只监听图像列表/文件关联，文件级事件仅监听回收站 PIDL，避免普通文件活动触发刷新。</summary>
internal enum ShellNotificationScope
{
    GlobalSystem,
    RecycleBin
}

public class ShellChangeNotifierService : IShellChangeNotifierService, IDisposable
{
    private const string RecycleBinClsid = "::{645FF040-5081-101B-9F08-00AA002F954E}";
    private readonly List<uint> _notifyIds = new();
    private readonly object _timerGate = new();
    private bool _globalRegistered;
    private bool _recycleBinRegistered;
    private volatile bool _disposed;

    private Timer? _iconThrottle;
    private volatile bool _iconPending;

    public uint NotifyMessageId { get; } = User32.RegisterWindowMessage("DesktopBox_ShellNotify_v1");

    public event EventHandler? SystemIconChanged;
    public event EventHandler? DesktopFilesChanged { add { } remove { } }

    internal const uint GlobalSystemIconChangeMask = Shell32.SHCNE_UPDATEIMAGE | Shell32.SHCNE_ASSOCCHANGED;
    internal const uint RecycleBinChangeMask = Shell32.SHCNE_CREATE | Shell32.SHCNE_DELETE
                                                | Shell32.SHCNE_UPDATEITEM | Shell32.SHCNE_UPDATEDIR
                                                | Shell32.SHCNE_RMDIR;

    public bool Register(IntPtr hwnd, bool force = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_globalRegistered && _recycleBinRegistered && !force) return true;
        if (force) DeregisterAll();
        if (hwnd == IntPtr.Zero) return false;

        // Global registration is intentionally limited to icon-list and association events.
        // File create/delete events are registered only for the Recycle Bin PIDL below.
        if (!_globalRegistered)
            _globalRegistered = RegisterScope(
                hwnd, GlobalSystemIconChangeMask, IntPtr.Zero, recursive: false, "global-system-icons");

        IntPtr recyclePidl = IntPtr.Zero;
        try
        {
            if (Shell32.SHParseDisplayName(RecycleBinClsid, IntPtr.Zero, out recyclePidl, 0, out _) == 0
                && recyclePidl != IntPtr.Zero)
            {
                if (!_recycleBinRegistered)
                    _recycleBinRegistered = RegisterScope(
                        hwnd, RecycleBinChangeMask, recyclePidl, recursive: true, "recycle-bin");
            }
            else
            {
                App.LogError(new Exception("Unable to resolve Recycle Bin PIDL"), "ShellChangeNotifier.Register");
            }
        }
        finally
        {
            if (recyclePidl != IntPtr.Zero) Shell32.ILFree(recyclePidl);
        }

        var fullyRegistered = _globalRegistered && _recycleBinRegistered;
        if (!fullyRegistered)
            App.LogError(new Exception(
                $"Shell notification registration incomplete: global={_globalRegistered}, recycleBin={_recycleBinRegistered}, " +
                $"err={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}"),
                "ShellChangeNotifier.Register");
        return fullyRegistered;
    }

    private bool RegisterScope(IntPtr hwnd, uint events, IntPtr pidl, bool recursive, string scope)
    {
        const uint sources = Shell32.SHCNRF_ShellLevel | Shell32.SHCNRF_InterruptLevel;
        var entry = new Shell32.SHChangeNotifyEntry { pidl = pidl, fRecursive = recursive };
        var id = Shell32.SHChangeNotifyRegister(hwnd, sources, events, NotifyMessageId, 1, ref entry);
        if (id != 0)
        {
            _notifyIds.Add(id);
            return true;
        }

        App.LogError(new Exception(
            $"SHChangeNotifyRegister failed for {scope}, err={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}"),
            "ShellChangeNotifier.RegisterScope");
        return false;
    }

    public void OnShellNotify(IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (!_disposed) ScheduleFire();
        }
        catch (Exception ex) { App.LogError(ex, "ShellChangeNotifier.OnShellNotify"); }
    }

    internal static bool ShouldRefreshSystemIcons(ShellNotificationScope scope, uint events) => scope switch
    {
        ShellNotificationScope.GlobalSystem => (events & GlobalSystemIconChangeMask) != 0,
        ShellNotificationScope.RecycleBin => (events & RecycleBinChangeMask) != 0,
        _ => false
    };

    internal static bool ShouldRefreshSystemIcons(uint events) =>
        ShouldRefreshSystemIcons(ShellNotificationScope.GlobalSystem, events)
        || ShouldRefreshSystemIcons(ShellNotificationScope.RecycleBin, events);

    /// <summary>节流合并:短时间内可能连发多个通知(回收站清空连发 CREATE/DELETE/UPDATE),
    /// 用 400ms 延迟合并为一次回调。单次 Timer(period=Infinite)复用时必须重新 Change 才会再次触发。</summary>
    private void ScheduleFire()
    {
        lock (_timerGate)
        {
            if (_disposed) return;
            if (_iconPending) return;
            _iconPending = true;
            if (_iconThrottle is null)
                _iconThrottle = new Timer(_ => FireSystemIconChanged(), null, 400, Timeout.Infinite);
            else
                _iconThrottle.Change(400, Timeout.Infinite);
        }
    }

    private void FireSystemIconChanged()
    {
        lock (_timerGate)
        {
            if (_disposed) return;
            _iconPending = false;
        }
        InvokeOnDispatcher(SystemIconChanged);
    }

    /// <summary>在 UI 线程触发事件(订阅者访问 UI 集合)。</summary>
    private void InvokeOnDispatcher(EventHandler? handler)
    {
        if (handler is null) return;
        if (_disposed) return;
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp is null)
        {
            if (_disposed) return;
            handler.Invoke(this, EventArgs.Empty);
            return;
        }
        if (disp.HasShutdownStarted || disp.HasShutdownFinished) return;
        disp.BeginInvoke(new Action(() =>
        {
            if (!_disposed) handler.Invoke(this, EventArgs.Empty);
        }));
    }

    public void Dispose()
    {
        Timer? timer;
        lock (_timerGate)
        {
            if (_disposed) return;
            _disposed = true;
            timer = _iconThrottle;
            _iconThrottle = null;
            _iconPending = false;
        }
        DeregisterAll();
        if (timer is not null)
        {
            using var waitHandle = new ManualResetEvent(false);
            if (timer.Dispose(waitHandle))
                waitHandle.WaitOne();
        }
        GC.SuppressFinalize(this);
    }

    private void DeregisterAll()
    {
        foreach (var id in _notifyIds)
        {
            try { Shell32.SHChangeNotifyDeregister(id); } catch { }
        }
        _notifyIds.Clear();
        _globalRegistered = false;
        _recycleBinRegistered = false;
    }

    ~ShellChangeNotifierService()
    {
        DeregisterAll();
        _iconThrottle?.Dispose();
    }
}
