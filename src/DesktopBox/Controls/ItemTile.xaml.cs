using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DesktopBox.Models;
using DesktopBox.Views;

namespace DesktopBox.Controls;

public partial class ItemTile : UserControl
{
    /// <summary>磁贴渲染尺寸/布局(由盒子 ViewMode 决定)。变化时重排内部元素。</summary>
    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(nameof(IconSize), typeof(TileSize), typeof(ItemTile),
            new PropertyMetadata(TileSize.Large, OnIconSizeChanged));

    public TileSize IconSize
    {
        get => (TileSize)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public ItemTile()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            SetFallback();
            ApplyLayout();
        };
    }

    private void SetFallback()
    {
        if (DataContext is not BoxItem item) return;
        FallbackText.Text = GetGlyph(item);
        // 不设彩色背景块:让图标/首字与系统原版一致(无底色)
        Fallback.Background = Brushes.Transparent;
        // 图标为空时隐藏 Image、显示占位首字;有图标时反之
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

    /// <summary>根据 TileSize 调整容器方向、图标尺寸、详情可见性。</summary>
    private static void OnIconSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ItemTile)d).ApplyLayout();

    private void ApplyLayout()
    {
        if (!IsInitialized) return;
        var size = IconSize;

        switch (size)
        {
            case TileSize.Large:
                Panel.Orientation = Orientation.Vertical;
                SetIcon(56, 50, 28, HorizontalAlignment.Center);
                NameText.TextAlignment = TextAlignment.Center;
                NameText.FontSize = 12;
                NameText.MaxWidth = 140;
                DetailBox.Visibility = Visibility.Collapsed;
                Root.Padding = new Thickness(4);
                break;
            case TileSize.Medium:
                Panel.Orientation = Orientation.Vertical;
                SetIcon(44, 38, 22, HorizontalAlignment.Center);
                NameText.TextAlignment = TextAlignment.Center;
                NameText.FontSize = 11;
                NameText.MaxWidth = 120;
                DetailBox.Visibility = Visibility.Collapsed;
                break;
            case TileSize.Small:
            case TileSize.List:
                // 图标在左,名称在右
                Panel.Orientation = Orientation.Horizontal;
                SetIcon(26, 22, 15, HorizontalAlignment.Center);
                NameText.TextAlignment = TextAlignment.Left;
                NameText.FontSize = 12;
                NameText.VerticalAlignment = VerticalAlignment.Center;
                NameText.Margin = new Thickness(6, 0, 4, 0);
                DetailBox.Visibility = Visibility.Collapsed;
                break;
            case TileSize.Detail:
                Panel.Orientation = Orientation.Horizontal;
                SetIcon(26, 22, 15, HorizontalAlignment.Center);
                NameText.TextAlignment = TextAlignment.Left;
                NameText.FontSize = 12;
                NameText.VerticalAlignment = VerticalAlignment.Center;
                NameText.Margin = new Thickness(6, 0, 4, 0);
                DetailBox.Visibility = Visibility.Visible;
                DetailBox.Orientation = Orientation.Horizontal;
                DetailBox.Margin = new Thickness(10, 0, 0, 0);
                break;
            case TileSize.Tile:
                Panel.Orientation = Orientation.Horizontal;
                SetIcon(44, 38, 22, HorizontalAlignment.Center);
                NameText.TextAlignment = TextAlignment.Left;
                NameText.FontSize = 12;
                NameText.VerticalAlignment = VerticalAlignment.Center;
                NameText.Margin = new Thickness(8, 0, 8, 0);
                DetailBox.Visibility = Visibility.Visible;
                DetailBox.Orientation = Orientation.Vertical;
                break;
        }
    }

    private void SetIcon(double box, double img, double fallbackFont, HorizontalAlignment align)
    {
        IconBox.Width = box; IconBox.Height = box;
        Img.Width = img; Img.Height = img;
        FallbackText.FontSize = fallbackFont;
        IconBox.HorizontalAlignment = align;
    }

    private BoxItem? Item => DataContext as BoxItem;

    private void OnDoubleClick(object sender, MouseButtonEventArgs e) => OpenItem();

    private void OpenItem()
    {
        if (Item is null) return;
        // 引用式盒子兜底:目标可能已被外部删除/移动(此时盒子里还留着悬空引用)。
        // 双击前校验,失效则提示并移除——避免直接 Process.Start 已失效路径而抛 Win32 错误。
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
            MessageBox.Show($"无法打开:{ex.Message}", "DesktopBox", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>右键:WPF 原生上下文菜单(打开/在资源管理器中显示/复制路径/从盒子移除)。
    /// 不再宿主 Shell 原生 IContextMenu —— 它在"主窗口 reparent 到 WorkerW"环境下,COM 自绘回调会
    /// 触发访问违规(CSE,0xc0000005),进程直接崩且 Dispatcher 无法捕获。改用 WPF 菜单彻底根除。
    /// 原生菜单的"属性/发送到/打开方式"通过"在资源管理器中显示"间接获得,能力不丢。</summary>
    // 原生 Shell 右键菜单由 C++ DLL(DesktopBox.ShellMenu.dll)提供,绕过 .NET COM interop 的 CSE 崩溃
    // (.NET 8 宿主 IContextMenu 自绘稳定触发 0xc0000005,CSE 不留堆栈无法根治;C++/Win32 同款实现稳定)。
    // 返回 0x7000=用户选了"从盒子移除";其他=取消或已执行选中的 Shell 命令(打开/编辑/打印/发送到/...)。
    [DllImport("DesktopBox.ShellMenu.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int ShowShellMenu(string path, int screenX, int screenY);

    private void OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Item is null) return;
        e.Handled = true;
        var pt = PointToScreen(new Point(0, ActualHeight));
        int result = 0;
        try { result = ShowShellMenu(Item.TargetPath, (int)pt.X, (int)pt.Y); }
        catch (Exception ex) { App.LogError(ex, "ItemTile.ShowShellMenu"); }   // DLL 缺失/P-Invoke 失败时记录
        if (result == 0x7000) { RemoveFromBox(); return; }

        // 原生菜单可能执行了"删除/剪切/重命名"等命令,使目标路径失效。盒子是引用式的(只存路径),
        // 需校验刷新:延一帧(等 InvokeCommand 的同步删除落地)后若路径已失效,自动移除并提示,
        // 让"右键删文件→磁贴立即消失",而不是残留成悬空引用、等双击才发现。
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Item is not null && IsLocalPathItem(Item) && !TargetExists(Item.TargetPath))
                NotifyGoneAndRemove();
        }));
    }

    /// <summary>是否为本地路径类条目(非系统图标/网址/::虚拟项)——这类才需校验文件存在性。</summary>
    private static bool IsLocalPathItem(BoxItem item) =>
        item.Type != ItemType.SystemIcon && item.Type != ItemType.Url
        && !item.TargetPath.StartsWith("::", StringComparison.Ordinal);

    /// <summary>目标路径是否存在(文件或目录)。</summary>
    private static bool TargetExists(string path) =>
        File.Exists(path) || Directory.Exists(path);

    /// <summary>目标已失效时:友好提示并从盒子移除该引用(不删任何文件)。</summary>
    private void NotifyGoneAndRemove()
    {
        if (Item is null) return;
        InputDialog.Inform($"「{Item.DisplayName}」的目标已不存在(可能被删除或移动),已从盒子移除该条目。\n(仅取消盒内显示,文件本体不受影响。)");
        RemoveFromBox();
    }

    private ContextMenu BuildContextMenu()
    {
        var item = Item!;
        var menu = new ContextMenu();

        if (item.Type != ItemType.SystemIcon && item.Type != ItemType.Url
            && !item.TargetPath.StartsWith("::"))
        {
            // 普通文件/文件夹/快捷方式:动态获取该文件类型的 Shell 动词(open/edit/print/runas/...),
            // 让菜单随文件类型变化(不再统一)。用托管 ProcessStartInfo.Verbs,不走 COM IContextMenu,不崩。
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

            var miLoc = new MenuItem { Header = "在资源管理器中显示" };
            miLoc.Click += (_, _) => OpenLocation();
            menu.Items.Add(miLoc);
        }
        else
        {
            // 系统图标/网址:只有"打开"(explorer 打开 ::{CLSID} 或浏览器打开 URL)
            var miOpen = new MenuItem { Header = "打开" };
            miOpen.Click += (_, _) => OpenItem();
            menu.Items.Add(miOpen);
        }

        var miCopy = new MenuItem { Header = "复制路径" };
        miCopy.Click += (_, _) =>
        {
            try { Clipboard.SetText(item.TargetPath); } catch { }
        };
        menu.Items.Add(miCopy);

        menu.Items.Add(new Separator());

        var miRemove = new MenuItem { Header = "从盒子移除" };
        miRemove.Click += (_, _) => RemoveFromBox();
        menu.Items.Add(miRemove);

        return menu;
    }

    /// <summary>常见 Shell 动词 → 中文显示名;未命中的动词原样(首字母大写)显示。</summary>
    private static readonly Dictionary<string, string> VerbDisplay = new(StringComparer.OrdinalIgnoreCase)
    {
        ["open"] = "打开",
        ["edit"] = "编辑",
        ["print"] = "打印",
        ["printto"] = "打印到",
        ["preview"] = "预览",
        ["runas"] = "以管理员身份运行",
        ["runasuser"] = "以其他用户身份运行",
        ["explore"] = "资源管理器中浏览",
        ["find"] = "查找",
    };

    private static string VerbDisplayName(string verb) =>
        VerbDisplay.TryGetValue(verb, out var display)
            ? display
            : (char.ToUpperInvariant(verb[0]) + verb[1..].ToLowerInvariant());

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
                // 诊断:记录 .lnk 解析结果。若 resolved 为空=解析失败→回退打开 .lnk 所在(桌面)
                App.LogError(new System.Exception($"lnk resolve: orig={original} | resolved={resolved ?? "(null)"} | use={target}"),
                    target == original ? "OpenLocation.lnk-FAILED" : "OpenLocation.lnk-OK");
            }
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { App.LogError(ex, "ItemTile.OpenLocation"); }
    }

    /// <summary>从盒子移除当前磁贴(不删文件本体)。</summary>
    private void RemoveFromBox()
    {
        if (Item is null) return;
        if (Window.GetWindow(this) is not Views.MainWindow mw) return;
        if (mw.DataContext is not ViewModels.MainViewModel vm) return;
        vm.RemoveItemAnywhere(Item);
    }
}
