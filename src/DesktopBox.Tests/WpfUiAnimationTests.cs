using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DesktopBox.Tests;

/// <summary>
/// 回归测试：WPF-UI hover 动画打 frozen 主题画刷导致致命崩溃（v1.7.10 线上事故：
/// "无法对不可变对象实例上的 (0).(1) 进行动画处理" → InvalidOperationException →
/// 进程死 → 所有盒子窗口全灭）。修复 = 实例级解冻（WpfUiAnimationGuard）。
/// </summary>
public class WpfUiAnimationTests
{
    [Fact]
    public void StoryboardOnFrozenBrush_ThrowsProductionCrash()
    {
        RunOnSta(() =>
        {
            // 机制确认：Storyboard 对 frozen 画刷做动画 = 线上崩溃的精确复现。
            var frozen = new SolidColorBrush(Colors.White);
            frozen.Freeze();
            var sb = new Storyboard();
            var anim = new DoubleAnimation(1.0, 0.5, TimeSpan.FromMilliseconds(10));
            Storyboard.SetTarget(anim, frozen);
            Storyboard.SetTargetProperty(anim, new PropertyPath(SolidColorBrush.OpacityProperty));
            sb.Children.Add(anim);

            var ex = Assert.Throws<InvalidOperationException>(() => sb.Begin(new Border(), true));
            Assert.Equal(unchecked((int)0x80131509), ex.HResult);
        });
    }

    [Fact]
    public void UnfreezeSubtree_MakesWpfUiButtonHoverSafe()
    {
        RunOnSta(() =>
        {
            var host = new StackPanel();
            host.Resources.MergedDictionaries.Add(LoadWpfUiDictionaries());
            var btn = new Button { Content = "ok", Width = 80, Height = 30 };
            host.Children.Add(btn);
            btn.ApplyTemplate();

            var border = FindBorders(btn).FirstOrDefault(b => b.Name == "ContentBorder");
            Assert.NotNull(border);
            var before = Assert.IsType<SolidColorBrush>(border!.Background);
            Assert.True(before.IsFrozen); // 地雷：WPF-UI 模板 hover 覆盖层画刷是 frozen 的

            var n = DesktopBox.Services.WpfUiAnimationGuard.UnfreezeSubtree(btn);
            Assert.True(n > 0);

            var after = Assert.IsType<SolidColorBrush>(border.Background);
            Assert.False(after.IsFrozen);
            Assert.NotSame(before, after);
            Assert.Equal(before.Color, after.Color);

            // 解冻后 hover 动画（Opacity）可正常 Begin，不再崩。
            var sb = new Storyboard();
            var anim = new DoubleAnimation(1.0, 0.5, TimeSpan.FromMilliseconds(10));
            Storyboard.SetTarget(anim, after);
            Storyboard.SetTargetProperty(anim, new PropertyPath(SolidColorBrush.OpacityProperty));
            sb.Children.Add(anim);
            var ex = Record.Exception(() => sb.Begin(new Border(), true));
            Assert.Null(ex);
        });
    }

    private static IEnumerable<Border> FindBorders(DependencyObject root)
    {
        var q = new Queue<DependencyObject>();
        q.Enqueue(root);
        while (q.Count > 0)
        {
            var d = q.Dequeue();
            if (d is Border b) yield return b;
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++) q.Enqueue(VisualTreeHelper.GetChild(d, i));
        }
    }

    private static ResourceDictionary LoadWpfUiDictionaries()
    {
        // 纯代码构造（松散 XAML 的 xmlns 映射时好时坏，不可靠）。
        var root = new ResourceDictionary();
        root.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary
        {
            Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark
        });
        root.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        return root;
    }

    private static void RunOnSta(Action action)
    {
        Exception? ex = null;
        var t = new Thread(() => { try { action(); } catch (Exception e) { ex = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (ex is not null) ExceptionDispatchInfo.Throw(ex);
    }
}
