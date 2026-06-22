using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
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
    private readonly MainViewModel _vm;
    private readonly SettingsWindow _settings;
    private readonly ILocalizerService _localizer;
    private readonly Dictionary<Guid, BoxWindow> _boxWindows = new();
    private IntPtr _desktopHost;
    private Forms.NotifyIcon? _tray;

    public MainWindow(MainViewModel vm, SettingsWindow settings)
    {
        InitializeComponent();
        _vm = vm;
        _settings = settings;
        _localizer = App.Services.GetRequiredService<ILocalizerService>();
        DataContext = _vm;
        _vm.LoadCommand.Execute(null);
        _vm.RefreshDesktopIconsState();

        _vm.ScreenWidth = SystemParametersHelper.LayoutWidth;
        _vm.Boxes.CollectionChanged += OnBoxesChanged;
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        SetupTray();
        // WinForms 托盘菜单不像 WPF DynamicResource 会自动刷新,语言切换后需手动重建菜单文本
        _localizer.LanguageChanged += (_, _) => RebuildTrayMenu();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Hide();
        SyncBoxWindows();
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
            var notifier = App.Services.GetRequiredService<IShellChangeNotifierService>();
            src.AddHook((IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (notifier.NotifyMessageId != 0 && (uint)msg == notifier.NotifyMessageId)
                {
                    notifier.OnShellNotify(wParam, lParam);
                    handled = true;
                    return IntPtr.Zero;
                }

                return IntPtr.Zero;
            });
            notifier.Register(hwnd);
        }
        catch (Exception ex) { App.LogError(ex, "MainWindow.ShellChangeNotify"); }
    }

    private void SetupTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = MakeIcon(),
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
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_localizer["menu.showBoxes"], null, (_, _) => ShowBoxes());
        menu.Items.Add(_localizer["menu.newBox"], null, (_, _) => _vm.AddBoxCommand.Execute(null));
        menu.Items.Add(_localizer["menu.addSysIcons"], null, (_, _) => _vm.AddSystemIconsBoxCommand.Execute(null));
        menu.Items.Add(_localizer["menu.organize"], null, (_, _) => OrganizeAndShowBoxes());
        menu.Items.Add(_localizer["menu.toggleIcons"], null, (_, _) => _vm.ToggleDesktopIconsCommand.Execute(null));
        menu.Items.Add(_localizer["menu.settings"], null, (_, _) => OnOpenSettings(null, null));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_localizer["menu.quit"], null, (_, _) => OnQuit(null, null));
        _tray.ContextMenuStrip = menu;
    }

    private void ShowBoxes()
    {
        ShowDesktopLayer();
        SyncBoxWindows();
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
        ShowDesktopLayer();
        SyncBoxWindows();
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
            RepairBoxWindowsOnDesktopLayer();
            await Task.Delay(120);
            if (Application.Current?.Dispatcher.HasShutdownStarted == true) return;
            RepairBoxWindowsOnDesktopLayer();
            await Task.Delay(450);
            if (Application.Current?.Dispatcher.HasShutdownStarted == true) return;
            RepairBoxWindowsOnDesktopLayer();
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

        foreach (var window in _boxWindows.Values)
            window.EnsureVisibleOnDesktopHost(host);
    }

    private static void ShowDesktopLayer()
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return;

            var shell = Activator.CreateInstance(shellType);
            shellType.InvokeMember("MinimizeAll", System.Reflection.BindingFlags.InvokeMethod, null, shell, Array.Empty<object>());
        }
        catch { }
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
        foreach (var (id, window) in _boxWindows.ToList())
        {
            if (!liveIds.Contains(id))
            {
                _boxWindows.Remove(id);
                window.CloseForRemoval();
            }
        }

        foreach (var box in _vm.Boxes)
            EnsureBoxWindow(box);
    }

    private void EnsureBoxWindow(BoxViewModel box)
    {
        if (_boxWindows.ContainsKey(box.Id))
            return;

        var host = GetDesktopHost();
        if (host == IntPtr.Zero)
            return;

        var window = new BoxWindow(box, _vm, host);
        _boxWindows[box.Id] = window;
    }

    private IntPtr GetDesktopHost(bool refresh = false)
    {
        if (!refresh && _desktopHost != IntPtr.Zero)
            return _desktopHost;

        _desktopHost = Native.User32.FindShellDefView();
        if (_desktopHost == IntPtr.Zero)
            _desktopHost = Native.User32.GetProgman();
        return _desktopHost;
    }

    private void OnOpenSettings(object? sender, RoutedEventArgs? e)
    {
        // 不设 Owner(主窗口贴桌面层后作 Owner 会抛"未显示过"异常);屏幕居中即可
        _settings.Show();
        _settings.Activate();
    }

    private void OnQuit(object? sender, RoutedEventArgs? e)
    {
        App.BeginShutdown();
        try { _vm.Save(); } catch { }
        _tray?.Dispose();
        _tray = null;
        Application.Current.Shutdown();
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
