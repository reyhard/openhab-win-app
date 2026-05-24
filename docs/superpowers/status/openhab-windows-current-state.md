# openHAB Windows Current State

Date: 2026-05-24

## Purpose

Read this file before implementation. Older dated status files remain useful as historical evidence, but this page summarizes the current product shape, release blockers, and verification gates.

## Shipped Product Shape

- Windows 11 tray app with compact flyout and larger main window.
- Main window defaults to embedded openHAB Main UI through WebView2.
- Main window left rail contains Settings, Notifications, and collapsible promoted Main UI pages discovered from `/rest/ui/components/ui:page`.
- Native sitemap rendering remains available as an independent right-side pane that is hidden by default and can stay visible while Main UI, Settings, or Notifications are active.
- Flyout and main window sitemap surfaces share the Windows sitemap renderer and row-planning path through `OpenHab.Rendering.SitemapSurface.SitemapRowPlanner` and `OpenHab.Windows.Tray.Rendering.SitemapSurface.SitemapSurfaceRenderer`.
- Connected sitemap homepage loading, subpage navigation, breadcrumbs, search descriptors, ButtonGrid dispatch, and event-stream widget updates route through `OpenHab.App.Runtime.SitemapRuntimeController`.
- App settings are UI-independent, persisted by `OpenHab.App.Settings.AppSettingsController`, and include endpoint mode, sitemap/main window shell state, notification preferences, device info sync, shortcuts, and verbose diagnostics.
- Cloud notifications support nested payload normalization, custom title/tag/reference id, app logo/hero image media resolution, toast buttons, command actions, URL/UI navigation actions, log-only notifications, and hide/remove semantics.
- Windows-specific functionality includes tray icon integration, startup task handling, notification activation, device-state sync readers, global hotkeys, and a radial shortcut command menu.
- Diagnostics now use `SafeDiagnosticText` and `SensitiveTextRedactor` for privacy-safe logs and user-facing status text in the main runtime, event stream, notifications, and Windows status surfaces.
- App-owned UI text now routes through localization resources with English and Polish resource sets. Appearance settings include a language selector with System language, English, and Polish options; explicit overrides are applied during startup through WinUI resource/culture override APIs, and the UI shows a restart-required notice when a changed language is not yet loaded.
- Packaging exists through `src/OpenHab.Windows.Package/OpenHab.Windows.Package.wapproj` and `build-package.ps1`; official package identity, signing ownership, distribution, and support policy remain release decisions.

## Recently Completed Remediation

- Plan A governance shell exists: `README.md`, `CONTRIBUTING.md`, `NOTICE`, `SECURITY.md`, and `.github/workflows/ci.yml`.
- Plan B privacy hardening is implemented: request failures, runtime status, SSE/event logging, notification logging, and Windows status text now use safe diagnostic text/redaction paths.
- `OpenHab.App.Tests` no longer requires VSTest blame-hang for normal clean exit after disabling Windows App SDK bootstrap initialization on the tray project reference used by App tests.
- Shared sitemap row planning, Windows sitemap surface rendering, and dispatcher refresh replay are implemented and covered by App tests.
- Sitemap event-stream start/connect failure handling now supports retry, stale attempt suppression, online-to-degraded state updates, and event handler attachment before duplicate-start detection.
- Settings saves are serialized and observable through queued saves plus `FlushAsync`.
- Tracked temporary signing/user/package artifacts are no longer present in `git ls-files`; `.gitignore` now covers `.pfx`, user project metadata, `AppPackages`, and `BundleArtifacts`.
- Crowdin localization foundation has landed on `main`: `crowdin.yml`, translation ownership docs, English and Polish WinUI `.resw` resources, fallback `ITextLocalizer` coverage, localized package manifest/app chrome/runtime/status/settings strings, and regression tests for resource parity, placeholder parity, and representative hardcoded settings labels.
- Language override support has landed on `main`: persisted app language setting, startup WinUI resource/culture override, Appearance language selector, explicit Polish override behavior, and restart-required messaging for non-live language changes.
- Sitemap media cache policy is implemented: chart URLs are stable by default with explicit opt-in cache busting, icon payload caching is bounded, and sitemap media caches clear when endpoint or credential profile inputs change.

## Current High-Priority Backlog

- P0: Finalize release ownership decisions: official package identity, signing certificate ownership, Microsoft Store or other distribution ownership, support policy, and security response path.
- P1: Run and record manual UI smoke checks for tray flyout, main window, Main UI WebView2 auth/navigation, notifications, settings, and shortcut command menu.
- P1: Run and record manual memory/performance measurements for launch, flyout open/close, main window open/close, and background resource release. `docs/superpowers/verification/2026-05-14-performance-optimization-results.md` currently records build success but no manual measurements.
- P1: Complete accessibility, dependency/license, and release packaging review before treating the app as official release-ready. Localization has a resource-backed English/Polish baseline, but still needs manual UI smoke review for wording, truncation, and untranslated app-owned strings.

## Verification Gates

- Direct logic gate: run direct test projects listed in `docs/superpowers/verification/openhab-windows-quality-gates.md`.
- Tray build gate: run `dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Release` for UI, Windows shell, notification, package-reference, or project-file changes.
- Full package gate: run `.\build-package.ps1 -Configuration Release -Platform x64` for package, manifest, startup-task, notification activation, signing, or release changes.
- Known environment issue: `OpenHab.Windows.Package.wapproj` imports `Microsoft.DesktopBridge.props`; environments without Visual Studio DesktopBridge/MSIX targets can still run the direct test projects.
- If Release build fails because files cannot be copied or overwritten while the app is running from Visual Studio or from a previous local run, try a Debug build or close the running app before diagnosing code changes.

## Latest Verification Evidence

2026-05-24 sitemap media cache policy worktree `investigate/sitemap-media-cache-policy`:

- Passed: `dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`79/79`).
- Passed: `dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`43/43`).
- Passed: `dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`125/125`).
- Passed: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`587/587`).
- Passed: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false` (0 warnings, 0 errors).

2026-05-18 localization/language merge on `main` at `f209aac`:

- Passed before merge on `crowdin-translations-plan`: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -p:UseSharedCompilation=false` (`463/463`).
- Passed before merge on `crowdin-translations-plan`: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false` (0 warnings, 0 errors).
- Passed before merge on `crowdin-translations-plan`: `.\build-package.ps1 -Configuration Release -Platform x64`.
- Merge into `main` required one conflict resolution in `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`; the resolution preserved both the existing `ShortcutActionEditorPlanner` field from `main` and the localization fields from the feature branch.
- Passed after merge on `main`: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -p:UseSharedCompilation=false` (`521/521`).
- Passed after merge on `main`: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug --no-restore -p:UseSharedCompilation=false` (0 warnings, 0 errors).
- Release tray build after merge was blocked by a local running `openHAB.exe` process (`PID 24336`) holding Release output DLLs, matching the known local output-lock caveat rather than a compile failure.

2026-05-15 current-state review on `main` at `4eb7fbf`:

- Passed: `dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore --logger "console;verbosity=minimal"` (`79/79`).
- Passed: `dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore --logger "console;verbosity=minimal"` (`39/39`).
- Passed: `dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --logger "console;verbosity=minimal"` (`101/101`).
- First App test attempt hit a local build output lock on `src\OpenHab.Windows.Notifications\obj\Debug\...\OpenHab.Windows.Notifications.dll` (`CS2012`). Retried with compiler shared compilation disabled.
- Passed: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`399/399`) and exited cleanly.

Earlier 2026-05-15 official-readiness Plan B verification evidence:

- Passed: `dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --no-restore`; App tests passed (`457/457`) and the VSTest host exited cleanly without blame-hang.

2026-05-14 performance optimization verification:

- Verification file: `docs/superpowers/verification/2026-05-14-performance-optimization-results.md`
- Commands run: `dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Release`
- Result: passed with 0 warnings and 0 errors after rerunning with approved network access for NuGet restore; manual memory measurements and smoke checks were not captured there.

2026-05-12 Main UI shell branch `feature/main-ui-shell`:

- Passed: `dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj` (`61/61`).
- Passed: `dotnet test tests/OpenHab.Sitemaps.Tests/OpenHab.Sitemaps.Tests.csproj` (`39/39`).
- Passed: `dotnet test tests/OpenHab.Rendering.Tests/OpenHab.Rendering.Tests.csproj` (`31/31`).
- Passed: `dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj` (`291/291`).
- Passed: `dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Debug` (0 warnings, 0 errors).
- Passed: `dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Release` (0 warnings, 0 errors).
- Passed: `.\build-package.ps1 -Configuration Release -Platform x64` using Visual Studio MSBuild and DesktopBridge targets.
- Caveat: `dotnet test OpenHab.Windows.sln -m:1` ran all test projects successfully (`61/61`, `39/39`, `31/31`, `291/291`) but exited non-zero because dotnet SDK MSBuild could not import `Microsoft.DesktopBridge.props` for `OpenHab.Windows.Package.wapproj`.

## Official-Readiness Plan Split

2026-05-14 design `docs/superpowers/specs/2026-05-14-openhab-windows-official-readiness-remediation-design.md` split remediation into:

- Plan A: fast public repository governance and CI shell.
- Plan B: privacy-safe diagnostics, App test host shutdown, and targeted maintainability extraction.

Current source on 2026-05-15 indicates both Plan A and Plan B implementation work has landed on `main`. Remaining work is release ownership, manual smoke/performance verification, and release review rather than the old diagnostics/testhost blockers.

## Historical Status References

- `docs/superpowers/status/2026-05-05-openhab-windows-foundation-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`
