using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopBox.Models;

namespace DesktopBox.Controls;

public partial class ItemTile : UserControl
{
    public ItemTile()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ApplyItem();
    }

    private void ApplyItem()
    {
        if (DataContext is not BoxItem item) return;

        if (!string.IsNullOrEmpty(item.IconCachePath) && File.Exists(item.IconCachePath))
        {
            try
            {
                Icon.Source = new BitmapImage(new System.Uri(item.IconCachePath));
                Fallback.Visibility = Visibility.Collapsed;
                return;
            }
            catch { /* 降级到 fallback */ }
        }
        ShowFallback(item);
    }

    private void ShowFallback(BoxItem item)
    {
        Fallback.Visibility = Visibility.Visible;
        FallbackText.Text = GetGlyph(item);
        Fallback.Background = new SolidColorBrush(GetFallbackColor(item));
    }

    private static string GetGlyph(BoxItem item)
    {
        var c = string.IsNullOrEmpty(item.DisplayName) ? '?' : item.DisplayName[0];
        if (!char.IsLetterOrDigit(c)) c = item.Type switch
        {
            ItemType.Folder => '文',
            ItemType.Url => '链',
            _ => '件'
        };
        return char.ToUpperInvariant(c).ToString();
    }

    private static Color GetFallbackColor(BoxItem item) => item.Type switch
    {
        ItemType.Folder  => Color.FromRgb(0x4C, 0xAF, 0x50), // 绿
        ItemType.Url     => Color.FromRgb(0x1E, 0x88, 0xE5), // 蓝
        ItemType.Shortcut=> Color.FromRgb(0x8E, 0x24, 0xAA), // 紫
        _                => Color.FromRgb(0x6D, 0x4C, 0x41)  // 棕
    };

    private BoxItem? Item => DataContext as BoxItem;

    private void OnDoubleClick(object sender, MouseButtonEventArgs e) => OpenItem();

    private void OnOpen(object sender, RoutedEventArgs e) => OpenItem();

    private void OpenItem()
    {
        if (Item is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(Item.TargetPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开:{ex.Message}", "DesktopBox",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnOpenLocation(object sender, RoutedEventArgs e)
    {
        if (Item is null) return;
        if (Item.Type == ItemType.Url) { OpenItem(); return; }
        try
        {
            var dir = Path.GetDirectoryName(Item.TargetPath) ?? "";
            Process.Start("explorer.exe", $"\"{dir}\"");
        }
        catch { }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Item is null) return;
        if (Window.GetWindow(this) is not Views.MainWindow mw) return;
        if (mw.DataContext is not ViewModels.MainViewModel vm) return;

        foreach (var box in vm.Boxes)
        {
            if (box.Items.Contains(Item))
            {
                vm.RemoveItem(box, Item);
                break;
            }
        }
    }
}
