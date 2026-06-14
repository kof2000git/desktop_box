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
        Owner = Application.Current.Windows.Count > 1
            ? Application.Current.MainWindow
            : null;
        if (Owner is null) WindowStartupLocation = WindowStartupLocation.CenterScreen;
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
