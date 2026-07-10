using DesktopBox.Models;
using DesktopBox.Services;
using DesktopBox.ViewModels;
using FluentAssertions;
using Microsoft.Win32;
using Moq;
using System.IO;

namespace DesktopBox.Tests;

public class ThemeServiceTests
{
    [Fact]
    public void SystemPreferenceChange_IsAppliedThroughDispatcher_WithoutDuplicateSubscription()
    {
        var dispatcher = new DeferredThemeDispatcher();
        var systemEvents = new FakeSystemThemeEvents();
        using var service = new TestThemeService(dispatcher, systemEvents);

        service.ApplySystem();
        service.ApplySystem();
        systemEvents.Raise(UserPreferenceCategory.General);

        systemEvents.SubscriptionCount.Should().Be(1);
        dispatcher.PendingCount.Should().Be(1);
        service.SystemThemeApplyCount.Should().Be(2);

        dispatcher.Drain();

        service.SystemThemeApplyCount.Should().Be(3);
    }

    [Fact]
    public void StopFollowingSystem_UnsubscribesAndDropsQueuedThemeChange()
    {
        var dispatcher = new DeferredThemeDispatcher();
        var systemEvents = new FakeSystemThemeEvents();
        using var service = new TestThemeService(dispatcher, systemEvents);
        service.ApplySystem();
        systemEvents.Raise(UserPreferenceCategory.General);

        service.StopFollowingSystem();
        dispatcher.Drain();
        systemEvents.Raise(UserPreferenceCategory.General);

        systemEvents.SubscriptionCount.Should().Be(0);
        dispatcher.PendingCount.Should().Be(0);
        service.SystemThemeApplyCount.Should().Be(1);
    }

    [Fact]
    public void Dispose_UnsubscribesOnceAndIgnoresFurtherCalls()
    {
        var dispatcher = new DeferredThemeDispatcher();
        var systemEvents = new FakeSystemThemeEvents();
        var service = new TestThemeService(dispatcher, systemEvents);
        service.ApplySystem();

        service.Dispose();
        service.Dispose();
        systemEvents.Raise(UserPreferenceCategory.General);

        systemEvents.SubscriptionCount.Should().Be(0);
        systemEvents.UnsubscribeCallCount.Should().Be(1);
        dispatcher.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task StopFollowingSystem_WaitsForInFlightThemeChange()
    {
        var systemEvents = new FakeSystemThemeEvents();
        using var service = new BlockingThemeService(new ImmediateThemeDispatcher(), systemEvents);
        service.ApplySystem();
        var raiseTask = Task.Run(() => systemEvents.Raise(UserPreferenceCategory.General));
        service.ThemeChangeStarted.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        using var stopStarted = new ManualResetEventSlim();

        var stopTask = Task.Run(() =>
        {
            stopStarted.Set();
            service.StopFollowingSystem();
        });
        stopStarted.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();

        try
        {
            var completedTask = await Task.WhenAny(stopTask, Task.Delay(100));
            completedTask.Should().NotBeSameAs(stopTask);
        }
        finally
        {
            service.ReleaseThemeChange.Set();
        }

        await Task.WhenAll(raiseTask, stopTask);

        systemEvents.SubscriptionCount.Should().Be(0);
    }

    [Fact]
    public void DisablingFollowSystemTheme_StopsFollowingBeforeApplyingSelectedTheme()
    {
        var startup = new Mock<IStartupService>();
        var theme = new Mock<IThemeService>();
        var store = new Mock<IPersistenceService>();
        var localizer = new Mock<ILocalizerService>();
        var calls = new List<ThemeCall>();
        store.Setup(s => s.Load()).Returns(new AppConfig
        {
            Settings = new AppSettings { FollowSystemTheme = true }
        });
        theme.Setup(t => t.IsSystemDark()).Returns(true);
        theme.Setup(t => t.StopFollowingSystem()).Callback(() => calls.Add(ThemeCall.StopFollowing));
        theme.Setup(t => t.Apply(It.IsAny<string>())).Callback(() => calls.Add(ThemeCall.ApplySelected));
        var viewModel = new SettingsViewModel(startup.Object, theme.Object, store.Object, localizer.Object);

        viewModel.FollowSystemTheme = false;

        calls.Should().Equal(ThemeCall.StopFollowing, ThemeCall.ApplySelected);
    }

    [Fact]
    public void LanguageSaveFailure_RollsBackSelectionAndRaisesPersistenceFailure()
    {
        var startup = new Mock<IStartupService>();
        var theme = new Mock<IThemeService>();
        var store = new Mock<IPersistenceService>();
        var localizer = new Mock<ILocalizerService>();
        store.Setup(s => s.Load()).Returns(new AppConfig
        {
            Settings = new AppSettings { Language = "zh-CN" }
        });
        store.Setup(s => s.Save(It.IsAny<AppConfig>())).Throws(new IOException("disk full"));
        var viewModel = new SettingsViewModel(startup.Object, theme.Object, store.Object, localizer.Object);
        Exception? reported = null;
        viewModel.PersistenceFailed += (_, error) => reported = error;

        viewModel.Language = "en-US";

        viewModel.Language.Should().Be("zh-CN");
        reported.Should().BeOfType<IOException>();
        localizer.Verify(l => l.Apply("en-US"), Times.Never);
    }

    [Fact]
    public void ThemeSaveFailure_RollsBackSelectionAndRaisesPersistenceFailure()
    {
        var startup = new Mock<IStartupService>();
        var theme = new Mock<IThemeService>();
        var store = new Mock<IPersistenceService>();
        var localizer = new Mock<ILocalizerService>();
        store.Setup(s => s.Load()).Returns(new AppConfig
        {
            Settings = new AppSettings { FollowSystemTheme = true, Theme = "Dark" }
        });
        theme.Setup(t => t.IsSystemDark()).Returns(true);
        store.Setup(s => s.Save(It.IsAny<AppConfig>())).Throws(new IOException("disk full"));
        var viewModel = new SettingsViewModel(startup.Object, theme.Object, store.Object, localizer.Object);
        Exception? reported = null;
        viewModel.PersistenceFailed += (_, error) => reported = error;

        viewModel.FollowSystemTheme = false;

        viewModel.FollowSystemTheme.Should().BeTrue();
        reported.Should().BeOfType<IOException>();
        theme.Verify(t => t.StopFollowingSystem(), Times.Never);
        theme.Verify(t => t.Apply(It.IsAny<string>()), Times.Never);
    }

    private enum ThemeCall
    {
        StopFollowing,
        ApplySelected
    }

    private sealed class TestThemeService : ThemeService
    {
        public TestThemeService(IThemeDispatcher dispatcher, ISystemThemeEvents systemEvents)
            : base(dispatcher, systemEvents)
        {
        }

        public int SystemThemeApplyCount { get; private set; }

        protected override void ApplyCurrentSystemTheme() => SystemThemeApplyCount++;
    }

    private sealed class BlockingThemeService : ThemeService
    {
        private int _applyCount;

        public BlockingThemeService(IThemeDispatcher dispatcher, ISystemThemeEvents systemEvents)
            : base(dispatcher, systemEvents)
        {
        }

        public ManualResetEventSlim ThemeChangeStarted { get; } = new();
        public ManualResetEventSlim ReleaseThemeChange { get; } = new();

        protected override void ApplyCurrentSystemTheme()
        {
            if (Interlocked.Increment(ref _applyCount) == 1)
                return;

            ThemeChangeStarted.Set();
            ReleaseThemeChange.Wait(TimeSpan.FromSeconds(2));
        }
    }

    private sealed class ImmediateThemeDispatcher : IThemeDispatcher
    {
        public void Invoke(Action action) => action();
    }

    private sealed class DeferredThemeDispatcher : IThemeDispatcher
    {
        private readonly Queue<Action> _pending = new();

        public int PendingCount => _pending.Count;

        public void Invoke(Action action) => _pending.Enqueue(action);

        public void Drain()
        {
            while (_pending.TryDequeue(out var action))
                action();
        }
    }

    private sealed class FakeSystemThemeEvents : ISystemThemeEvents
    {
        private readonly List<UserPreferenceChangedEventHandler> _handlers = new();

        public int SubscriptionCount => _handlers.Count;
        public int UnsubscribeCallCount { get; private set; }

        public void Subscribe(UserPreferenceChangedEventHandler handler) => _handlers.Add(handler);

        public void Unsubscribe(UserPreferenceChangedEventHandler handler)
        {
            UnsubscribeCallCount++;
            _handlers.Remove(handler);
        }

        public void Raise(UserPreferenceCategory category)
        {
            var args = new UserPreferenceChangedEventArgs(category);
            foreach (var handler in _handlers.ToArray())
                handler(this, args);
        }
    }
}
