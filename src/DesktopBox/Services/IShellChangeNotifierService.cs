using System;

namespace DesktopBox.Services;

/// <summary>监听 shell 的图标更新、关联变化和回收站空满切换。基于 SHChangeNotifyRegister。</summary>
public interface IShellChangeNotifierService
{
    /// <summary>注册窗口(HWND)接收 shell 变化通知。返回是否成功。</summary>
    bool Register(IntPtr hwnd, bool force = false);

    /// <summary>MainWindow 收到注册时的自定义消息号后,调此方法解析通知内容。</summary>
    /// <param name="wParam">消息 wParam</param>
    /// <param name="lParam">消息 lParam</param>
    void OnShellNotify(IntPtr wParam, IntPtr lParam);

    /// <summary>检测到需要刷新系统图标时触发(回收站空满切换、系统图标列表更新、关联变化等)。</summary>
    event EventHandler? SystemIconChanged;

    /// <summary>兼容旧订阅方保留；当前实现不会自动删除失效引用。</summary>
    event EventHandler? DesktopFilesChanged;

    /// <summary>已注册的 shell 变化通知消息号(传给 SHChangeNotifyRegister 的 wMsg)。</summary>
    uint NotifyMessageId { get; }
}
