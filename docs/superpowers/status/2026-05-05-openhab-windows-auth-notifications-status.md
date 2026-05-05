# openHAB Windows Auth & Notifications Status

Date: 2026-05-05

## Source Documents

- Design spec: `docs/superpowers/specs/2026-05-04-openhab-windows-sitemap-client-design.md`
- Connected homepage status: `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`
- Auth & notifications plan: `docs/superpowers/plans/2026-05-05-openhab-windows-auth-notifications.md`

## Completed

### Authorization

- Added `ICredentialStore` abstraction and `WindowsCredentialStore` implementation backed by `PasswordVault`.
- Added `Bearer` token header injection in `OpenHabHttpClient` with credential-safe exception messages.
- Added `HasLocalToken` / `HasCloudToken` to `AppSettings` with `SetApiTokenAsync`, `ClearApiTokenAsync`, `GetApiTokenAsync`, and `InitializeAsync` controller methods.
- Added API token password boxes to the tray settings UI (local and cloud, per-endpoint).
- Wired credential store and token-aware HTTP client factory into tray app startup.
- Fixed duplicate credential handling in `WindowsCredentialStore.StoreAsync`.
- Added thread-safe settings updates with lock, exhaustive `TransportKind` dispatch, and startup hydration of token flags.
- Added JSON file persistence for app settings (`%LocalAppData%\OpenHab.WinApp\settings.json`). Settings survive app restarts. Token flags excluded from serialization (tokens stored in PasswordVault only).
- Added crash tolerance for unpackaged apps: credential store and notification initialization wrapped in try-catch for graceful degradation.

### Notifications

- Created `OpenHab.Windows.Notifications` project with cloud notification polling via `NotificationPoller`.
- Added Windows toast display via `ToastService` using `AppNotificationManager`.
- Wired notification polling into tray app startup (cloud-only/automatic modes).
- Toast activation opens the main window.
- Added thread safety (Interlocked), bounded dedup set, DispatcherQueue-aware event marshaling, and error diagnostics to the poller.

### Bug Fixes Applied

- **Frame widget rendering**: Added `Frame` to `SitemapWidgetType` enum. JSON parser now extracts and flattens inline child widgets from Frame containers so switches and other controls inside frames render correctly.
- **Settings persistence**: Settings now saved to JSON file on every change and loaded at startup. Endpoints, sitemap name, skin, and endpoint mode survive restarts.
- **Unpackaged app crash**: `WindowsCredentialStore` construction and `NotificationPoller` initialization wrapped in try-catch to prevent crash when WinRT APIs are unavailable without admin rights.

## Verification

- `dotnet test OpenHab.Windows.sln`: **100 tests run, 100 passed, 0 failed, 0 skipped** (4 test projects).
- `dotnet build OpenHab.Windows.sln --configuration Release`: build succeeded, **0 warnings, 0 errors**.

### Test breakdown

| Project | Tests | Status |
|---------|-------|--------|
| OpenHab.Core.Tests | 27 | All pass |
| OpenHab.App.Tests | 33 | All pass |
| OpenHab.Sitemaps.Tests | 30 | All pass |
| OpenHab.Rendering.Tests | 10 | All pass |
| **Total** | **100** | **0 failed** |

### Tests added this slice

| Area | Tests |
|------|-------|
| Credential store (store, retrieve, remove, blank rejection) | 5 |
| HTTP client auth (header injection, omission, token redaction, URL leak) | 4 |
| App settings tokens (set, clear, get, no-store errors, hydration, non-interference) | 11 |
| Frame widget parsing (flatten children from Frame containers) | 1 |
| **Total new** | **21** |

## Still Out Of Scope

- openHAB event stream subscriptions and live item updates.
- OAuth2 / myopenhab.org login flow.
- Notification history or inbox UI.
- Rich notification actions (dismiss, mark read, reply).
- Subpage navigation.
- Offline cache persistence.
- WebView/Main UI fallback routing.
- MSIX packaging and signing.
