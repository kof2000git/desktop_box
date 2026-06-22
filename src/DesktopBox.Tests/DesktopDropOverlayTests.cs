using System.Windows;
using DesktopBox.Controls;
using FluentAssertions;

namespace DesktopBox.Tests;

public class DesktopDropOverlayTests
{
    [Fact]
    public void BuildExclusionRects_InflatesAndClipsBoxBoundsInsideOverlay()
    {
        var overlay = new Rect(0, 0, 500, 400);
        var excluded = new[]
        {
            new Rect(100, 80, 240, 180),
            new Rect(-20, 10, 30, 40),
            new Rect(600, 600, 20, 20)
        };

        var rects = DesktopDropOverlay.BuildExclusionRects(overlay, excluded, padding: 8);

        rects.Should().HaveCount(2);
        rects[0].X.Should().Be(92);
        rects[0].Y.Should().Be(72);
        rects[0].Width.Should().Be(256);
        rects[0].Height.Should().Be(196);
        rects[1].X.Should().Be(0);
        rects[1].Y.Should().Be(2);
        rects[1].Width.Should().Be(18);
        rects[1].Height.Should().Be(56);
    }

    [Fact]
    public void BuildExclusionRects_IgnoresEmptyAndOffscreenBounds()
    {
        var rects = DesktopDropOverlay.BuildExclusionRects(
            new Rect(0, 0, 300, 200),
            new[]
            {
                new Rect(10, 10, 0, 20),
                new Rect(500, 10, 50, 50)
            });

        rects.Should().BeEmpty();
    }
}
