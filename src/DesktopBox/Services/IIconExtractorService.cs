namespace DesktopBox.Services;

public interface IIconExtractorService
{
    /// <summary>提取目标路径的图标并缓存为 PNG;返回缓存路径,失败返回 null。</summary>
    string? Extract(string targetPath);

    /// <param name="forceRefresh">true 时删除系统图标的旧缓存,重新提取当前状态(回收站空/满切换后用)。</param>
    string? Extract(string targetPath, bool forceRefresh);
}
