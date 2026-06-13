using System;
using System.Windows;
using System.Windows.Interop;
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
            return true;
        }
        catch
        {
            return false; // 降级:窗口保持普通状态,不崩
        }
    }
}
