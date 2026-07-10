using System.IO;
using DesktopBox.Models;
using DesktopBox.Services;
using FluentAssertions;

namespace DesktopBox.Tests;

public class JsonStoreServiceTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"dbx_{Guid.NewGuid():N}.json");

    [Fact]
    public void SaveThenLoad_RoundTripsBoxes()
    {
        var path = TempFile();
        var store = new JsonStoreService(path);
        var cfg = new AppConfig
        {
            Boxes = new()
            {
                new Box { Name = "常用", X = 10, Y = 20,
                    Items = new() { new BoxItem { Type = ItemType.Url, TargetPath = "https://x", DisplayName = "X" } } }
            },
            Settings = new AppSettings { AutoStart = true }
        };

        store.Save(cfg);
        var loaded = store.Load();

        loaded.Boxes.Should().HaveCount(1);
        loaded.Boxes[0].Name.Should().Be("常用");
        loaded.Boxes[0].Items[0].TargetPath.Should().Be("https://x");
        loaded.Settings.AutoStart.Should().BeTrue();
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsNewConfig()
    {
        var store = new JsonStoreService(TempFile() + "_absent");
        var cfg = store.Load();
        cfg.Boxes.Should().BeEmpty();
    }

    [Fact]
    public void Load_WhenCorrupt_ReturnsNewConfig()
    {
        var path = TempFile();
        File.WriteAllText(path, "{ not valid json");
        var store = new JsonStoreService(path);
        var cfg = store.Load();
        cfg.Boxes.Should().BeEmpty();
    }

    [Fact]
    public void SaveThenLoad_RoundTripsTabbedBox()
    {
        var path = TempFile();
        var store = new JsonStoreService(path);
        var cfg = new AppConfig
        {
            Boxes = new()
            {
                new Box
                {
                    Name = "桌面整理",
                    Tabs = new()
                    {
                        new BoxTab { Name = "应用程序",
                            Items = new() { new BoxItem { Type = ItemType.File, TargetPath = @"C:\a.exe", DisplayName = "A" } } },
                        new BoxTab { Name = "文档" }
                    }
                }
            }
        };

        store.Save(cfg);
        var loaded = store.Load();

        loaded.Boxes.Should().HaveCount(1);
        loaded.Boxes[0].Tabs.Should().HaveCount(2);
        loaded.Boxes[0].Tabs[0].Name.Should().Be("应用程序");
        loaded.Boxes[0].Tabs[0].Items.Should().HaveCount(1);
        loaded.Boxes[0].Tabs[0].Items[0].DisplayName.Should().Be("A");
        loaded.Boxes[0].Tabs[1].Name.Should().Be("文档");
        loaded.Boxes[0].Items.Should().BeEmpty();   // 标签模式 Items 为空
    }

    [Fact]
    public void Load_LegacyBoxWithoutTabs_TreatsAsNormalMode()
    {
        // 模拟旧版本(无 Tabs 字段)的 boxes.json:应反序列化为普通模式(Tabs 空)
        var path = TempFile();
        File.WriteAllText(path,
            @"{""boxes"":[{""name"":""旧盒子"",""x"":5,""y"":6," +
            @"""items"":[{""displayName"":""X""}]}],""settings"":{}}");

        var cfg = new JsonStoreService(path).Load();

        cfg.Boxes.Should().HaveCount(1);
        cfg.Boxes[0].Name.Should().Be("旧盒子");
        cfg.Boxes[0].Tabs.Should().BeEmpty();   // 缺省 → 空 → 普通模式
        cfg.Boxes[0].Items.Should().HaveCount(1);
        cfg.Boxes[0].Items[0].DisplayName.Should().Be("X");
    }

    [Fact]
    public void Save_WhenReplacingExistingFile_PreservesPreviousConfigInBackup()
    {
        var path = TempFile();
        var store = new JsonStoreService(path);
        store.Save(ConfigNamed("before"));

        store.Save(ConfigNamed("after"));

        new JsonStoreService(path).Load().Boxes.Single().Name.Should().Be("after");
        new JsonStoreService(path + ".bak").Load().Boxes.Single().Name.Should().Be("before");
        File.Exists(path + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void Load_WhenPrimaryIsCorrupt_RestoresValidBackupAndArchivesCorruptContent()
    {
        var path = TempFile();
        var store = new JsonStoreService(path);
        store.Save(ConfigNamed("recoverable"));
        store.Save(ConfigNamed("current"));
        const string corruptJson = "{ broken primary";
        File.WriteAllText(path, corruptJson);

        var loaded = store.Load();

        loaded.Boxes.Single().Name.Should().Be("recoverable");
        new JsonStoreService(path).Load().Boxes.Single().Name.Should().Be("recoverable");
        var archived = Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.corrupt");
        archived.Should().ContainSingle();
        File.ReadAllText(archived[0]).Should().Be(corruptJson);
        File.Exists(path + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void Load_WhenNoValidBackupExists_ReturnsEmptyConfigWithoutLosingCorruptContent()
    {
        var path = TempFile();
        const string corruptJson = "{ broken primary";
        File.WriteAllText(path, corruptJson);
        File.WriteAllText(path + ".bak", "{ broken backup");

        var loaded = new JsonStoreService(path).Load();

        loaded.Boxes.Should().BeEmpty();
        var archived = Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.corrupt");
        archived.Should().ContainSingle();
        File.ReadAllText(archived[0]).Should().Be(corruptJson);
    }

    [Fact]
    public void Save_WhenFileReplaceIsUnsupported_UsesSameDirectoryFallback()
    {
        var path = TempFile();
        var initial = new JsonStoreService(path);
        initial.Save(ConfigNamed("before"));
        var store = new JsonStoreService(path, (_, _, _) => throw new PlatformNotSupportedException());

        store.Save(ConfigNamed("after"));

        new JsonStoreService(path).Load().Boxes.Single().Name.Should().Be("after");
        new JsonStoreService(path + ".bak").Load().Boxes.Single().Name.Should().Be("before");
        File.Exists(path + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void Save_WhenReplacementFails_PropagatesExceptionAndCleansTemporaryFile()
    {
        var path = TempFile();
        var initial = new JsonStoreService(path);
        initial.Save(ConfigNamed("before"));
        var store = new JsonStoreService(path, (_, _, _) => throw new IOException("replace failed"));

        var action = () => store.Save(ConfigNamed("after"));

        action.Should().Throw<IOException>().WithMessage("replace failed");
        File.Exists(path + ".tmp").Should().BeFalse();
        new JsonStoreService(path).Load().Boxes.Single().Name.Should().Be("before");
    }

    [Fact]
    public void Save_WhenCalledConcurrently_LeavesReadableFilesAndNoTemporaryFile()
    {
        var path = TempFile();
        var store = new JsonStoreService(path);

        var action = () => Parallel.For(0, 20, i => store.Save(ConfigNamed($"box-{i}")));

        action.Should().NotThrow();
        new JsonStoreService(path).Load().Boxes.Should().ContainSingle();
        new JsonStoreService(path + ".bak").Load().Boxes.Should().ContainSingle();
        File.Exists(path + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void Load_WhenPrimaryIsTemporarilyUnreadable_UsesBackupAndBlocksSave()
    {
        var path = TempFile();
        var store = new JsonStoreService(path);
        store.Save(ConfigNamed("backup"));
        store.Save(ConfigNamed("primary"));

        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var loaded = store.Load();

            loaded.Boxes.Single().Name.Should().Be("backup");
            var save = () => store.Save(ConfigNamed("must-not-overwrite"));
            save.Should().Throw<IOException>();
        }

        store.Load().Boxes.Single().Name.Should().Be("primary");
        var retry = () => store.Save(ConfigNamed("after-retry"));
        retry.Should().NotThrow();
    }

    [Fact]
    public void Load_WhenPrimaryIsUnreadableAndNoBackup_PropagatesReadFailure()
    {
        var path = TempFile();
        File.WriteAllText(path, "valid but locked");
        var store = new JsonStoreService(path);

        using var locked = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var load = () => store.Load();

        load.Should().Throw<IOException>();
    }

    [Fact]
    public void Load_WhenCorruptWithoutBackup_ArchivesPrimaryOnlyOnce()
    {
        var path = TempFile();
        File.WriteAllText(path, "{ broken primary");
        var store = new JsonStoreService(path);

        store.Load();
        store.Load();

        Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.corrupt")
            .Should().ContainSingle();
        File.Exists(path).Should().BeFalse();
    }

    private static AppConfig ConfigNamed(string name) => new()
    {
        Boxes = new() { new Box { Name = name } }
    };

}
