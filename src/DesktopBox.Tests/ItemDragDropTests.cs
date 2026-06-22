using System.IO;
using System.Windows;
using DesktopBox.Controls;
using DesktopBox.Models;
using FluentAssertions;

namespace DesktopBox.Tests;

public class ItemDragDropTests
{
    [Fact]
    public void CanDragAsFile_AllowsExistingFilesAndFoldersOnly()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var file = Path.Combine(dir.FullName, "a.txt");
            File.WriteAllText(file, "x");

            ItemDragDrop.CanDragAsFile(Item(ItemType.File, file)).Should().BeTrue();
            ItemDragDrop.CanDragAsFile(Item(ItemType.Folder, dir.FullName)).Should().BeTrue();
            ItemDragDrop.CanDragAsFile(Item(ItemType.File, Path.Combine(dir.FullName, "missing.txt"))).Should().BeFalse();
            ItemDragDrop.CanDragAsFile(Item(ItemType.Url, "https://example.com")).Should().BeFalse();
            ItemDragDrop.CanDragAsFile(Item(ItemType.SystemIcon, "::CLSID")).Should().BeFalse();
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void CreateFileDropData_UsesNativeFileDropPayload()
    {
        var item = Item(ItemType.File, @"C:\Temp\a.txt");
        var data = ItemDragDrop.CreateFileDropData(item);

        data.GetDataPresent(DataFormats.FileDrop).Should().BeTrue();
        data.GetData(DataFormats.FileDrop).Should().BeEquivalentTo(new[] { item.TargetPath });
        data.GetData(DataFormats.UnicodeText).Should().Be(item.TargetPath);
        data.GetData(ItemDragDrop.DragSourceItemFormat).Should().Be(item.Id);
    }

    private static BoxItem Item(ItemType type, string target) => new()
    {
        Type = type,
        TargetPath = target,
        DisplayName = Path.GetFileName(target)
    };
}
