# DesktopBox Stability Hardening Design

## Scope

Fix every defect identified in the 2026-07-10 repository review without adding unrelated product features. The user-visible behavior remains reference-only desktop organization, with improved recovery and clearer handling of temporarily unavailable targets.

## Architecture

### Native shell menu isolation

Replace the in-process P/Invoke DLL boundary with a small native Windows helper executable. The WPF process launches the helper with the target path and menu coordinates, waits asynchronously, and interprets the helper exit code. Third-party `IContextMenu` implementations then execute outside the DesktopBox address space. The helper retains the existing menu behavior, fixes command-buffer sizing, and transfers clipboard ownership one handle at a time.

### Recoverable persistence

`JsonStoreService` owns primary, temporary, backup, and corrupt-copy files. Successful replacement preserves the previous valid configuration as `.bak`. A corrupt primary is moved aside with a timestamp; a valid backup is restored. Saving falls back from `File.Replace` to copy-and-overwrite semantics for portable filesystems that do not support replacement. Save failures remain exceptions at the storage boundary and are logged and surfaced through a single application notification.

### Relevant shell notifications only

Filter at registration scope instead of decoding every global filesystem event. Global registration covers only image-list and file-association state; file-level registration is limited to the Recycle Bin PIDL. Ordinary filesystem activity therefore never reaches the application. A desktop deletion or rename no longer removes missing targets automatically. Existing references become unavailable naturally and may recover when a drive or sync provider returns; explicit open/right-click checks report unavailability without removing the reference.

### Deterministic lifecycle

Theme updates marshal to the WPF dispatcher. Switching away from system-follow mode unsubscribes from `SystemEvents`. Shell registration, timers, tray menus, box windows, and shortcut COM objects receive deterministic cleanup. Fatal or unknown dispatcher exceptions are logged, but only explicitly recoverable UI exceptions are marked handled.

### Release consistency

The project version is the single release source. Packaging scripts pass that version to Inno Setup, CI builds the installer, and the native helper is included beside the application. Test dependencies are updated so vulnerability scanning is clean.

## Error Handling

- Native helper start failure: fall back to the managed menu because no native command could have run.
- Native helper crash after start: log and report the failure without opening a second menu, avoiding duplicate side effects when the helper fails after invoking a Shell command.
- Native helper hang: allow normal menu interaction, but enforce a generous timeout and terminate only the helper.
- Corrupt configuration: preserve the corrupt file, recover backup when possible, never overwrite the only forensic copy.
- Save failure: preserve dirty in-memory state, log details, and show one non-blocking tray notification.
- Offline targets: keep references; do not interpret `File.Exists == false` as proof of permanent deletion.

## Testing

- Tests must be written and observed failing before production changes.
- Add behavior tests for native source safety contracts, helper launch construction, config backup/recovery/fallback, shell path classification, offline-reference retention, theme subscription transitions, and version propagation.
- Keep the complete managed suite green and compile the C++ helper with MSVC.
- Run release build with warnings as errors, package vulnerability scan, and coverage collection.

## Acceptance Criteria

1. No in-process call to `DesktopBox.ShellMenu.dll` remains.
2. Native command buffers and clipboard ownership follow Win32 contracts.
3. Corrupt config is preserved and valid backup recovery is automatic.
4. Non-desktop filesystem activity does not trigger item pruning or system-icon refresh.
5. Temporarily unavailable paths remain in boxes.
6. Manual theme selection remains stable after disabling system-follow mode.
7. Native and managed resources are deterministically disposed.
8. Application, installer, CI artifacts, and documentation use one version source.
9. Production and test dependency vulnerability scans are clean.
