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

## Status Page First

Before making assumptions about shipped behavior, backlog priority, release blockers, or verification gates, read:

- `docs/superpowers/status/openhab-windows-current-state.md`

Treat that status page as the source of truth for:

- what is actually finished;
- what remains out of scope;
- current high-priority backlog items;
- latest verification evidence;
- which verification gate applies to the change.

Do not maintain implementation progress directly in this file. If work changes shipped behavior, release readiness, backlog status, or verification evidence, update the status page instead of adding progress notes here.

Historical status references:

- `docs/superpowers/status/2026-05-05-openhab-windows-foundation-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`

Use historical status files only as background evidence. The design/spec docs describe intended direction, not necessarily shipped behavior.

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

Useful project docs, in priority order:

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
- Do not use PowerShell reflection against built assemblies for diagnostics, especially patterns such as `Assembly.LoadFrom`, `GetType`, `GetMethod`, and `Invoke` on private methods. Windows Defender has flagged these command lines as trojan-like behavior. Prefer normal tests, small temporary test cases, public APIs, logs, or debugger-free repro code instead.
- The repo may contain untracked planning or local-reference files under `.docs/` and `docs/`; do not delete or rewrite them unless the task requires it.

## Verification

Primary verification commands:

- `dotnet test OpenHab.Windows.sln`
- `dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Release`
- `.\build-package.ps1 -Configuration Release -Platform x64`

Use `dotnet build` for layer-specific and tray-app builds. Use `.\build-package.ps1` for full solution/package builds because the `.wapproj` imports DesktopBridge targets that are available through Visual Studio MSBuild, not necessarily through the standalone .NET SDK MSBuild path. The script locates Visual Studio with `vswhere` and verifies `Microsoft.DesktopBridge.props` before invoking MSBuild.

If Release build fails because files cannot be copied or overwritten while the app is running from Visual Studio, try a Debug build before diagnosing code changes.

If a change is isolated to one layer, targeted tests are fine during iteration, but run the full solution tests before claiming completion when practical.

## Coverage and Sonar

Treat coverage as a signal for testable logic, not as a reason to hide behavior. Add or extend tests for pure decisions, parsing, planning, formatting, mapping, and state transitions before considering exclusions.

When code cannot be tested meaningfully because it is thin WinUI, WinRT, Win32, Windows App SDK, COM, registry, credential, toast, tray, WebView, or live OS integration glue, keep exclusions narrow and documented:

- File-level exclusions must be listed in `docs/superpowers/verification/coverage-exclusion-inventory.md`, `coverage.runsettings`, and `.github/workflows/sonarcloud.yml` using the syntax each tool expects.
- Partial method or constructor exclusions should use `[ExcludeFromCodeCoverage]` with a specific justification and must be documented under the inventory's partial exclusions section.
- Do not exclude large mixed files until testable decisions have been extracted to `OpenHab.App`, `OpenHab.Rendering`, `OpenHab.Sitemaps`, `OpenHab.Core`, or a small tested helper.
- Keep duplicated WinUI code-behind out of Sonar CPD only when shared behavior has already been extracted and tested elsewhere; document this in the Sonar workflow comment or the coverage inventory.
- After changing exclusions, run coverage with `--collect "XPlat Code Coverage" --settings coverage.runsettings` and confirm excluded files or members are absent from generated `coverage.opencover.xml` where practical.
- CsWinRT warnings such as `CsWinRT1030` are expected for some Windows-targeted projects because generated WinRT interop may require unsafe code for trimming/AOT compatibility. Keep `AllowUnsafeBlocks` centralized and conditional for `*-windows*` target frameworks; do not add handwritten unsafe code unless there is a separate reviewed need.

## Subagent Delegation

When delegating work to subagents, choose the model based on task complexity:

- Use `gpt-5.3-codex` with `medium` reasoning effort for more complex tasks.
- Use `gpt-5.4-mini` with `high` reasoning effort for simpler tasks.

Default to `gpt-5.3-codex` when the task requires stronger reasoning, broader code understanding, architecture-sensitive changes, or higher implementation risk.
