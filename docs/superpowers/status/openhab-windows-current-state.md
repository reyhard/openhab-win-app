# openHAB Windows Current State

Date: 2026-05-13

## Purpose

Read this file before implementation. Older dated status files remain useful as historical evidence, but this page summarizes the current product shape, release blockers, and verification gates.

## Shipped Product Shape

- Windows 11 tray app with compact flyout and larger main window.
- Native sitemap rendering through `OpenHab.Rendering` descriptors and `OpenHab.Windows.Tray.Rendering.SitemapControlFactory`.
- Connected sitemap homepage loading through `OpenHab.App.Runtime.SitemapRuntimeController`.
- Settings, credentials, startup integration, packaging, subpage navigation, breadcrumbs, ButtonGrid dispatch, and event-stream widget updates have later evidence in source and dated plans/status files.
- Cloud notifications support advanced openHAB Cloud payloads: nested payload normalization, custom title/tag/reference id, app logo/hero image media resolution, toast buttons, command actions, URL/UI navigation actions, log-only notifications, and hide/remove semantics.

## Current High-Priority Backlog

- P0: Review tracked temporary signing certificates, user publish metadata, package artifacts, and release signing inputs.
- P0: Redact server-provided response bodies before they reach logs, diagnostics, or status text.
- P1: Add shared Windows-layer sitemap row planning before unifying flyout and main window behavior.
- P1: Track sitemap event-stream start/connect outcomes instead of discarding tasks.
- P1: Replace fire-and-forget settings saves with observable serialized persistence.
- P1: Add dispatcher refresh replay when `DispatcherQueue.TryEnqueue` rejects work.
- P2: Document direct-test and full-solution gates while DesktopBridge prerequisites remain environment-specific.
- P2: Improve chart/icon loading and cache policy after row reconciliation is shared.

## Verification Gates

- Everyday logic gate: run direct test projects listed in `docs/superpowers/verification/openhab-windows-quality-gates.md`.
- Full gate: run `dotnet test OpenHab.Windows.sln` and `dotnet build OpenHab.Windows.sln --configuration Release` when DesktopBridge package targets are installed.
- Known environment issue: `OpenHab.Windows.Package.wapproj` imports `Microsoft.DesktopBridge.props`; environments without that target can still run direct test projects.
- 2026-05-13 advanced notification verification: Core, Sitemaps, Rendering direct test projects passed; App notification tests passed 117/117; `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore` passed. `dotnet test OpenHab.Windows.sln --no-restore --blame-hang --blame-hang-timeout 20s` hit the known DesktopBridge import issue, then completed Core/Sitemaps/Rendering and executed App assertions 230/230 successfully, but the App VSTest host did not exit and was aborted by blame-hang after 20 seconds; listed active tests were existing tray/UI helper tests rather than notification failures.

## Historical Status References

- `docs/superpowers/status/2026-05-05-openhab-windows-foundation-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`
