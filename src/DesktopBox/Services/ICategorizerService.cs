namespace DesktopBox.Services;

public interface ICategorizerService
{
    /// <summary>把一个路径归类到分类名(中文):程序/文档/图片/压缩包/视频/音乐/文件夹/其他。</summary>
    string Categorize(string path);
}
