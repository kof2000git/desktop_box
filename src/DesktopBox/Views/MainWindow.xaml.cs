using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using DesktopBox.ViewModels;
using Application = System.Windows.Application;
using Forms = System.Windows.Forms;

namespace DesktopBox.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly SettingsWindow _settings;
    private Forms.NotifyIcon? _tray;

    public MainWindow(MainViewModel vm, SettingsWindow settings)
    {
        InitializeComponent();
        _vm = vm;
        _settings = settings;
        DataContext = _vm;
        _vm.LoadCommand.Execute(null);

        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        SetupTray();
    }

    private void SetupTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "DesktopBox 桌面整理盒子",
            Visible = true
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示盒子", null, (_, _) => ShowBoxes());
        menu.Items.Add("新建盒子", null, (_, _) => _vm.AddBoxCommand.Execute(null));
        menu.Items.Add("设置", null, (_, _) => OnOpenSettings(null, null));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => OnQuit(null, null));
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowBoxes();
    }

    private void ShowBoxes()
    {
        Show();
        Activate();
    }

    /// <summary>用 GDI+ 现场画一个方块当托盘图标,无需 .ico 文件。</summary>
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

    private void OnOpenSettings(object? sender, RoutedEventArgs? e)
    {
        _settings.Owner = this;
        _settings.Show();
        _settings.Activate();
    }

    private void OnQuit(object? sender, RoutedEventArgs? e)
    {
        _tray?.Dispose();
        _tray = null;
        Application.Current.Shutdown();
    }

    // 关闭窗口 = 缩到托盘(不退出)
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
