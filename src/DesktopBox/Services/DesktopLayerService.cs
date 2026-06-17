using System;
using System.Windows;
using System.Windows.Interop;
using DesktopBox;
using DesktopBox.Native;

namespace DesktopBox.Services;

public class DesktopLayerService : IDesktopService
{
    public bool AttachToDesktop(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            var workerW = User32.GetWorkerW();
            if (workerW == IntPtr.Zero) return false;

            User32.SetParent(hwnd, workerW);
            // reparent 到 WorkerW 后,AllowsTransparency 透明窗口(layered)可能带 WS_EX_TOPMOST
            // 或 z-order 异常,浮在浏览器等普通窗口之上。强制移除置顶标志。
            User32.SetWindowPos(hwnd, User32.HWND_NOTOPMOST, 0, 0, 0, 0,
                User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);

            return true;
        }
        catch (Exception ex)
        {
            App.LogError(ex, "AttachToDesktop");
            return false; // 降级:窗口保持普通状态,不崩
        }
    }
}
