using System;
using System.Runtime.InteropServices;

namespace DesktopBox.Native;

public static class DwmApi
{
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;     // Win11 22000+
    public const int DWMSBT_MAINWINDOW = 2;              // Mica

    [DllImport("dwmapi.dll", PreserveSig = false)]
    public static extern void DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct Margins { public int Left, Right, Top, Bottom; }
}
