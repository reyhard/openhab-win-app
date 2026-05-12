# openHAB Windows Main UI Shell Design

Date: 2026-05-12

## Purpose

Add first-class openHAB Main UI support to the Windows Main Window while preserving the existing native sitemap renderer. The Main Window should default to Main UI, move app-owned settings and notifications to a Windows-style left rail, and let users optionally show the native sitemap beside Main UI.

This spec covers shell layout, Main UI WebView behavior, promoted page discovery, sitemap pane behavior, settings/notification relocation, error handling, and verification scope. It does not cover tray flyout redesign, Windows Widgets, cloud push notifications, or native rendering of Main UI widgets.

## Context

The app currently has a native sitemap-centered Main Window with sitemap content on the left and a right-side `Pivot` for notifications/settings. The current product direction is a Windows 11 tray app with compact flyout and larger main window. Existing shipped behavior includes native sitemap rendering through `OpenHab.Rendering`, connected sitemap runtime loading through `OpenHab.App.Runtime.SitemapRuntimeController`, and settings/notification surfaces in the tray project.

The Android app and openHAB docs support a split model:

- Sitemaps are native app/client surfaces.
- Main UI and other non-sitemap UIs are rendered through a WebView.
- Main UI is the primary openHAB web UI and supports pages, promoted sidebar pages, and bottom/home navigation.

Relevant source references:

- Android Main UI WebView: `MainUiWebViewFragment` loads `/`, rewrites `myopenhab.org` to `home.myopenhab.org`, and supports a configured start page.
- Android generic WebView handling: `AbstractWebViewFragment` owns WebView lifecycle, same-host navigation, error state, permissions, and JS bridge preferences.
- openHAB docs: Main UI overview, Pages settings, UI design overview, and Android app docs.

## Goals

- Main Window opens to openHAB Main UI by default.
- Native sitemap is hidden by default and can be shown as an optional split pane.
- Settings and notifications move from the right side to a Windows 11-style left navigation model.
- Promoted openHAB Main UI pages are discovered from the server and shown in a collapsible left-rail section.
- Main UI navigation and native sitemap navigation remain independent.
- Existing sitemap runtime, renderer, settings controller, endpoint selection, and notification store are reused instead of duplicated.

## Non-Goals

- Do not implement a native renderer for Main UI pages/widgets.
- Do not scrape Main UI HTML to discover pages.
- Do not remove or replace the existing native sitemap renderer.
- Do not redesign the tray flyout in this work.
- Do not add Windows Widgets or new notification transports in this work.
- Do not push WinUI/WebView concerns into `OpenHab.Core`, `OpenHab.Sitemaps`, or `OpenHab.Rendering`.

## Shell And Layout

The Main Window becomes a three-region shell:

- Left rail: native Windows navigation.
- Center surface: WebView2-hosted openHAB Main UI by default.
- Right optional pane: native sitemap surface, hidden by default.

The left rail contains:

- App-owned entries such as Home/Main UI, Notifications, Settings, diagnostics/about.
- A collapsible `Main UI Pages` section populated from discovered promoted openHAB pages.
- Connection status near the bottom.

The center surface contains one active shell page at a time:

- Main UI WebView.
- Native notifications page.
- Native settings page.
- Native diagnostics/about page if present.

When a native app page replaces Main UI in the center, the WebView instance should remain alive while the Main Window is open so returning to Main UI does not force a reload.

The right sitemap pane:

- Is hidden by default.
- Is toggled by a native shell button.
- Appears inside the existing window width, splitting space with the center surface.
- Does not resize the outer window.
- Remains visible across center-surface changes when enabled, including Settings and Notifications.

The openHAB Main UI bottom navigation, such as Overview, Locations, and Devices, remains inside the WebView. The Windows shell must not duplicate those bottom tabs.

## Main UI WebView Behavior

Main UI is hosted with WebView2. The app builds the URL from the currently active endpoint selected by existing endpoint-mode logic.

Startup behavior:

- Main Window starts with Main UI visible and sitemap hidden.
- Default Main UI route is `/`.
- For `https://myopenhab.org`, Main UI navigation rewrites the host to `https://home.myopenhab.org`, matching the Android app behavior.
- The WebView keeps its in-window state while the Main Window is open.
- Clicking a discovered promoted page in the left rail navigates only the WebView.

Navigation behavior:

- Same-host navigation stays inside WebView.
- External-host navigation opens in the system browser.
- Browser history belongs to the WebView and does not alter sitemap history.
- Native sitemap history belongs to the sitemap pane and does not alter WebView history.
- Back behavior should prefer the focused surface: WebView history when Main UI has focus, sitemap back stack when sitemap has focus, otherwise shell-level navigation if available.

Failure behavior:

- Main UI load failure shows a native center placeholder with retry and open-in-browser actions.
- WebView2 unavailable or initialization failure shows a native fallback with open-in-browser.
- Full URLs that could contain credentials, tokens, or sensitive query strings must not be logged.

## Promoted Page Discovery

The app should discover Main UI pages from openHAB REST/API contracts. It must not scrape rendered Main UI HTML.

Only pages promoted to Main UI/sidebar are shown in the `Main UI Pages` section. All discoverable but non-promoted pages are omitted from this shell navigation.

Discovery behavior:

- Refresh on endpoint/profile changes.
- Refresh on explicit user refresh.
- Cache the last successful discovery result for fast startup.
- Treat cached links as stale until refresh succeeds.
- If discovery fails, show a compact error row inside `Main UI Pages` with retry.
- If no promoted pages are found, default the section collapsed and show an empty/help row only when expanded.

The app-level model should be UI-independent and small:

```csharp
public sealed record MainUiPageLink(
    string Uid,
    string Label,
    string Route,
    string? Icon,
    string? Type,
    int? Order);
```

`Route` is a relative Main UI route that navigates the WebView. Route activation must not change the native sitemap state.

The collapsible section state should be persisted:

- Expanded by default when promoted pages exist.
- Collapsed by default when no promoted pages exist.
- User changes are remembered.

## Native Sitemap Pane

The native sitemap pane reuses the existing sitemap runtime and renderer:

- `OpenHab.App.Runtime.SitemapRuntimeController`
- `OpenHab.Windows.Tray.Rendering.SitemapSurface.SitemapSurfaceRenderer`
- Existing sitemap picker and selected sitemap setting
- Existing native page transition behavior
- Existing event-stream update behavior

Sitemap pane behavior:

- Hidden by default in Main Window.
- Toggled from the shell header.
- Visibility is independent of the selected center page.
- Shows the currently selected sitemap.
- Keeps its own back stack independently from Main UI.
- Continues receiving sitemap event updates independently from WebView.
- Shows an empty/selection state if no sitemap is configured.
- Shows errors only in the sitemap pane when sitemap loading fails.

Layout behavior:

- MVP may use a fixed right-pane width with sensible minimums.
- A draggable splitter is optional if implementation cost is low.
- Toggling should animate smoothly when feasible.
- Layout correctness and input reliability are higher priority than animation polish.

## Settings And Notifications

Settings and notifications move from the right-side `Pivot` into shell-managed native pages.

Behavior:

- `Notifications` opens a native center page.
- `Settings` opens a native center page.
- If the sitemap pane is visible, it stays visible while `Notifications`, `Settings`, diagnostics/about, or Main UI pages are active.
- Returning to Main UI restores the existing WebView instance.
- App-owned pages remain outside `Main UI Pages`.
- Settings and notification logic remain in `OpenHab.App` and `OpenHab.Windows.Tray`.

Implementation implication:

- Extract the current notification and settings UI from `MainWindow.xaml` into reusable controls/pages before or during shell refactoring.
- Keep credential, endpoint, startup, notification, and device-info-sync settings owned by existing settings/runtime layers.
- Avoid moving app settings or notification behavior into `OpenHab.Core`.

## Error Handling

Errors are isolated to the region they affect:

- Main UI WebView errors affect only the center Main UI page.
- Promoted page discovery errors affect only the collapsible `Main UI Pages` section.
- Sitemap load errors affect only the right sitemap pane.
- Endpoint/auth failures appear once in the shell status area and are referenced by affected panes without duplicating long error text.

Logging requirements:

- Redact credentials, bearer tokens, passwords, cookies, and sensitive query strings.
- Do not log full Main UI navigation URLs when query strings are present.
- Keep server-provided response bodies out of user-visible diagnostics unless they pass existing redaction.

## Architecture

Expected layering:

- `OpenHab.Core`: REST client methods and DTO parsing for page discovery, with no WinUI/WebView dependency.
- `OpenHab.App`: Main UI page-link models, discovery/filtering service, cached state, and settings for collapsed/expanded navigation if not purely visual.
- `OpenHab.Windows.Tray`: WinUI shell, WebView2 host, left rail, settings/notifications pages, sitemap pane composition, animations, and browser handoff.

Recommended new concepts:

- `MainUiPageLink` in `OpenHab.App` or `OpenHab.Core` depending on whether it represents raw API data or app-ready navigation data.
- `MainUiPageDiscoveryService` in `OpenHab.App` to convert API results into promoted app navigation links.
- `MainUiWebViewHost` or equivalent WinUI control in `OpenHab.Windows.Tray`.
- `MainWindowShellViewModel` or focused controller to hold selected shell page, sitemap visibility, promoted links, and discovery status.

The current `MainWindow.xaml.cs` is already large. This work should reduce additional growth by extracting shell pages/controls rather than adding all behavior directly to `MainWindow`.

## Data Flow

Startup:

1. App resolves settings and active endpoint.
2. Main Window shell initializes with Main UI selected and sitemap hidden.
3. WebView host navigates to the active endpoint's Main UI route.
4. Promoted page discovery starts asynchronously.
5. Cached promoted links may render immediately, marked as refreshing/stale.
6. Successful discovery replaces cached links and persists the cache.

Promoted page click:

1. User expands `Main UI Pages`.
2. User selects a promoted page.
3. Shell selects Main UI center surface if needed.
4. WebView navigates to the page route.
5. Native sitemap pane remains unchanged.

Sitemap toggle:

1. User clicks `Show sitemap`.
2. Shell sets sitemap pane visible.
3. Existing sitemap runtime loads or refreshes if needed.
4. Center WebView width is reduced inside the current window.
5. WebView state and sitemap state remain independent.

Settings/notifications:

1. User selects an app-owned left-rail entry.
2. Shell swaps the center content to the native page.
3. WebView instance is retained.
4. Sitemap pane visibility is preserved.
5. Returning to Home/Main UI reveals the retained WebView.

## Verification

Automated tests:

- `OpenHab.Core` tests for new REST method(s), auth application, failure handling, and response parsing.
- `OpenHab.App` tests for promoted-page filtering, route normalization, cache behavior, and discovery failure states.
- `OpenHab.App` settings tests for any new persisted shell/discovery settings.
- `OpenHab.Windows.Tray` tests where practical for view-model/controller state transitions, not fragile pixel assertions.

Manual smoke checks:

- Main Window opens with Main UI visible and sitemap hidden.
- Local endpoint loads Main UI at `/`.
- `myopenhab.org` Main UI route rewrites to `home.myopenhab.org`.
- Same-host links stay in WebView.
- External links open system browser.
- Promoted pages appear in collapsible `Main UI Pages`.
- Non-promoted pages do not appear.
- Promoted page click navigates WebView only.
- Sitemap toggle shows/hides native sitemap without resizing the outer window.
- Visible sitemap remains visible when switching to Settings or Notifications.
- Sitemap navigation does not alter WebView history.
- Settings and notifications open as native center pages and returning to Main UI preserves WebView state.
- WebView failure shows retry/open-browser state.
- Discovery failure only affects the left section.
- Full solution tests pass when practical: `dotnet test OpenHab.Windows.sln`.

## Open Questions For Implementation

- Exact openHAB REST endpoint and JSON fields for promoted Main UI pages must be confirmed against a real openHAB instance or API explorer before implementation.
- Whether promoted page cache belongs in `settings.json` or a separate non-settings app cache should be decided during planning.
- Whether a fixed sitemap width is sufficient for MVP or a draggable splitter is worth adding immediately should be decided during planning.

## References

- openHAB Main UI overview: https://www.openhab.org/docs/mainui/
- openHAB Settings - Pages: https://www.openhab.org/docs/mainui/settings/pages
- openHAB UI design overview: https://www.openhab.org/docs/ui/
- openHAB Android app docs: https://www.openhab.org/docs/apps/android
- Android `MainUiWebViewFragment`: https://github.com/openhab/openhab-android/blob/main/mobile/src/main/java/org/openhab/habdroid/ui/activity/MainUiWebViewFragment.kt
- Android `AbstractWebViewFragment`: https://github.com/openhab/openhab-android/blob/main/mobile/src/main/java/org/openhab/habdroid/ui/activity/AbstractWebViewFragment.kt
