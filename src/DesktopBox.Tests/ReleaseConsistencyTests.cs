using FluentAssertions;
using System.IO;
using System.Linq;

namespace DesktopBox.Tests;

public class ReleaseConsistencyTests
{
    [Fact]
    public void InstallerVersion_ComesFromPublishedExecutable()
    {
        var installerPath = FindRepositoryFile("DesktopBox.iss");
        var source = File.ReadAllText(installerPath);

        source.Should().Contain("GetFileVersion");
        source.Should().Contain("{#MyAppVersion}");
        source.Should().NotContain("AppVersion=1.7.0");
        source.Should().NotContain("AppVerName=DesktopBox 1.7.0");
        source.Should().Contain("publish\\DesktopBox.ShellMenu.exe");
        source.Should().Contain("[InstallDelete]");
        source.Should().Contain("Name: \"{app}\\DesktopBox.ShellMenu.dll\"");
    }

    [Fact]
    public void ReleaseScript_PackagesShellMenuHelperExecutable()
    {
        var source = ReadRepositoryFile("release.ps1");

        source.Should().Contain("publish/DesktopBox.ShellMenu.exe");
        source.Should().NotContain("publish/DesktopBox.ShellMenu.dll");
    }

    [Fact]
    public void ContinuousIntegration_BuildsPortableAndInstallerArtifacts()
    {
        var source = ReadRepositoryFile(".github", "workflows", "build.yml");

        source.Should().Contain("publish/DesktopBox.ShellMenu.exe");
        source.Should().Contain("DesktopBox.iss");
        source.Should().Contain("release/DesktopBoxSetup.exe");
        source.Should().NotContain("publish/DesktopBox.ShellMenu.dll");
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("使用说明.md")]
    public void Documentation_DescribesCurrentShellAndDesktopBehavior(string fileName)
    {
        var source = ReadRepositoryFile(fileName);

        source.Should().Contain("DesktopBox.ShellMenu.exe");
        source.Should().NotContain("DesktopBox.ShellMenu.dll");
        source.Should().Contain("失效引用会保留");
        source.Should().Contain("多显示器");
        source.Should().NotContain("仅主显示器");
    }

    private static string ReadRepositoryFile(params string[] parts) =>
        File.ReadAllText(FindRepositoryFile(parts));

    private static string FindRepositoryFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
