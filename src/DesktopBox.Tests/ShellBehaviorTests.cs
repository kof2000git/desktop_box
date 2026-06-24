using DesktopBox.Native;
using DesktopBox.Services;
using FluentAssertions;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Threading;

namespace DesktopBox.Tests;

public class ShellBehaviorTests
{
    [Fact]
    public void DesktopBoxProject_PublishesNativeShellMenuDllWhenPresent()
    {
        var projectPath = FindRepositoryFile("src", "DesktopBox", "DesktopBox.csproj");
        var project = System.Xml.Linq.XDocument.Load(projectPath);
        var shellMenuItem = project
            .Descendants("Content")
            .SingleOrDefault(e => ((string?)e.Attribute("Include"))?.EndsWith("DesktopBox.ShellMenu.dll", StringComparison.OrdinalIgnoreCase) == true);

        shellMenuItem.Should().NotBeNull();
        shellMenuItem!.Attribute("Condition")?.Value.Should().Contain("Exists");
        GetItemMetadata(shellMenuItem, "CopyToOutputDirectory").Should().Be("PreserveNewest");
        GetItemMetadata(shellMenuItem, "CopyToPublishDirectory").Should().Be("PreserveNewest");
    }

    [Fact]
    public void AppManifest_DeclaresWindows8OrNewerCompatibilityForLayeredChildWindows()
    {
        var manifestPath = FindRepositoryFile("src", "DesktopBox", "app.manifest");
        var manifest = File.ReadAllText(manifestPath);

        manifest.Should().Contain("{4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38}");
        manifest.Should().Contain("{1f676c76-80e1-4239-95bb-83d0f6d0da78}");
        manifest.Should().Contain("{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}");
    }

    [Fact]
    public void User32_ExposesLayeredWindowAlphaApis()
    {
        User32.GWL_EXSTYLE.Should().Be(-20);
        User32.WS_EX_LAYERED.Should().Be(0x00080000);
        User32.LWA_ALPHA.Should().Be(0x00000002);
        typeof(User32).GetMethod(nameof(User32.SetLayeredWindowAttributes)).Should().NotBeNull();
    }

    [Fact]
    public void BoxWindow_UsesHwndSourceWithLayeredStyleFromCreation()
    {
        var sourcePath = FindRepositoryFile("src", "DesktopBox", "Views", "BoxWindow.cs");
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain("HwndSourceParameters");
        source.Should().Contain("WS_EX_LAYERED");
        source.Should().Contain("ExtendedWindowStyle");
        source.Should().Contain("CompositionTarget.BackgroundColor");
        source.Should().Contain("Colors.Transparent");
    }

    [Fact]
    public void BoxWindow_CanRepairHostAndChildZOrderAfterDesktopIconsToggle()
    {
        var sourcePath = FindRepositoryFile("src", "DesktopBox", "Views", "BoxWindow.cs");
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain("EnsureVisibleOnDesktopHost");
        source.Should().Contain("GetParent");
        source.Should().Contain("SetParent");
        source.Should().Contain("HWND_TOP");
        source.Should().Contain("SWP_SHOWWINDOW");
    }

    [Fact]
    public void BoxWindow_KeepsDesktopChildAtTopWhenMovingOrResizing()
    {
        var sourcePath = FindRepositoryFile("src", "DesktopBox", "Views", "BoxWindow.cs");
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain("MoveResize(Native.User32.HWND_TOP)");
        source.Should().Contain("private void MoveResize(IntPtr insertAfter)");
        source.Should().NotContain("HWND_NOTOPMOST");
    }

    [Fact]
    public void BoxWindow_ConvertsModelPixelsToWpfDipForContentSize()
    {
        var sourcePath = FindRepositoryFile("src", "DesktopBox", "Views", "BoxWindow.cs");
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain("TransformToDevice");
        source.Should().Contain("Box.Width / scale.X");
        source.Should().Contain("Box.Height / scale.Y");
    }

    [Fact]
    public void MainWindow_RepairsBoxWindowsWhenDesktopIconsVisibilityChanges()
    {
        var sourcePath = FindRepositoryFile("src", "DesktopBox", "Views", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain("nameof(MainViewModel.DesktopIconsVisible)");
        source.Should().Contain("RepairBoxWindowsAfterDesktopIconToggle");
        source.Should().Contain("RepairBoxWindowsOnDesktopLayer");
        source.Should().Contain("GetDesktopHost(refresh: true)");
    }

    [Fact]
    public void MainWindow_UsesShellDefViewAsPrimaryVisibleDesktopHost()
    {
        var sourcePath = FindRepositoryFile("src", "DesktopBox", "Views", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain("FindShellDefView");
        source.Should().Contain("GetProgman");
        source.IndexOf("FindShellDefView", StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("GetProgman", StringComparison.Ordinal));
    }

    [Fact]
    public void MainWindow_RebuildsBoxWindowsWhenDesktopHostOrHandleIsGone()
    {
        var sourcePath = FindRepositoryFile("src", "DesktopBox", "Views", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain("GetWorkerW");
        source.Should().Contain("IsHandleAlive");
        source.Should().Contain("RefreshDesktopLayer");
        source.Should().Contain("SyncBoxWindows");
        source.Should().Contain("TryGetValue");
    }

    [Fact]
    public void MainWindow_ReleasesTrayIconNativeHandle()
    {
        var sourcePath = FindRepositoryFile("src", "DesktopBox", "Views", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain("GetHicon");
        source.Should().Contain("DestroyIcon");
        source.Should().Contain("Clone");
    }

    [Fact]
    public void DesktopIconsService_ReadsActualDesktopListViewVisibilityBeforeRegistryFallback()
    {
        var servicePath = FindRepositoryFile("src", "DesktopBox", "Services", "DesktopIconsService.cs");
        var service = File.ReadAllText(servicePath);
        var user32Path = FindRepositoryFile("src", "DesktopBox", "Native", "User32.cs");
        var user32 = File.ReadAllText(user32Path);

        user32.Should().Contain("FindDesktopListView");
        user32.Should().Contain("SysListView32");
        service.Should().Contain("FindDesktopListView");
        service.Should().Contain("IsWindowVisible");
        service.IndexOf("FindDesktopListView", StringComparison.Ordinal)
            .Should().BeLessThan(service.IndexOf("OpenSubKey", StringComparison.Ordinal));
    }

    [Fact]
    public void BoxControl_ConstrainsWrapPanelsToScrollViewportWidth()
    {
        var xamlPath = FindRepositoryFile("src", "DesktopBox", "Controls", "BoxControl.xaml");
        var xaml = File.ReadAllText(xamlPath);

        xaml.Should().Contain("x:Name=\"ItemsScroll\"");
        xaml.Should().Contain("x:Name=\"ItemsViewport\"");
        xaml.Should().Contain("Path=ViewportWidth");
        xaml.Should().Contain("AncestorType=ItemsControl");
        xaml.Should().Contain("ActualWidth");
    }

    [Fact]
    public void BoxControl_UsesHeaderMouseDragAndLargerResizeHitTargets()
    {
        var xamlPath = FindRepositoryFile("src", "DesktopBox", "Controls", "BoxControl.xaml");
        var xaml = File.ReadAllText(xamlPath);
        var codePath = FindRepositoryFile("src", "DesktopBox", "Controls", "BoxControl.xaml.cs");
        var code = File.ReadAllText(codePath);

        xaml.Should().Contain("MouseLeftButtonDown=\"OnHeaderDown\"");
        xaml.Should().Contain("MouseMove=\"OnHeaderMove\"");
        xaml.Should().Contain("MouseLeftButtonUp=\"OnHeaderUp\"");
        xaml.Should().Contain("LostMouseCapture=\"OnLostMouseCapture\"");
        xaml.Should().Contain("MouseEnter=\"OnBoxMouseEnter\"");
        xaml.Should().Contain("MouseLeave=\"OnBoxMouseLeave\"");
        xaml.Should().Contain("Height=\"10\" Margin=\"0,0,44,0\"");
        xaml.Should().Contain("Width=\"10\"");
        xaml.Should().Contain("Tag=\"S\"");
        xaml.Should().Contain("Height=\"18\" Margin=\"12,0,12,4\"");
        xaml.Should().Contain("Tag=\"E\"");
        xaml.Should().Contain("Width=\"18\" Margin=\"0,36,4,12\"");
        xaml.Should().Contain("Tag=\"NE\"");
        xaml.Should().Contain("Width=\"12\" Height=\"28\" Margin=\"0,4,0,0\"");
        xaml.Should().Contain("Tag=\"SE\"");
        xaml.Should().Contain("Width=\"28\" Height=\"28\" Margin=\"0,0,4,4\"");
        xaml.Should().Contain("DragStarted=\"OnResizeStarted\"");
        xaml.Should().Contain("DragCompleted=\"OnResizeCompleted\"");
        code.Should().Contain("OnHeaderMove");
        code.Should().Contain("_resizeBoxOrigin");
        code.Should().Contain("e.HorizontalChange * scale.X");
        code.Should().Contain("e.VerticalChange * scale.Y");
        code.Should().Contain("_isResizing = true");
        code.Should().Contain("if (!_isResizing || Vm is null) return");
        code.Should().Contain("BoxResize.Apply");
        code.Should().NotContain("PointToScreen(Mouse.GetPosition(this))");
        code.Should().Contain("OnResizeCompleted");
        code.Should().Contain("SystemParametersHelper.ClampIntoScreens");
        code.Should().Contain("NavigateByFirstLetter");
        code.Should().Contain("FirstLetterNavigator.FindNextIndex");
        code.Should().Contain("BringIntoView");
        code.Should().Contain("_subscribedVm.ViewModeChanged -= OnViewModeChanged");
        xaml.Should().NotContain("Width=\"{Binding Width}\"");
        xaml.Should().NotContain("Height=\"{Binding Height}\"");
    }

    [Fact]
    public void App_RegistersHoveredBoxFirstLetterKeyboardNavigator()
    {
        var appPath = FindRepositoryFile("src", "DesktopBox", "App.xaml.cs");
        var app = File.ReadAllText(appPath);
        var keyboardPath = FindRepositoryFile("src", "DesktopBox", "Controls", "FirstLetterKeyboardNavigator.cs");
        var keyboard = File.ReadAllText(keyboardPath);

        app.Should().Contain("AddSingleton<FirstLetterKeyboardNavigator>");
        keyboard.Should().Contain("SetWindowsHookEx");
        keyboard.Should().Contain("NavigationCharFromVirtualKey");
        keyboard.Should().Contain("TryHandleOnUiThread");
    }

    [Fact]
    public void ItemTile_UsesNativeFileDropAndNonBlockingDesktopOverlayForDragOut()
    {
        var tilePath = FindRepositoryFile("src", "DesktopBox", "Controls", "ItemTile.xaml.cs");
        var tile = File.ReadAllText(tilePath);
        var overlayPath = FindRepositoryFile("src", "DesktopBox", "Controls", "DesktopDropOverlay.cs");
        var overlay = File.ReadAllText(overlayPath);
        var dragPath = FindRepositoryFile("src", "DesktopBox", "Controls", "ItemDragDrop.cs");
        var drag = File.ReadAllText(dragPath);

        tile.Should().Contain("ItemDragDrop.CanDragAsFile");
        tile.Should().Contain("DragDrop.DoDragDrop");
        tile.Should().Contain("DesktopDropOverlay.TryCreate(boxBounds)");
        tile.Should().Contain("DroppedOnDesktop");
        tile.Should().NotContain("SetWindowPos(\r\n            boxHandle");
        tile.Should().NotContain("HwndSource.FromVisual");
        overlay.Should().Contain("AllowDrop = true");
        overlay.Should().Contain("ParentWindow = parent");
        overlay.Should().Contain("ApplyExclusionRegion");
        overlay.Should().Contain("BuildExclusionRects");
        overlay.Should().Contain("SetWindowRgn");
        overlay.Should().Contain("RGN_DIFF");
        overlay.Should().Contain("FindShellDefView");
        overlay.Should().Contain("GetProgman");
        drag.Should().Contain("DataFormats.FileDrop");
    }

    [Fact]
    public void BoxControl_RejectsDesktopBoxInternalDragOutDrops()
    {
        var codePath = FindRepositoryFile("src", "DesktopBox", "Controls", "BoxControl.xaml.cs");
        var code = File.ReadAllText(codePath);

        code.Should().Contain("ItemDragDrop.DragSourceItemFormat");
        code.Should().Contain("DragDropEffects.None");
        code.IndexOf("ItemDragDrop.DragSourceItemFormat", StringComparison.Ordinal)
            .Should().BeLessThan(code.IndexOf("DataFormats.FileDrop", StringComparison.Ordinal));
    }

    [Fact]
    public void BoxControl_SavesResizeOnlyWhenDragCompletes()
    {
        var codePath = FindRepositoryFile("src", "DesktopBox", "Controls", "BoxControl.xaml.cs");
        var code = File.ReadAllText(codePath);
        var resizeBody = code[code.IndexOf("private void OnResize(", StringComparison.Ordinal)
            ..code.IndexOf("private void OnResizeCompleted", StringComparison.Ordinal)];

        resizeBody.Should().NotContain("ScheduleSave");
        code.Should().Contain("private void OnResizeCompleted");
        code.Should().Contain("MainVm.ScheduleSave()");
    }

    [Fact]
    public void MainWindow_DoesNotUseRejectedLayeredAlphaPatch()
    {
        var sourcePath = FindRepositoryFile("src", "DesktopBox", "Views", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        source.Should().NotContain("SetLayeredWindowAttributes");
    }

    [Fact]
    public void MainWindow_DoesNotUseSystemWideMinimizeAllToShowBoxes()
    {
        var sourcePath = FindRepositoryFile("src", "DesktopBox", "Views", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        source.Should().NotContain("MinimizeAll");
        source.Should().Contain("RefreshDesktopLayer");
    }

    [Fact]
    public void IconExtractorService_ResolvesShortcutIconLocationBeforeFallbackingToLnk()
    {
        var sourcePath = FindRepositoryFile("src", "DesktopBox", "Services", "IconExtractorService.cs");
        var source = File.ReadAllText(sourcePath);
        var resolverPath = FindRepositoryFile("src", "DesktopBox", "Native", "ShellLinkResolver.cs");
        var resolver = File.ReadAllText(resolverPath);

        source.Should().Contain("ExtractShortcutIcon");
        source.Should().Contain("ResolveIconLocation");
        source.Should().Contain("ExtractIconResource");
        source.Should().Contain("ResolveTarget(lnkPath)");
        source.Should().Contain("ExtractPathIcon(lnkPath");
        resolver.Should().Contain("IconLocation");
        resolver.Should().Contain("WScript.Shell");
    }

    [Fact]
    public void IconExtractorService_CanExtractIconFromShortcutIconLocation()
    {
        RunOnSta(() =>
        {
            var tempDir = Directory.CreateTempSubdirectory("dbx_").FullName;
            try
            {
                var targetPath = Path.Combine(tempDir, "target.txt");
                File.WriteAllText(targetPath, "x");

                var shortcutPath = Path.Combine(tempDir, "shortcut.lnk");
                var iconSource = Path.Combine(Environment.SystemDirectory, "shell32.dll");
                CreateShortcut(shortcutPath, targetPath, iconSource, 3);

                var resolved = ShellLinkResolver.ResolveIconLocation(shortcutPath);
                resolved.iconPath.Should().NotBeNull();
                resolved.iconPath!.Should().EndWith("shell32.dll");
                resolved.iconIndex.Should().Be(3);

                var extractor = new IconExtractorService();
                var png = extractor.Extract(shortcutPath);

                png.Should().NotBeNull();
                File.Exists(png!).Should().BeTrue();
                new FileInfo(png!).Length.Should().BeGreaterThan(0);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        });
    }

    [Fact]
    public void ShellChangeNotifierCanReRegisterAfterTaskbarRestart()
    {
        var sourcePath = FindRepositoryFile("src", "DesktopBox", "Services", "ShellChangeNotifierService.cs");
        var source = File.ReadAllText(sourcePath);
        var ifacePath = FindRepositoryFile("src", "DesktopBox", "Services", "IShellChangeNotifierService.cs");
        var iface = File.ReadAllText(ifacePath);

        source.Should().Contain("Register(IntPtr hwnd, bool force = false)");
        source.Should().Contain("SHChangeNotifyDeregister");
        source.Should().Contain("force");
        iface.Should().Contain("bool force = false");
    }

    [Fact]
    public void NativeShellMenu_SetsExplorerCompatibleClipboardForCutAndCopy()
    {
        var sourcePath = FindRepositoryFile("src", "DesktopBox.ShellMenu", "DesktopBox.ShellMenu.cpp");
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain("GetCommandString");
        source.Should().Contain("GCS_VERBW");
        source.Should().Contain("SetFileClipboard");
        source.Should().Contain("CF_HDROP");
        source.Should().Contain("Preferred DropEffect");
        source.Should().Contain("DROPEFFECT_MOVE");
        source.Should().Contain("DROPEFFECT_COPY");
    }

    [Fact]
    public void NativeShellMenu_OpensAllPropertiesThroughShellExecute()
    {
        var sourcePath = FindRepositoryFile("src", "DesktopBox.ShellMenu", "DesktopBox.ShellMenu.cpp");
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain("IsPropertiesCommand");
        source.Should().Contain("ShowShellProperties");
        source.Should().Contain("ShellExecuteExW");
        source.Should().Contain("SEE_MASK_INVOKEIDLIST");
        source.Should().Contain("L\"properties\"");
        source.Should().Contain("if (IsPropertiesCommand(verb))");
        source.Should().NotContain("IsShortcutPath(path) && IsPropertiesCommand");
    }

    [Fact]
    public async Task StaTaskRunner_DoesNotBlockCallerWhileWorkRuns()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var task = StaTaskRunner.Run(() =>
        {
            started.Set();
            release.Wait();
            return Thread.CurrentThread.GetApartmentState();
        });

        started.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        task.IsCompleted.Should().BeFalse();

        release.Set();
        var apartment = await task;
        apartment.Should().Be(ApartmentState.STA);
    }

    [Theory]
    [InlineData(Shell32.SHCNE_UPDATEIMAGE)]
    [InlineData(Shell32.SHCNE_ASSOCCHANGED)]
    [InlineData(Shell32.SHCNE_DELETE)]
    [InlineData(Shell32.SHCNE_UPDATEDIR)]
    [InlineData(Shell32.SHCNE_UPDATEITEM)]
    public void ShellNotificationsThatCanChangeRecycleBin_RefreshSystemIcons(uint evt)
    {
        ShellChangeNotifierService.ShouldRefreshSystemIcons(evt).Should().BeTrue();
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }

    private static string? GetItemMetadata(System.Xml.Linq.XElement item, string name) =>
        item.Attribute(name)?.Value ?? item.Element(name)?.Value;

    private static void CreateShortcut(string shortcutPath, string targetPath, string iconSource, int iconIndex)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        shellType.Should().NotBeNull("WScript.Shell should be available on Windows");

        dynamic shell = Activator.CreateInstance(shellType!)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
        shortcut.IconLocation = $"{iconSource},{iconIndex}";
        shortcut.Save();
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        exception?.Throw();
    }

}
