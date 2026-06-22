using System.Windows;
using DesktopBox.Controls;
using FluentAssertions;

namespace DesktopBox.Tests;

public class EdgeMagnetTests
{
    private static readonly Rect[] Screen = [new Rect(0, 0, 1920, 1080)];

    [Fact]
    public void HorizontalSnap_ReleasesImmediatelyWhenDraggedBackInsideFromLeftEdge()
    {
        var state = new BoxEdgeSnapState();

        EdgeMagnet.ApplyHorizontal(state, 6, 240, Screen).Should().Be(EdgeMagnet.Margin);
        state.Snapped.Should().BeTrue();

        EdgeMagnet.ApplyHorizontal(state, 19, 240, Screen).Should().Be(19);
        state.Snapped.Should().BeFalse();
    }

    [Fact]
    public void HorizontalSnap_ReleasesImmediatelyWhenDraggedBackInsideFromRightEdge()
    {
        var state = new BoxEdgeSnapState();
        const double width = 240;
        var rightEdge = Screen[0].Right - width - EdgeMagnet.Margin;

        EdgeMagnet.ApplyHorizontal(state, rightEdge - 6, width, Screen).Should().Be(rightEdge);
        state.Snapped.Should().BeTrue();

        EdgeMagnet.ApplyHorizontal(state, rightEdge - 15, width, Screen).Should().Be(rightEdge - 15);
        state.Snapped.Should().BeFalse();
    }

    [Fact]
    public void VerticalSnap_ReleasesImmediatelyWhenDraggedBackInsideFromTopEdge()
    {
        var state = new BoxEdgeSnapState();

        EdgeMagnet.ApplyVertical(state, 8, 200, Screen).Should().Be(EdgeMagnet.Margin);
        state.Snapped.Should().BeTrue();

        EdgeMagnet.ApplyVertical(state, 17, 200, Screen).Should().Be(17);
        state.Snapped.Should().BeFalse();
    }
}
