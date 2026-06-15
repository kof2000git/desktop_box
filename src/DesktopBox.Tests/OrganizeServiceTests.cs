using System.IO;
using System.Linq;
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
    public void ScanAndCategorize_ReferencesWithoutMovingFiles()
    {
        var svc = Setup(out var files);
        try
        {
            var entries = svc.ScanAndCategorize();

            entries.Should().HaveCount(4);
            // 扫描分类不写 manifest
            svc.HasActiveOrganize.Should().BeFalse();

            // 关键:文件原封不动留在桌面
            File.Exists(files[0]).Should().BeTrue();
            File.Exists(files[1]).Should().BeTrue();
            File.Exists(files[2]).Should().BeTrue();
            Directory.Exists(files[3]).Should().BeTrue();

            // 路径为原始路径(引用),分类正确
            entries.Should().OnlyContain(e => e.Path.StartsWith(_desktop!));
            entries.Single(e => e.Path == files[0]).Category.Should().Be(CategorizerService.Document);
            entries.Single(e => e.Path == files[1]).Category.Should().Be(CategorizerService.Image);
            entries.Single(e => e.Path == files[2]).Category.Should().Be(CategorizerService.Program);
            entries.Single(e => e.Path == files[3]).Category.Should().Be(CategorizerService.FolderCat);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void ScanAndCategorize_WithEmptyDesktop_ReturnsEmpty()
    {
        _desktop = Directory.CreateTempSubdirectory("dbx_empty_").FullName;
        _manifest = Path.Combine(Path.GetTempPath(), "dbx_man_" + Guid.NewGuid().ToString("N") + ".json");
        var scanner = new Mock<IDesktopScannerService>();
        scanner.Setup(s => s.ScanDesktop()).Returns(new List<string>());
        var svc = new OrganizeService(scanner.Object, new CategorizerService(), _manifest);
        try
        {
            svc.ScanAndCategorize().Should().BeEmpty();
            svc.HasActiveOrganize.Should().BeFalse();
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void RecordBoxIds_ThenGetOrganizeBoxId_RoundTrips()
    {
        var svc = Setup(out _);
        try
        {
            svc.HasActiveOrganize.Should().BeFalse();
            svc.GetOrganizeBoxId().Should().BeNull();

            var id = Guid.NewGuid();
            svc.RecordBoxIds(new[] { id });

            svc.HasActiveOrganize.Should().BeTrue();
            svc.GetOrganizeBoxId().Should().Be(id);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void GetOrganizeBoxId_WhenManifestMissing_ReturnsNull()
    {
        var svc = Setup(out _);
        try
        {
            svc.GetOrganizeBoxId().Should().BeNull();
        }
        finally { Cleanup(); }
    }
}
