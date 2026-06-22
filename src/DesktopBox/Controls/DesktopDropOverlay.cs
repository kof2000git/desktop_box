using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopBox.Controls;

public sealed class DesktopDropOverlay : IDisposable
{
    private const double BoxExclusionPadding = 8;
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;

    private readonly HwndSource _source;
    private bool _disposed;

    private DesktopDropOverlay(HwndSource source, Rect bounds)
    {
        _source = source;
        var scale = GetDpiScale();
        var surface = new Border
        {
            Width = Math.Max(1, bounds.Width / scale.X),
            Height = Math.Max(1, bounds.Height / scale.Y),
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            AllowDrop = true
        };
        surface.DragEnter += OnDragOverDesktop;
        surface.DragOver += OnDragOverDesktop;
        surface.Drop += OnDropOnDesktop;
        _source.RootVisual = surface;
    }

    public bool DroppedOnDesktop { get; private set; }

    public static DesktopDropOverlay? TryCreate(IEnumerable<Rect> excludedBounds)
    {
        var parent = Native.User32.FindShellDefView();
        if (parent == IntPtr.Zero)
            parent = Native.User32.GetProgman();
        if (parent == IntPtr.Zero)
            return null;

        var bounds = GetVirtualDesktopBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        var parameters = new HwndSourceParameters("DesktopBox.DropOverlay")
        {
            ParentWindow = parent,
            WindowStyle = WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
            ExtendedWindowStyle = WS_EX_TOOLWINDOW | WS_EX_LAYERED,
            PositionX = (int)Math.Floor(bounds.Left),
            PositionY = (int)Math.Floor(bounds.Top),
            Width = Math.Max(1, (int)Math.Ceiling(bounds.Width)),
            Height = Math.Max(1, (int)Math.Ceiling(bounds.Height)),
            UsesPerPixelOpacity = false
        };

        var source = new HwndSource(parameters);
        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        ApplyExclusionRegion(source.Handle, bounds, excludedBounds);
        Native.User32.SetWindowPos(
            source.Handle,
            Native.User32.HWND_TOP,
            parameters.PositionX,
            parameters.PositionY,
            parameters.Width,
            parameters.Height,
            Native.User32.SWP_NOACTIVATE | Native.User32.SWP_SHOWWINDOW);

        return new DesktopDropOverlay(source, bounds);
    }

    internal static IReadOnlyList<Int32Rect> BuildExclusionRects(
        Rect overlayBounds,
        IEnumerable<Rect> excludedBounds,
        double padding = BoxExclusionPadding)
    {
        var result = new List<Int32Rect>();
        foreach (var excluded in excludedBounds)
        {
            if (excluded.Width <= 0 || excluded.Height <= 0)
                continue;

            var rect = excluded;
            rect.Inflate(padding, padding);
            rect.Intersect(overlayBounds);
            if (rect.IsEmpty)
                continue;

            var left = (int)Math.Floor(rect.Left - overlayBounds.Left);
            var top = (int)Math.Floor(rect.Top - overlayBounds.Top);
            var right = (int)Math.Ceiling(rect.Right - overlayBounds.Left);
            var bottom = (int)Math.Ceiling(rect.Bottom - overlayBounds.Top);
            if (right > left && bottom > top)
                result.Add(new Int32Rect(left, top, right - left, bottom - top));
        }

        return result;
    }

    private static void ApplyExclusionRegion(IntPtr hwnd, Rect overlayBounds, IEnumerable<Rect> excludedBounds)
    {
        var width = Math.Max(1, (int)Math.Ceiling(overlayBounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(overlayBounds.Height));
        var region = Native.Gdi32.CreateRectRgn(0, 0, width, height);
        if (region == IntPtr.Zero)
            return;

        var transferredToWindow = false;
        try
        {
            foreach (var excluded in BuildExclusionRects(overlayBounds, excludedBounds))
            {
                var hole = Native.Gdi32.CreateRectRgn(
                    excluded.X,
                    excluded.Y,
                    excluded.X + excluded.Width,
                    excluded.Y + excluded.Height);
                if (hole == IntPtr.Zero)
                    continue;

                try
                {
                    Native.Gdi32.CombineRgn(region, region, hole, Native.Gdi32.RGN_DIFF);
                }
                finally
                {
                    Native.Gdi32.DeleteObject(hole);
                }
            }

            transferredToWindow = Native.User32.SetWindowRgn(hwnd, region, true) != 0;
        }
        finally
        {
            if (!transferredToWindow)
                Native.Gdi32.DeleteObject(region);
        }
    }

    private static Rect GetVirtualDesktopBounds()
    {
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens.Length > 0)
            {
                var left = screens.Min(s => s.Bounds.Left);
                var top = screens.Min(s => s.Bounds.Top);
                var right = screens.Max(s => s.Bounds.Right);
                var bottom = screens.Max(s => s.Bounds.Bottom);
                return new Rect(left, top, right - left, bottom - top);
            }
        }
        catch
        {
            // Fall back to WPF virtual-screen metrics below.
        }

        return new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
    }

    private (double X, double Y) GetDpiScale()
    {
        var transform = _source.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var x = transform.M11 == 0 ? 1 : transform.M11;
        var y = transform.M22 == 0 ? 1 : transform.M22;
        return (x, y);
    }

    private void OnDragOverDesktop(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(ItemDragDrop.DragSourceItemFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDropOnDesktop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ItemDragDrop.DragSourceItemFormat))
        {
            DroppedOnDesktop = true;
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _source.RootVisual = null;
        _source.Dispose();
    }
}
