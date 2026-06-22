using DesktopBox.Controls;
using DesktopBox.Models;
using FluentAssertions;

namespace DesktopBox.Tests;

public class FirstLetterNavigatorTests
{
    [Fact]
    public void FindNextIndex_MatchesDisplayNameFirstLetterCaseInsensitively()
    {
        var items = Items("Alpha", "beta", "Gamma");

        FirstLetterNavigator.FindNextIndex(items, 'B', -1).Should().Be(1);
        FirstLetterNavigator.FindNextIndex(items, 'g', -1).Should().Be(2);
    }

    [Fact]
    public void FindNextIndex_RepeatedKeyCyclesPastCurrentMatch()
    {
        var items = Items("Alpha", "Archive", "Beta", "App");

        FirstLetterNavigator.FindNextIndex(items, 'A', -1).Should().Be(0);
        FirstLetterNavigator.FindNextIndex(items, 'A', 0).Should().Be(1);
        FirstLetterNavigator.FindNextIndex(items, 'A', 1).Should().Be(3);
        FirstLetterNavigator.FindNextIndex(items, 'A', 3).Should().Be(0);
    }

    [Fact]
    public void FindNextIndex_IgnoresEmptyNamesAndUnsupportedKeys()
    {
        var items = Items("", "  ", "1Password", "Alpha");

        FirstLetterNavigator.FindNextIndex(items, '1', -1).Should().Be(2);
        FirstLetterNavigator.FindNextIndex(items, '/', -1).Should().Be(-1);
        FirstLetterNavigator.FindNextIndex(Array.Empty<BoxItem>(), 'A', -1).Should().Be(-1);
    }

    private static BoxItem[] Items(params string[] names) =>
        names.Select(name => new BoxItem { DisplayName = name }).ToArray();
}
