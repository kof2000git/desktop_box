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
