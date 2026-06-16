using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
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

        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        _vm.ScreenWidth = Width;

        SetupTray();
        // WinForms 托盘菜单不像 WPF DynamicResource 会自动刷新,语言切换后需手动重建菜单文本
        _localizer.LanguageChanged += (_, _) => RebuildTrayMenu();

        // Win+D / "显示桌面" 的真实机制(已由日志证实):窗口本身完全正常(非最小化、可见),
        // 只是桌面(Progman)被 shell 提到一个更高的 z-order band,把盒子盖在下面。
        //   - HWND_TOP 提顶盖不过它(实测:提顶成功但盒子仍不可见);
        //   - "TOPMOST 闪一下再降回"不稳定(降回那一步会被 shell 重新压到桌面之下)。
        // 可靠解法:前台是桌面期间持续保持 TOPMOST(必然盖过桌面);前台切到别的窗口时降回
        // NOTOPMOST(让位给用户的浏览器/文件夹,不长期遮挡)。这是桌面常驻工具的标准做法。
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
                Dispatcher.BeginInvoke(new Action(() => WindowState = WindowState.Normal));
        };

        StartWinDCoverGuard();
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
                }
                return IntPtr.Zero;
            });
            notifier.Register(hwnd);
        }
        catch (Exception ex) { App.LogError(ex, "MainWindow.ShellChangeNotify"); }
    }

    private System.Windows.Threading.DispatcherTimer? _guard;
    private bool _lastWasDesktop = false; // 上次前台是否为桌面,仅在切换时落一次日志,避免刷屏

    private void StartWinDCoverGuard()
    {
        // DispatcherTimer:UI 线程触发 + 字段持有防 GC(System.Threading.Timer 局部变量会被回收导致停摆)
        // 间隔尽量短:Win+D 后桌面盖住盒子的窗口期 ≈ 这个间隔,10ms 内肉眼完全察觉不到闪烁。
        _guard = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        _guard.Tick += (_, _) =>
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                // 极少数情况下 Win+D 走真正的最小化:兜底恢复
                if (Native.User32.IsIconic(hwnd))
                    Native.User32.ShowWindow(hwnd, Native.User32.SW_RESTORE);

                // 检测前台窗口类名判断是否为桌面(Progman / ApplicationManager_DesktopShellWindow)
                var fg = Native.User32.GetForegroundWindow();
                if (fg == IntPtr.Zero) return;
                var sb = new System.Text.StringBuilder(256);
                Native.User32.GetClassName(fg, sb, 256);
                var cls = sb.ToString();
                var isDesktop = cls.Contains("rogman", StringComparison.OrdinalIgnoreCase)
                             || cls.Contains("esktopShell", StringComparison.OrdinalIgnoreCase);

                const uint flags = Native.User32.SWP_NOMOVE | Native.User32.SWP_NOSIZE | Native.User32.SWP_NOACTIVATE;
                if (isDesktop)
                {
                    Native.User32.SetWindowPos(hwnd, Native.User32.HWND_TOPMOST, 0, 0, 0, 0, flags);
                }
                else if (_lastWasDesktop)
                {
                    Native.User32.SetWindowPos(hwnd, Native.User32.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
                }
                _lastWasDesktop = isDesktop;
            }
            catch { }
        };
        _guard.Start();
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
        menu.Items.Add(_localizer["menu.organize"], null, (_, _) => _vm.OrganizeCommand.Execute(null));
        menu.Items.Add(_localizer["menu.toggleIcons"], null, (_, _) => _vm.ToggleDesktopIconsCommand.Execute(null));
        menu.Items.Add(_localizer["menu.settings"], null, (_, _) => OnOpenSettings(null, null));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_localizer["menu.quit"], null, (_, _) => OnQuit(null, null));
        _tray.ContextMenuStrip = menu;
    }

    private void ShowBoxes()
    {
        Show();
        Activate();
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
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private void OnNewBox(object sender, RoutedEventArgs e) => _vm.AddBoxCommand.Execute(null);
    private void OnAddSystemIcons(object sender, RoutedEventArgs e) => _vm.AddSystemIconsBoxCommand.Execute(null);
    private void OnOrganize(object sender, RoutedEventArgs e) => _vm.OrganizeCommand.Execute(null);
    private void OnToggleIcons(object sender, RoutedEventArgs e) => _vm.ToggleDesktopIconsCommand.Execute(null);

    private void OnOpenSettings(object? sender, RoutedEventArgs? e)
    {
        // 不设 Owner(主窗口贴桌面层后作 Owner 会抛"未显示过"异常);屏幕居中即可
        _settings.Show();
        _settings.Activate();
    }

    private void OnQuit(object? sender, RoutedEventArgs? e)
    {
        _tray?.Dispose();
        _tray = null;
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_tray is { Visible: true })
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
