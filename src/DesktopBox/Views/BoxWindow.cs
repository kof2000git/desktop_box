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
    private readonly IntPtr _handle;
    private readonly BoxControl _content;
    private bool _disposed;

    public BoxWindow(BoxViewModel box, MainViewModel mainVm, IntPtr parent)
    {
        MainVm = mainVm;
        Box = box;
        var clientPosition = Native.User32.ScreenPointToClient(parent, box.X, box.Y);

        var parameters = new HwndSourceParameters(box.Header)
        {
            ParentWindow = parent,
            WindowStyle = WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
            ExtendedWindowStyle = WS_EX_TOOLWINDOW | WS_EX_LAYERED,
            PositionX = clientPosition.X,
            PositionY = clientPosition.Y,
            Width = Math.Max(1, (int)Math.Round(box.Width)),
            Height = Math.Max(1, (int)Math.Round(box.Height)),
            UsesPerPixelOpacity = false
        };

        _source = new HwndSource(parameters);
        _handle = _source.Handle;
        _source.Disposed += OnSourceDisposed;
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

    public IntPtr Handle => _disposed || _source.IsDisposed ? IntPtr.Zero : _handle;
    public bool IsDisposed => _disposed;
    public bool IsHandleAlive => !_disposed && !_source.IsDisposed && Native.User32.IsWindow(_handle);
    private DateTime _lastMoveResizeUtc;

    public void EnsureVisibleOnDesktopHost(IntPtr parent)
    {
        if (_disposed || _source.IsDisposed || parent == IntPtr.Zero)
            return;
        if (!Native.User32.IsWindow(parent) || !Native.User32.IsWindow(_handle))
            return;

        try
        {
            if (Native.User32.GetParent(_handle) != parent)
            {
                // 可见窗口跨进程换父先隐藏，避免撕裂/闪。失败只记日志，不抛。
                Native.User32.ShowWindow(_handle, 0); // SW_HIDE
                Native.User32.SetParent(_handle, parent);
            }

            var clientPosition = Native.User32.ScreenPointToClient(parent, Box.X, Box.Y);
            Native.User32.SetWindowPos(
                _handle,
                Native.User32.HWND_TOP,
                clientPosition.X,
                clientPosition.Y,
                Math.Max(1, (int)Math.Round(Box.Width)),
                Math.Max(1, (int)Math.Round(Box.Height)),
                Native.User32.SWP_CROSSPROC);
        }
        catch (Exception ex)
        {
            Services.LogService.Error(ex, "BoxWindow.EnsureVisible");
        }
    }

    public void CloseForRemoval() => Dispose();

    private void OnBoxPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || _source.IsDisposed)
            return;

        if (e.PropertyName is nameof(BoxViewModel.X) or nameof(BoxViewModel.Y) or nameof(BoxViewModel.Width) or nameof(BoxViewModel.Height))
            MoveResize(Native.User32.HWND_TOP);
        else if (e.PropertyName == nameof(BoxViewModel.Header))
            Native.User32.SetWindowText(_handle, Box.Header);
    }

    private void MoveResize(IntPtr insertAfter)
    {
        if (_disposed || _source.IsDisposed)
            return;
        // 节流：拖动/缩放每像素都会触发 PropertyChanged→跨进程 SetWindowPos，
        // 高频强制 explorer 重排是卡顿/闪烁放大器。16ms 合并一帧。
        var now = DateTime.UtcNow;
        if ((now - _lastMoveResizeUtc).TotalMilliseconds < 16)
            return;
        _lastMoveResizeUtc = now;

        var scale = GetDpiScale();
        _content.Width = Math.Max(1, Box.Width / scale.X);
        _content.Height = Math.Max(1, Box.Height / scale.Y);
        var parent = Native.User32.GetParent(_handle);
        var clientPosition = Native.User32.ScreenPointToClient(parent, Box.X, Box.Y);
        Native.User32.SetWindowPos(
            _handle,
            insertAfter,
            clientPosition.X,
            clientPosition.Y,
            Math.Max(1, (int)Math.Round(Box.Width)),
            Math.Max(1, (int)Math.Round(Box.Height)),
            Native.User32.SWP_CROSSPROC);
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

        MarkDisposed();
        try
        {
            // 先从 explorer 子链表摘掉再销毁：explorer 正枚举/绘制子窗口时直接销毁
            // 会损坏子链表（桌面闪一下/图标重排的来源之一）。
            if (Native.User32.IsWindow(_handle))
            {
                Native.User32.ShowWindow(_handle, 0); // SW_HIDE
                Native.User32.SetParent(_handle, IntPtr.Zero);
            }
        }
        catch { }
        _source.Disposed -= OnSourceDisposed;
        _source.RootVisual = null;
        _source.Dispose();
    }

    private void OnSourceDisposed(object? sender, EventArgs e) => MarkDisposed();

    private void MarkDisposed()
    {
        if (_disposed)
            return;

        _disposed = true;
        Box.PropertyChanged -= OnBoxPropertyChanged;
    }
}
