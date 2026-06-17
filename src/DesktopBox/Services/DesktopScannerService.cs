using System.IO;

namespace DesktopBox.Services;

public class DesktopScannerService : IDesktopScannerService
{
    public List<string> ScanDesktop()
    {
        var result = new List<string>();
        var dirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),   // 用户桌面
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) // 公共桌面 C:\Users\Public\Desktop
        };

        foreach (var d in dirs.Distinct().Where(Directory.Exists))
        {
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(d))
                {
                    if (Path.GetFileName(entry).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add(entry);
                }
            }
            catch (Exception ex)
            {
                App.LogError(ex, "DesktopScanner.ScanDesktop");
            }
        }
        return result;
    }
}
