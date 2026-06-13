using System.Windows;

namespace DesktopBox.Services;

public interface IDesktopService
{
    /// <summary>把窗口贴到桌面图标层(WorkerW)之上。成功返回 true,失败返回 false(调用方应降级为置顶窗口)。</summary>
    bool AttachToDesktop(Window window);
}
