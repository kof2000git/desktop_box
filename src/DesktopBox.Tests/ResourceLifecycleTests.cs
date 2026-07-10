using FluentAssertions;
using System.IO;

namespace DesktopBox.Tests;

public class ResourceLifecycleTests
{
    [Fact]
    public void MainWindow_RebuildTrayMenu_DisposesReplacedMenu()
    {
        var source = ReadRepositoryFile("src", "DesktopBox", "Views", "MainWindow.xaml.cs");
        var body = Between(source, "private void RebuildTrayMenu()", "private void ShowBoxes()");

        body.Should().Contain("var oldMenu = _tray.ContextMenuStrip;");
        body.Should().Contain("_tray.ContextMenuStrip = menu;");
        body.Should().Contain("oldMenu?.Dispose();");
        body.IndexOf("_tray.ContextMenuStrip = menu;", StringComparison.Ordinal)
            .Should().BeLessThan(body.IndexOf("oldMenu?.Dispose();", StringComparison.Ordinal));
    }

    [Fact]
    public void MainWindow_ShutdownCleanup_IsIdempotentAndReleasesAllOwnedResources()
    {
        var source = ReadRepositoryFile("src", "DesktopBox", "Views", "MainWindow.xaml.cs");
        var cleanup = Between(source, "private void DisposeOwnedResources()", "private void OnQuit(");

        source.Should().Contain("private bool _ownedResourcesDisposed;");
        cleanup.Should().Contain("if (_ownedResourcesDisposed)");
        cleanup.Should().Contain("_ownedResourcesDisposed = true;");
        cleanup.Should().Contain("_tray.ContextMenuStrip = null;");
        cleanup.Should().Contain("menu?.Dispose();");
        cleanup.Should().Contain("_tray.Dispose();");
        cleanup.Should().Contain("_trayIcon?.Dispose();");
        cleanup.Should().Contain("_trayIcon = null;");
        cleanup.Should().Contain("foreach (var window in _boxWindows.Values.ToList())");
        cleanup.Should().Contain("window.CloseForRemoval();");
        cleanup.Should().Contain("_boxWindows.Clear();");
        source.Should().Contain("DisposeOwnedResources();\n        Application.Current.Shutdown();");
        source.Should().Contain("protected override void OnClosed(EventArgs e)");
    }

    [Fact]
    public void MainWindow_OwnsAndDisposesClonedTrayIcon()
    {
        var source = ReadRepositoryFile("src", "DesktopBox", "Views", "MainWindow.xaml.cs");
        var setup = Between(source, "private void SetupTray()", "private void RebuildTrayMenu()");

        source.Should().Contain("private Icon? _trayIcon;");
        setup.Should().Contain("_trayIcon = MakeIcon();");
        setup.Should().Contain("Icon = _trayIcon");
    }

    [Fact]
    public void MainWindow_PersistenceFailure_IsShownOnceThroughTrayAndUnsubscribedOnCleanup()
    {
        var source = ReadRepositoryFile("src", "DesktopBox", "Views", "MainWindow.xaml.cs");

        source.Should().Contain("_vm.PersistenceFailed += OnPersistenceFailed;");
        source.Should().Contain("_vm.PersistenceFailed -= OnPersistenceFailed;");
        source.Should().Contain("_settingsVm.PersistenceFailed += OnPersistenceFailed;");
        source.Should().Contain("_settingsVm.PersistenceFailed -= OnPersistenceFailed;");
        source.Should().Contain("private void OnPersistenceFailed(object? sender, Exception exception)");
        source.Should().Contain("_tray.ShowBalloonTip(");
    }

    [Fact]
    public void MainWindow_CancelsQuitWhenFinalSaveFails()
    {
        var source = ReadRepositoryFile("src", "DesktopBox", "Views", "MainWindow.xaml.cs");
        var quit = Between(source, "private void OnQuit(", "protected override void OnClosed(");

        quit.Should().Contain("if (!_vm.TrySave())");
        quit.Should().Contain("App.CancelShutdown();");
        quit.IndexOf("if (!_vm.TrySave())", StringComparison.Ordinal)
            .Should().BeLessThan(quit.IndexOf("DisposeOwnedResources();", StringComparison.Ordinal));
    }

    [Fact]
    public void ShellLinkResolver_FinalReleasesShortcutBeforeShell_InBothOperations()
    {
        var source = ReadRepositoryFile("src", "DesktopBox", "Native", "ShellLinkResolver.cs");

        source.Should().Contain("using System.Runtime.InteropServices;");
        source.Should().Contain("finally", Exactly.Twice());
        source.Should().Contain("Marshal.FinalReleaseComObject(shortcut);", Exactly.Twice());
        source.Should().Contain("Marshal.FinalReleaseComObject(shell);", Exactly.Twice());

        foreach (var method in new[] { "ResolveTarget", "ResolveIconLocation" })
        {
            var body = MethodBody(source, method);
            body.IndexOf("Marshal.FinalReleaseComObject(shortcut);", StringComparison.Ordinal)
                .Should().BeLessThan(body.IndexOf("Marshal.FinalReleaseComObject(shell);", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void CategorizerService_DelegatesShortcutResolutionToShellLinkResolver()
    {
        var source = ReadRepositoryFile("src", "DesktopBox", "Services", "CategorizerService.cs");

        source.Should().Contain("ShellLinkResolver.ResolveTarget(path)");
        source.Should().NotContain("Type.GetTypeFromProgID");
        source.Should().NotContain("Activator.CreateInstance");
        source.Should().NotContain("private static string ResolveShortcut");
    }

    private static string MethodBody(string source, string methodName)
    {
        var start = source.IndexOf($" {methodName}(", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var nextMethod = source.IndexOf("\n    public static ", start + 1, StringComparison.Ordinal);
        return nextMethod < 0 ? source[start..] : source[start..nextMethod];
    }

    private static string Between(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
