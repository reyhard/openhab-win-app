# openHAB Windows Shell Chrome Refinement Design

Date: 2026-05-13

## Purpose

Refine the Main Window shell introduced by the Main UI support work. The refined shell should reduce header clutter, make the left app navigation collapsible, and make native sitemap visibility a shell-level control that is visually tied to the right-side sitemap pane.

This spec is a delta on `2026-05-12-openhab-windows-main-ui-shell-design.md`. It does not replace the Main UI WebView, promoted-page discovery, sitemap runtime, or settings/notification relocation design.

## Goals

- Make the left app sidebar collapsible.
- Preserve a seamless Windows 11-style title/header surface with minimal visual separation from content.
- Move the native sitemap show/hide control to the right side of the title bar, near the window controls.
- Keep the native sitemap as an independent right-side panel, separate from the left navigation.
- Show sitemap picker and refresh controls only when the sitemap panel is visible.
- Place sitemap picker and refresh above the expanded sitemap panel, not in the global title bar.
- Remove duplicated small settings/subsettings titles when a larger breadcrumb/header already identifies the page.

## Non-Goals

- Do not move sitemap navigation into the left app sidebar.
- Do not put sitemap picker or refresh controls in the global title bar.
- Do not duplicate Main UI bottom navigation in native chrome.
- Do not redesign the tray flyout as part of this refinement.
- Do not introduce new openHAB REST contracts beyond the promoted Main UI pages and existing sitemap APIs.

## Approved Layout Direction

The Main Window uses four visual regions:

- Native title bar / shell chrome.
- Collapsible left app navigation.
- Center content surface.
- Optional right sitemap panel.

The native sitemap toggle belongs in the right side of the title bar, before the standard minimize/maximize/close controls. This placement makes the control feel global, avoids consuming content-header space, and keeps it near the right-side panel it affects.

The left sidebar remains app navigation. It owns app-level destinations such as Home, Main UI Pages, Notifications, Settings, and connection status. It does not own sitemap page navigation.

The right sitemap panel owns all sitemap-specific UI. When the panel is expanded, its header contains the sitemap selector and refresh button. When the panel is hidden, those controls are hidden too.

## Title Bar Behavior

The title bar should feel like part of the window, not a separate toolbar. It should use the same neutral background as the content shell, with only subtle hover/active states for command buttons.

Title-bar command order:

- Existing app/icon area on the left remains lightweight.
- Flexible empty space occupies the middle.
- Sitemap toggle appears on the right command side.
- Native window controls remain at the far right.

The sitemap toggle states:

- Hidden state: neutral icon button.
- Visible state: active icon button using the app accent color or selected command styling.
- Tooltip/accessibility name: `Show sitemap` or `Hide sitemap` depending on state.

The title bar must not contain the sitemap name, sitemap picker, or refresh action. Those controls are contextual to the visible sitemap panel.

## Left Sidebar Behavior

The left app navigation supports expanded and collapsed states.

Expanded state:

- Shows openHAB branding and companion subtitle.
- Shows app navigation labels.
- Shows the collapsible `Main UI Pages` section.
- Shows connection status near the bottom.

Collapsed state:

- Reduces to an icon-only navigation rail.
- Keeps app-owned destinations reachable.
- Keeps selected state visible.
- Hides promoted Main UI page labels unless there is a compact design that remains readable.
- Does not alter sitemap visibility.

The sidebar collapse state should be persisted with existing app settings if practical. If persistence adds disproportionate complexity, it can default to expanded for the first implementation, but the user action should still work during the window session.

## Right Sitemap Panel Behavior

The sitemap panel is hidden by default.

When visible:

- It appears on the right side of the Main Window.
- It remains visible across Home/Main UI, Settings, Notifications, and other native center pages.
- It contains a small panel header with sitemap selector and refresh button.
- It renders the currently selected sitemap below the panel header.
- It keeps native sitemap navigation and back stack independent from Main UI WebView navigation.

When hidden:

- The center content expands into the freed space.
- Sitemap selector and refresh controls are not visible anywhere else.
- Sitemap runtime state may be retained in memory while the Main Window is open if that avoids reload churn, but hidden UI must not keep stealing focus or visible space.

Smooth panel animation is desirable, but layout correctness and reliable input are higher priority.

## Center Content And Page Headers

The center content surface hosts Main UI WebView, Settings, Notifications, and other native pages.

Settings and subsettings should avoid duplicated titles. If a larger breadcrumb/header already says `Settings > Appearance`, the smaller repeated `Settings` title inside the page body should be removed. Body content should start with the actual category content or a concise supporting subtitle only when it adds information.

Main UI content should not receive an extra native top toolbar. The WebView should remain visually dominant, with shell controls kept in title/sidebar/panel chrome.

## Data And State

State to track in the Windows shell layer:

- Left sidebar expanded/collapsed.
- Current center shell page.
- Sitemap panel visible/hidden.
- Selected sitemap, using existing selected-sitemap settings.
- Main UI Pages section expanded/collapsed, using existing promoted-page shell behavior.

Existing sitemap runtime, promoted-page discovery, settings, notification, and WebView state ownership remain unchanged from the Main UI shell design.

## Accessibility

- Title-bar sitemap toggle must be keyboard reachable.
- Toggle must expose a clear automation name and pressed/expanded state.
- Collapsed sidebar buttons must expose labels through automation names and tooltips.
- Focus should not jump into the sitemap panel merely because it was opened, unless opened through keyboard and moving focus is the expected accessible behavior.

## Error Handling

Errors remain isolated by region:

- Sitemap load errors appear inside the right sitemap panel.
- Main UI WebView errors appear in the center Main UI host.
- Settings and notifications errors appear in their native center pages.

If the sitemap panel is hidden while it has an error, the title-bar toggle may show a subtle attention state only if it does not create visual noise. The first implementation can omit this attention state.

## Testing And Verification

Unit coverage should remain focused on non-WinUI behavior already owned by app/runtime layers. Shell chrome changes are primarily WinUI integration work and should be verified through targeted build and manual UI checks.

Verification commands:

- `dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj`
- `dotnet test tests/OpenHab.Sitemaps.Tests/OpenHab.Sitemaps.Tests.csproj`
- `dotnet test tests/OpenHab.Rendering.Tests/OpenHab.Rendering.Tests.csproj`
- `dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj`
- `dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Release`

Manual checks:

- Main Window starts with Main UI visible and sitemap hidden.
- Sitemap toggle is on the right side of the title bar near window controls.
- Opening sitemap reveals the right-side panel with selector and refresh above the sitemap content.
- Hiding sitemap removes selector and refresh from view.
- Left sidebar expands and collapses without changing sitemap visibility.
- Settings and Notifications remain usable with sitemap hidden or visible.
- Settings subpages do not show duplicated small titles below the breadcrumb/header.
