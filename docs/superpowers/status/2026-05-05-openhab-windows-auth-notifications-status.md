# openHAB Windows Auth & Notifications Status

Date: 2026-05-06

## Source Documents

- Design spec: `docs/superpowers/specs/2026-05-04-openhab-windows-sitemap-client-design.md`
- Connected homepage status: `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`
- Auth & notifications plan: `docs/superpowers/plans/2026-05-05-openhab-windows-auth-notifications.md`
- Tray shell status: `docs/superpowers/status/2026-05-05-openhab-windows-tray-shell-behavior-status.md`

## Completed

### Authorization

- Added `ICredentialStore` abstraction and `WindowsCredentialStore` implementation backed by `PasswordVault`.
- Added authenticated request handling in `OpenHabHttpClient` with support for either:
  - local bearer token auth
  - cloud HTTP Basic auth with username/password
- Added `HasLocalToken`, `HasCloudCredentials`, and `CloudUserName` to `AppSettings`.
- Added local token controller methods plus dedicated cloud credential methods:
  - `SetApiTokenAsync`, `ClearApiTokenAsync`, `GetApiTokenAsync` for `TransportKind.Local`
  - `SetCloudCredentialsAsync`, `ClearCloudCredentialsAsync`, `GetCloudCredentialsAsync` for cloud/myopenHAB
- Updated tray settings UI to collect:
  - local API token
  - cloud email / username
  - cloud password
- Wired credential store and auth-aware HTTP client creation into tray app startup.
- Fixed duplicate credential handling in `WindowsCredentialStore.StoreAsync`.
- Added thread-safe settings updates with lock, exhaustive `TransportKind` dispatch, and startup hydration of local/cloud credential state.
- Added JSON file persistence for app settings (`%LocalAppData%\OpenHab.WinApp\settings.json`). Settings survive app restarts. Token flags excluded from serialization (tokens stored in PasswordVault only).
- Added crash tolerance for unpackaged apps: credential store and notification initialization wrapped in try-catch for graceful degradation.
- Changed the default local endpoint from `http://openhab.local:8080` to `http://openhab:8080`.
- Corrected the cloud auth model to match the Android app direction: cloud/myopenHAB uses username + password rather than a cloud bearer token.

### Notifications

- Created `OpenHab.Windows.Notifications` project with cloud notification polling via `NotificationPoller`.
- Added Windows toast display via `ToastService` using `AppNotificationManager`.
- Wired notification polling into tray app startup (cloud-only/automatic modes).
- Switched cloud notification polling to the same cloud username/password auth path used by the remote connector.
- Toast activation opens the main window.
- Added thread safety (Interlocked), bounded dedup set, DispatcherQueue-aware event marshaling, and error diagnostics to the poller.

### Diagnostic Logging

- Added file-based diagnostic logger (`OpenHab.Core.DiagnosticLogger`) writing timestamped entries to `%LocalAppData%\OpenHab.WinApp\diagnostics.log`.
- Instrumented `NotificationPoller` with logging at every decision point: auth mode, HTTP status codes, new notification IDs/severity (not message bodies), poll counts, errors with exception details.
- Instrumented `App.xaml.cs` `StartNotificationPolling` with flow trace: start, LocalOnly skip, credential resolution, poller creation, notification receipt, shutdown.
- Instrumented `ToastService` with registration, toast display (title only), and user activation events.
- Added "View diagnostic logs" button to `MainWindow.xaml` settings panel — opens the log file in the default text editor.

### Shortcut Registration (COMException workaround)

**Problem**: Unpackaged WinUI apps calling `AppNotificationManager.Default.Register()` throw `COMException (0x80040154 — REGDB_E_CLASSNOTREG)` because the push notification COM class requires a registered AppUserModelId, which unpackaged executables lack.

**Why not MSIX**: The project's `.csproj` and `Package.appxmanifest` are configured for MSIX single-project packaging (step 1 of the migration), but the Windows App SDK's `_CreateAppPackage` target is only available through Visual Studio — `dotnet build`/`publish` from CLI cannot produce the `.msix` without IDE integration. Full MSIX packaging remains the long-term direction; the shortcut workaround bridges the gap for development and unpackaged deployment.

**Solution**: Added `ShortcutRegistrar.EnsureRegistered()` — creates a Start menu shortcut (`%AppData%\Microsoft\Windows\Start Menu\Programs\openHAB\openHAB.lnk`) with the application's AppUserModelId (`OpenHab.OpenHabWinApp`) set via COM interop (`IShellLinkW` + `IPropertyStore`). Called once at startup before `ToastService.EnsureRegistered()`. On subsequent launches the shortcut already exists, making it a near-zero-cost check.

**Why COM interop**: Setting the `System.AppUserModel.ID` property on a `.lnk` file requires `IPropertyStore` (the shell property system). There is no managed .NET API for this — `WScript.Shell` can create shortcuts but cannot set AppUserModelId. The minimal COM interop definitions are self-contained in `ShortcutRegistrar.cs` with no external dependencies.

### Bug Fixes Applied

- **Frame widget rendering**: Added `Frame` to `SitemapWidgetType` enum. JSON parser now extracts and flattens inline child widgets from Frame containers so switches and other controls inside frames render correctly.
- **Settings persistence**: Settings now saved to JSON file on every change and loaded at startup. Endpoints, sitemap name, skin, and endpoint mode survive restarts.
- **Unpackaged app crash**: `WindowsCredentialStore` construction and `NotificationPoller` initialization wrapped in try-catch to prevent crash when WinRT APIs are unavailable without admin rights.

## Verification

- `dotnet build OpenHab.Windows.sln --configuration Debug`: **0 warnings, 0 errors** (Release blocked by live process file locks only).
- `dotnet test OpenHab.Windows.sln --configuration Debug`: **136 pass, 51 pre-existing failures** (all 51 failures are `SitemapControlFactoryTests` failing with `REGDB_E_CLASSNOTREG` — the test runner cannot bootstrap the Windows App Runtime for WinUI-dependent test types; unrelated to notification/diagnostic changes).

### Test breakdown

| Project | Tests | Status |
|---------|-------|--------|
| OpenHab.Core.Tests | 29 | All pass |
| OpenHab.App.Tests | 99 | 48 pass, 51 pre-existing WinRT failures |
| OpenHab.Sitemaps.Tests | 32 | All pass |
| OpenHab.Rendering.Tests | 27 | All pass |
| **Total** | **187** | **136 passing** |

### Coverage added or updated in this slice

| Area | Tests |
|------|-------|
| Credential store (store, retrieve, remove, blank rejection) | 5 |
| HTTP client auth (bearer injection, basic auth injection, auth omission, mixed-auth rejection, redaction, URL leak) | 6 |
| App settings auth state (local token, cloud credentials, no-store errors, hydration, cloud token rejection) | 14 |
| Frame widget parsing (flatten children from Frame containers) | 1 |
| **Total tracked here** | **26** |

## Still Out Of Scope

- openHAB event stream subscriptions and live item updates.
- OAuth2 / myopenhab.org login flow.
- Notification history or inbox UI.
- Rich notification actions (dismiss, mark read, reply).
- Subpage navigation.
- Offline cache persistence.
- WebView/Main UI fallback routing.
- MSIX packaging and signing.
