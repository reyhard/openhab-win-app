# openHAB Windows Foundation Status

Date: 2026-05-05

## Source Documents

- Design spec: `docs/superpowers/specs/2026-05-04-openhab-windows-sitemap-client-design.md`
- Foundation plan: `docs/superpowers/plans/2026-05-04-openhab-windows-foundation.md`

## Completed And Merged

The foundation vertical slice from the original implementation plan was completed and merged into `main`.

Completed plan tasks:

- Task 1: Scaffolded `OpenHab.Windows.sln` with `OpenHab.Core`, `OpenHab.Sitemaps`, `OpenHab.Rendering`, and matching xUnit test projects.
- Task 2: Added endpoint profiles and local/cloud/automatic transport selection.
- Task 3: Added openHAB HTTP client contract and implementation for item commands, item state updates, sitemap JSON loading, request failure exceptions, credential-safe diagnostics, base-path-preserving URI construction, and cancellation-aware test support.
- Task 4: Added sitemap models and normalization for visibility filtering, supported vs fallback widgets, navigation flags, mappings, validation, and defensive list snapshots.
- Task 5: Added sitemap interaction intents and navigator behavior for subpage navigation, back stack, switch command intents, fallback intents, no-op intents, and argument diagnostics.
- Task 6: Added rendering descriptors, skin contract, Basic sitemap skin, Windows 11 sitemap skin, and shared row mapping to keep skin behavior aligned.
- Task 7: Added Windows device state telemetry mapping from battery, charging, lock, and session snapshots to configured openHAB Item state updates.
- Task 8: Verified the full foundation solution and cleaned up generated-output ignore rules.

Final merged commit on `main`:

- `060d6c9 chore: ignore generated build output`

Verification after merge:

- `dotnet test OpenHab.Windows.sln`: 49 passed, 0 failed.
- `dotnet build OpenHab.Windows.sln --configuration Release`: 0 warnings, 0 errors.

## Foundation Scope Now Available

The repo now has UI-independent contracts and runtime pieces for later app work:

- Core server profile and transport selection model.
- Core openHAB HTTP API client surface.
- Sitemap compatibility model and normalizer.
- Sitemap navigation intent model.
- Skin-neutral render descriptor model.
- Basic and Windows 11 skin mapping layer.
- Device-state-to-openHAB telemetry mapper.
- Tests covering the above foundation behavior.

## Left From The Original Design

These items were intentionally outside the foundation plan and still need separate plans/implementation:

- WinUI or Windows App SDK app shell.
- System tray icon and tray flyout window.
- Main window and settings UI.
- First-run setup flow.
- Secure credential storage.
- Persisted server profiles and settings migrations.
- Real event stream client for live item updates.
- openHAB sitemap JSON parsing from actual REST payloads into the foundation models.
- WebView2/Main UI fallback surface.
- Windows native notifications and notification activation routing.
- Cloud notification polling.
- Device state collection from real Windows APIs.
- Sending mapped device state updates through the active openHAB client on a schedule/event trigger.
- Connection status UI for local, cloud, degraded, and offline states.
- Cached offline sitemap state storage.
- Windows 11 tray/flyout visual implementation for the Basic and W11 skins.
- UI automation tests.
- MSIX packaging, startup integration, signing, and release workflow.
- Optional Windows Widgets support.

## Suggested Next Plan

The next implementation plan should build a thin vertical UI slice on top of the completed foundation:

1. Create the Windows App SDK/WinUI shell.
2. Add a tray presence and a minimal flyout host.
3. Render a small in-memory normalized sitemap through the existing Basic/W11 skin descriptors.
4. Add first settings surface for skin selection and endpoint mode.
5. Keep real openHAB connectivity limited to the existing `OpenHabHttpClient` until event stream and sitemap JSON parsing are planned.
