using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using DesktopBox.Models;
using DesktopBox.Services;
using DesktopBox.ViewModels;
using FluentAssertions;
using Moq;

namespace DesktopBox.Tests;

public class MainViewModelTests
{
    private readonly Mock<IPersistenceService> _store = new();
    private readonly Mock<IIconExtractorService> _icon = new();
    private readonly Mock<IOrganizeService> _organize = new();
    private readonly Mock<IDesktopIconsService> _desktopIcons = new();
    private readonly Mock<ILocalizerService> _localizer = new();
    private readonly Mock<IShellChangeNotifierService> _shellChange = new();
    private readonly Mock<ICategorizerService> _categorizer = new();

    private MainViewModel NewVm()
    {
        _store.Reset();
        _icon.Reset();
        _organize.Reset();
        _desktopIcons.Reset();
        _localizer.Reset();
        _shellChange.Reset();
        _categorizer.Reset();
        // Localizer 索引器回退:返回 key 本身(模拟"无翻译"行为)
        _localizer.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => k);
        _store.Setup(s => s.Load()).Returns(new AppConfig());
        _icon.Setup(i => i.Extract(It.IsAny<string>())).Returns((string?)null);
        _organize.SetupGet(o => o.HasActiveOrganize).Returns(false);
        _organize.Setup(o => o.CountOrganizable()).Returns(0);
        _desktopIcons.SetupGet(d => d.AreIconsVisible).Returns(true);
        return new MainViewModel(_store.Object, new DropParserService(), _icon.Object, _organize.Object, _categorizer.Object, _desktopIcons.Object, _localizer.Object, _shellChange.Object);
    }

    [Fact]
    public void AddBox_IncreasesCollection()
    {
        var vm = NewVm();
        vm.AddBoxCommand.Execute(null);
        vm.Boxes.Should().HaveCount(1);
        vm.Boxes[0].Name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void RemoveBox_DecreasesCollection()
    {
        var vm = NewVm();
        vm.AddBoxCommand.Execute(null);
        vm.RemoveBoxCommand.Execute(vm.Boxes[0]);
        vm.Boxes.Should().BeEmpty();
    }

    [Fact]
    public void Load_ReadsFromStore()
    {
        _store.Setup(s => s.Load()).Returns(new AppConfig
        {
            Boxes = new() { new Box { Name = "已存在" } }
        });
        var vm = new MainViewModel(_store.Object, new DropParserService(), _icon.Object, _organize.Object, _categorizer.Object, _desktopIcons.Object, _localizer.Object, _shellChange.Object);
        vm.LoadCommand.Execute(null);
        vm.Boxes.Should().ContainSingle(b => b.Name == "已存在");
    }

    [Fact]
    public void AddItemToBox_Path_AddsItemWithCorrectPath()
    {
        _store.Setup(s => s.Load()).Returns(new AppConfig
        {
            Boxes = new() { new Box { Name = "B" } }
        });
        _icon.Setup(i => i.Extract(It.IsAny<string>())).Returns("/icons/x.png");
        var vm = new MainViewModel(_store.Object, new DropParserService(), _icon.Object, _organize.Object, _categorizer.Object, _desktopIcons.Object, _localizer.Object, _shellChange.Object);
        vm.LoadCommand.Execute(null);

        var exe = System.IO.Path.ChangeExtension(System.IO.Path.GetTempFileName(), ".exe");
        System.IO.File.WriteAllText(exe, "X");
        try
        {
            vm.AddItemToBox(vm.Boxes.First(), exe);
            vm.Boxes.First().Items.Should().ContainSingle();
            vm.Boxes.First().Items[0].TargetPath.Should().Be(exe);
            vm.Boxes.First().Items[0].Type.Should().Be(ItemType.File);
        }
        finally { System.IO.File.Delete(exe); }
    }

    [Fact]
    public void Save_PersistsCurrentBoxes()
    {
        var vm = NewVm();
        vm.AddBoxCommand.Execute(null);
        _store.Invocations.Clear();
        vm.Save();
        _store.Verify(s => s.Save(It.Is<AppConfig>(c => c.Boxes.Count == 1)), Times.Once);
    }

    [Fact]
    public void Save_WhenPersistenceFails_RaisesFailureNotification()
    {
        var vm = NewVm();
        var failure = new IOException("disk full");
        _store.Setup(s => s.Save(It.IsAny<AppConfig>())).Throws(failure);
        Exception? reported = null;
        vm.PersistenceFailed += (_, error) => reported = error;

        vm.Save();

        reported.Should().BeSameAs(failure);
    }

    [Fact]
    public void TrySave_ReturnsFalseWhenPersistenceFails()
    {
        var vm = NewVm();
        _store.Setup(s => s.Save(It.IsAny<AppConfig>())).Throws(new IOException("disk full"));

        vm.TrySave().Should().BeFalse();
    }

    [Fact]
    public void SystemIconChanged_DeduplicatesSystemIconPathsBeforeExtraction()
    {
        var vm = NewVm();
        const string recycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";
        const string thisPc = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
        vm.Boxes.Add(new BoxViewModel(new Box
        {
            Name = "sys",
            Items =
            {
                new BoxItem { Type = ItemType.SystemIcon, TargetPath = recycleBin, DisplayName = "Recycle Bin" },
                new BoxItem { Type = ItemType.SystemIcon, TargetPath = recycleBin, DisplayName = "Recycle Bin duplicate" },
                new BoxItem { Type = ItemType.SystemIcon, TargetPath = thisPc, DisplayName = "This PC" }
            }
        }));

        var calls = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var done = new ManualResetEventSlim();
        _icon.Setup(i => i.Extract(It.IsAny<string>(), true))
            .Returns<string, bool>((path, _) =>
            {
                calls.AddOrUpdate(path, 1, (_, count) => count + 1);
                if (calls.Count == 2) done.Set();
                return path + ".png";
            });

        _shellChange.Raise(s => s.SystemIconChanged += null, EventArgs.Empty);

        done.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        calls[recycleBin].Should().Be(1);
        calls[thisPc].Should().Be(1);
    }

    [Fact]
    public void SystemIconChanged_CoalescesRequestsWhileRefreshIsRunning()
    {
        var vm = NewVm();
        const string recycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";
        vm.Boxes.Add(new BoxViewModel(new Box
        {
            Name = "sys",
            Items =
            {
                new BoxItem { Type = ItemType.SystemIcon, TargetPath = recycleBin, DisplayName = "Recycle Bin" }
            }
        }));

        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var calls = 0;
        _icon.Setup(i => i.Extract(It.IsAny<string>(), true))
            .Returns<string, bool>((_, _) =>
            {
                var current = Interlocked.Increment(ref calls);
                if (current == 1)
                {
                    firstStarted.Set();
                    releaseFirst.Wait(TimeSpan.FromSeconds(2));
                }
                return $"icon-{current}.png";
            });

        _shellChange.Raise(s => s.SystemIconChanged += null, EventArgs.Empty);
        firstStarted.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();

        for (var i = 0; i < 5; i++)
            _shellChange.Raise(s => s.SystemIconChanged += null, EventArgs.Empty);

        Volatile.Read(ref calls).Should().Be(1);
        releaseFirst.Set();

        SpinWait.SpinUntil(() => Volatile.Read(ref calls) >= 2, TimeSpan.FromSeconds(2)).Should().BeTrue();
        Thread.Sleep(200);
        Volatile.Read(ref calls).Should().Be(2);
    }

    [Fact]
    public void DesktopFileNotification_RetainsTemporarilyUnavailableReference()
    {
        var vm = NewVm();
        var box = new BoxViewModel(new Box
        {
            Name = "offline",
            Items =
            {
                new BoxItem
                {
                    Type = ItemType.File,
                    TargetPath = @"Z:\temporarily-offline\document.txt",
                    DisplayName = "document.txt"
                }
            }
        });
        vm.Boxes.Add(box);

        _shellChange.Raise(s => s.DesktopFilesChanged += null, EventArgs.Empty);

        box.Items.Should().ContainSingle();
    }

    [Fact]
    public void Dispose_CancelsPendingSaveAndUnsubscribesLongLivedEvents()
    {
        var vm = NewVm();
        vm.ScheduleSave();

        vm.Dispose();
        Thread.Sleep(500);

        _store.Verify(s => s.Save(It.IsAny<AppConfig>()), Times.Never);
        _localizer.VerifyRemove(l => l.LanguageChanged -= It.IsAny<EventHandler>(), Times.Once);
        _shellChange.VerifyRemove(s => s.SystemIconChanged -= It.IsAny<EventHandler>(), Times.Once);
    }

    [Fact]
    public void ScheduleSave_RemovesTimerInstalledDuringConcurrentDispose()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "DesktopBox", "ViewModels", "MainViewModel.cs"));
        var start = source.IndexOf("public void ScheduleSave()", StringComparison.Ordinal);
        var end = source.IndexOf("public void Save()", start, StringComparison.Ordinal);
        var body = source[start..end];

        body.Should().Contain("Interlocked.CompareExchange(ref _debounce, null, timer)");
        body.Should().Contain("if (_disposed)");
        body.IndexOf("Interlocked.CompareExchange", StringComparison.Ordinal)
            .Should().BeGreaterThan(body.IndexOf("Interlocked.Exchange(ref _debounce, timer)", StringComparison.Ordinal));
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }

    // ToggleDesktopIcons 依赖 GUI 对话框(InputDialog.Inform),命令末尾会弹窗,
    // 无法在无界面测试中执行。其逻辑仅为"翻转并调用 SetVisible",靠桌面图标服务的
    // 手动验证覆盖。

    [Fact]
    public void PruneMissingItems_RemovesOnlyMissingLocalTargets()
    {
        var existing = Path.Combine(Path.GetTempPath(), $"dbx-exist-{Guid.NewGuid():N}.txt");
        File.WriteAllText(existing, "ok");
        var missing = Path.Combine(Path.GetTempPath(), $"dbx-missing-{Guid.NewGuid():N}.txt");
        try
        {
            _store.Setup(s => s.Load()).Returns(new AppConfig
            {
                Boxes =
                [
                    new Box
                    {
                        Name = "B",
                        Items =
                        [
                            new BoxItem { Type = ItemType.File, TargetPath = existing, DisplayName = "live" },
                            new BoxItem { Type = ItemType.File, TargetPath = missing, DisplayName = "gone" },
                            new BoxItem { Type = ItemType.SystemIcon, TargetPath = "::{645FF040-5081-101B-9F08-00AA002F954E}", DisplayName = "Recycle" },
                            new BoxItem { Type = ItemType.Url, TargetPath = "https://example.com", DisplayName = "web" },
                        ]
                    }
                ]
            });
            var vm = new MainViewModel(_store.Object, new DropParserService(), _icon.Object, _organize.Object, _categorizer.Object, _desktopIcons.Object, _localizer.Object, _shellChange.Object);
            vm.LoadCommand.Execute(null);

            var removed = vm.PruneMissingItems();

            removed.Should().Be(1);
            vm.Boxes[0].Items.Should().HaveCount(3);
            vm.Boxes[0].Items.Select(i => i.DisplayName).Should().BeEquivalentTo("live", "Recycle", "web");
        }
        finally
        {
            if (File.Exists(existing)) File.Delete(existing);
        }
    }

    [Fact]
    public void IsMissingLocalTarget_KeepsSystemIconsAndUrls()
    {
        MainViewModel.IsMissingLocalTarget(new BoxItem
        {
            Type = ItemType.SystemIcon,
            TargetPath = "::{645FF040-5081-101B-9F08-00AA002F954E}"
        }).Should().BeFalse();

        MainViewModel.IsMissingLocalTarget(new BoxItem
        {
            Type = ItemType.Url,
            TargetPath = "https://example.com"
        }).Should().BeFalse();

        var missing = Path.Combine(Path.GetTempPath(), $"dbx-nope-{Guid.NewGuid():N}.txt");
        MainViewModel.IsMissingLocalTarget(new BoxItem
        {
            Type = ItemType.File,
            TargetPath = missing
        }).Should().BeTrue();
    }
}
