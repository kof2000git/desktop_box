using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DesktopBox.Controls;
using DesktopBox.Services;
using DesktopBox.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Application = System.Windows.Application;
using Forms = System.Windows.Forms;

namespace DesktopBox.Views;

public partial class MainWindow : Window
{
    private readonly uint _taskbarCreatedMessage = Native.User32.RegisterWindowMessage("TaskbarCreated");
    private readonly MainViewModel _vm;
    private readonly SettingsViewModel _settingsVm;
    private readonly SettingsWindow _settings;
    private readonly ILocalizerService _localizer;
    private readonly Dictionary<Guid, BoxWindow> _boxWindows = new();
    private IntPtr _desktopHost;
    private Forms.NotifyIcon? _tray;
    private Icon? _trayIcon;
    private bool _ownedResourcesDisposed;
    private Timer? _desktopHostRetry;
    private DateTime _lastPersistenceFailureNotificationUtc;
    private DateTime _lastRefreshUtc;
    private IntPtr _mainHwnd = IntPtr.Zero;
    private System.Windows.Interop.HwndSourceHook? _hwndHook;

    public MainWindow(MainViewModel vm, SettingsViewModel settingsVm, SettingsWindow settings)
    {
        InitializeComponent();
        _vm = vm;
        _settingsVm = settingsVm;
        _settings = settings;
        _localizer = App.Services.GetRequiredService<ILocalizerService>();
        DataContext = _vm;
        _vm.LoadCommand.Execute(null);
        _vm.RefreshDesktopIconsState();

        _vm.ScreenWidth = SystemParametersHelper.LayoutWidth;
        _vm.Boxes.CollectionChanged += OnBoxesChanged;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _vm.PersistenceFailed += OnPersistenceFailed;
        _settingsVm.PersistenceFailed += OnPersistenceFailed;

        SetupTray();
        // WinForms 托盘菜单不像 WPF DynamicResource 会自动刷新,语言切换后需手动重建菜单文本
        _localizer.LanguageChanged += OnLanguageChanged;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Hide();
        RefreshDesktopLayer();
        // 开机自启时资源管理器桌面层可能尚未就绪,周期性重试挂接盒子窗口。
        ScheduleDesktopHostRetry();
    }

    // 系统图标自动刷新:窗口句柄就绪后注册接收 shell 变化通知(回收站空/满切换等),
    // 并挂 HwndSource hook 拦截通知消息转发给 ShellChangeNotifierService。
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var src = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
            if (src is null) return;
            _mainHwnd = hwnd;
            var notifier = App.Services.GetRequiredService<IShellChangeNotifierService>();
            _hwndHook = (IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if ((uint)msg == _taskbarCreatedMessage)
                {
                    // explorer 重启：新 DefView 半初始化，此时立即 SetParent 会挂到将死窗口
                    // （建完即毁→循环闪）。失效缓存 + 延迟 1.5s 等新桌面层建完再挂。
                    _desktopHost = IntPtr.Zero;
                    Native.User32.InvalidateWorkerWCache();
                    Services.LogService.Warn("DesktopHost.TaskbarCreated", "explorer 重启，延迟重挂桌面层");
                    try { notifier.Register(hwnd, force: true); } catch (Exception ex) { App.LogError(ex, "MainWindow.ReRegisterShellNotify"); }
                    RestoreTrayIcon();
                    _ = Task.Delay(1500).ContinueWith(_ =>
                    {
                        try
                        {
                            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (!_ownedResourcesDisposed) RefreshDesktopLayer();
                            }));
                        }
                        catch { }
                    }, TaskScheduler.Default);
                    handled = true;
                    return IntPtr.Zero;
                }

                if (notifier.NotifyMessageId != 0 && (uint)msg == notifier.NotifyMessageId)
                {
                    notifier.OnShellNotify(wParam, lParam);
                    handled = true;
                    return IntPtr.Zero;
                }

                return IntPtr.Zero;
            };
            src.AddHook(_hwndHook);
            notifier.Register(hwnd);
        }
        catch (Exception ex) { App.LogError(ex, "MainWindow.ShellChangeNotify"); }
    }

    private void SetupTray()
    {
        _trayIcon = MakeIcon();
        _tray = new Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Visible = true
        };
        RebuildTrayMenu();
        _tray.DoubleClick += (_, _) => ShowBoxes();
    }

    /// <summary>重建托盘菜单文本 + tooltip(启动时 + 语言切换时调用)。</summary>
    private void RebuildTrayMenu()
    {
        if (_tray is null) return;
        _tray.Text = $"DesktopBox {_localizer["app.trayText"]} v{App.Version}";
        var oldMenu = _tray.ContextMenuStrip;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_localizer["menu.showBoxes"], null, (_, _) => ShowBoxes());
        menu.Items.Add(_localizer["menu.newBox"], null, (_, _) => _vm.AddBoxCommand.Execute(null));
        menu.Items.Add(_localizer["menu.addSysIcons"], null, (_, _) => _vm.AddSystemIconsBoxCommand.Execute(null));
        menu.Items.Add(_localizer["menu.organize"], null, (_, _) => OrganizeAndShowBoxes());
        menu.Items.Add(_localizer["menu.toggleIcons"], null, (_, _) => _vm.ToggleDesktopIconsCommand.Execute(null));
        menu.Items.Add(_localizer["menu.refreshItems"], null, (_, _) => RefreshMissingItemsFromTray());
        menu.Items.Add(_localizer["menu.settings"], null, (_, _) => OnOpenSettings(null, null));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_localizer["menu.openLog"], null, (_, _) => Services.LogService.OpenLogDirectory());
        menu.Items.Add(_localizer["menu.quit"], null, (_, _) => OnQuit(null, null));
        _tray.ContextMenuStrip = menu;
        oldMenu?.Dispose();
    }

    private void RefreshMissingItemsFromTray()
    {
        var removed = _vm.PruneMissingItems();
        RefreshDesktopLayer();
        if (_tray is null) return;
        var message = string.Format(_localizer["dialog.refreshItems.done"], removed);
        _tray.ShowBalloonTip(4000, _localizer["app.name"], message, Forms.ToolTipIcon.Info);
    }

    private void ScheduleDesktopHostRetry()
    {
        _desktopHostRetry?.Dispose();
        var attempts = 0;
        _desktopHostRetry = new Timer(_ =>
        {
            attempts++;
            var disp = Application.Current?.Dispatcher;
            if (disp is null || disp.HasShutdownStarted || _ownedResourcesDisposed)
            {
                Interlocked.Exchange(ref _desktopHostRetry, null)?.Dispose();
                return;
            }

            disp.BeginInvoke(new Action(() =>
            {
                if (_ownedResourcesDisposed) return;
                RefreshDesktopLayer();
                var hasHost = _desktopHost != IntPtr.Zero && Native.User32.IsWindow(_desktopHost);
                var hasWindows = _boxWindows.Count > 0 || _vm.Boxes.Count == 0;
                if ((hasHost && hasWindows) || attempts >= 30)
                {
                    Interlocked.Exchange(ref _desktopHostRetry, null)?.Dispose();
                }
            }));
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }

    private void ShowBoxes()
    {
        // 用户显式点"显示盒子"：绕过防抖强制重建 + 落诊断日志，看窗到底建没建。
        RefreshDesktopLayer(force: true);
        LogBoxWindows("ShowBoxes");
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RebuildTrayMenu();

    private void OnPersistenceFailed(object? sender, Exception exception)
    {
        var message = string.Format(_localizer["notification.saveFailed"], exception.Message);
        if (App.IsShuttingDown)
        {
            MessageBox.Show(message, _localizer["app.errorTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_tray is null) return;
        var now = DateTime.UtcNow;
        if (now - _lastPersistenceFailureNotificationUtc < TimeSpan.FromSeconds(30)) return;
        _lastPersistenceFailureNotificationUtc = now;
        _tray.ShowBalloonTip(5000, _localizer["app.errorTitle"], message, Forms.ToolTipIcon.Warning);
    }

    private static System.Drawing.Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(System.Drawing.Color.FromArgb(255, 56, 130, 226));
            g.FillRectangle(brush, 4, 4, 24, 24);
            using var pen = new Pen(System.Drawing.Color.White, 2f);
            g.DrawRectangle(pen, 10, 10, 12, 12);
        }
        var handle = bmp.GetHicon();
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)icon.Clone();
        }
        finally
        {
            Native.User32.DestroyIcon(handle);
        }
    }

    private void OnNewBox(object sender, RoutedEventArgs e) => _vm.AddBoxCommand.Execute(null);
    private void OnAddSystemIcons(object sender, RoutedEventArgs e) => _vm.AddSystemIconsBoxCommand.Execute(null);
    private void OnOrganize(object sender, RoutedEventArgs e)
        => OrganizeAndShowBoxes();

    private void OrganizeAndShowBoxes()
    {
        _vm.OrganizeCommand.Execute(null);
        RefreshDesktopLayer(force: true);
        LogBoxWindows("Organize");
    }
    private void OnToggleIcons(object sender, RoutedEventArgs e) => _vm.ToggleDesktopIconsCommand.Execute(null);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.DesktopIconsVisible))
            RepairBoxWindowsAfterDesktopIconToggle();
    }

    private async void RepairBoxWindowsAfterDesktopIconToggle()
    {
        try
        {
            // 显隐切换会重建 DefView：原来 0/120/450ms 三连刷会和 explorer 重建竞态导致闪烁。
            // 改为立即一次 + 500ms 后防抖一次。
            RefreshDesktopLayer();
            await Task.Delay(500);
            if (Application.Current?.Dispatcher.HasShutdownStarted == true) return;
            if (_ownedResourcesDisposed) return;
            RefreshDesktopLayer();
        }
        catch (Exception ex)
        {
            App.LogError(ex, "MainWindow.RepairBoxWindowsAfterDesktopIconToggle");
        }
    }

    private void RepairBoxWindowsOnDesktopLayer()
    {
        var host = GetDesktopHost(refresh: true);
        if (host == IntPtr.Zero)
            return;

        foreach (var window in _boxWindows.Values.ToList())
        {
            if (!window.IsHandleAlive)
                continue;

            try
            {
                window.EnsureVisibleOnDesktopHost(host);
            }
            catch (Exception ex)
            {
                App.LogError(ex, "MainWindow.RepairBoxWindowsOnDesktopLayer");
            }
        }
    }

    private void RefreshDesktopLayer(bool force = false)
    {
        // 防抖：重试风暴（2s×30）+ 图标切换连刷会狂调 GetWorkerW（0x052C 新建壁纸窗口）。
        // 200ms 内重复调用直接合并；用户显式操作（显示盒子/整理）用 force 绕过。
        var now = DateTime.UtcNow;
        if (!force && (now - _lastRefreshUtc).TotalMilliseconds < 200)
            return;
        _lastRefreshUtc = now;

        var prev = _desktopHost;
        _desktopHost = ResolveDesktopHost();
        if (_desktopHost == IntPtr.Zero)
        {
            Services.LogService.Warn("DesktopHost.Resolve", "桌面宿主未就绪（explorer 可能正在启动），等待重试");
            return;
        }
        if (prev != _desktopHost)
            Services.LogService.Info("DesktopHost.Resolve", $"host 切换 0x{prev:X} -> 0x{_desktopHost:X} boxes={_vm.Boxes.Count}");

        SyncBoxWindows();
    }

    private void OnBoxesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (BoxViewModel box in e.OldItems)
            {
                if (_boxWindows.Remove(box.Id, out var window))
                    window.CloseForRemoval();
            }
        }

        if (e.NewItems is not null)
        {
            foreach (BoxViewModel box in e.NewItems)
                EnsureBoxWindow(box);
        }
    }

    private void SyncBoxWindows()
    {
        var liveIds = _vm.Boxes.Select(b => b.Id).ToHashSet();
        int removed = 0;
        foreach (var (id, window) in _boxWindows.ToList())
        {
            if (!liveIds.Contains(id) || !window.IsHandleAlive)
            {
                _boxWindows.Remove(id);
                window.CloseForRemoval();
                removed++;
            }
        }

        int before = _boxWindows.Count;
        foreach (var box in _vm.Boxes)
            EnsureBoxWindow(box);

        RepairBoxWindowsOnDesktopLayer();
        Services.LogService.Info("DesktopHost.Sync",
            $"boxes={_vm.Boxes.Count} windows={_boxWindows.Count} new={_boxWindows.Count - before} removed={removed}");
    }

    /// <summary>诊断：逐窗记录 HWND/可见性/矩形，定位"盒子不可见"（窗没建 vs 建了看不见）。</summary>
    private void LogBoxWindows(string source)
    {
        try
        {
            Services.LogService.Info("DesktopHost.Windows",
                $"{source}: boxes={_vm.Boxes.Count} windows={_boxWindows.Count} host=0x{_desktopHost:X}");
            foreach (var (id, window) in _boxWindows.ToList())
            {
                BoxViewModel? box = null;
                try { box = _vm.Boxes.FirstOrDefault(b => b.Id == id); } catch { }
                Services.LogService.Info("DesktopHost.Windows",
                    $"{source}: id={id} header={Services.LogService.Truncate(box?.Header)} {window.Describe()}");
            }
        }
        catch { }
    }

    private void EnsureBoxWindow(BoxViewModel box)
    {
        if (_boxWindows.TryGetValue(box.Id, out var existing))
        {
            if (existing.IsHandleAlive)
                return;

            _boxWindows.Remove(box.Id);
        }

        var host = GetDesktopHost();
        if (host == IntPtr.Zero)
        {
            Services.LogService.Warn("DesktopHost.Ensure",
                $"host=0 未建窗 header={Services.LogService.Truncate(box.Header)}");
            return;
        }

        try
        {
            var window = new BoxWindow(box, _vm, host);
            _boxWindows[box.Id] = window;
            Services.LogService.Info("DesktopHost.Ensure",
                $"已建窗 header={Services.LogService.Truncate(box.Header)} host=0x{host:X} {window.Describe()}");
        }
        catch (Exception ex)
        {
            App.LogError(ex, "MainWindow.EnsureBoxWindow");
        }
    }

    private IntPtr GetDesktopHost(bool refresh = false)
    {
        if (!refresh && _desktopHost != IntPtr.Zero && Native.User32.IsWindow(_desktopHost))
            return _desktopHost;

        _desktopHost = ResolveDesktopHost();
        return _desktopHost;
    }

    /// <summary>
    /// 单一 host 策略：永远优先 Progman（和图标同级，不抢 DefView 绘制，最稳定）。
    /// DefView 只在 Progman 拿不到时用；WorkerW（带缓存，只 spawn 一次）最后兜底。
    /// 来回切父是桌面抖动的主因，所以顺序固定、不翻转。
    /// </summary>
    private static IntPtr ResolveDesktopHost()
    {
        var progman = Native.User32.GetProgman();
        if (progman != IntPtr.Zero && Native.User32.IsWindow(progman))
            return progman;
        var def = Native.User32.FindShellDefView();
        if (def != IntPtr.Zero && Native.User32.IsWindow(def))
            return def;
        var worker = Native.User32.GetWorkerW();
        return Native.User32.IsWindow(worker) ? worker : IntPtr.Zero;
    }

    private void RestoreTrayIcon()
    {
        if (_tray is null) return;

        try
        {
            _tray.Visible = false;
            _tray.Visible = true;
            RebuildTrayMenu();
        }
        catch (Exception ex)
        {
            App.LogError(ex, "MainWindow.RestoreTrayIcon");
        }
    }

    private void OnOpenSettings(object? sender, RoutedEventArgs? e)
    {
        // 不设 Owner(主窗口贴桌面层后作 Owner 会抛"未显示过"异常);屏幕居中即可
        _settings.Show();
        _settings.Activate();
    }

    private void DisposeOwnedResources()
    {
        if (_ownedResourcesDisposed)
            return;

        _ownedResourcesDisposed = true;
        Interlocked.Exchange(ref _desktopHostRetry, null)?.Dispose();
        // 移除 HwndSource hook：否则 explorer 的 shellNotify 会打到半析构窗口。
        try
        {
            if (_mainHwnd != IntPtr.Zero && _hwndHook is not null)
            {
                var src = System.Windows.Interop.HwndSource.FromHwnd(_mainHwnd);
                src?.RemoveHook(_hwndHook);
            }
        }
        catch { }
        _hwndHook = null;
        _mainHwnd = IntPtr.Zero;
        _vm.Boxes.CollectionChanged -= OnBoxesChanged;
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm.PersistenceFailed -= OnPersistenceFailed;
        _settingsVm.PersistenceFailed -= OnPersistenceFailed;
        _localizer.LanguageChanged -= OnLanguageChanged;

        foreach (var window in _boxWindows.Values.ToList())
        {
            try
            {
                window.CloseForRemoval();
            }
            catch (Exception ex)
            {
                App.LogError(ex, "MainWindow.DisposeBoxWindow");
            }
        }
        _boxWindows.Clear();

        if (_tray is not null)
        {
            var menu = _tray.ContextMenuStrip;
            _tray.ContextMenuStrip = null;
            menu?.Dispose();
            _tray.Dispose();
            _tray = null;
        }
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private void OnQuit(object? sender, RoutedEventArgs? e)
    {
        App.BeginShutdown();
        if (!_vm.TrySave())
        {
            App.CancelShutdown();
            return;
        }
        DisposeOwnedResources();
        Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        DisposeOwnedResources();
        base.OnClosed(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_tray is { Visible: true } && !App.IsShuttingDown)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }
}
