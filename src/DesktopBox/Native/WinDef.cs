using System.Runtime.InteropServices;

namespace DesktopBox.Native;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left, Top, Right, Bottom;
}
