namespace DesktopBox.Services;

public interface IDesktopScannerService
{
    /// <summary>返回桌面(当前用户 + 公共)上所有可见条目的完整路径。</summary>
    List<string> ScanDesktop();
}
