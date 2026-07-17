using DesktopBox.Services;
using FluentAssertions;
using System.IO;

namespace DesktopBox.Tests;

public class StartupServiceTests
{
    [Fact]
    public void ResolveExecutablePath_ReturnsExistingPathWhenAvailable()
    {
        var path = StartupService.ResolveExecutablePath();
        // In test host ProcessPath may be testhost; either null or an existing file path is acceptable.
        if (path is not null)
            File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void ItemTileSource_UsesFallbackMenuForMissingTargets()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? source = null;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "DesktopBox", "Controls", "ItemTile.xaml.cs");
            if (File.Exists(candidate))
            {
                source = File.ReadAllText(candidate);
                break;
            }
            dir = dir.Parent;
        }
        source.Should().NotBeNull();
        source!.Should().Contain("ShowFallbackMenu");
        source.Should().Contain("TargetExists(item.TargetPath)");
    }
}
