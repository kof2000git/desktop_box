using System.IO;
using DesktopBox.Models;
using DesktopBox.Services;
using FluentAssertions;
using Moq;

namespace DesktopBox.Tests;

public class OrganizeServiceTests
{
    private string? _desktop;
    private string? _manifest;

    private OrganizeService Setup(out string[] files)
    {
        _desktop = Directory.CreateTempSubdirectory("dbx_desk_").FullName;
        _manifest = Path.Combine(Path.GetTempPath(), "dbx_man_" + Guid.NewGuid().ToString("N") + ".json");

        var pdf = Path.Combine(_desktop, "a.pdf"); File.WriteAllText(pdf, "x");
        var jpg = Path.Combine(_desktop, "b.jpg"); File.WriteAllText(jpg, "x");
        var exe = Path.Combine(_desktop, "c.exe"); File.WriteAllText(exe, "x");
        var folder = Path.Combine(_desktop, "我的文件夹"); Directory.CreateDirectory(folder);
        files = new[] { pdf, jpg, exe, folder };

        var scanner = new Mock<IDesktopScannerService>();
        scanner.Setup(s => s.ScanDesktop()).Returns(new List<string> { pdf, jpg, exe, folder });
        return new OrganizeService(scanner.Object, new CategorizerService(), _manifest);
    }

    private void Cleanup()
    {
        try { if (Directory.Exists(_desktop)) Directory.Delete(_desktop, true); } catch { }
        try { if (File.Exists(_manifest)) File.Delete(_manifest); } catch { }
    }

    [Fact]
    public void Organize_ReferencesWithoutMovingFiles()
    {
        var svc = Setup(out var files);
        try
        {
            var res = svc.Organize();

            res.Should().NotBeNull();
            res!.Entries.Should().HaveCount(4);
            svc.HasActiveOrganize.Should().BeTrue();

            // 关键:文件原封不动留在桌面
            File.Exists(files[0]).Should().BeTrue();
            File.Exists(files[1]).Should().BeTrue();
            File.Exists(files[2]).Should().BeTrue();
            Directory.Exists(files[3]).Should().BeTrue();

            // 条目路径就是原始路径(引用)
            res.Entries.Should().OnlyContain(e => e.CurrentPath.StartsWith(_desktop!));
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Restore_ClearsManifestWithoutTouchingFiles()
    {
        var svc = Setup(out var files);
        try
        {
            svc.Organize();
            var res = svc.Restore();

            res.Should().NotBeNull();
            svc.HasActiveOrganize.Should().BeFalse();

            // 文件依旧在原位(还原不移动任何东西)
            File.Exists(files[0]).Should().BeTrue();
            File.Exists(files[2]).Should().BeTrue();
            Directory.Exists(files[3]).Should().BeTrue();
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Organize_WithEmptyDesktop_ReturnsNull()
    {
        _desktop = Directory.CreateTempSubdirectory("dbx_empty_").FullName;
        _manifest = Path.Combine(Path.GetTempPath(), "dbx_man_" + Guid.NewGuid().ToString("N") + ".json");
        var scanner = new Mock<IDesktopScannerService>();
        scanner.Setup(s => s.ScanDesktop()).Returns(new List<string>());
        var svc = new OrganizeService(scanner.Object, new CategorizerService(), _manifest);
        try
        {
            svc.Organize().Should().BeNull();
            svc.HasActiveOrganize.Should().BeFalse();
        }
        finally { Cleanup(); }
    }
}
