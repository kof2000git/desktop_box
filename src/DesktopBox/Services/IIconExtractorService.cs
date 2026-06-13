namespace DesktopBox.Services;

public interface IIconExtractorService
{
    /// <summary>提取目标路径的图标并缓存为 PNG;返回缓存路径,失败返回 null。</summary>
    string? Extract(string targetPath);
}
