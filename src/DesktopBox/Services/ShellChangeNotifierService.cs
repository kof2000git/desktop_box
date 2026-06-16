using System;
using System.Threading;
using DesktopBox.Native;

namespace DesktopBox.Services;

/// <summary>基于 SHChangeNotifyRegister 的 shell 变化通知监听。
/// 监听广泛事件(文件创建/删除/重命名/更新/系统图标更新/关联变化),收到任意通知即视为
/// 可能需要刷新系统图标(回收站空满切换实际发的是 CREATE/RENAME/UPDATE 等事件,而非 UPDATEIMAGE)。
/// 用节流合并短时间内的多次通知,避免频繁删除/拷贝导致图标反复重提。</summary>
public class ShellChangeNotifierService : IShellChangeNotifierService
{
    private uint _notifyId;
    private bool _registered;
    private Timer? _throttle;
    private volatile bool _pending;

    public uint NotifyMessageId { get; } = User32.RegisterWindowMessage("DesktopBox_ShellNotify_v1");

    public event EventHandler? SystemIconChanged;

    public bool Register(IntPtr hwnd)
    {
        if (_registered) return true;
        if (hwnd == IntPtr.Zero) return false;

        // 监听广泛事件:回收站空/满走 CREATE/RENAME/DELETE/UPDATE;关联变化、系统图标更新也覆盖
        const uint events =
            Shell32.SHCNE_RENAMEITEM   |   // 0x001 重命名
            Shell32.SHCNE_CREATE       |   // 0x002 创建(文件进回收站)
            Shell32.SHCNE_DELETE       |   // 0x004 删除(回收站清空)
            Shell32.SHCNE_UPDATEITEM   |   // 0x800 项目更新
            Shell32.SHCNE_UPDATEDIR    |   // 0x1000 目录更新(回收站内容变)
            Shell32.SHCNE_UPDATEIMAGE  |   // 0x8000 系统图标列表更新
            Shell32.SHCNE_ASSOCCHANGED;    // 0x08000000 关联变化
        const uint sources = Shell32.SHCNRF_ShellLevel | Shell32.SHCNRF_InterruptLevel | Shell32.SHCNRF_NewDelivery;

        var entries = new[] { new Shell32.SHChangeNotifyEntry { pidl = IntPtr.Zero, fRecursive = true } };
        _notifyId = Shell32.SHChangeNotifyRegister(hwnd, sources, events, NotifyMessageId, 1, ref entries);
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
            // 解析事件类型(NewDelivery 模式 Lock;lParam 直接是事件标志时兜底)。
            // 这里不再按事件类型过滤——回收站空/满实际发的是 CREATE/DELETE/UPDATE 等通用事件,
            // 监听到任意 shell 变化就标记需要刷新系统图标,由节流统一触发。
            uint events;
            var lockHandle = Shell32.SHChangeNotification_Lock(lParam, 0, out var count, out events);
            if (lockHandle != IntPtr.Zero)
            {
                try { }
                finally { Shell32.SHChangeNotification_Unlock(lockHandle); }
            }
            else
            {
                events = (uint)(long)lParam;
            }

            // 节流:短时间内可能连发多个通知(回收站清空连发 CREATE/DELETE/UPDATE),
            // 用 400ms 延迟合并为一次刷新。必须在 UI 线程触发事件(订阅者访问 UI 集合)。
            if (!_pending)
            {
                _pending = true;
                if (_throttle is null)
                {
                    _throttle = new Timer(_ => ThrottledFire(), null, 400, Timeout.Infinite);
                }
                else
                {
                    // 单次 Timer(dueTime=400,period=Infinite)复用时必须重新 Change 才会再次触发,
                    // 否则第二次之后的通知永远不刷新(回收站清空图标不更新的根因)
                    _throttle.Change(400, Timeout.Infinite);
                }
            }
        }
        catch (Exception ex) { App.LogError(ex, "ShellChangeNotifier.OnShellNotify"); }
    }

    private void ThrottledFire()
    {
        _pending = false;
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp is null || disp.HasShutdownStarted)
        {
            SystemIconChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        disp.BeginInvoke(new Action(() => SystemIconChanged?.Invoke(this, EventArgs.Empty)));
    }

    ~ShellChangeNotifierService()
    {
        if (_registered && _notifyId != 0)
        {
            try { Shell32.SHChangeNotifyDeregister(_notifyId); } catch { }
        }
        _throttle?.Dispose();
    }
}
