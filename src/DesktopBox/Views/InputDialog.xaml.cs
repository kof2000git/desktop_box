using System.Windows;

namespace DesktopBox.Views;

public partial class InputDialog : Window
{
    private string? _result;
    private bool _isConfirm;

    public InputDialog()
    {
        InitializeComponent();
        Owner = System.Windows.Application.Current.Windows.Count > 1
            ? System.Windows.Application.Current.MainWindow
            : null;
        if (Owner is null) WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Input.Focus();
    }

    /// <summary>弹出输入框,返回用户输入;取消返回 null。</summary>
    public static string? Prompt(string title, string message, string defaultValue = "")
    {
        var d = new InputDialog();
        d.Title = string.IsNullOrEmpty(title) ? "DesktopBox" : title;
        d.Message.Text = message;
        d.Input.Text = defaultValue;
        d._isConfirm = false;
        d.Input.SelectAll();
        d.ShowDialog();
        return d._result;
    }

    /// <summary>弹出确认框,返回是否确认。</summary>
    public static bool Confirm(string message)
    {
        var d = new InputDialog();
        d.Title = "DesktopBox";
        d.Message.Text = message;
        d.Input.Visibility = Visibility.Collapsed;
        d._isConfirm = true;
        d.OkButton.Content = "确定";
        d.ShowDialog();
        return d._result != null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        _result = _isConfirm ? "yes" : Input.Text;
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
