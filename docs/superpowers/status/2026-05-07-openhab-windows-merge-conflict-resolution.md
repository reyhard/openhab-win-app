# Merge Conflict Resolution — Lost Features from icon-polish-and-row-consistency

**Date:** 2026-05-07
**Branches:** `feature/event-stream-live-updates` → `main`
**Stash:** `stash@{0}` from `feature/icon-polish-and-row-consistency`

## What happened

The event-stream branch was merged into main while the stash from `feature/icon-polish-and-row-consistency` contained uncommitted changes. The stash's refactored `SitemapControlFactory.cs` (339 lines vs 1000+ in feature branch) was fundamentally incompatible with the SSE infrastructure. After multiple merge attempts, the stash version was abandoned and the feature branch version was used instead.

## Features lost from the stash

The following files from the stash were NOT merged because they conflicted irreconcilably with the SSE event stream code:

| File | What it had | Why it couldn't merge |
|------|------------|----------------------|
| `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs` | Heavily refactored (339 lines): buttongrid layout, `Row`/`Column`/`IsActive` on `SitemapMapOption`, `NormalizeIconName`, `CanResolveNormalizedIcon`, `ResolveGlyphForIcon`, `BuildOpenHabIconUri` with full logic, external icon source support | SSE added `IconAuthContext`, `UpdateState`, `SetVisibility`, `FindVisualChild`, toggle suppression — all missing from stash version. Feature branch version kept because tests pass and SSE works. |
| `src/OpenHab.Windows.Tray/FlyoutWindow.xaml` | Notifications section changes (menu flyout, sitemap selection, etc.) | Feature branch version kept — has `LocalOnlyNote`, `NotificationPollBox` |
| `src/OpenHab.Windows.Tray/MainWindow.xaml` | Settings pane changes, sitemap flyout, etc. | Feature branch version kept — has `LocalOnlyNote`, `NotificationPollBox`, `FlyoutWidthBox` |
| `tests/OpenHab.Rendering.Tests/SitemapSkinTests.cs` | 51 lines of new skin tests | Tests depend on stash's refactored SitemapControlFactory |

## What was preserved

The feature branch version of `SitemapControlFactory.cs` has:
- Full icon loading (SVG/PNG with auth, caching, probe)
- Win11 glyph mapping with normalized fallback
- `IconAuthContext` for Bearer/Basic auth on icon requests
- All SSE additions: `UpdateState`, `SetVisibility`, `FindVisualChild`, toggle suppression
- `SuppressIconLogging` guards on all icon log lines

## How to restore

In a future session:

1. Check out the stash: `git stash show -p stash@{0} > stash.patch`
2. Apply only the SitemapControlFactory logic changes (not the full file replacement)
3. Specifically port these from the stash:
   - Buttongrid layout support (`CreateButtonGrid` method)
   - `SitemapMapOption` extensions: `Row`, `Column`, `IsActive`
   - `RenderControlKind.Button`, `ButtonGrid`, `Image` enum values
4. Keep the SSE methods (`UpdateState`, `SetVisibility`, `FindVisualChild`, toggle suppression) intact
5. Keep the icon loading infrastructure (`IconAuthContext`, `TryAddIcon`, probe logic) intact
6. Re-add `SitemapSkinTests.cs` from the stash

The stash is still available: `git stash list` shows `stash@{0}` on the `feature/icon-polish-and-row-consistency` branch.

## Current state

- **Build:** 0 errors, 0 warnings
- **Tests:** 234/234 passing
- **SSE:** Working with raw `rest/events` (proven reliable)
- **Sitemap events:** Infrastructure present but dormant (unreliable on some servers)
- **Notifications:** Polling every 30s (configurable 10-600s), LocalOnly info text shown
