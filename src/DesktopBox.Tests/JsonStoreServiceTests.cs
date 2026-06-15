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
}
