using System.Windows;

namespace DesktopBox.Views;

public partial class InputDialog : Window
{
    private string? _result;
    private bool _isConfirm;
    private bool _isInform;

    public InputDialog()
    {
        InitializeComponent();
        // 不设 Owner:主窗口被贴到桌面层(WorkerW)后,被 WPF 判为"未显示过"会导致
        // "无法将 Owner 属性设置为之前未显示的 window" 异常。主窗口本就是全屏,
        // 屏幕居中与父窗口居中视觉一致,直接用 CenterScreen 最稳。
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Input.Focus();
    }

    /// <summary>输入框:返回用户输入,取消返回 null。</summary>
    public static string? Prompt(string title, string message, string defaultValue = "")
    {
        var d = new InputDialog
        {
            Title = string.IsNullOrEmpty(title) ? "DesktopBox" : title
        };
        d.Message.Text = message;
        d.Input.Text = defaultValue;
        d.Input.SelectAll();
        d.ShowDialog();
        return d._result;
    }

    /// <summary>确认框:返回是否确认。</summary>
    public static bool Confirm(string message)
    {
        var d = new InputDialog { Title = "DesktopBox" };
        d.Message.Text = message;
        d.Input.Visibility = Visibility.Collapsed;
        d._isConfirm = true;
        d.OkButton.Content = "确定";
        d.ShowDialog();
        return d._result != null;
    }

    /// <summary>纯提示框:只有一个确定按钮。</summary>
    public static void Inform(string message)
    {
        var d = new InputDialog { Title = "DesktopBox" };
        d.Message.Text = message;
        d.Input.Visibility = Visibility.Collapsed;
        d.CancelButton.Visibility = Visibility.Collapsed;
        d._isInform = true;
        d.ShowDialog();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        _result = (_isConfirm || _isInform) ? "ok" : Input.Text;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _result = null;
        DialogResult = false;
        Close();
    }
}
