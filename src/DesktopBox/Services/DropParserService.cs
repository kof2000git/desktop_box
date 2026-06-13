using System.IO;
using DesktopBox.Models;

namespace DesktopBox.Services;

public class DropParserService : IDropParserService
{
    public BoxItem ParsePath(string path)
    {
        var type = ItemType.File;
        if (Directory.Exists(path)) type = ItemType.Folder;
        else if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) type = ItemType.Shortcut;

        var name = type == ItemType.Folder
            ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : Path.GetFileNameWithoutExtension(path);

        return new BoxItem
        {
            Type = type,
            TargetPath = path,
            DisplayName = string.IsNullOrEmpty(name) ? Path.GetFileName(path) : name
        };
    }

    public BoxItem ParseUrl(string url) => new()
    {
        Type = ItemType.Url,
        TargetPath = url,
        DisplayName = url
    };
}
