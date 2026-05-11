# openHAB Windows Device Info Sync And Settings Rewrite Design

Date: 2026-05-11

## Purpose

Implement audit backlog item B8 by adding configurable Windows device and connectivity telemetry, exposed in the UI as **Device Info Sync**, and rewrite the right-side Settings tab into a Windows 11-style grouped settings surface.

This design keeps the main window shape intact: sitemap content remains on the left, and the right side switches between Notifications and Settings.

## Context

The repository already has a small core device-state mapper:

- `src/OpenHab.Core/DeviceState/DeviceStateSnapshot.cs`
- `src/OpenHab.Core/DeviceState/DeviceStateMapping.cs`
- `src/OpenHab.Core/DeviceState/DeviceStateMapper.cs`

That mapper currently covers battery level, charging state, locked state, and session state. The missing product feature is user-configurable Item mapping, Windows value collection, and a sender/orchestrator that writes those values to openHAB.

The openHAB Android app provides the configuration model to follow: each device-information signal is individually enabled, each has a default Item name, and an optional device identifier can prefix Item names for multi-device households. Android distinguishes event-based and scheduled device information sends. Windows should copy that user model, but use Windows-relevant signals and Windows APIs.

References:

- openHAB Android device information docs: https://www.openhab.org/docs/apps/android.html
- Windows Focus session API docs: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/focus-session

## Goals

- Add opt-in Device Info Sync settings with a device identifier, sync interval, status, and per-signal openHAB Item mappings.
- Expand telemetry signals to cover battery, charging, lock/session, Wi-Fi, runtime connection state, and Windows Focus/DND state.
- Rewrite the Settings tab into a Windows Settings-style list of category rows with drill-in subpages.
- Keep Settings in the current right-side panel next to the sitemap, not a full-window replacement.
- Keep telemetry non-blocking: failures must not break sitemap browsing, commands, or notifications.
- Avoid sending sensitive network or credential data.

## Non-Goals

- Do not add Bluetooth, display state, idle time, power mode, next alarm, IP address, BSSID, MAC address, or broad Android parity in this first release.
- Do not move WinUI, Windows API, or dispatcher concerns into `OpenHab.Core`, `OpenHab.Sitemaps`, `OpenHab.Rendering`, or `OpenHab.App`.
- Do not redesign the left-side sitemap renderer as part of this work.
- Do not add cloud notification feature changes beyond moving its poll interval into the rewritten settings UI.

## Architecture

`OpenHab.Core` owns pure telemetry data and mapping. `DeviceStateSnapshot`, `DeviceStateMapping`, and `DeviceStateMapper` expand to include Wi-Fi connected/name, openHAB connection state, and Focus/DND state. The mapper remains deterministic: snapshot plus mapping produces zero or more `DeviceStateUpdate` values.

`OpenHab.App` owns persisted settings and the UI-independent sync service. Settings get a `DeviceInfoSyncSettings` object with:

- enabled flag
- device identifier
- sync interval in minutes
- per-signal Item mappings

The sync service takes an openHAB client, a snapshot source, and settings. It sends only configured signals through `IOpenHabClient.SetItemStateAsync`. It owns scheduling, batching, last-result status, and failure isolation.

`OpenHab.Windows.Tray` owns Windows-specific collection and shell wiring. It provides collectors for battery/power, session lock/unlock, Wi-Fi/network, Focus state, and runtime connection state. `App.xaml.cs` starts the Device Info Sync service after settings and credentials are initialized, beside notification polling, and disposes it during shutdown.

## Settings UX

The right-side `Pivot` remains. Notifications remain one tab. Settings becomes a Windows Settings-style tab with top-level category rows. Each row uses an icon, title, subtitle, and chevron. Selecting a row replaces the Settings content with a subpage header and back button.

Top-level Settings categories:

- `Connection`: endpoint mode, local/cloud endpoints, local token, cloud credentials, selected sitemap.
- `General`: launch at startup, flyout width, animation speed, notification poll interval, and other small app behavior settings.
- `Appearance`: skin, follow system theme, Windows 11 icons, chart quality.
- `Device Info Sync`: device identifier, enable switch, sync interval, signal Item mappings, and local sync status.
- `About`: version and diagnostic log button.

`General` intentionally absorbs Flyout and Notifications settings for now. Those categories can split later if they grow beyond a few options.

## Device Info Sync UX

The user-facing name is **Device Info Sync**. Internal types may continue using `DeviceState` or `DeviceTelemetry` where that fits existing code, but UI text should not call this analytics or telemetry.

Subpage layout:

```text
< Settings

Device Info Sync
Sync Windows device information to openHAB Items

[ Sync device information ]  On/Off

Device identity
  Device identifier      Desk
  Sync interval          15 minutes

Sync status
  Last sync              14:31
  Last result            7 Items updated

Signals
  Battery level          DeskBatteryLevel        Number
  Charging state         DeskChargingState       Switch/String
  Locked state           DeskLockedState         Switch
  Session state          DeskSessionState        String
  Wi-Fi name             DeskWifiName            String
  Wi-Fi connected        DeskWifiConnected       Switch
  openHAB connection     DeskOpenHabConnection   String
  Focus / DND            DeskFocusState          Switch/String
```

When `Sync device information` is off, collapse the detailed identity, interval, status, and signal mapping controls. The page should show only the master switch and a short disabled state. This avoids a page full of inactive text boxes.

Each signal row is individually configurable. A blank Item name disables that signal. A row-level enable/collapse pattern may be used so the Item-name field appears only when the signal is enabled.

Device identifier defaults to the sanitized Windows machine name. Default Item names use `{DeviceIdentifier}{SignalName}`. For example, device identifier `Desk` produces `DeskBatteryLevel` and `DeskWifiName`. Users can override every Item name.

Default sync interval is 15 minutes. Valid range is 1 to 240 minutes.

## Signals And States

First-release signals:

| Signal | openHAB state | Notes |
| --- | --- | --- |
| Battery level | `0`-`100` Number | Omit when unavailable, such as many desktops. |
| Charging state | `ON` or `OFF` | `ON` means charging or AC power is present. |
| Locked state | `ON` or `OFF` | `ON` when the Windows session is locked. |
| Session state | `active`, `locked`, `sleep`, `resume`, `unknown` | String state for automation rules that need more detail than `LockedState`. |
| Wi-Fi connected | `ON` or `OFF` | No SSID required for this signal. |
| Wi-Fi name | SSID or `UNDEF` | SSID is opt-in and sent only when the signal is configured. |
| openHAB connection | `online`, `degraded`, `offline`, `unknown` | Derived from `SitemapRuntimeController.Current.ConnectionState`. |
| Focus / DND | `ON`, `OFF`, or `UNSUPPORTED` | Uses `FocusSessionManager` where supported. |

The first release should keep charging state simple. A future release can add richer charger type strings if Windows exposes them reliably and the app can map them without breaking existing Switch Item setups.

## Send Triggers

Device Info Sync sends the current snapshot:

- after app startup once settings and credentials are hydrated
- on the configured periodic interval
- on Windows session lock/unlock
- on resume/power events where available
- on network availability/profile changes
- on Focus state changes where supported
- when runtime connection state changes

The service should debounce bursts and send a single current snapshot. Event-driven sends do not reset the periodic interval requirement; the periodic send remains a recovery path for missed events and values that can drift.

## Transport And Failure Handling

Device Info Sync uses the same endpoint selection and credential model as foreground browsing. If the active runtime client is connected locally, sync should use local. If cloud is selected or automatic fallback is active, sync should use that resolved path.

Failures are non-blocking. A failed sync updates Device Info Sync status and diagnostics, then the service waits for the next trigger or interval. Failures must not throw into app startup, sitemap rendering, item commands, notification polling, or tray shell operations.

Status should include:

- last successful sync time
- last attempted sync time
- last result summary
- last error summary without sensitive response bodies or credentials
- Focus unsupported state where applicable

## Privacy

Device Info Sync is opt-in. No signals are sent until the master switch is enabled and at least one Item mapping is configured.

SSID can reveal location or network identity, so Wi-Fi name is opt-in like every other signal. The app must not send BSSID, MAC address, IP address, Windows username, tokens, passwords, endpoint URLs with embedded credentials, or raw server error bodies as device info.

Diagnostics should log signal names and high-level outcomes, not sensitive values.

## Components

Expected implementation units:

- `DeviceInfoSyncSettings` under `OpenHab.App.Settings`.
- Expanded `DeviceStateSnapshot`, `DeviceStateMapping`, and `DeviceStateMapper` under `OpenHab.Core.DeviceState`.
- `DeviceInfoSyncService` under `OpenHab.App`, or a focused app runtime namespace.
- Windows snapshot source and collectors under `OpenHab.Windows.Tray.DeviceInfo`.
- Settings UI navigator/helpers in `MainWindow.xaml` and `MainWindow.xaml.cs`, or small adjacent Windows-layer classes if the settings rewrite would otherwise make `MainWindow.xaml.cs` too large.

## Testing

Add or extend tests for:

- mapper output for every new signal
- omitted and unconfigured signals
- `UNDEF` and `UNSUPPORTED` formatting
- settings serialization and migration
- sync interval bounds
- sanitized default device identifier
- default Item-name generation
- blank Item names disabling signals
- sender behavior with `FakeOpenHabClient`
- failed sends updating status without throwing
- batching/debouncing multiple triggers

Windows collector wrappers should be thin and tested through interfaces where practical. Direct Windows API behavior can receive manual smoke coverage because it depends on OS state and hardware.

## Verification

During implementation, run the direct test gate:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj
```

Before claiming release readiness, run the full solution/package gates when DesktopBridge prerequisites are available:

```powershell
dotnet test OpenHab.Windows.sln
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release
.\build-package.ps1 -Configuration Release -Platform x64
```

Manual smoke checks:

- open the main window
- switch between Notifications and Settings
- open each Settings category and use back navigation
- enable and disable Device Info Sync
- configure default and custom Item names
- confirm sitemap browsing and commands continue while sync is enabled
- confirm diagnostics show sync attempts without credentials or sensitive network details
