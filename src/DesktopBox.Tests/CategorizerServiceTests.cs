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
    [InlineData(".jpg", "图片")]
    [InlineData(".png", "图片")]
    [InlineData(".zip", "压缩包")]
    [InlineData(".7z", "压缩包")]
    [InlineData(".mp4", "视频")]
    [InlineData(".mkv", "视频")]
    [InlineData(".mp3", "音乐")]
    [InlineData(".flac", "音乐")]
    [InlineData(".exe", "程序")]
    [InlineData(".msi", "程序")]
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
}
