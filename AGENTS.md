# AGENTS

## Project Summary

This repository contains a Windows companion app for openHAB. The current product direction is a Windows 11 tray app with a native sitemap renderer, a compact flyout, and a larger main window path when needed.

The implementation is intentionally layered:

- `OpenHab.Core`: endpoint selection, server profiles, HTTP client, and device-state mapping.
- `OpenHab.Sitemaps`: sitemap models, parsing, normalization, and navigation intents.
- `OpenHab.Rendering`: skin-neutral render descriptors plus Basic and Windows 11 skin mapping.
- `OpenHab.App`: UI-independent app settings and sitemap runtime/controller logic.
- `OpenHab.Windows.Tray`: WinUI/Windows App SDK shell, tray icon, flyout window, and current settings surface.
- `tests/*`: xUnit coverage for core, sitemap, rendering, and app/runtime behavior.

## Current Status

Do not maintain implementation progress directly in this file.

Use the status docs as the source of truth for what is finished, what was verified, and what remains out of scope. Read this consolidated tracker first:

- `docs/superpowers/status/openhab-windows-current-state.md`

Historical status references:

- `docs/superpowers/status/2026-05-05-openhab-windows-foundation-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`

Read the latest relevant status doc before assuming a feature exists. The design/spec docs describe intended direction, not necessarily shipped behavior.

## Key Files

Useful entry points when orienting:

- `OpenHab.Windows.sln`
- `src/OpenHab.Windows.Tray/MainWindow.xaml`
- `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs`
- `src/OpenHab.App/Sitemaps/SitemapRenderController.cs`
- `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`
- `src/OpenHab.Core/Api/OpenHabHttpClient.cs`
- `src/OpenHab.Sitemaps/Parsing/OpenHabSitemapJsonParser.cs`
- `src/OpenHab.Rendering/Skins/Windows11SitemapSkin.cs`

Runtime logs and local app state are written under:

- `%localappdata%\OpenHab.WinApp`

Useful files there include `diagnostics.log`, `task-crash.log`, `settings.json`, and `notifications.json`. Prefer `diagnostics.log` first when tracing runtime behavior.

Useful project docs:

- `docs/superpowers/status/openhab-windows-current-state.md` - start here for current shipped behavior, release blockers, and verification gates.
- `docs/superpowers/verification/openhab-windows-quality-gates.md` - direct test and full-solution verification commands, including DesktopBridge caveats.
- `docs/superpowers/specs/2026-05-04-openhab-windows-sitemap-client-design.md` - original sitemap client architecture and intended rendering/navigation direction.
- `.docs/design.md` - broader product/design notes and UI direction.

Historical implementation evidence, when needed:

- `docs/superpowers/status/2026-05-05-openhab-windows-foundation-status.md` - foundation/runtime status at that checkpoint.
- `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md` - UI slice status at that checkpoint.
- `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md` - connected homepage status at that checkpoint.

Treat the status docs as the best summary of what is actually finished.

## Tech Stack

- `.NET 10`
- WinUI via `Microsoft.WindowsAppSDK`
- Windows tray integration currently uses `System.Windows.Forms.NotifyIcon`
- xUnit for tests

Prefer preserving the existing project split. Do not push WinUI-specific concerns into `OpenHab.Core`, `OpenHab.Sitemaps`, or `OpenHab.Rendering`.

## Working Rules

- Keep domain/runtime logic UI-independent unless the code clearly belongs in `OpenHab.Windows.Tray`.
- Reuse the existing sitemap model, normalizer, runtime controller, and skin pipeline instead of adding parallel rendering paths.
- When adding new functionality, extend the existing layer that owns the concern rather than bypassing it from the tray app.
- For openHAB behavior, prefer contracts and tests around parsers, runtime controllers, and render descriptors before wiring UI.
- Be careful with logs and diagnostics: do not expose credentials, tokens, or sensitive endpoint data.
- The repo may contain untracked planning or local-reference files under `.docs/` and `docs/`; do not delete or rewrite them unless the task requires it.

## Verification

Primary verification commands:

- `dotnet test OpenHab.Windows.sln`
- `dotnet build OpenHab.Windows.sln --configuration Release`

If Release build fails because files cannot be copied or overwritten while the app is running from Visual Studio, try a Debug build before diagnosing code changes.

If a change is isolated to one layer, targeted tests are fine during iteration, but run the full solution tests before claiming completion when practical.

## Subagent Delegation

When delegating work to subagents, choose the model based on task complexity:

- Use `gpt-5.3-codex` with `medium` reasoning effort for more complex tasks.
- Use `gpt-5.4-mini` with `high` reasoning effort for simpler tasks.

Default to `gpt-5.3-codex` when the task requires stronger reasoning, broader code understanding, architecture-sensitive changes, or higher implementation risk.
