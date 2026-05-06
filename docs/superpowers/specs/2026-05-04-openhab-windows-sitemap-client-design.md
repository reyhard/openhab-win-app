# openHAB Windows Sitemap Client Design

Date: 2026-05-04

## Purpose

Build a Windows 11 openHAB companion app centered on a native sitemap renderer. The app lives in the system tray, opens a fast flyout near the right-side tray area, and can also open a larger main window. It should feel native on Windows while preserving openHAB sitemap compatibility.

The MVP focuses on:

- Native sitemap rendering, not an embedded Basic UI page.
- Two global skins: Basic openHAB-style and Windows 11-style.
- Local, cloud-only, and automatic local/cloud connection modes.
- Item commands, live item updates, and cached offline browsing.
- Opt-in Windows device state reporting back to openHAB Items.
- WebView/Main UI fallback for unsupported sitemap content.

## Product Direction

The app should mirror the Android app conceptually: a native client that presents openHAB sitemaps and can send device information to openHAB. Main UI remains available, but it is not the foundation of the tray flyout.

The flyout primarily renders the selected sitemap. It should preserve sitemap navigation, frames, rows, subpages, mappings, visibility rules, and item interactions. The user selects one global skin in settings:

- `BasicSitemapSkin`: close to openHAB Basic UI/Android sitemap presentation.
- `Windows11SitemapSkin`: same sitemap structure and behavior, presented with Windows 11 visual language.

The MVP uses a global skin setting. The data model should leave room for later per-sitemap skin overrides, but per-page skinning is out of scope.

## Architecture

The app is built around a native sitemap runtime. The core layer loads sitemap definitions and item state from openHAB, keeps state synchronized, and exposes a neutral sitemap page tree to UI surfaces. The Windows app renders that tree through a skinable renderer.

The renderer has three responsibilities:

- Parse and normalize sitemap pages, widgets, frames, mappings, visibility rules, icons, and item links.
- Convert user interaction into behavior intents such as command, navigate, refresh, open fallback, or show read-only state.
- Render the same widget model through Basic and Windows 11 skins.

The tray flyout and main app share the same sitemap runtime. The flyout is constrained and optimized for fast access. The main app can show the same sitemap with more space and can expose Main UI/WebView tabs. Main UI is an escape hatch for unsupported content, not the default flyout path.

## Components

`OpenHab.Core`

Handles server profiles, authentication, REST/API calls, event stream subscriptions, command sending, sitemap loading, item state cache, and cloud/local transport routing. It has no Windows UI dependency.

`OpenHab.Sitemaps`

Owns sitemap models, widget normalization, visibility evaluation, navigation stack, mappings, dynamic icons, and unsupported-widget detection. This is the compatibility layer.

`OpenHab.Rendering`

Defines the skin contract and maps normalized sitemap widgets to renderable controls. Skins render state and raise intents; they do not fetch data or send commands directly.

`OpenHab.Windows.App`

Hosts the tray icon, flyout window, main window, settings, status UI, WebView2 fallback, startup behavior, and single-instance behavior.

`OpenHab.Windows.DeviceState`

Collects opt-in Windows device state and sends it to configured openHAB Items. MVP capabilities are battery level, charging state, lock/unlock state, and session/power transitions where detectable.

`OpenHab.Windows.Notifications`

Shows local Windows notifications from item/event rules and cloud-polled notification envelopes. Notification actions route back into the app or a known sitemap/Main UI route.

`OpenHab.Tests`

Covers sitemap compatibility, widget behavior, command dispatch, transport routing, settings/security, event reconnect, device telemetry, and renderer mapping.

## Data Flow

On startup, the app loads the selected server profile, endpoint mode, credentials, global skin setting, and last selected sitemap/page. It connects to the selected openHAB transport, fetches sitemap definitions and item state, then starts a live event subscription for updates.

The Windows app should use openHAB's HTTP API surface for sitemap loading, item state, commands, and device state updates, plus server-push events for live state changes. It should not rely on repeated polling for normal foreground updates.

When the user opens the flyout, it renders immediately from cached sitemap state. The runtime refreshes stale state in the background. User actions flow as intents:

- Send item command.
- Set item state for device telemetry.
- Navigate into a sitemap subpage.
- Refresh the current page.
- Open unsupported content in fallback UI.
- Open Main UI or external browser.

After a user command, the affected widget can show a pending state. The event stream should normally confirm the updated state. If no event arrives, the app refreshes the item directly.

Unsupported or partially supported sitemap widgets are marked with fallback capability. The skin shows a compatible row/card with an open action that routes to WebView/Main UI or browser based on settings.

## Connection And Cloud Behavior

Each server profile supports:

- Local endpoint, such as `http://openhab:8080`.
- Cloud or remote endpoint, such as `https://myopenhab.org`.
- Local API token authentication where needed.
- Cloud/myopenHAB username and password authentication where needed.
- Endpoint mode: `Automatic`, `LocalOnly`, or `CloudOnly`.

Authentication is intentionally asymmetric:

- Local openHAB access uses an API token and `Authorization: Bearer <token>`.
- Cloud/myopenHAB access uses username and password with HTTP Basic authentication, matching the Android app's remote connection model.

The app should not treat myopenHAB as a bearer-token transport by default. The same cloud username/password must be used consistently for foreground cloud browsing and cloud notification polling.

`Automatic` prefers local when reachable and falls back to cloud when local fails. `LocalOnly` never sends traffic through cloud. `CloudOnly` uses the cloud endpoint for foreground browsing, commands, cloud notification polling, and device telemetry. This makes laptops outside the home network a first-class scenario.

The active transport must be visible in the flyout and main app: connected locally, connected through cloud, degraded, or offline. Transport switching should preserve sitemap navigation and cached item state where possible. After a switch, the runtime refreshes the current page and item states.

Connection logic must not treat cloud as notification-only. When configured, cloud is a full remote openHAB path, subject to myopenHAB capabilities and latency.

## Windows Device State Reporting

Device state reporting is opt-in. Users map each supported Windows state to an openHAB Item. The app sends updates through the active or configured background endpoint mode.

MVP telemetry:

- `PcBatteryLevel`: `Number`, laptop battery percentage.
- `PcChargingState`: `Switch` or `String`, charging/AC state.
- `PcLockedState`: `Switch`, locked/unlocked.
- `PcSessionState`: `String`, active, locked, sleep/resume, or unknown where detectable.

Later telemetry can include network name, power mode, display state, idle time, and additional hardware presence signals.

Telemetry failures are non-blocking. If a state cannot be read or sent, only the telemetry capability becomes degraded. Sitemap browsing and item commands continue normally.

## Error Handling And Offline Behavior

The app distinguishes `Connected`, `Degraded`, and `Offline`.

`Connected` means sitemap loading, commands, and live updates are working. `Degraded` means cached browsing works but one channel is failing, such as event stream disconnect, cloud polling failure, or telemetry update failure. `Offline` means the selected profile cannot currently be reached.

The flyout always opens. Offline mode renders the last cached sitemap/page with a clear status indicator and disables actions that require the server. Normal command failures use row-level feedback or a quiet inline message. Authentication and configuration failures can open settings.

Event stream reconnect uses backoff and must not freeze the UI. After reconnect, the runtime refreshes the current page and resumes live updates.

## Testing

Testing is centered on sitemap compatibility and transport behavior.

Unit tests cover:

- Sitemap normalization.
- Widget behavior.
- Visibility rules.
- Mappings and dynamic icons.
- Skin selection.
- Command intent generation.
- Device telemetry item mapping.
- Credential redaction.
- Settings migration.

Integration tests use a mock openHAB/myopenHAB server with endpoints for sitemaps, item state, item commands, events, cloud notification polling, and telemetry item updates. They cover local-only, cloud-only, automatic failover, reconnect after event stream loss, command round trips, unsupported widget fallback, and telemetry failures.

UI tests stay small but real:

- First-run setup.
- Basic vs Windows 11 skin selection.
- Tray flyout opening.
- Sitemap subpage navigation.
- Switch and slider commands.
- Offline cached state.
- Local/cloud status indicator.
- WebView/Main UI fallback smoke path.

Device state tests use fake Windows battery/session/power APIs. Tests must prove telemetry maps to configured Items and that telemetry failures degrade only telemetry, not sitemap browsing.

## MVP Definition Of Done

- App runs on Windows 11 and creates a tray presence.
- Tray flyout opens near the right-side tray area.
- User can configure local and/or cloud endpoints.
- User can choose `Automatic`, `LocalOnly`, or `CloudOnly`.
- User can choose Basic or Windows 11 sitemap skin globally.
- App loads and renders openHAB sitemaps natively.
- User can navigate sitemap subpages.
- User can send item commands from supported widgets.
- Live item updates refresh rendered widgets.
- Unsupported widgets expose a fallback action.
- Cached sitemap state is visible offline.
- Active transport status is visible.
- Opt-in Windows battery, charging, lock, and session state can be sent to mapped Items.
- Credentials and tokens are stored securely and redacted from logs.
- Unit and integration tests cover the core sitemap, transport, and telemetry behavior.
