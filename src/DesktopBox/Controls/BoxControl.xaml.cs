using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopBox.Models;
using DesktopBox.Services;
using DesktopBox.ViewModels;
using DesktopBox.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DesktopBox.Controls;

public partial class BoxControl : UserControl
{
    private Point _dragOrigin;
    private Point _boxOrigin;            // 拖动起始时盒子位置:用"起点+累积位移"算意图位置,不受磁吸覆盖
    private bool _isDragging;
    private bool _isResizing;
    private readonly BoxEdgeSnapState _snapX = new();
    private readonly BoxEdgeSnapState _snapY = new();
    private MainViewModel? _mainVm;
    private ILocalizerService? _localizer;
    private BoxViewModel? _subscribedVm;
    private FirstLetterKeyboardNavigator? _keyboardNavigator;

    private MainViewModel MainVm =>
        _mainVm ??= App.Services.GetRequiredService<MainViewModel>();

    private ILocalizerService Localizer =>
        _localizer ??= App.Services.GetRequiredService<ILocalizerService>();

    private FirstLetterKeyboardNavigator KeyboardNavigator =>
        _keyboardNavigator ??= App.Services.GetRequiredService<FirstLetterKeyboardNavigator>();

    public BoxControl()
    {
        InitializeComponent();
        // WPF-UI hover 动画打 frozen 主题画刷会致命崩溃（v1.7.10）：Loaded 后把实例画刷解冻。
        Loaded += (_, _) =>
        {
            try { WpfUiAnimationGuard.UnfreezeSubtree(this); } catch { }
        };
        DataContextChanged += (_, _) =>
        {
            if (_subscribedVm is not null)
                _subscribedVm.ViewModeChanged -= OnViewModeChanged;

            _subscribedVm = Vm;
            if (_subscribedVm is not null)
                _subscribedVm.ViewModeChanged += OnViewModeChanged;
        };
    }

    private BoxViewModel? Vm => DataContext as BoxViewModel;

    private void OnBoxMouseEnter(object sender, MouseEventArgs e) => KeyboardNavigator.Activate(this);

    private void OnBoxMouseLeave(object sender, MouseEventArgs e) => KeyboardNavigator.Deactivate(this);

    public bool NavigateByFirstLetter(char key)
    {
        if (Vm is null || Vm.DisplayItems.Count == 0)
            return false;

        var normalized = char.ToUpperInvariant(key);
        var currentIndex = _lastNavigationKey == normalized ? _lastNavigationIndex : -1;
        var nextIndex = FirstLetterNavigator.FindNextIndex(Vm.DisplayItems, normalized, currentIndex);
        if (nextIndex < 0)
            return false;

        _lastNavigationKey = normalized;
        _lastNavigationIndex = nextIndex;

        var item = Vm.DisplayItems[nextIndex];
        MainVm.ClearSelection();
        item.IsSelected = true;
        ScrollItemIntoView(item);
        return true;
    }

    private char? _lastNavigationKey;
    private int _lastNavigationIndex = -1;

    private void ScrollItemIntoView(BoxItem item)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateLayout();
            var tile = FindVisualChildren<ItemTile>(ItemsViewport)
                .FirstOrDefault(t => ReferenceEquals(t.DataContext, item));
            tile?.BringIntoView();
        }), DispatcherPriority.Loaded);
    }

    /// <summary>视图模式变更:切到详细信息时惰性填充大小/修改时间。</summary>
    private void OnViewModeChanged(BoxViewModel vm)
    {
        if (vm.ViewMode == ViewMode.Detail)
            MainVm.EnsureDetailFields(vm);
    }

    // ---- 拖动盒子 ----
    private void OnHeaderDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2 || Vm is null) return;
        _isDragging = true;
        _dragOrigin = PointToScreen(e.GetPosition(this));
        _boxOrigin = new Point(Vm.X, Vm.Y);   // 记录起点:拖动期间意图位置=起点+累积位移
        ResetSnapState();
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }

    private void OnHeaderMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || Vm is null) return;
        var pos = PointToScreen(e.GetPosition(this));
        // 意图位置 = 起点位置 + 累积位移(不被磁吸覆盖,慢拖也能持续累积以脱离磁吸)
        double x = _boxOrigin.X + (pos.X - _dragOrigin.X);
        double y = _boxOrigin.Y + (pos.Y - _dragOrigin.Y);
        ApplyEdgeMagnet(Vm, ref x, ref y);
        // 越界约束:保证盒子有足够可见区域留在屏内,防止拖出屏幕后只剩一条小边
        // 被父窗口(DefView 只在屏幕内)裁掉 = "拖着拖着就不见了"。
        var (cx, cy) = SystemParametersHelper.ClampIntoScreens(
            x, y, Math.Min(Vm.Width, 320), Math.Min(Vm.Height, 120));
        Vm.X = cx;
        Vm.Y = cy;
    }

    /// <summary>边缘磁吸(带迟滞):盒子靠近显示器工作区边缘时自动贴合。</summary>
    private void ApplyEdgeMagnet(BoxViewModel vm, ref double x, ref double y)
    {
        var screens = SystemParametersHelper.AllScreens;
        x = EdgeMagnet.ApplyHorizontal(_snapX, x, vm.Width, screens);
        y = EdgeMagnet.ApplyVertical(_snapY, y, vm.Height, screens);
    }

    private void ResetSnapState()
    {
        _snapX.Snapped = false;
        _snapX.Edge = null;
        _snapY.Snapped = false;
        _snapY.Edge = null;
    }

    private void OnHeaderUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        Mouse.Capture(null);
        MainVm.ScheduleSave();
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        MainVm.ScheduleSave();
    }

    // ---- 缩放 ----
    // 实现：屏幕坐标锚定法。缩放中唯一的自由度是当前鼠标的屏幕位置；对角/对边锚点
    // 在 DragStarted 时用屏幕坐标钉死，永不移动。尺寸=鼠标−锚点（按方向投影），每次
    // DragDelta 独立计算、零累积。
    // 历史：旧版用 DragDelta 的 e.HorizontalChange 逐次累加到 _resizeDx/_resizeDy。
    // WPF Thumb 的 delta 相对 Thumb 自身，而 Thumb 钉在窗口边缘上——拖动时窗口一变大，
    // Thumb 就追着鼠标跑，相对位移反向，形成正反馈振荡（窗口忽大忽小、日志里 dx 抖到
    // ±9000）。改成鼠标绝对坐标后回路消失，鼠标指哪边在哪。
    private void OnResizeStarted(object sender, DragStartedEventArgs e)
    {
        if (Vm is null) return;
        _isResizing = true;
        _resizeDir = (sender as FrameworkElement)?.Tag as string ?? "SE";
        _resizeOrigin = new Rect(Vm.X, Vm.Y, Vm.Width, Vm.Height);
        _resizeAnchor = ComputeAnchor(_resizeOrigin, _resizeDir);
        Services.LogService.Info("BoxResize.Start",
            $"dir={_resizeDir} origin=({_resizeOrigin.X:0},{_resizeOrigin.Y:0},{_resizeOrigin.Width:0}x{_resizeOrigin.Height:0}) anchor=({_resizeAnchor.X:0},{_resizeAnchor.Y:0})");
    }

    private string _resizeDir = "SE";
    private Rect _resizeOrigin;
    private Point _resizeAnchor;

    /// <summary>方向的对侧锚点（拖动中固定不动）：SE/W/S 用左上系，NW/E/N 用右下系。</summary>
    private static Point ComputeAnchor(Rect r, string dir) => dir switch
    {
        "SE" => new Point(r.Left, r.Top),
        "E" => new Point(r.Left, r.Top),
        "S" => new Point(r.Left, r.Top),
        "NW" => new Point(r.Right, r.Bottom),
        "W" => new Point(r.Right, r.Bottom),
        "N" => new Point(r.Right, r.Bottom),
        "NE" => new Point(r.Left, r.Bottom),
        "SW" => new Point(r.Right, r.Top),
        _ => new Point(r.Left, r.Top)
    };

    private void OnResize(object sender, DragDeltaEventArgs e)
    {
        if (!_isResizing || Vm is null) return;
        var dir = (sender as FrameworkElement)?.Tag as string ?? "SE";
        // 只信当前鼠标屏幕位置（PointToScreen 映射稳定，与 Thumb 无关），不信 delta。
        var mouse = PointToScreen(Mouse.GetPosition(this));
        var bounds = SystemParametersHelper.VirtualBounds;
        double x = _resizeOrigin.X, y = _resizeOrigin.Y;
        double w = _resizeOrigin.Width, h = _resizeOrigin.Height;

        switch (dir)
        {
            case "SE":
                w = ClampRange(mouse.X - _resizeAnchor.X, BoxResize.MinWidth, bounds.Right - x);
                h = ClampRange(mouse.Y - _resizeAnchor.Y, BoxResize.MinHeight, bounds.Bottom - y);
                break;
            case "S":
                h = ClampRange(mouse.Y - _resizeAnchor.Y, BoxResize.MinHeight, bounds.Bottom - y);
                break;
            case "E":
                w = ClampRange(mouse.X - _resizeAnchor.X, BoxResize.MinWidth, bounds.Right - x);
                break;
            case "SW":
                w = ClampRange(_resizeAnchor.X - mouse.X, BoxResize.MinWidth, _resizeAnchor.X - bounds.Left);
                h = ClampRange(mouse.Y - _resizeAnchor.Y, BoxResize.MinHeight, bounds.Bottom - y);
                x = _resizeAnchor.X - w;
                break;
            case "W":
                w = ClampRange(_resizeAnchor.X - mouse.X, BoxResize.MinWidth, _resizeAnchor.X - bounds.Left);
                x = _resizeAnchor.X - w;
                break;
            case "NW":
                w = ClampRange(_resizeAnchor.X - mouse.X, BoxResize.MinWidth, _resizeAnchor.X - bounds.Left);
                h = ClampRange(_resizeAnchor.Y - mouse.Y, BoxResize.MinHeight, _resizeAnchor.Y - bounds.Top);
                x = _resizeAnchor.X - w;
                y = _resizeAnchor.Y - h;
                break;
            case "N":
                h = ClampRange(_resizeAnchor.Y - mouse.Y, BoxResize.MinHeight, _resizeAnchor.Y - bounds.Top);
                y = _resizeAnchor.Y - h;
                break;
            case "NE":
                w = ClampRange(mouse.X - _resizeAnchor.X, BoxResize.MinWidth, bounds.Right - x);
                h = ClampRange(_resizeAnchor.Y - mouse.Y, BoxResize.MinHeight, _resizeAnchor.Y - bounds.Top);
                y = _resizeAnchor.Y - h;
                break;
        }

        Vm.X = x;
        Vm.Y = y;
        Vm.Width = w;
        Vm.Height = h;
    }

    /// <summary>把值夹在 [min,max] 内；max < min 时取 max（极小屏幕兜底）。</summary>
    private static double ClampRange(double value, double min, double max)
    {
        if (max < min) max = min;
        return Math.Clamp(value, min, max);
    }

    private void OnResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!_isResizing) return;
        _isResizing = false;
        Services.LogService.Info("BoxResize.Done",
            $"dir={_resizeDir} final={Vm.Width:0}x{Vm.Height:0} at ({Vm.X:0},{Vm.Y:0})");
        MainVm.ScheduleSave();
    }

    private (double X, double Y) GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
        var x = transform.M11 == 0 ? 1 : transform.M11;
        var y = transform.M22 == 0 ? 1 : transform.M22;
        return (x, y);
    }

    // ---- 拖放导入(归当前标签 / Items)----
    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ItemDragDrop.DragSourceItemFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (Vm is null || MainVm is null) return;
        if (e.Data.GetDataPresent(ItemDragDrop.DragSourceItemFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        try
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // Outlook/浏览器等虚拟拖放源 GetData(FileDrop) 可能不是 string[]，
                // 裸强转会在 UI 线程直接崩。先 is 校验 + 限批 500（防池饥饿/数千Timer）。
                if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
                {
                    Services.LogService.Warn("Drop.Ignored",
                        $"FileDrop 格式不符，formats=[{string.Join(",", e.Data.GetFormats())}]");
                    e.Handled = true;
                    return;
                }
                const int MaxBatch = 500;
                if (paths.Length > MaxBatch)
                {
                    Services.LogService.Warn("Drop.Truncated", $"批量 {paths.Length} > {MaxBatch}，只导入前 {MaxBatch}");
                    InputDialog.Inform($"一次最多导入 {MaxBatch} 个文件，本次只导入前 {MaxBatch} 个（共 {paths.Length} 个）。");
                    paths = paths.Take(MaxBatch).ToArray();
                }
                foreach (var p in paths)
                {
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    MainVm.AddItemToBox(Vm, p);
                }
            }
            else if (e.Data.GetDataPresent(DataFormats.Text))
            {
                // 大文本粘贴（10MB 日志）会在 UI 线程复制大字符串，先限长。
                var raw = e.Data.GetData(DataFormats.Text) as string;
                var txt = raw is not null && raw.Length > 2048 ? raw[..2048] : raw;
                if (!string.IsNullOrWhiteSpace(txt)) MainVm.AddItemToBox(Vm, txt.Trim());
            }
        }
        catch (Exception ex)
        {
            App.LogError(ex, "BoxControl.OnDrop");
        }
        e.Handled = true;
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e) { /* 预留 */ }

    /// <summary>点击条目列表的空白区域(非磁贴)→ 清除所有选中(资源管理器行为)。
    /// Preview 隧道事件:点磁贴时也会触发,需排除命中 ItemTile 的情况,否则 Ctrl 多选会被先清空。</summary>
    private void OnItemsAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 命中测试:若点中的是 ItemTile(或其子元素),不处理,交给磁贴自己的选中逻辑
        if (e.OriginalSource is DependencyObject d && FindAncestor<ItemTile>(d) is not null) return;
        MainVm.ClearSelection();
    }

    private static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
                yield return typed;
            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    private void OnRename(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var newName = InputDialog.Prompt(Localizer["dialog.renameBox.title"], Localizer["dialog.renameBox.prompt"], Vm.Name);
        if (!string.IsNullOrWhiteSpace(newName)) Vm.Name = newName.Trim();
        MainVm.ScheduleSave();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (Vm is null || Vm.TotalItemCount == 0) return;
        if (InputDialog.Confirm(Localizer["dialog.clearBox.confirm"]))
        {
            Vm.ClearAll();
            MainVm.ScheduleSave();
        }
    }

    /// <summary>批量删除当前所有选中的磁贴(跨盒子)。由右键选中项冒泡到此 ContextMenu 触发。</summary>
    private void OnRemoveSelected(object sender, RoutedEventArgs e)
    {
        var selected = MainVm.GetSelectedItems();
        if (selected.Count == 0) return;
        if (!InputDialog.Confirm(string.Format(Localizer["dialog.removeSelected.confirm"], selected.Count))) return;
        MainVm.RemoveSelected();
    }

    private void OnRefreshItems(object sender, RoutedEventArgs e)
    {
        var removed = MainVm.PruneMissingItems();
        InputDialog.Inform(string.Format(Localizer["dialog.refreshItems.done"], removed));
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        // 非空盒子(含任意标签里的条目)不允许直接删除,避免用户误以为文件丢失
        if (Vm.TotalItemCount > 0)
        {
            InputDialog.Inform(string.Format(Localizer["dialog.deleteBox.nonEmpty"], Vm.Header, Vm.TotalItemCount));
            return;
        }

        if (InputDialog.Confirm(string.Format(Localizer["dialog.deleteBox.empty"], Vm.Header)))
            MainVm.RemoveBoxCommand.Execute(Vm);
    }

    // ---- 标签操作 ----
    private void OnAddTab(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.AddTab("");   // 空=用当前语言的默认标签名(BoxViewModel.AddTab 内部本地化)
        MainVm.ScheduleSave();
    }

    private void OnRenameTab(object sender, RoutedEventArgs e)
    {
        if (Vm is null || Vm.SelectedTab is null) return;
        var name = InputDialog.Prompt(Localizer["box.menu.renameTab"], Localizer["dialog.renameBox.prompt"], Vm.SelectedTab.Name);
        if (!string.IsNullOrWhiteSpace(name)) Vm.SelectedTab.Name = name.Trim();
        MainVm.ScheduleSave();
    }

    private void OnDeleteTab(object sender, RoutedEventArgs e)
    {
        if (Vm is null || Vm.SelectedTab is null) return;
        var tab = Vm.SelectedTab;
        if (tab.Items.Count > 0)
        {
            InputDialog.Inform(string.Format(Localizer["dialog.deleteTab.nonEmpty"], tab.Header, tab.Items.Count));
            return;
        }
        if (Vm.Tabs.Count <= 1)
        {
            InputDialog.Inform(Localizer["dialog.deleteTab.last"]);
            return;
        }
        Vm.Tabs.Remove(tab);   // BoxViewModel.OnTabsChanged 会自动重选
        MainVm.ScheduleSave();
    }

    private void OnConvertToTabbed(object sender, RoutedEventArgs e)
        => MainVm.ConvertToTabbedCommand.Execute(Vm);

    private void OnMergeIn(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var candidates = MainVm.Boxes.Where(b => b.Id != Vm.Id).ToList();
        if (candidates.Count == 0)
        {
            InputDialog.Inform(Localizer["dialog.merge.none"]);
            return;
        }
        var names = candidates
            .Select(b => $"{b.Header}{(b.IsTabbed ? "  (标签盒)" : "")}  ·  {b.TotalItemCount} 项")
            .ToList();
        int idx = InputDialog.SelectIndex(Localizer["dialog.merge.title"], Localizer["dialog.merge.prompt"], names);
        if (idx < 0 || idx >= candidates.Count) return;
        MainVm.MergeIn(Vm, candidates[idx]);
    }

    private void OnSplit(object sender, RoutedEventArgs e)
        => MainVm.SplitBoxCommand.Execute(Vm);

    // ---- 显示/隐藏桌面图标(标题栏快捷按钮,复用全局命令)----
    private void OnToggleDesktopIcons(object sender, RoutedEventArgs e)
        => MainVm.ToggleDesktopIconsCommand.Execute(null);

    // ---- 视图模式切换(全局:所有盒子/标签共享)----
    private void OnViewLarge(object sender, RoutedEventArgs e) => SetView(ViewMode.Large);
    private void OnViewMedium(object sender, RoutedEventArgs e) => SetView(ViewMode.Medium);
    private void OnViewSmall(object sender, RoutedEventArgs e) => SetView(ViewMode.Small);
    private void OnViewList(object sender, RoutedEventArgs e) => SetView(ViewMode.List);
    private void OnViewDetail(object sender, RoutedEventArgs e) => SetView(ViewMode.Detail);
    private void OnViewTile(object sender, RoutedEventArgs e) => SetView(ViewMode.Tile);

    private void SetView(ViewMode mode) => MainVm.SetGlobalViewMode(mode);

    // ---- 标签拖拽排序(单击=切换,按住左键拖动=换位)----
    private Point _tabDragOrigin;
    private object? _tabDragData;

    private void OnTabListMouseDown(object sender, MouseButtonEventArgs e)
    {
        _tabDragOrigin = e.GetPosition(null);
        _tabDragData = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext;
    }

    private void OnTabListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _tabDragData is null) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _tabDragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _tabDragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var data = _tabDragData;
        _tabDragData = null;
        if (data is BoxTab) DragDrop.DoDragDrop(TabList, data, DragDropEffects.Move);
    }

    private void OnTabListDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(BoxTab)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTabListDrop(object sender, DragEventArgs e)
    {
        if (Vm is null || !e.Data.GetDataPresent(typeof(BoxTab))) return;
        var dragged = e.Data.GetData(typeof(BoxTab)) as BoxTab;
        var target = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as BoxTab;
        if (dragged is null || target is null || ReferenceEquals(dragged, target)) return;
        int oldIdx = Vm.Tabs.IndexOf(dragged);
        int newIdx = Vm.Tabs.IndexOf(target);
        if (oldIdx >= 0 && newIdx >= 0) Vm.Tabs.Move(oldIdx, newIdx);
        MainVm.ScheduleSave();
    }

    /// <summary>沿可视树向上找指定类型的祖先。</summary>
    private static T? Ancestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null && d is not T)
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        return d as T;
    }
}
