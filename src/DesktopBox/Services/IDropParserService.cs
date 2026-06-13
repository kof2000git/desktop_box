using DesktopBox.Models;

namespace DesktopBox.Services;

public interface IDropParserService
{
    BoxItem ParsePath(string path);
    BoxItem ParseUrl(string url);
}
