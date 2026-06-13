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
}
