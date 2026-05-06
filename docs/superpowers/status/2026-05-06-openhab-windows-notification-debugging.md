# Notification Debugging & Diagnostic Logging

Date: 2026-05-06

## Problem

Cloud connector notifications were not being delivered. The app polled the cloud API but gave no indication why notifications weren't appearing — no visible errors, no logs, no diagnostic data.

## Root Causes Found

### 1. Wrong API endpoint (primary cause)
The `NotificationPoller` was polling `{cloudBaseUri}/rest/notifications?limit=20`. The openHAB Cloud service (myopenhab.org) does **not** expose notifications under `/rest/`. The correct endpoint is:

```
GET {cloudBaseUri}/api/v1/notifications
```

- `/rest/` → local openHAB REST API namespace
- `/api/v1/` → openHAB Cloud service API namespace

Source: [openhab-cloud README](https://github.com/openhab/openhab-cloud/blob/main/README.md) and the openHAB Android app's `CloudNotificationListFragment.kt`.

### 2. JSON model field mismatch
The cloud API returns `_id` (MongoDB ObjectId), not `Id`. The C# `CloudNotification` record used standard property names that didn't match the MongoDB document fields. Fixed with `[JsonPropertyName("_id")]` and other explicit mappings.

### 3. Silent error swallowing
The notification pipeline had zero diagnostic output:
- `PollOnceAsync` silently returned on non-2xx HTTP responses: `if (!response.IsSuccessStatusCode) return;`
- `PollLoopAsync` caught exceptions but stored `LastError` — a field never read by any code
- `StartNotificationPolling` wrapped everything in `catch {}` with no logging
- The `LastError` field on `NotificationPoller` was a dead property — set but never consumed

### 4. Toast notification COM registration failure
`AppNotificationManager.Default.Register()` throws `COMException (REGDB_E_CLASSNOTREG)` on this system. This is the Windows push notification COM class — it requires either:

- **MSIX packaging** (not viable: Windows App SDK auto-initializer crashes with `CLR20r3` during packaged launch on this machine)
- **Sparse package identity** (not implemented)

**Attempted fix — MSIX packaging**: The project was configured for single-project MSIX (`Package.appxmanifest`, certificate, `WindowsPackageType=MSIX`), but the Windows App Runtime auto-initializer crashes with `REGDB_E_CLASSNOTREG` at module load time (`.cctor()`), preventing the app from launching entirely. The crash occurs in `DeploymentManagerCS.AutoInitialize.AccessWindowsAppSDK()` before any managed code executes.

**Attempted fix — Shortcut registration**: A Start menu shortcut with `AppUserModelId=OpenHab.OpenHabWinApp` was created via COM interop (`IShellLinkW` + `IPropertyStore`). The shortcut provides app identity but doesn't register the `AppNotificationManager` COM server. `REGDB_E_CLASSNOTREG` persists.

**Current state**: `ToastService` gracefully degrades when `AppNotificationManager` is unavailable. Notifications are polled from the cloud, parsed, and logged to the diagnostic file. Windows toast display is skipped with a warning.

## Diagnostic Logging Implementation

### Log file location
```
%LOCALAPPDATA%\OpenHab.WinApp\diagnostics.log
```
Same directory as `settings.json`. Timestamped, append-only. Thread-safe via lock.

### Logger API (`OpenHab.Core.DiagnosticLogger`)
- `Info(message)` — normal flow events
- `Warn(message)` — non-fatal issues
- `Error(message, exception?)` — errors with optional exception details
- `LogPath` — static property returning the full log file path

### Logged events — Notification pipeline

| Component | Events logged |
|-----------|--------------|
| `App.xaml.cs` | Startup, LocalOnly skip, credential resolution, poller creation, notification receipt (severity only), shutdown |
| `NotificationPoller` | Start/stop, poll loop entry, auth mode (Bearer/Basic/none), HTTP status codes, per-notification ID+severity, poll counts, errors with exceptions |
| `ToastService` | Registration success/failure, toast display (title only), user activation |
| `ShortcutRegistrar` | Shortcut creation path, already-present skip, registration failures |

### What is NOT logged
- Credentials, tokens, or passwords
- Full notification message bodies (only IDs and severities)
- Full endpoint URLs (only host components)

### Viewing logs
The settings panel in `MainWindow.xaml` has a "View diagnostic logs" button that opens the log file in the default text editor.

## Debugging Workflow

1. Launch the app
2. Check `%LOCALAPPDATA%\OpenHab.WinApp\diagnostics.log`
3. Diagnose from the log flow:
   - No "Starting notification polling" → app didn't reach notification setup
   - "EndpointMode is LocalOnly" → intended skip, change mode in settings
   - "Cloud credentials resolved: no" → enter cloud username/password in settings
   - "Toast notifications unavailable" → expected on this system, notifications logged to file only
   - HTTP 404 → wrong endpoint (fixed now)
   - HTTP 401 → invalid credentials
   - "Polled N new notifications" → working correctly
   - "Poll error" with exception → network/auth issue, check exception details

## Files Changed

### Core changes
- `src/OpenHab.Core/DiagnosticLogger.cs` — new file-based logger
- `src/OpenHab.Windows.Notifications/CloudNotification.cs` — added JSON property mappings
- `src/OpenHab.Windows.Notifications/NotificationPoller.cs` — fixed endpoint, added logging
- `src/OpenHab.Windows.Notifications/ToastService.cs` — graceful COM degradation, added logging
- `src/OpenHab.Windows.Notifications/ShortcutRegistrar.cs` — COM interop for Start menu shortcut registration
- `src/OpenHab.Windows.Tray/App.xaml.cs` — added notification pipeline logging
- `src/OpenHab.Windows.Tray/MainWindow.xaml` + `.cs` — "View diagnostic logs" button

### Config changes
- `src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj` — `WindowsPackageType=None`, RID config
- `src/OpenHab.Windows.Notifications/OpenHab.Windows.Notifications.csproj` — `WindowsPackageType=None`

### MSIX artifacts (not active, preserved for future)
- `src/OpenHab.Windows.Tray/Package.appxmanifest` — package manifest
- `src/OpenHab.Windows.Tray/OpenHab_TemporaryKey.pfx` — test signing certificate
- `src/OpenHab.Windows.Tray/Properties/launchSettings.json` — VS launch profiles

## Next Steps for Notification Toasts

To get Windows toast notifications working:
1. Resolve the Windows App Runtime auto-initializer crash on this machine (investigate `DeploymentManagerCS.AutoInitialize.AccessWindowsAppSDK()` failure)
2. OR implement a custom toast window (small WinUI popup near the tray area)
3. OR use the `NotifyIcon.ShowBalloonTip()` fallback
