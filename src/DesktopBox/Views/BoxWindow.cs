using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using DesktopBox.Controls;
using DesktopBox.ViewModels;

namespace DesktopBox.Views;

public sealed class BoxWindow : IDisposable
{
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;

    private readonly HwndSource _source;
    private readonly BoxControl _content;
    private bool _disposed;

    public BoxWindow(BoxViewModel box, MainViewModel mainVm, IntPtr parent)
    {
        MainVm = mainVm;
        Box = box;

        var parameters = new HwndSourceParameters(box.Header)
        {
            ParentWindow = parent,
            WindowStyle = WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
            ExtendedWindowStyle = WS_EX_TOOLWINDOW | WS_EX_LAYERED,
            PositionX = (int)Math.Round(box.X),
            PositionY = (int)Math.Round(box.Y),
            Width = Math.Max(1, (int)Math.Round(box.Width)),
            Height = Math.Max(1, (int)Math.Round(box.Height)),
            UsesPerPixelOpacity = false
        };

        _source = new HwndSource(parameters);
        _source.CompositionTarget.BackgroundColor = System.Windows.Media.Colors.Transparent;
        _content = new BoxControl
        {
            DataContext = box
        };
        _source.RootVisual = _content;
        MoveResize(Native.User32.HWND_TOP);
        box.PropertyChanged += OnBoxPropertyChanged;
    }

    public MainViewModel MainVm { get; }

    public BoxViewModel Box { get; }

    public IntPtr Handle => _source.Handle;
    public bool IsDisposed => _disposed;
    public bool IsHandleAlive => !_disposed && Native.User32.IsWindow(Handle);

    public void EnsureVisibleOnDesktopHost(IntPtr parent)
    {
        if (_disposed || parent == IntPtr.Zero)
            return;

        if (Native.User32.GetParent(Handle) != parent)
            Native.User32.SetParent(Handle, parent);

        Native.User32.SetWindowPos(
            Handle,
            Native.User32.HWND_TOP,
            (int)Math.Round(Box.X),
            (int)Math.Round(Box.Y),
            Math.Max(1, (int)Math.Round(Box.Width)),
            Math.Max(1, (int)Math.Round(Box.Height)),
            Native.User32.SWP_NOACTIVATE | Native.User32.SWP_SHOWWINDOW);
    }

    public void CloseForRemoval() => Dispose();

    private void OnBoxPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
            return;

        if (e.PropertyName is nameof(BoxViewModel.X) or nameof(BoxViewModel.Y) or nameof(BoxViewModel.Width) or nameof(BoxViewModel.Height))
            MoveResize(Native.User32.HWND_TOP);
        else if (e.PropertyName == nameof(BoxViewModel.Header))
            Native.User32.SetWindowText(Handle, Box.Header);
    }

    private void MoveResize(IntPtr insertAfter)
    {
        var scale = GetDpiScale();
        _content.Width = Math.Max(1, Box.Width / scale.X);
        _content.Height = Math.Max(1, Box.Height / scale.Y);
        Native.User32.SetWindowPos(
            Handle,
            insertAfter,
            (int)Math.Round(Box.X),
            (int)Math.Round(Box.Y),
            Math.Max(1, (int)Math.Round(Box.Width)),
            Math.Max(1, (int)Math.Round(Box.Height)),
            Native.User32.SWP_NOACTIVATE | Native.User32.SWP_SHOWWINDOW);
    }

    private (double X, double Y) GetDpiScale()
    {
        var transform = _source.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
        var x = transform.M11 == 0 ? 1 : transform.M11;
        var y = transform.M22 == 0 ? 1 : transform.M22;
        return (x, y);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Box.PropertyChanged -= OnBoxPropertyChanged;
        _source.RootVisual = null;
        _source.Dispose();
    }
}
