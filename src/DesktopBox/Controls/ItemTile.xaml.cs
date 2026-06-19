using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DesktopBox.Models;
using DesktopBox.Services;
using DesktopBox.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DesktopBox.Controls;

public partial class ItemTile : UserControl
{
    private static readonly SemaphoreSlim ShellMenuGate = new(1, 1);

    /// <summary>磁贴渲染尺寸/布局(由盒子 ViewMode 决定)。变化时重排内部元素。</summary>
    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(nameof(IconSize), typeof(TileSize), typeof(ItemTile),
            new PropertyMetadata(TileSize.Large, OnIconSizeChanged));

    public TileSize IconSize
    {
        get => (TileSize)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    private ILocalizerService? _localizer;
    private ILocalizerService Localizer =>
        _localizer ??= App.Services.GetRequiredService<ILocalizerService>();

    public ItemTile()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            SetFallback();
            ApplyLayout();
        };
        // 单击选中(普通=单选,Ctrl=多选)。不拦截双击打开(OnDoubleClick 仍触发)。
        PreviewMouseLeftButtonDown += OnTileMouseDown;
    }

    private void OnTileMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Item is null) return;
        var vm = App.Services.GetRequiredService<ViewModels.MainViewModel>();
        vm.HandleTileClick(Item, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
    }

    private void SetFallback()
    {
        if (DataContext is not BoxItem item) return;
        FallbackText.Text = GetGlyph(item);
        Fallback.Background = Brushes.Transparent;
        bool hasIcon = !string.IsNullOrEmpty(item.IconCachePath);
        Img.Visibility = hasIcon ? Visibility.Visible : Visibility.Collapsed;
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

    /// <summary>根据 TileSize 调整行列定义、图标尺寸、详情可见性。</summary>
    private static void OnIconSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ItemTile)d).ApplyLayout();

    private static readonly GridLength Star = new(1, GridUnitType.Star);
    private static readonly GridLength Auto = GridLength.Auto;
    private static GridLength Fix(double v) => new(v, GridUnitType.Pixel);

    private const double DetailModifiedWidth = 108;
    private const double DetailSizeWidth = 68;

    private void ApplyLayout()
    {
        if (!IsInitialized) return;
        var size = IconSize;

        foreach (var child in new UIElement[] { IconBox, NameText, ModifiedText, SizeText })
        {
            Grid.SetColumnSpan(child, 1);
            Grid.SetRowSpan(child, 1);
        }
        IconBox.HorizontalAlignment = HorizontalAlignment.Center;
        IconBox.VerticalAlignment = VerticalAlignment.Top;
        ModifiedText.Visibility = Visibility.Collapsed;
        SizeText.Visibility = Visibility.Collapsed;

        switch (size)
        {
            case TileSize.Large:
                SetCols(LayoutGrid, Star);
                SetRows(LayoutGrid, Auto, Auto);
                Place(IconBox, 0, 0); Place(NameText, 0, 1);
                SetIcon(56, 50, 28);
                NameText.TextAlignment = TextAlignment.Center;
                NameText.FontSize = 12;
                NameText.VerticalAlignment = VerticalAlignment.Top;
                NameText.Margin = new Thickness(0, 3, 0, 0);
                break;
            case TileSize.Medium:
                SetCols(LayoutGrid, Star);
                SetRows(LayoutGrid, Auto, Auto);
                Place(IconBox, 0, 0); Place(NameText, 0, 1);
                SetIcon(44, 38, 22);
                NameText.TextAlignment = TextAlignment.Center;
                NameText.FontSize = 11;
                NameText.VerticalAlignment = VerticalAlignment.Top;
                NameText.Margin = new Thickness(0, 3, 0, 0);
                break;
            case TileSize.Small:
            case TileSize.List:
                SetCols(LayoutGrid, Auto, Star);
                SetRows(LayoutGrid, Auto);
                Place(IconBox, 0, 0); Place(NameText, 1, 0);
                SetIcon(26, 22, 15);
                IconBox.HorizontalAlignment = HorizontalAlignment.Left;
                IconBox.VerticalAlignment = VerticalAlignment.Center;
                NameText.TextAlignment = TextAlignment.Left;
                NameText.FontSize = 12;
                NameText.VerticalAlignment = VerticalAlignment.Center;
                NameText.Margin = new Thickness(6, 0, 4, 0);
                break;
            case TileSize.Detail:
                SetCols(LayoutGrid, Auto, Star, Fix(DetailModifiedWidth), Fix(DetailSizeWidth));
                SetRows(LayoutGrid, Auto);
                Place(IconBox, 0, 0); Place(NameText, 1, 0);
                Place(ModifiedText, 2, 0); Place(SizeText, 3, 0);
                SetIcon(26, 22, 15);
                IconBox.HorizontalAlignment = HorizontalAlignment.Left;
                IconBox.VerticalAlignment = VerticalAlignment.Center;
                NameText.TextAlignment = TextAlignment.Left;
                NameText.FontSize = 12;
                NameText.VerticalAlignment = VerticalAlignment.Center;
                NameText.Margin = new Thickness(6, 0, 8, 0);
                ModifiedText.Visibility = Visibility.Visible;
                SizeText.Visibility = Visibility.Visible;
                ModifiedText.VerticalAlignment = VerticalAlignment.Center;
                ModifiedText.Margin = new Thickness(0, 0, 8, 0);
                SizeText.VerticalAlignment = VerticalAlignment.Center;
                break;
            case TileSize.Tile:
                SetCols(LayoutGrid, Auto, Star);
                SetRows(LayoutGrid, Auto, Auto, Auto);
                Place(IconBox, 0, 0, rowSpan: 3);
                Place(NameText, 1, 0);
                Place(ModifiedText, 1, 1);
                Place(SizeText, 1, 2);
                SetIcon(44, 38, 22);
                IconBox.HorizontalAlignment = HorizontalAlignment.Left;
                IconBox.VerticalAlignment = VerticalAlignment.Center;
                NameText.TextAlignment = TextAlignment.Left;
                NameText.FontSize = 12;
                NameText.VerticalAlignment = VerticalAlignment.Top;
                NameText.Margin = new Thickness(8, 2, 8, 2);
                ModifiedText.Visibility = Visibility.Visible;
                SizeText.Visibility = Visibility.Visible;
                ModifiedText.VerticalAlignment = VerticalAlignment.Center;
                ModifiedText.Margin = new Thickness(8, 0, 8, 1);
                SizeText.VerticalAlignment = VerticalAlignment.Center;
                SizeText.Margin = new Thickness(8, 0, 8, 2);
                break;
        }
    }

    private static void SetCols(Grid g, params GridLength[] widths)
    {
        g.ColumnDefinitions.Clear();
        foreach (var w in widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
    }

    private static void SetRows(Grid g, params GridLength[] heights)
    {
        g.RowDefinitions.Clear();
        foreach (var h in heights) g.RowDefinitions.Add(new RowDefinition { Height = h });
    }

    private static void Place(UIElement e, int col, int row, int colSpan = 1, int rowSpan = 1)
    {
        Grid.SetColumn(e, col);
        Grid.SetRow(e, row);
        Grid.SetColumnSpan(e, colSpan);
        Grid.SetRowSpan(e, rowSpan);
    }

    private void SetIcon(double box, double img, double fallbackFont)
    {
        IconBox.Width = box; IconBox.Height = box;
        Img.Width = img; Img.Height = img;
        FallbackText.FontSize = fallbackFont;
    }

    private BoxItem? Item => DataContext as BoxItem;

    private void OnDoubleClick(object sender, MouseButtonEventArgs e) => OpenItem();

    private void OpenItem()
    {
        if (Item is null) return;
        if (IsLocalPathItem(Item) && !TargetExists(Item.TargetPath))
        {
            NotifyGoneAndRemove();
            return;
        }
        try
        {
            if (Item.Type == ItemType.SystemIcon || Item.TargetPath.StartsWith("::"))
                Process.Start(new ProcessStartInfo("explorer.exe", Item.TargetPath) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(Item.TargetPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Localizer["dialog.openFail"], ex.Message),
                Localizer["app.errorTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>右键:原生 Shell 菜单由 C++ DLL(DesktopBox.ShellMenu.dll)提供,绕过 .NET COM interop 的 CSE 崩溃。
    /// 返回 0x7000=用户选了"从盒子移除";其他=取消或已执行选中的 Shell 命令。</summary>
    [DllImport("DesktopBox.ShellMenu.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int ShowShellMenu(string path, int screenX, int screenY);

    private async void OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = Item;
        if (item is null) return;

        // 多选批量场景:若当前处于多选(>1)且右键的是已选中项,不弹单文件 shell 菜单,
        // 让事件冒泡到 BoxControl 的 ContextMenu(含"从盒子移除选中项"等批量操作)。
        // 资源管理器也是这种行为:多选后右键弹的是批量菜单,而非单文件 verb 菜单。
        if (item.IsSelected)
        {
            var vm = App.Services.GetRequiredService<ViewModels.MainViewModel>();
            if (vm.GetSelectedItems().Count > 1) return;   // 放行 → 冒泡到 BoxControl 菜单
        }

        e.Handled = true;
        if (!await ShellMenuGate.WaitAsync(0))
            return;

        var pt = PointToScreen(new Point(0, ActualHeight));
        try
        {
            int result = 0;
            bool fallback = item.Type == ItemType.Url;
            if (!fallback)
            {
                try
                {
                    result = await Services.StaTaskRunner.Run(() =>
                        ShowShellMenu(item.TargetPath, (int)pt.X, (int)pt.Y));
                }
                catch (Exception ex)
                {
                    App.LogError(ex, "ItemTile.ShowShellMenu");
                    fallback = true;
                }
            }
            if (fallback)
            {
                var menu = BuildContextMenu();
                menu.PlacementTarget = this;
                menu.Placement = PlacementMode.Bottom;
                menu.IsOpen = true;
                return;
            }
            if (result == 0x7000) { RemoveFromBox(); return; }

            // 原生菜单可能执行了"删除/剪切/重命名"等命令,使目标路径失效。延一帧校验刷新。
            await Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Item is not null && ReferenceEquals(Item, item) && IsLocalPathItem(item) && !TargetExists(item.TargetPath))
                    NotifyGoneAndRemove();
            }));
        }
        finally
        {
            ShellMenuGate.Release();
        }
    }

    private static bool IsLocalPathItem(BoxItem item) =>
        item.Type != ItemType.SystemIcon && item.Type != ItemType.Url
        && !item.TargetPath.StartsWith("::", StringComparison.Ordinal);

    private static bool TargetExists(string path) =>
        File.Exists(path) || Directory.Exists(path);

    private void NotifyGoneAndRemove()
    {
        if (Item is null) return;
        InputDialog.Inform(string.Format(Localizer["dialog.targetGone"], Item.DisplayName));
        RemoveFromBox();
    }

    private ContextMenu BuildContextMenu()
    {
        var item = Item!;
        var menu = new ContextMenu();

        if (item.Type != ItemType.SystemIcon && item.Type != ItemType.Url
            && !item.TargetPath.StartsWith("::"))
        {
            string[] verbs = Array.Empty<string>();
            try { verbs = new ProcessStartInfo(item.TargetPath) { UseShellExecute = true }.Verbs; }
            catch (Exception ex) { App.LogError(ex, "ItemTile.QueryVerbs"); }

            bool added = false;
            foreach (var v in verbs)
            {
                if (string.IsNullOrEmpty(v)) continue;
                var verb = v;
                var mi = new MenuItem { Header = VerbDisplayName(verb) };
                mi.Click += (_, _) =>
                {
                    try { Process.Start(new ProcessStartInfo(item.TargetPath) { Verb = verb, UseShellExecute = true }); }
                    catch (Exception ex) { App.LogError(ex, "ItemTile.VerbInvoke"); }
                };
                menu.Items.Add(mi);
                added = true;
            }
            if (added) menu.Items.Add(new Separator());

            var miLoc = new MenuItem { Header = Localizer["item.showInExplorer"] };
            miLoc.Click += (_, _) => OpenLocation();
            menu.Items.Add(miLoc);
        }
        else
        {
            var miOpen = new MenuItem { Header = Localizer["item.open"] };
            miOpen.Click += (_, _) => OpenItem();
            menu.Items.Add(miOpen);
        }

        var miCopy = new MenuItem { Header = Localizer["item.copyPath"] };
        miCopy.Click += (_, _) =>
        {
            try { Clipboard.SetText(item.TargetPath); } catch { }
        };
        menu.Items.Add(miCopy);

        menu.Items.Add(new Separator());

        var miRemove = new MenuItem { Header = Localizer["item.remove"] };
        miRemove.Click += (_, _) => RemoveFromBox();
        menu.Items.Add(miRemove);

        return menu;
    }

    /// <summary>Shell 动词 → 显示名:先查本地化 verb.&lt;name&gt;,找不到则首字母大写原样显示。</summary>
    private string VerbDisplayName(string verb)
    {
        var key = "verb." + verb.ToLowerInvariant();
        var localized = Localizer[key];
        if (!ReferenceEquals(localized, key) && localized != key) return localized;
        return char.ToUpperInvariant(verb[0]) + verb[1..].ToLowerInvariant();
    }

    private void OpenLocation()
    {
        if (Item is null) return;
        try
        {
            var original = Item.TargetPath;
            var target = original;
            if (original.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = Native.ShellLinkResolver.ResolveTarget(original);
                target = !string.IsNullOrEmpty(resolved) ? resolved : original;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { App.LogError(ex, "ItemTile.OpenLocation"); }
    }

    /// <summary>从盒子移除当前磁贴(不删文件本体)。</summary>
    private void RemoveFromBox()
    {
        if (Item is null) return;
        var vm = App.Services.GetRequiredService<ViewModels.MainViewModel>();
        vm.RemoveItemAnywhere(Item);
    }
}
