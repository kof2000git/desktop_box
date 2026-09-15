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
    private Timer? _resizeFlushTimer;
    private IntPtr _pendingInsertAfter;
    private uint _pendingExtraFlags;
    private int _lastX = int.MinValue, _lastY = int.MinValue, _lastW, _lastH;
    private double _lastContentW, _lastContentH;

    /// <summary>诊断快照：HWND/可见性/实际矩形/父窗口/模型坐标，用于定位"盒子不可见"。</summary>
    public string Describe()
    {
        try
        {
            var alive = IsHandleAlive;
            var visible = alive && Native.User32.IsWindowVisible(_handle);
            var rect = Native.User32.GetWindowRect(_handle, out var r)
                ? $"{r.Left},{r.Top},{r.Right - r.Left}x{r.Bottom - r.Top}" : "n/a";
            var parent = alive ? Native.User32.GetParent(_handle) : IntPtr.Zero;
            return $"hwnd=0x{_handle:X} alive={alive} visible={visible} rect=[{rect}] " +
                   $"parent=0x{parent:X} model=({Box.X},{Box.Y},{Box.Width}x{Box.Height})";
        }
        catch { return "describe-failed"; }
    }

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
        // 逐帧路径（拖动/缩放 PropertyChanged）：不重排 Z 序。
        // 每帧 HWND_TOP 会逼 DefView 下所有兄弟（含图标层）重排重绘，是闪烁主因；
        // 置顶只在创建/修复时做一次（EnsureVisibleOnDesktopHost），之后保持即可。
        MoveResizeCore(insertAfter, Native.User32.SWP_NOZORDER, force: false);
    }

    private void MoveResizeCore(IntPtr insertAfter, uint extraFlags, bool force)
    {
        if (_disposed || _source.IsDisposed)
            return;
        // 节流：拖动/缩放时输入可达上百 Hz，每次都跨进程 SetWindowPos + 全量重排，
        // explorer 图标层跟着每帧重绘=抖动。33ms 合并一帧；尾帧用计时器防抖补齐
        // （松手 33ms 后精确落位；持续拖动时计时器不断顺延，不会像 idle 补帧那样失效）。
        var now = DateTime.UtcNow;
        if (!force && (now - _lastMoveResizeUtc).TotalMilliseconds < 33)
        {
            _pendingInsertAfter = insertAfter;
            _pendingExtraFlags = extraFlags;
            try
            {
                _resizeFlushTimer ??= new Timer(_ =>
                {
                    try
                    {
                        _source.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (!_disposed && !_source.IsDisposed)
                                MoveResizeCore(_pendingInsertAfter, _pendingExtraFlags, force: true);
                        }));
                    }
                    catch { }
                }, null, Timeout.Infinite, Timeout.Infinite);
                _resizeFlushTimer.Change(33, Timeout.Infinite);
            }
            catch { }
            return;
        }
        _lastMoveResizeUtc = now;

        var scale = GetDpiScale();
        var contentW = Math.Max(1, Box.Width / scale.X);
        var contentH = Math.Max(1, Box.Height / scale.Y);
        if (contentW != _lastContentW || contentH != _lastContentH)
        {
            _lastContentW = contentW;
            _lastContentH = contentH;
            _content.Width = contentW;
            _content.Height = contentH;
        }
        var parent = Native.User32.GetParent(_handle);
        var clientPosition = Native.User32.ScreenPointToClient(parent, Box.X, Box.Y);
        int w = Math.Max(1, (int)Math.Round(Box.Width));
        int h = Math.Max(1, (int)Math.Round(Box.Height));
        // 去重：四角缩放时 X/Y/W/H 四个属性各触发一次 MoveResize，圆整后矩形常常无变化；
        // 空刷一次跨进程 SetWindowPos 就逼图标层重绘一次，闪烁就是这么来的。
        if (clientPosition.X == _lastX && clientPosition.Y == _lastY && w == _lastW && h == _lastH)
            return;
        _lastX = clientPosition.X;
        _lastY = clientPosition.Y;
        _lastW = w;
        _lastH = h;
        Native.User32.SetWindowPos(
            _handle,
            insertAfter,
            clientPosition.X,
            clientPosition.Y,
            w,
            h,
            Native.User32.SWP_CROSSPROC | extraFlags);
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
        try { Interlocked.Exchange(ref _resizeFlushTimer, null)?.Dispose(); } catch { }
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
