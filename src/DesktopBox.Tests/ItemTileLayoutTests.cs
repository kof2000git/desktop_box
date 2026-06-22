using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopBox.Controls;
using DesktopBox.Models;
using DesktopBox.ViewModels;
using FluentAssertions;

namespace DesktopBox.Tests;

public class ItemTileLayoutTests
{
    [Fact]
    public void SelectingTile_DoesNotMoveContentSurface()
    {
        RunOnSta(() =>
        {
            foreach (var size in Enum.GetValues<TileSize>())
            {
                var item = new BoxItem
                {
                    DisplayName = "A_B",
                    TargetPath = @"C:\Temp\A_B.txt",
                    Type = ItemType.File
                };
                var tile = ArrangeTile(size, item);
                var layoutGrid = (FrameworkElement)tile.FindName("LayoutGrid");

                var before = OriginOf(layoutGrid, tile);
                item.IsSelected = true;
                Arrange(tile);
                var after = OriginOf(layoutGrid, tile);

                after.X.Should().BeApproximately(before.X, 0.01, $"{size} selection must not shift content horizontally");
                after.Y.Should().BeApproximately(before.Y, 0.01, $"{size} selection must not shift content vertically");
            }
        });
    }

    [Fact]
    public void NameText_ReservesBottomRoomForUnderscores()
    {
        RunOnSta(() =>
        {
            foreach (var size in Enum.GetValues<TileSize>())
            {
                var item = new BoxItem
                {
                    DisplayName = "A_B",
                    TargetPath = @"C:\Temp\A_B.txt",
                    Type = ItemType.File
                };
                var tile = ArrangeTile(size, item);
                var nameText = (System.Windows.Controls.TextBlock)tile.FindName("NameText");

                nameText.LineStackingStrategy.Should().Be(LineStackingStrategy.BlockLineHeight);
                nameText.LineHeight.Should().BeGreaterThan(nameText.FontSize);
                nameText.Padding.Bottom.Should().BeGreaterThan(0);
            }
        });
    }

    [Fact]
    public void BoxControl_ResizeThumbsRemainAboveVisualGrip()
    {
        RunOnSta(() =>
        {
            var box = new BoxViewModel(new Box { Name = "B", Width = 240, Height = 200 });
            var control = new BoxControl { DataContext = box, Width = box.Width, Height = box.Height };
            Arrange(control);

            var thumbs = FindVisualChildren<System.Windows.Controls.Primitives.Thumb>(control).ToList();
            var se = thumbs.Single(t => (string?)t.Tag == "SE");

            se.ActualWidth.Should().BeApproximately(28, 0.01);
            se.ActualHeight.Should().BeApproximately(28, 0.01);
            se.IsHitTestVisible.Should().BeTrue();
            thumbs.Select(t => (string?)t.Tag)
                .Should().Contain(new[] { "N", "S", "W", "E", "NW", "NE", "SW", "SE" });
        });
    }

    [Fact]
    public void BoxControl_ToggleIconsButtonIsNotCoveredByNorthEastResizeThumb()
    {
        RunOnSta(() =>
        {
            var box = new BoxViewModel(new Box { Name = "B", Width = 260, Height = 200 });
            var control = new BoxControl { DataContext = box, Width = box.Width, Height = box.Height };
            Arrange(control);

            var button = ((Button)control.FindName("ToggleIconsBtn"));
            var buttonCenter = button.TranslatePoint(
                new Point(button.ActualWidth / 2, button.ActualHeight / 2),
                control);
            var hit = VisualTreeHelper.HitTest(control, buttonCenter)?.VisualHit;

            FindAncestor<Button>(hit).Should().BeSameAs(button);
            FindAncestor<Thumb>(hit).Should().BeNull();
        });
    }

    private static ItemTile ArrangeTile(TileSize size, BoxItem item)
    {
        var tile = new ItemTile
        {
            IconSize = size,
            DataContext = item
        };
        Arrange(tile);
        return tile;
    }

    private static void Arrange(FrameworkElement element)
    {
        var available = new Size(260, 140);
        element.Measure(available);
        element.Arrange(new Rect(available));
        element.UpdateLayout();
    }

    private static Point OriginOf(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).Transform(new Point(0, 0));

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
                yield return typed;
            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? root) where T : DependencyObject
    {
        while (root is not null)
        {
            if (root is T typed)
                return typed;
            root = VisualTreeHelper.GetParent(root);
        }

        return null;
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        exception?.Throw();
    }
}
