using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace DesktopBox.Views;

public partial class InputDialog : Window
{
    public InputDialog()
    {
        InitializeComponent();
        // 不设 Owner:主窗口被贴到桌面层(WorkerW)后,被 WPF 判为"未显示过"会导致
        // "无法将 Owner 属性设置为之前未显示的 window" 异常。主窗口本就是全屏,
        // 屏幕居中与父窗口居中视觉一致,直接用 CenterScreen 最稳。
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // 贴桌面层(WorkerW 子窗口)的主窗口 z-order 特殊,弹窗需强制置顶,
        // 否则会被全屏主窗口里的盒子盖住;而 ShowDialog 的模态禁用又让主窗口点不动,
        // 表现为"对话框看不见 + 盒子卡住"。
        Topmost = true;
        Input.Focus();
    }

    /// <summary>输入框:返回用户输入,取消返回 null。</summary>
    public static string? Prompt(string title, string message, string defaultValue = "")
    {
        var d = new InputDialog
        {
            Title = string.IsNullOrEmpty(title) ? $"DesktopBox v{App.Version}" : title
        };
        d.Message.Text = message;
        d.Input.Text = defaultValue;
        d.Input.SelectAll();
        d.ShowDialog();
        return d.DialogResult == true ? d.Input.Text : null;
    }

    /// <summary>确认框:返回是否确认。</summary>
    public static bool Confirm(string message)
    {
        var d = new InputDialog { Title = $"DesktopBox v{App.Version}" };
        d.Message.Text = message;
        d.Input.Visibility = Visibility.Collapsed;
        d.ShowDialog();
        return d.DialogResult == true;
    }

    /// <summary>纯提示框:只有一个确定按钮。</summary>
    public static void Inform(string message)
    {
        var d = new InputDialog { Title = $"DesktopBox v{App.Version}" };
        d.Message.Text = message;
        d.Input.Visibility = Visibility.Collapsed;
        d.CancelButton.Visibility = Visibility.Collapsed;
        d.ShowDialog();
    }

    /// <summary>下拉选择框:返回选中的索引,取消返回 -1。</summary>
    public static int SelectIndex(string title, string message, IList<string> options, int defaultIndex = 0)
    {
        var d = new InputDialog
        {
            Title = string.IsNullOrEmpty(title) ? $"DesktopBox v{App.Version}" : title
        };
        d.Message.Text = message;
        d.Input.Visibility = Visibility.Collapsed;
        d.Picker.Visibility = Visibility.Visible;
        d.Picker.ItemsSource = options;
        if (options.Count > 0)
            d.Picker.SelectedIndex = defaultIndex < 0 || defaultIndex >= options.Count ? 0 : defaultIndex;
        d.ShowDialog();
        return d.DialogResult == true ? d.Picker.SelectedIndex : -1;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
