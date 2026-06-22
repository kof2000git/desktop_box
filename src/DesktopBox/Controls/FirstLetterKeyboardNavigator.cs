using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace DesktopBox.Controls;

public sealed class FirstLetterKeyboardNavigator : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private readonly LowLevelKeyboardProc _callback;
    private IntPtr _hook;
    private WeakReference<BoxControl>? _hoveredControl;
    private bool _disposed;

    public FirstLetterKeyboardNavigator()
    {
        _callback = HookCallback;
    }

    public void Activate(BoxControl control)
    {
        if (_disposed)
            return;

        _hoveredControl = new WeakReference<BoxControl>(control);
        EnsureHook();
    }

    public void Deactivate(BoxControl control)
    {
        if (_hoveredControl?.TryGetTarget(out var target) == true && ReferenceEquals(target, control))
            _hoveredControl = null;
    }

    public static char? NavigationCharFromVirtualKey(int vkCode)
    {
        if (vkCode is >= 0x41 and <= 0x5A)
            return (char)vkCode;

        if (vkCode is >= 0x30 and <= 0x39)
            return (char)vkCode;

        if (vkCode is >= 0x60 and <= 0x69)
            return (char)('0' + vkCode - 0x60);

        return null;
    }

    private void EnsureHook()
    {
        if (_hook != IntPtr.Zero)
            return;

        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            App.LogError(new InvalidOperationException($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}"), nameof(FirstLetterKeyboardNavigator));
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == WM_KEYDOWN && !HasBlockingModifier())
        {
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var key = NavigationCharFromVirtualKey((int)info.VkCode);
            if (key is not null && TryHandleOnUiThread(key.Value))
                return new IntPtr(1);
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool TryHandleOnUiThread(char key)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
            return false;

        if (dispatcher.CheckAccess())
            return TryHandle(key);

        return dispatcher.Invoke(() => TryHandle(key));
    }

    private bool TryHandle(char key)
    {
        if (_hoveredControl is null || !_hoveredControl.TryGetTarget(out var control) || !control.IsMouseOver)
            return false;

        return control.NavigateByFirstLetter(key);
    }

    private static bool HasBlockingModifier() =>
        IsKeyDown(VK_CONTROL) || IsKeyDown(VK_MENU) || IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN);

    private static bool IsKeyDown(int virtualKey) =>
        (GetKeyState(virtualKey) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _hoveredControl = null;
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct KbdLlHookStruct
    {
        public readonly uint VkCode;
        public readonly uint ScanCode;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
