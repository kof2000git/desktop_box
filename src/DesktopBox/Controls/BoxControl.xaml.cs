using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
    private bool _snappedX, _snappedY;   // 磁吸迟滞状态:当前是否已吸附在某条垂直/水平边
    private MainViewModel? _mainVm;
    private ILocalizerService? _localizer;

    private MainViewModel MainVm =>
        _mainVm ??= App.Services.GetRequiredService<MainViewModel>();

    private ILocalizerService Localizer =>
        _localizer ??= App.Services.GetRequiredService<ILocalizerService>();

    public BoxControl()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (Vm is { } vm) vm.ViewModeChanged += OnViewModeChanged;
        };
    }

    private BoxViewModel? Vm => DataContext as BoxViewModel;

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
        _dragOrigin = e.GetPosition(null);
        _boxOrigin = new Point(Vm.X, Vm.Y);   // 记录起点:拖动期间意图位置=起点+累积位移
        _snappedX = _snappedY = false;        // 新一次拖动:重置磁吸迟滞状态
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }

    private void OnHeaderMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || Vm is null) return;
        var pos = e.GetPosition(null);
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
        const double snap = 12;     // 吸附阈值:靠近此距离时贴合
        const double release = 40;  // 脱离阈值:已吸附时需远离此距离才脱开(迟滞量)
        const double margin = 4;    // 贴合后离边的余量

        double threshX = _snappedX ? release : snap;
        _snappedX = false;
        foreach (var s in SystemParametersHelper.AllScreens)
        {
            double left = s.Left + margin;
            double right = s.Right - vm.Width - margin;
            if (Math.Abs(x - left) <= threshX) { x = left; _snappedX = true; break; }
            if (Math.Abs(x - right) <= threshX) { x = right; _snappedX = true; break; }
        }
        double threshY = _snappedY ? release : snap;
        _snappedY = false;
        foreach (var s in SystemParametersHelper.AllScreens)
        {
            double top = s.Top + margin;
            double bottom = s.Bottom - vm.Height - margin;
            if (Math.Abs(y - top) <= threshY) { y = top; _snappedY = true; break; }
            if (Math.Abs(y - bottom) <= threshY) { y = bottom; _snappedY = true; break; }
        }
    }

    private void OnHeaderUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        Mouse.Capture(null);
        MainVm.ScheduleSave();
    }

    // ---- 缩放 ----
    private void OnResize(object sender, DragDeltaEventArgs e)
    {
        if (Vm is null) return;
        var dir = (sender as FrameworkElement)?.Tag as string ?? "SE";
        double dx = e.HorizontalChange, dy = e.VerticalChange;
        const double minW = 140, minH = 120;

        if (dir.Contains('E')) Vm.Width = Math.Max(minW, Vm.Width + dx);
        if (dir.Contains('S')) Vm.Height = Math.Max(minH, Vm.Height + dy);
        if (dir.Contains('W'))
        {
            double newW = Math.Max(minW, Vm.Width - dx);
            Vm.X += Vm.Width - newW;
            Vm.Width = newW;
        }
        if (dir.Contains('N'))
        {
            double newH = Math.Max(minH, Vm.Height - dy);
            Vm.Y += Vm.Height - newH;
            Vm.Height = newH;
        }
        MainVm.ScheduleSave();
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
