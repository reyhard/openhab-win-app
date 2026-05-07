# Event Stream & Live Updates — Implementation Status

**Date:** 2026-05-07
**Branch:** `feature/event-stream-live-updates`

## Completed

### Core SSE Infrastructure
- **Event model** (`ItemStateChangedEvent`, `ItemCommandEvent`) and SSE message parser for openHAB's wire format
- **SSE event stream client** with exponential backoff reconnection (1s → 30s), Bearer/Basic auth support, and thread-safe connection state
- **Partial UI rebuilds** — `ChangedRowIndices` on snapshot enables updating only affected widgets instead of full page redraws
- **App lifecycle wiring** — client creation, connection startup, graceful shutdown

### Notification Improvements
- Poll interval configurable via settings (default 30s, min 10s, max 600s)
- LocalOnly mode shows informational text in the notifications tab

### Widget Infrastructure for Sitemap Events (dormant — see Known Issues)
- `WidgetId` field on all widget models, parsed from sitemap JSON
- `SitemapWidgetEvent` record and `SitemapEventParser` for the `rest/sitemaps/events/` endpoint
- `widgetIdMap` index alongside existing `itemIndexMap`
- `SitemapControlFactory.SetVisibility()` and `SitemapControlFactory.UpdateState()` for per-row visibility toggling
- `IsVisible` field on `NormalizedSitemapWidget` and `SitemapRowDescriptor` — invisible widgets now pass through normalization

## Bugs Found & Fixed

| # | Bug | Root Cause | Fix |
|---|-----|-----------|-----|
| 1 | Item states never updated via SSE | Wire format mismatch: parser expected `ItemStateChangedEvent`, openHAB sends `ItemStateEvent` | Accept both types in parser |
| 2 | Topic filter matched nothing | Filter used `*/statechanged`, real topic is `*/state` | Changed filter to `*/state` |
| 3 | SSE events parsed but UI never updated | `ApplyItemState` updated snapshot but UI wasn't notified | Added `SnapshotChanged` event wired to `RefreshRuntimeBindings()` |
| 4 | SSE connection immediately dropped | Server required auth but SSE client sent bare requests | Added Bearer/Basic auth to SSE client |
| 5 | Toggle switch loops (ON→OFF→ON) | `UpdateState` set `ToggleSwitch.IsOn`, firing `Toggled` event → sent command to server → feedback loop | Suppress `Toggled` event via `Tag = "suppress"` during programmatic state updates |
| 6 | Icon logging flooded diagnostics | Every icon fetch logged at Info level | Added `SuppressIconLogging` flag (default `true`) |
| 7 | Sitemap switch didn't update subscription | `StartSitemapEventStreamAsync` had `_eventStreamStarted` guard preventing reconnection | Added sitemap/page tracking and re-subscription on change |
| 8 | `rest/sitemaps/events/` endpoint unreliable | Servers may not deliver widget events (community forum confirms this). Only ALIVE pings received, no widget updates. | **Reverted to raw `rest/events` as primary** (proven reliable). Sitemap events code kept dormant for servers that support it. |

## Architecture

```
openHAB Server
    │
    ├── rest/events?topics=openhab/items/*/state,openhab/items/*/command  ← PRIMARY (reliable)
    │   │
    │   └──► OpenHabEventStreamClient (SSE)
    │           ├── ItemStateChangedEvent → ApplyItemState → widget state → UI partial rebuild
    │           └── ConnectionStateChanged → snapshot status update
    │
    └── rest/sitemaps/events/{subscriptionId}  ← DORMANT (unreliable on many servers)
        │
        └──► OpenHabEventStreamClient (SSE)
                └── SitemapWidgetEvent → ApplyWidgetEvent → state + visibility update
```

## Still Out Of Scope

- **Visibility condition re-evaluation** — When an item state changes via SSE, widgets whose visibility depends on that item are not re-evaluated. The current workaround: toggling in the app triggers a full page refresh (server handles visibility). External changes don't trigger this.
- **Sitemap event subscription on all servers** — The `rest/sitemaps/events/` endpoint is unreliable. Needs investigation on whether it's a server version issue, API usage issue, or server configuration issue.
- **AND conditions in visibility** — The sitemap event format supports AND conditions; our parser would need updating.

## Verification

- **Build:** 0 errors, 0 warnings (Release)
- **Tests:** 244/244 passing
- **SSE tested with:** openHAB server on localhost, Bearer token auth, file-based sitemaps
