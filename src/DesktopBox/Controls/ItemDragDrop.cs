using System.IO;
using System.Windows;
using DesktopBox.Models;

namespace DesktopBox.Controls;

public static class ItemDragDrop
{
    public static bool CanDragAsFile(BoxItem item) =>
        item.Type is not ItemType.Url and not ItemType.SystemIcon
        && !item.TargetPath.StartsWith("::", StringComparison.Ordinal)
        && (File.Exists(item.TargetPath) || Directory.Exists(item.TargetPath));

    public static DataObject CreateFileDropData(BoxItem item)
    {
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { item.TargetPath });
        data.SetData(DataFormats.Text, item.TargetPath);
        data.SetData(DataFormats.UnicodeText, item.TargetPath);
        data.SetData(DragSourceItemFormat, item.Id);
        return data;
    }

    public const string DragSourceItemFormat = "DesktopBox.ItemId";
}
