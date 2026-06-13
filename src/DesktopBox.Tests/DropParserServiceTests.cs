using System.IO;
using DesktopBox.Models;
using DesktopBox.Services;
using FluentAssertions;

namespace DesktopBox.Tests;

public class DropParserServiceTests
{
    private readonly DropParserService _sut = new();

    [Fact]
    public void ParsePath_Folder_ReturnsFolderType()
    {
        var dir = Directory.CreateTempSubdirectory("dbx_").FullName;
        try
        {
            var item = _sut.ParsePath(dir);
            item.Type.Should().Be(ItemType.Folder);
            item.TargetPath.Should().Be(dir);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ParsePath_Exe_ReturnsFileType_WithExeName()
    {
        var f = Path.ChangeExtension(Path.GetTempFileName(), ".exe");
        File.WriteAllText(f, "X");
        try
        {
            var item = _sut.ParsePath(f);
            item.Type.Should().Be(ItemType.File);
            item.DisplayName.Should().Be(Path.GetFileNameWithoutExtension(f));
        }
        finally { File.Delete(f); }
    }

    [Fact]
    public void ParsePath_Lnk_ReturnsShortcutType()
    {
        var f = Path.ChangeExtension(Path.GetTempFileName(), ".lnk");
        File.WriteAllText(f, "X");
        try
        {
            var item = _sut.ParsePath(f);
            item.Type.Should().Be(ItemType.Shortcut);
        }
        finally { File.Delete(f); }
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com")]
    public void ParseUrl_HttpUrl_ReturnsUrlType(string url)
    {
        var item = _sut.ParseUrl(url);
        item.Type.Should().Be(ItemType.Url);
        item.TargetPath.Should().Be(url);
    }
}
