using DesktopBox.Native;
using DesktopBox.Services;
using FluentAssertions;

namespace DesktopBox.Tests;

public class ShellBehaviorTests
{
    [Fact]
    public async Task StaTaskRunner_DoesNotBlockCallerWhileWorkRuns()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var task = StaTaskRunner.Run(() =>
        {
            started.Set();
            release.Wait();
            return Thread.CurrentThread.GetApartmentState();
        });

        started.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        task.IsCompleted.Should().BeFalse();

        release.Set();
        var apartment = await task;
        apartment.Should().Be(ApartmentState.STA);
    }

    [Theory]
    [InlineData(Shell32.SHCNE_UPDATEIMAGE)]
    [InlineData(Shell32.SHCNE_ASSOCCHANGED)]
    [InlineData(Shell32.SHCNE_DELETE)]
    [InlineData(Shell32.SHCNE_UPDATEDIR)]
    [InlineData(Shell32.SHCNE_UPDATEITEM)]
    public void ShellNotificationsThatCanChangeRecycleBin_RefreshSystemIcons(uint evt)
    {
        ShellChangeNotifierService.ShouldRefreshSystemIcons(evt).Should().BeTrue();
    }
}
