using System.IO;
using DesktopBox.Models;
using DesktopBox.Services;
using FluentAssertions;
using Moq;

namespace DesktopBox.Tests;

public class OrganizeServiceTests
{
    private string? _desktop;
    private string? _organized;
    private string? _manifest;

    private OrganizeService Setup(out string[] files)
    {
        _desktop = Directory.CreateTempSubdirectory("dbx_desk_").FullName;
        _organized = Path.Combine(Path.GetTempPath(), "dbx_org_" + Guid.NewGuid().ToString("N"));
        _manifest = Path.Combine(Path.GetTempPath(), "dbx_man_" + Guid.NewGuid().ToString("N") + ".json");

        var pdf = Path.Combine(_desktop, "a.pdf"); File.WriteAllText(pdf, "x");
        var jpg = Path.Combine(_desktop, "b.jpg"); File.WriteAllText(jpg, "x");
        var exe = Path.Combine(_desktop, "c.exe"); File.WriteAllText(exe, "x");
        var folder = Path.Combine(_desktop, "我的文件夹"); Directory.CreateDirectory(folder);

        files = new[] { pdf, jpg, exe, folder };

        var scanner = new Mock<IDesktopScannerService>();
        scanner.Setup(s => s.ScanDesktop())
               .Returns(new List<string> { pdf, jpg, exe, folder });

        return new OrganizeService(scanner.Object, new CategorizerService(), _organized, _manifest);
    }

    private void Cleanup()
    {
        try { if (Directory.Exists(_desktop)) Directory.Delete(_desktop, true); } catch { }
        try { if (Directory.Exists(_organized)) Directory.Delete(_organized, true); } catch { }
        try { if (File.Exists(_manifest)) File.Delete(_manifest); } catch { }
    }

    [Fact]
    public void Organize_MovesAndCategorizesFiles()
    {
        var svc = Setup(out var files);
        try
        {
            var res = svc.Organize();

            res.Should().NotBeNull();
            res!.Entries.Should().HaveCount(4);
            svc.HasActiveOrganize.Should().BeTrue();

            // 桌面上原文件已被移走
            File.Exists(files[0]).Should().BeFalse();
            // 分类目录里各有 1 项
            Directory.GetFiles(Path.Combine(svc.OrganizedRoot, "文档")).Should().ContainSingle();
            Directory.GetFiles(Path.Combine(svc.OrganizedRoot, "图片")).Should().ContainSingle();
            Directory.GetFiles(Path.Combine(svc.OrganizedRoot, "程序")).Should().ContainSingle();
            Directory.GetDirectories(Path.Combine(svc.OrganizedRoot, "文件夹")).Should().ContainSingle();
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Restore_MovesFilesBackAndClearsManifest()
    {
        var svc = Setup(out var files);
        try
        {
            svc.Organize();
            var res = svc.Restore();

            res.Should().NotBeNull();
            res!.Manifest.Moves.Should().HaveCount(4);
            svc.HasActiveOrganize.Should().BeFalse();

            // 文件已回到桌面
            File.Exists(files[0]).Should().BeTrue();
            File.Exists(files[1]).Should().BeTrue();
            File.Exists(files[2]).Should().BeTrue();
            Directory.Exists(files[3]).Should().BeTrue();
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Organize_WithEmptyDesktop_ReturnsNull()
    {
        _desktop = Directory.CreateTempSubdirectory("dbx_empty_").FullName;
        _organized = Path.Combine(Path.GetTempPath(), "dbx_org_" + Guid.NewGuid().ToString("N"));
        _manifest = Path.Combine(Path.GetTempPath(), "dbx_man_" + Guid.NewGuid().ToString("N") + ".json");
        var scanner = new Mock<IDesktopScannerService>();
        scanner.Setup(s => s.ScanDesktop()).Returns(new List<string>());
        var svc = new OrganizeService(scanner.Object, new CategorizerService(), _organized, _manifest);
        try
        {
            svc.Organize().Should().BeNull();
            svc.HasActiveOrganize.Should().BeFalse();
        }
        finally { Cleanup(); }
    }
}
