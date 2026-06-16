using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace DesktopBox.Native;

public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

public static class User32
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const uint SMTO_NORMAL = 0x0000;
    public const uint WM_COMMAND = 0x0111;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public const int SW_RESTORE = 9;
    public const int SW_SHOWNOACTIVATE = 4;

    public const int GWL_STYLE = -16;

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    /// <summary>HWND_TOP:把窗口置于非 topmost 序列顶部(不置顶,不挡后续激活的窗口)。</summary>
    public static readonly IntPtr HWND_TOP = IntPtr.Zero;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetParent(IntPtr hWnd);

    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const long WS_EX_TOPMOST = 0x00000008;

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    public const uint WM_SPAWN_WORKERW = 0x052C;

    public static IntPtr GetProgman() => FindWindow("Progman", null);

    public static IntPtr GetWorkerW()
    {
        var progman = GetProgman();
        if (progman == IntPtr.Zero) return IntPtr.Zero;
        // 0x052C 让 Progman 创建 WorkerW,但创建有延迟,需重试几次 EnumWindows 才能找到
        for (int attempt = 0; attempt < 5; attempt++)
        {
            SendMessageTimeout(progman, WM_SPAWN_WORKERW, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out _);
            IntPtr workerW = IntPtr.Zero;
            EnumWindows((topHwnd, _) =>
            {
                var shellDefView = FindWindowEx(topHwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shellDefView != IntPtr.Zero)
                {
                    workerW = FindWindowEx(IntPtr.Zero, topHwnd, "WorkerW", null);
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            if (workerW != IntPtr.Zero) return workerW;
            Thread.Sleep(60);
        }
        return IntPtr.Zero;
    }

    /// <summary>定位桌面图标视图窗口 SHELLDLL_DefView(用于控制桌面图标显隐)。</summary>
    public static IntPtr FindShellDefView()
    {
        var def = FindWindowEx(GetProgman(), IntPtr.Zero, "SHELLDLL_DefView", null);
        if (def != IntPtr.Zero) return def;

        IntPtr found = IntPtr.Zero;
        EnumWindows((topHwnd, _) =>
        {
            var d = FindWindowEx(topHwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (d != IntPtr.Zero) { found = d; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
