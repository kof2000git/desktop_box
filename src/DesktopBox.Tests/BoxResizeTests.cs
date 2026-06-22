using System.Windows;
using DesktopBox.Controls;
using FluentAssertions;

namespace DesktopBox.Tests;

public class BoxResizeTests
{
    private static readonly Rect Origin = new(300, 200, 240, 180);

    [Theory]
    [InlineData("E", 40, 0, 300, 200, 280, 180)]
    [InlineData("S", 0, 40, 300, 200, 240, 220)]
    [InlineData("SE", 40, 40, 300, 200, 280, 220)]
    [InlineData("W", -40, 0, 260, 200, 280, 180)]
    [InlineData("N", 0, -40, 300, 160, 240, 220)]
    [InlineData("NW", -40, -40, 260, 160, 280, 220)]
    public void Apply_ResizesExpectedEdgesOnly(string direction, double dx, double dy, double x, double y, double width, double height)
    {
        var resized = BoxResize.Apply(Origin, direction, dx, dy);

        resized.X.Should().Be(x);
        resized.Y.Should().Be(y);
        resized.Width.Should().Be(width);
        resized.Height.Should().Be(height);
    }

    [Fact]
    public void Apply_NorthEastResizeDoesNotMoveLeftEdge()
    {
        var resized = BoxResize.Apply(Origin, "NE", 80, -50);

        resized.X.Should().Be(Origin.X);
        resized.Y.Should().Be(150);
        resized.Width.Should().Be(320);
        resized.Height.Should().Be(230);
    }

    [Fact]
    public void Apply_NorthEastShrinkClampsHeightWithoutMovingAcrossBottom()
    {
        var resized = BoxResize.Apply(Origin, "NE", 30, 500);

        resized.X.Should().Be(Origin.X);
        resized.Y.Should().Be(Origin.Bottom - BoxResize.MinHeight);
        resized.Width.Should().Be(270);
        resized.Height.Should().Be(BoxResize.MinHeight);
    }

    [Fact]
    public void Apply_AccumulatedNorthEastDeltasKeepLeftEdgeStable()
    {
        var dx = 0.0;
        var dy = 0.0;
        Rect resized = Origin;

        foreach (var (stepX, stepY) in new[] { (12.0, -8.0), (18.0, -14.0), (25.0, -20.0) })
        {
            dx += stepX;
            dy += stepY;
            resized = BoxResize.Apply(Origin, "NE", dx, dy);
        }

        resized.X.Should().Be(Origin.X);
        resized.Width.Should().Be(Origin.Width + dx);
        resized.Y.Should().Be(Origin.Y + dy);
        resized.Height.Should().Be(Origin.Height - dy);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("None")]
    [InlineData("CENTER")]
    public void Apply_EmptyDirectionLeavesRectUnchanged(string direction)
    {
        BoxResize.Apply(Origin, direction, 50, 50).Should().Be(Origin);
    }
}
