using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DesktopBox.Services;

/// <summary>
/// WPF-UI hover 动画崩溃防护（v1.7.10 线上崩溃根因修复）。
/// WPF-UI 的 Button/ComboBox/ListBoxItem/TextBox/MenuItem/ToolTip 等模板里，
/// hover 覆盖层 Border 的 Background 直接指向主题字典里被 Freeze 的共享画刷，
/// IsMouseOver 触发器对其跑 Opacity 动画 → InvalidOperationException
/// （"无法对不可变对象实例上的 (0).(1) 进行动画处理"）→ 致命崩溃 → 进程死 →
/// 所有盒子窗口全灭。主题子字典是密封的，无法原地解冻，改为实例级修复：
/// 控件 Loaded（模板已实例化）后，把子树里 frozen 的画刷逐个换成未冻结克隆
/// （颜色一致，只换实例），动画即可正常播放。
/// 注意：克隆是快照，运行时切换系统主题后已创建控件的 WPF-UI 镀层颜色需重启
/// 才跟随新主题（不崩溃，只是不跟色，可接受；App 自己的配色本就是硬编码深色）。
/// </summary>
public static class WpfUiAnimationGuard
{
    private static readonly DependencyProperty[] BrushProperties =
    {
        Control.BackgroundProperty, Control.BorderBrushProperty, Control.ForegroundProperty,
        Border.BackgroundProperty, Border.BorderBrushProperty,
        Panel.BackgroundProperty,
        TextBlock.BackgroundProperty, TextBlock.ForegroundProperty,
        Shape.FillProperty, Shape.StrokeProperty,
    };

    private static int _registered;

    /// <summary>注册全局弹窗钩（ToolTip/ContextMenu 与主树分离，需在 Opened 时修）。
    /// 在 App.OnStartup 调用一次，多次调用安全。</summary>
    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;
        EventManager.RegisterClassHandler(
            typeof(ToolTip), ToolTip.OpenedEvent,
            new RoutedEventHandler(OnPopupOpened));
        EventManager.RegisterClassHandler(
            typeof(ContextMenu), ContextMenu.OpenedEvent,
            new RoutedEventHandler(OnPopupOpened));
    }

    private static void OnPopupOpened(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is DependencyObject d) UnfreezeSubtree(d);
        }
        catch { }
    }

    /// <summary>解冻子树里所有 frozen 画刷（模板覆盖层等），返回替换个数。线程：UI 线程。</summary>
    public static int UnfreezeSubtree(DependencyObject? root)
    {
        if (root is null) return 0;
        var count = 0;
        var q = new Queue<DependencyObject>();
        q.Enqueue(root);
        while (q.Count > 0)
        {
            var d = q.Dequeue();
            try
            {
                if (d is FrameworkElement fe) fe.ApplyTemplate();
                foreach (var dp in BrushProperties)
                {
                    Freezable? v;
                    try { v = d.GetValue(dp) as Freezable; }
                    catch { continue; }
                    if (v is null || !v.IsFrozen) continue;
                    try
                    {
                        var clone = v.Clone();
                        if (clone.IsFrozen) continue;
                        d.SetValue(dp, clone);
                        count++;
                    }
                    catch { }
                }
            }
            catch { }
            int n;
            try { n = VisualTreeHelper.GetChildrenCount(d); }
            catch { continue; }
            for (int i = 0; i < n; i++)
            {
                try { q.Enqueue(VisualTreeHelper.GetChild(d, i)); }
                catch { }
            }
        }
        return count;
    }
}
