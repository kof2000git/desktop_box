using FluentAssertions;
using System.IO;
using System.Linq;

namespace DesktopBox.Tests;

public class ItemAvailabilityTests
{
    [Fact]
    public void MissingTargetNotification_DoesNotRemoveReference()
    {
        var source = ReadRepositoryFile("src", "DesktopBox", "Controls", "ItemTile.xaml.cs");
        var start = source.IndexOf("private void NotifyTargetUnavailable()", StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0);
        var end = source.IndexOf("private ContextMenu BuildContextMenu()", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        var body = source[start..end];
        body.Should().Contain("InputDialog.Inform");
        body.Should().NotContain("RemoveFromBox();");
        source.Should().NotContain("NotifyGoneAndRemove");
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
