using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DesktopBox.ViewModels;
using DesktopBox.Views;

namespace DesktopBox.Controls;

public partial class BoxControl : UserControl
{
    private Point _dragOrigin;
    private bool _isDragging;
    private MainViewModel? _mainVm;

    private MainViewModel MainVm =>
        _mainVm ??= App.Services.GetService(typeof(MainViewModel)) as MainViewModel;

    public BoxControl()
    {
        InitializeComponent();
    }

    private BoxViewModel? Vm => DataContext as BoxViewModel;

    // ---- 拖动盒子 ----
    private void OnHeaderDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2 || Vm is null) return;
        _isDragging = true;
        _dragOrigin = e.GetPosition(null);
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }

    private void OnHeaderMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || Vm is null) return;
        var pos = e.GetPosition(null);
        Vm.X += pos.X - _dragOrigin.X;
        Vm.Y += pos.Y - _dragOrigin.Y;
        _dragOrigin = pos;
    }

    private void OnHeaderUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        Mouse.Capture(null);
        MainVm?.ScheduleSave();
    }

    // ---- 缩放 ----
    private void OnResize(object sender, DragDeltaEventArgs e)
    {
        if (Vm is null) return;
        Vm.Width = Math.Max(140, Vm.Width + e.HorizontalChange);
        Vm.Height = Math.Max(100, Vm.Height + e.VerticalChange);
        MainVm?.ScheduleSave();
    }

    // ---- 拖放导入 ----
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
        var newName = InputDialog.Prompt("重命名盒子", "请输入新的盒子名称：", Vm.Name);
        if (!string.IsNullOrWhiteSpace(newName)) Vm.Name = newName.Trim();
        MainVm?.ScheduleSave();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (Vm is null || Vm.Items.Count == 0) return;
        if (InputDialog.Confirm("确定清空此盒子的所有条目吗?"))
        {
            Vm.Items.Clear();
            MainVm?.ScheduleSave();
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (InputDialog.Confirm($"确定删除盒子「{Vm.Name}」吗?"))
            MainVm.RemoveBoxCommand.Execute(Vm);
    }
}
