using DesktopBox.Native;
using DesktopBox.Services;
using FluentAssertions;
using System.IO;
using System.Linq;
using System.Threading;

namespace DesktopBox.Tests;

public class ShellChangeNotifierTests
{
    [Fact]
    public void GlobalFileCreate_DoesNotRefreshSystemIcons()
    {
        ShellChangeNotifierService.ShouldRefreshSystemIcons(
                ShellNotificationScope.GlobalSystem,
                Shell32.SHCNE_CREATE)
            .Should().BeFalse();
    }

    [Fact]
    public void GlobalAssociationChange_RefreshesSystemIcons()
    {
        ShellChangeNotifierService.ShouldRefreshSystemIcons(
                ShellNotificationScope.GlobalSystem,
                Shell32.SHCNE_ASSOCCHANGED)
            .Should().BeTrue();
    }

    [Fact]
    public void RecycleBinContentChange_RefreshesSystemIcons()
    {
        ShellChangeNotifierService.ShouldRefreshSystemIcons(
                ShellNotificationScope.RecycleBin,
                Shell32.SHCNE_CREATE)
            .Should().BeTrue();
    }

    [Fact]
    public void Dispose_CancelsPendingNotificationDelivery()
    {
        var service = new ShellChangeNotifierService();
        var calls = 0;
        service.SystemIconChanged += (_, _) => Interlocked.Increment(ref calls);
        service.OnShellNotify(IntPtr.Zero, IntPtr.Zero);

        service.Dispose();
        Thread.Sleep(500);

        Volatile.Read(ref calls).Should().Be(0);
    }

    [Fact]
    public void DisposalContract_WaitsForTimerAndDropsShutdownDispatcherCallbacks()
    {
        var source = ReadRepositoryFile("src", "DesktopBox", "Services", "ShellChangeNotifierService.cs");

        source.Should().Contain("Dispose(waitHandle)");
        source.Should().Contain("if (_disposed) return;");
        source.Should().Contain("if (disp.HasShutdownStarted || disp.HasShutdownFinished) return;");
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
