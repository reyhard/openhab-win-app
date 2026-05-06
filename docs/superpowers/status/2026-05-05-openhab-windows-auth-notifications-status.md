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

### Bug Fixes Applied

- **Frame widget rendering**: Added `Frame` to `SitemapWidgetType` enum. JSON parser now extracts and flattens inline child widgets from Frame containers so switches and other controls inside frames render correctly.
- **Settings persistence**: Settings now saved to JSON file on every change and loaded at startup. Endpoints, sitemap name, skin, and endpoint mode survive restarts.
- **Unpackaged app crash**: `WindowsCredentialStore` construction and `NotificationPoller` initialization wrapped in try-catch to prevent crash when WinRT APIs are unavailable without admin rights.

## Verification

- `dotnet build OpenHab.Windows.sln --configuration Release`: passed in `main`, `0` warnings, `0` errors.
- `dotnet test OpenHab.Windows.sln --configuration Release`: passed in `main`.

### Test breakdown

| Project | Tests | Status |
|---------|-------|--------|
| OpenHab.Core.Tests | 29 | All pass |
| OpenHab.App.Tests | 45 | All pass |
| OpenHab.Sitemaps.Tests | 30 | All pass |
| OpenHab.Rendering.Tests | 10 | All pass |
| **Total** | **114** | **0 failed** |

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
