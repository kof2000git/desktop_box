using System.IO;
using DesktopBox.Services;
using FluentAssertions;

namespace DesktopBox.Tests;

public class CategorizerServiceTests
{
    private readonly CategorizerService _sut = new();

    private string TmpWithExt(string ext)
    {
        var f = Path.GetTempFileName();
        var p = Path.ChangeExtension(f, ext);
        File.Move(f, p);
        File.WriteAllText(p, "x");
        return p;
    }

    [Theory]
    [InlineData(".pdf", "文档")]
    [InlineData(".docx", "文档")]
    [InlineData(".md", "文档")]
    [InlineData(".txt", "文档")]
    [InlineData(".jpg", "图片")]
    [InlineData(".png", "图片")]
    [InlineData(".zip", "压缩包")]
    [InlineData(".7z", "压缩包")]
    [InlineData(".mp4", "视频")]
    [InlineData(".mkv", "视频")]
    [InlineData(".mp3", "音频")]
    [InlineData(".flac", "音频")]
    [InlineData(".ape", "音频")]
    [InlineData(".exe", "应用程序")]
    [InlineData(".msi", "应用程序")]
    [InlineData(".appx", "应用程序")]
    [InlineData(".zzz", "其他")]
    public void Categorize_ByExtension(string ext, string expected)
    {
        var p = TmpWithExt(ext);
        try { _sut.Categorize(p).Should().Be(expected); }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Categorize_Folder_ReturnsFolderCategory()
    {
        var dir = Directory.CreateTempSubdirectory("dbx_").FullName;
        try { _sut.Categorize(dir).Should().Be(CategorizerService.FolderCat); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Categorize_ShortcutFile_ReturnsShortcutCategory()
    {
        // 伪 .lnk 文件:WScript.Shell 解析失败 → 按策略归「快捷方式」
        var lnk = Path.Combine(Path.GetTempPath(), $"dbx_{System.Guid.NewGuid():N}.lnk");
        File.WriteAllText(lnk, "fake");
        try { _sut.Categorize(lnk).Should().Be(CategorizerService.Shortcut); }
        finally { File.Delete(lnk); }
    }
}
