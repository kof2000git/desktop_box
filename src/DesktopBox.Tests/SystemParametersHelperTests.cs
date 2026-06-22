using System.Windows;
using DesktopBox.Controls;
using FluentAssertions;

namespace DesktopBox.Tests;

public class SystemParametersHelperTests
{
    [Fact]
    public void ClampIntoScreens_ClampsRightOverflowToRightEdgeInsteadOfTopLeft()
    {
        var screen = new Rect(0, 0, 1920, 1080);

        var clamped = SystemParametersHelper.ClampIntoScreens(1950, 10, 120, 40, [screen]);

        clamped.X.Should().Be(1800);
        clamped.Y.Should().Be(10);
    }

    [Fact]
    public void ClampIntoScreens_ClampsTopRightOverflowToNearestCorner()
    {
        var screen = new Rect(0, 0, 1920, 1080);

        var clamped = SystemParametersHelper.ClampIntoScreens(1950, -20, 120, 40, [screen]);

        clamped.X.Should().Be(1800);
        clamped.Y.Should().Be(0);
    }

    [Fact]
    public void ClampIntoScreens_UsesNearestScreenWhenOutsideAllScreens()
    {
        var screens = new[]
        {
            new Rect(0, 0, 1920, 1080),
            new Rect(1920, 0, 1920, 1080)
        };

        var clamped = SystemParametersHelper.ClampIntoScreens(3900, 100, 120, 40, screens);

        clamped.X.Should().Be(3720);
        clamped.Y.Should().Be(100);
    }
}
