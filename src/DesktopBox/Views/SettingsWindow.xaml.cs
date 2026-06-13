using System.Windows;
using DesktopBox.ViewModels;

namespace DesktopBox.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            LightRadio.IsChecked = !vm.IsDark;
    }
}
