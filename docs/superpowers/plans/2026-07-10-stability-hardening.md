# DesktopBox Stability Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the reviewed native memory, crash-isolation, persistence-loss, notification-amplification, lifecycle, release, and dependency defects.

**Architecture:** Move Shell context menus into a native helper process, make persistence recoverable, filter Shell events by decoded targets, retain offline references, and give OS-backed resources explicit lifetimes. Preserve existing UI behavior except that missing targets remain visible instead of being silently removed.

**Tech Stack:** .NET 8, WPF, xUnit, Win32/COM C++, Inno Setup, GitHub Actions

---

### Task 1: Native Shell Menu Safety and Isolation

**Files:**
- Modify: `src/DesktopBox.ShellMenu/DesktopBox.ShellMenu.cpp`
- Modify: `src/DesktopBox.ShellMenu/build_dll.bat`
- Create: `src/DesktopBox/Services/ShellMenuProcessRunner.cs`
- Create: `src/DesktopBox/Services/IShellMenuRunner.cs`
- Modify: `src/DesktopBox/Controls/ItemTile.xaml.cs`
- Modify: `src/DesktopBox/App.xaml.cs`
- Modify: `src/DesktopBox/DesktopBox.csproj`
- Modify: `release.ps1`
- Modify: `.github/workflows/build.yml`
- Test: `src/DesktopBox.Tests/ShellBehaviorTests.cs`

- [ ] Add failing tests proving the native source uses character counts, transfers clipboard ownership independently, builds a Windows helper EXE, and managed code launches the helper instead of P/Invoke.
- [ ] Run the focused tests and confirm failures describe the current DLL implementation.
- [ ] Convert the native entry point to a helper EXE, fix both memory-safety defects, add managed asynchronous process execution with timeout/crash fallback, and register it through DI.
- [ ] Run the focused tests and compile the helper with MSVC.

### Task 2: Recoverable Persistence

**Files:**
- Modify: `src/DesktopBox/Services/JsonStoreService.cs`
- Modify: `src/DesktopBox/ViewModels/MainViewModel.cs`
- Modify: `src/DesktopBox/ViewModels/SettingsViewModel.cs`
- Modify: `src/DesktopBox/Views/MainWindow.xaml.cs`
- Test: `src/DesktopBox.Tests/JsonStoreServiceTests.cs`
- Test: `src/DesktopBox.Tests/MainViewModelTests.cs`

- [ ] Add failing tests for backup creation, corrupt-primary quarantine, backup restoration, save fallback, and preservation of corrupt input.
- [ ] Run focused tests and confirm the old empty-config behavior fails them.
- [ ] Implement backup/recovery and logging while keeping storage exceptions visible to callers; expose one non-blocking save-failure notification.
- [ ] Run persistence and ViewModel tests.

### Task 3: Shell Notification Filtering and Offline References

**Files:**
- Modify: `src/DesktopBox/Native/Shell32.cs`
- Modify: `src/DesktopBox/Services/ShellChangeNotifierService.cs`
- Modify: `src/DesktopBox/ViewModels/MainViewModel.cs`
- Modify: `src/DesktopBox/Services/IShellChangeNotifierService.cs`
- Test: `src/DesktopBox.Tests/ShellBehaviorTests.cs`
- Test: `src/DesktopBox.Tests/MainViewModelTests.cs`

- [ ] Add failing tests for desktop/public-desktop path classification, unrelated-event suppression, Recycle Bin routing, and retention of unavailable targets.
- [ ] Run focused tests and confirm global event behavior fails them.
- [ ] Filter at registration scope, limit file-level events to the Recycle Bin PIDL, remove automatic global pruning, and coalesce only relevant refresh work.
- [ ] Run Shell and ViewModel tests.

### Task 4: Theme and Resource Lifetimes

**Files:**
- Modify: `src/DesktopBox/Services/IThemeService.cs`
- Modify: `src/DesktopBox/Services/ThemeService.cs`
- Modify: `src/DesktopBox/ViewModels/SettingsViewModel.cs`
- Modify: `src/DesktopBox/Services/ShellChangeNotifierService.cs`
- Modify: `src/DesktopBox/Views/MainWindow.xaml.cs`
- Modify: `src/DesktopBox/Native/ShellLinkResolver.cs`
- Modify: `src/DesktopBox/Services/CategorizerService.cs`
- Test: `src/DesktopBox.Tests/ThemeServiceTests.cs`
- Test: `src/DesktopBox.Tests/ShellBehaviorTests.cs`

- [ ] Add failing tests for follow-mode unsubscribe, dispatcher marshalling contracts, menu disposal, notifier disposal, and COM release contracts.
- [ ] Run focused tests and confirm failures.
- [ ] Implement deterministic lifetimes and UI-thread theme application.
- [ ] Run focused tests.

### Task 5: Release and Dependency Consistency

**Files:**
- Modify: `DesktopBox.iss`
- Modify: `release.ps1`
- Modify: `.github/workflows/build.yml`
- Modify: `src/DesktopBox.Tests/DesktopBox.Tests.csproj`
- Modify: `README.md`
- Modify: `使用说明.md`
- Test: `src/DesktopBox.Tests/ShellBehaviorTests.cs`

- [ ] Add failing tests that reject hard-coded installer version drift and require helper packaging.
- [ ] Run focused tests and confirm current version mismatch fails.
- [ ] Parameterize installer version, update release/CI packaging, update test packages, and align documentation with retained missing references and current multi-monitor behavior.
- [ ] Run focused tests and package vulnerability scan.

### Task 6: Full Verification and Independent Review

- [ ] Run `dotnet test DesktopBox.sln -c Release`.
- [ ] Run `dotnet build DesktopBox.sln -c Release --no-restore -warnaserror`.
- [ ] Compile the native helper with `src\\DesktopBox.ShellMenu\\build_dll.bat`.
- [ ] Run `dotnet list DesktopBox.sln package --vulnerable --include-transitive`.
- [ ] Collect coverage and compare critical-path coverage with the baseline.
- [ ] Run an independent code-review and architecture-review pass, fix every blocking finding, then repeat verification.
