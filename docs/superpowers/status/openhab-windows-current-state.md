# openHAB Windows Current State

Date: 2026-05-12

## Purpose

Read this file before implementation. Older dated status files remain useful as historical evidence, but this page summarizes the current product shape, release blockers, and verification gates.

## Shipped Product Shape

- Windows 11 tray app with compact flyout and larger main window.
- Main window defaults to embedded openHAB Main UI through WebView2.
- Main window left rail contains Settings, Notifications, and collapsible promoted Main UI pages discovered from `/rest/ui/components/ui:page`.
- Native sitemap rendering remains available as an independent right-side pane that is hidden by default and can stay visible while Main UI, Settings, or Notifications are active.
- Native sitemap rendering through `OpenHab.Rendering` descriptors and `OpenHab.Windows.Tray.Rendering.SitemapControlFactory`.
- Connected sitemap homepage loading through `OpenHab.App.Runtime.SitemapRuntimeController`.
- Settings, credentials, notifications, startup integration, packaging, subpage navigation, breadcrumbs, ButtonGrid dispatch, and event-stream widget updates have later evidence in source and dated plans/status files.

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

## Latest Verification Evidence

2026-05-12 Main UI shell branch `feature/main-ui-shell`:

- Passed: `dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj` (`61/61`).
- Passed: `dotnet test tests/OpenHab.Sitemaps.Tests/OpenHab.Sitemaps.Tests.csproj` (`39/39`).
- Passed: `dotnet test tests/OpenHab.Rendering.Tests/OpenHab.Rendering.Tests.csproj` (`31/31`).
- Passed: `dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj` (`291/291`).
- Passed: `dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Debug` (0 warnings, 0 errors).
- Passed: `dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Release` (0 warnings, 0 errors).
- Passed: `.\build-package.ps1 -Configuration Release -Platform x64` using Visual Studio MSBuild and DesktopBridge targets.
- Caveat: `dotnet test OpenHab.Windows.sln -m:1` ran all test projects successfully (`61/61`, `39/39`, `31/31`, `291/291`) but exited non-zero because dotnet SDK MSBuild could not import `Microsoft.DesktopBridge.props` for `OpenHab.Windows.Package.wapproj`.

## Historical Status References

- `docs/superpowers/status/2026-05-05-openhab-windows-foundation-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`
