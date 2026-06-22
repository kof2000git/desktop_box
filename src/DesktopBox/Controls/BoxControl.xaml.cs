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
    private Rect _resizeBoxOrigin;
    private double _resizeDx;
    private double _resizeDy;
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
        // 越界约束:保证标题栏区域留在某屏内,防止拖到屏外找不到
        var (cx, cy) = SystemParametersHelper.ClampIntoScreens(x, y);
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
    private void OnResizeStarted(object sender, DragStartedEventArgs e)
    {
        if (Vm is null) return;
        _isResizing = true;
        _resizeBoxOrigin = new Rect(Vm.X, Vm.Y, Vm.Width, Vm.Height);
        _resizeDx = 0;
        _resizeDy = 0;
    }

    private void OnResize(object sender, DragDeltaEventArgs e)
    {
        if (!_isResizing || Vm is null) return;
        var dir = (sender as FrameworkElement)?.Tag as string ?? "SE";
        var scale = GetDpiScale();
        _resizeDx += e.HorizontalChange * scale.X;
        _resizeDy += e.VerticalChange * scale.Y;

        var resized = BoxResize.Apply(_resizeBoxOrigin, dir, _resizeDx, _resizeDy);
        Vm.X = resized.X;
        Vm.Y = resized.Y;
        Vm.Width = resized.Width;
        Vm.Height = resized.Height;
    }

    private void OnResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!_isResizing) return;
        _isResizing = false;
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
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (Vm is null || MainVm is null) return;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var p in paths) MainVm.AddItemToBox(Vm, p);
        }
        else if (e.Data.GetDataPresent(DataFormats.Text))
        {
            var txt = (string)e.Data.GetData(DataFormats.Text);
            if (!string.IsNullOrWhiteSpace(txt)) MainVm.AddItemToBox(Vm, txt.Trim());
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
