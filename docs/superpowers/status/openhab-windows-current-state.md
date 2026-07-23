# openHAB Windows Current State

Date: 2026-07-23

## Purpose

Read this file before implementation. Older dated status files remain useful as historical evidence, but this page summarizes the current product shape, release blockers, and verification gates.

Treat this page as the source of truth for current shipped behavior, backlog priority, release blockers, and verification evidence.

## Shipped Product Shape

- Windows 11 tray app with compact flyout and larger main window.
- Main window defaults to embedded openHAB Main UI through WebView2.
- Main window left rail contains Settings, Notifications, and collapsible promoted Main UI pages discovered from `/rest/ui/components/ui:page`; cached promoted links render immediately, then refresh when the main window is created, and promoted page icons from `config.icon` are downloaded through the shared openHAB/Iconify icon loading path.
- Native sitemap rendering remains available as an independent right-side pane that is hidden by default and can stay visible while Main UI, Settings, or Notifications are active.
- Flyout and main window sitemap surfaces share the Windows sitemap renderer and row-planning path through `OpenHab.Rendering.SitemapSurface.SitemapRowPlanner` and `OpenHab.Windows.Tray.Rendering.SitemapSurface.SitemapSurfaceRenderer`.
- Native sitemap Setpoint widgets render as decrease/increase button pairs that send clamped `step` commands; Slider widgets continue to render as sliders.
- First flyout activation preloads and renders the sitemap snapshot before showing when no cached runtime descriptor exists, avoiding an empty first-open surface while preserving fast cached subsequent opens.
- Connected sitemap homepage loading, subpage navigation, breadcrumbs, search descriptors, ButtonGrid dispatch, and event-stream widget updates route through `OpenHab.App.Runtime.SitemapRuntimeController`.
- App settings are UI-independent, persisted by `OpenHab.App.Settings.AppSettingsController`, and include endpoint mode, sitemap/main window shell state, notification preferences, device info sync, shortcuts, and verbose diagnostics.
- Cloud notifications support nested payload normalization, custom title/tag/reference id, app logo/hero image media resolution, toast buttons, command actions, URL/UI navigation actions, log-only notifications, and hide/remove semantics.
- Cloud-notification deduplication is seeded from the newest 500 persisted undismissed notification IDs and evicts only the oldest retained ID at capacity; crossing the old 200-ID boundary no longer clears the complete seen set and redispatches historical notifications.
- Notification-history mutations are serialized through one coalescing writer per normalized storage path, committed through same-directory atomic replacement, and exposed through a generation-aware `FlushAsync`; normal shutdown stops the poller before flushing the latest history snapshot.
- Notification hero-image storage is process-owned and bounded to 64 files, 32 MiB total, and 30 days, with atomic cache writes, best-effort pruning, and invalidation when endpoint or credential profile inputs change.
- Windows-specific functionality includes tray icon integration, startup task handling, notification activation, device-state sync readers, global hotkeys, and a radial shortcut command menu.
- Diagnostics now use `SafeDiagnosticText` and `SensitiveTextRedactor` for privacy-safe logs and user-facing status text in the main runtime, event stream, notifications, and Windows status surfaces.
- App-owned UI text now routes through localization resources with English and Polish resource sets. Appearance settings include a language selector with System language, English, and Polish options; explicit overrides are applied during startup through WinUI resource/culture override APIs, and the UI shows a restart-required notice when a changed language is not yet loaded.
- Reviewed dynamic sitemap, navigation, Main UI, shortcut, voice-status, and voice-confirmation text now also routes through the English/Polish localization layer, including dynamic accessibility names.
- Sitemap SSE connected, disconnected, and reconnecting transitions update and publish the app runtime snapshot only when observable state changes; explicit reconnect callers receive the real connection task so failures and cancellation remain observable.
- Packaging exists through `src/OpenHab.Windows.Package/OpenHab.Windows.Package.wapproj` and `build-package.ps1`; official package identity, signing ownership, distribution, and support policy remain release decisions.
- The sitemap flyout "Open main window" button opens the main window on the Main UI root page by default instead of restoring the last selected main-window page; tray context-menu open behavior remains separate.
- Main window promoted Main UI page links stay materialized while the left sidebar is collapsed, so re-expanding the sidebar restores visible promoted pages when their chevron is still expanded.

## Recently Completed Remediation

- openHAB 5.1.4/5.2.0 compatibility work added sanitized genuine contract captures, parser/client/SSE/runtime regressions, opaque variable-width sitemap identifier preservation, nested 5.2 ButtonGrid normalization while retaining legacy ButtonGrid support, and hardened sitemap subscription location/SSE handling.
- The embedded Main UI host was reviewed against a disposable default openHAB 5.2.0 HTTP server and lower-layer contracts. This is not an embedded WebView2/manual certification and does not make Chat, logs, voice, persistence, or editing native app features.

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
- Notification resend reliability remediation is implemented: the poller uses a bounded FIFO recent-ID set aligned with the 500-entry store, and deterministic oldest-to-newest seed selection retains the newest undismissed history across restart.
- Notification persistence reliability is implemented: immutable snapshots are coalesced and serialized per file, writes use atomic replacement, explicit flushes are generation barriers, and app shutdown waits for the poller before flushing history.
- SSE state publication/reconnect task observation, reviewed dynamic English/Polish localization coverage, and bounded notification-media caching with profile invalidation are implemented.

## Current High-Priority Backlog

- P0: Complete and record the openHAB 5.2 live compatibility matrix: 5.1.4 local plus API token; 5.2.0 local plus API token; 5.2.0 local plus Basic authentication; and 5.2.0 through myopenHAB. Use an explicit dedicated reversible test Item only, and verify restoration.
- P0: Complete the embedded WebView2/manual Main UI matrix: authentication/session behavior, promoted and file-backed/read-only pages, Chat/log/voice routes, navigation, and native-sitemap coexistence. The current 5.2 evidence is source inspection, lower-layer tests, and a disposable root HTTP check only.

- P0: Run and record the configured live notification smoke with more than 200 historical entries, two cloud poll intervals, one new notification, normal exit, restart, and two further poll intervals. Automated regressions cover 501 retained IDs and flush/reload behavior, but a real cloud/toast run is still required before closing the user-observed resend incident.
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

2026-07-23 openHAB 5.2 compatibility worktree `feature/openhab-5.2-compatibility` at `6b0b796b741abfa568a7813918575e08e0abfc96`:

- Sanitized captures from disposable official openHAB 5.1.4 and 5.2.0 images are covered by automated parser/client/SSE/runtime tests. They cover legacy and nested ButtonGrid shapes, empty arrays, and opaque variable-width widget identifiers.
- Passed serialized direct tests: Core `141/141`, Sitemaps `50/50`, Rendering `129/129`, App `663/663` (total `983/983`). Existing OpenCover reports include `OpenHabSitemapJsonParser`, `SitemapEventParser`, `OpenHabEventStreamClient`, `OpenHabHttpClient`, and `SitemapRuntimeController`; no coverage thresholds or exclusions changed.
- Passed tray Release build: `0` warnings, `0` errors. `git diff --check` passed. Repository-wide `dotnet format --verify-no-changes` remains blocked by pre-existing whitespace debt outside compatibility files; the compatibility-changed C# files pass scoped formatting.
- The probe’s PowerShell syntax and FTP invalid-URI exit-2 checks passed. Its controlled loopback fake integration verifies strict header/body subscription Location handling, event-only SSE timeout, header precedence, a timed-out post-write parser helper with successful explicit-item restoration, and redacted failure output. No live/personal endpoint was accessed.
- Compatibility release recommendation: **not ready**. Authenticated local/API-token/Basic and myopenHAB probes, full live sitemap evidence, embedded WebView2/manual UI evidence, package/release ownership gates, and existing product release blockers remain open. No version, package identity, manifest, or signing changes were made.

2026-07-15 notification reliability and review remediation on `main`:

- Root cause addressed for the observed old-notification resend: the previous capacity rollover cleared the full in-memory seen-ID set. The poller now retains the newest 500 IDs and evicts only the oldest; regression coverage crosses the actual limit with 501 persisted IDs and performs repeated polls without redispatch.
- Notification persistence now uses a serialized per-path writer, coalesced immutable snapshots, same-directory atomic replacement, generation-aware flush barriers, and poller-stop-before-flush shutdown ordering. Tests cover rapid and mixed mutations, flush/reload, temporary-file cleanup, failure recovery, and newer-write draining.
- SSE runtime snapshots now publish connected/disconnected/reconnecting changes without redundant events, and reconnect failures/cancellation are observable through the returned task.
- Dynamic Windows status, Main UI, voice, navigation, and accessibility text uses English/Polish resources; resource and placeholder parity plus reviewed-source literal regressions are covered.
- Notification media caching is bounded to 64 files, 32 MiB, and 30 days, prunes oldest entries after age removal, falls back to text-only notifications on media failure, and clears on endpoint/auth profile change.
- Passed: `dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` (`79/79`).
- Passed: `dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` (`44/44`).
- Passed: `dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` (`129/129`).
- Passed: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` (`644/644`). Total direct tests: `896/896`.
- Passed: App coverage collection with `--collect "XPlat Code Coverage" --settings coverage.runsettings` (`644/644`); `coverage.opencover.xml` contains `RecentNotificationIdSet`, `NotificationStorePersistenceQueue`, `NotificationMediaCache`, and the changed `SitemapRuntimeController` methods. No coverage exclusions were added.
- Passed: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false` (0 warnings, 0 errors).
- Passed: `git diff --check` and `dotnet format OpenHab.Windows.sln --no-restore --verify-no-changes --include <all remediation C# files>`.
- Formatting caveat: the repository-wide unscoped formatter gate remains nonzero because of pre-existing unrelated whitespace debt, including `SitemapUiLogic.cs`, `RadialCommandMenuWindow.cs`, and `ShortcutRecorderControl.cs`; every C# file changed by this remediation passes the formatter.
- Manual evidence pending: a configured openHAB/cloud/toast resend-and-restart smoke and Polish visual/truncation review were not run in this non-interactive verification session and remain listed in the high-priority backlog.

2026-06-08 SonarQube MCP reported-issues remediation:

- Queried SonarQube MCP project `reyhard_openhab-win-app`: 156 open issues, 0 bugs, 0 vulnerabilities, 0 security hotspots.
- Addressed a focused low-risk batch of reported issues: boolean literal cleanup, LINQ filter simplifications, static method candidates, dead fallback helper removal, unused local removal, nested ternary extraction, repeated resource-key literal extraction, test `Thread.Sleep` replacement, and `DeviceStateMapper` cognitive-complexity refactoring.
- Left broader architectural debt and expected WinRT analyzer warnings for separate work: constructor parameter-count issues, larger WinUI/App cognitive-complexity refactors, native callback lifetime field in `RadialCommandMenuWindow`, and CsWinRT warnings called out by project guidance.
- Initial targeted Core test hit a local `CS2012` output lock on `OpenHab.Core.dll`; rerunning after the lock cleared passed.
- Passed: `dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` (`79/79`).
- Passed: `dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` (`44/44`).
- Passed: `dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` (`129/129`).
- Passed: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` (`612/612`).
- Passed: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false` (0 warnings, 0 errors).

2026-06-07 setpoint button rendering worktree `fix/setpoint-buttons`:

- Confirmed against openHAB sitemap documentation that Setpoint renders a value controlled by decrease/increase buttons, while Slider presents a user-adjustable slider.
- Fix: sitemap Setpoint widgets now map to `RenderControlKind.Setpoint` instead of `RenderControlKind.Slider`; the WinUI sitemap factory renders compact down/up chevron buttons that send clamped numeric commands based on `minValue`, `maxValue`, and `step`.
- Slider widgets and color-temperature picker widgets continue to map to slider controls.
- Passed by exit code: `dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false`.
- Passed by exit code: `dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false`.
- Passed by exit code: `dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false`.
- Passed by exit code: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false`.
- Passed: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false` (0 warnings, 0 errors).
- Fresh worktree caveat: initial tray build without restore failed because `src\OpenHab.Windows.Tray\obj\project.assets.json` did not exist; restore required approved network access to NuGet.

2026-05-31 CI runtime test determinism fix on `main`:

- GitHub Actions run `26713695049`, job `78728404461`, failed in `Test App` at `SitemapRuntimeControllerTests.NavigateToChildDoesNotWaitForColdSitemapEventSubscriptionStartup`: the test expected the `Task.Run` navigation wrapper to complete before a 250 ms delay, but the Windows 2025 runner completed the delay first while the wrapper was still waiting for activation.
- Root cause: the test depended on thread-pool scheduling latency rather than only asserting the intended contract that `NavigateToChildAsync` completes without waiting for cold sitemap event subscription startup.
- Fix: the fake event stream client now supports an asynchronous subscription block, and the test calls `NavigateToChildAsync` directly, asserting the returned task is already complete before releasing the blocked subscription.
- Passed: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~SitemapRuntimeControllerTests.NavigateToChildDoesNotWaitForColdSitemapEventSubscriptionStartup" --logger "console;verbosity=normal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` (`1/1`).
- Passed: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` (`612/612`).
- Passed: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false` (0 warnings, 0 errors).

2026-05-31 shortcut settings expander load-animation worktree `fix/settings-expander-load-animation`:

- Investigation: CommunityToolkit `SettingsExpander.IsExpanded` defaults to `false`, and the Toolkit template binds it into a WinUI `Expander` whose expanded visual state animates content translation. The Shortcuts page was creating the Command Menu expander with the shared factory default `isExpanded: true`, so each page rebuild started from a fresh expanded control and replayed the opening visual.
- Red/green regression: `SettingsPageControlTransitionTests.CommandMenuSettingsExpanderStartsExpandedWithoutInitialExpansionAnimation` first failed because the source still used `isExpanded: false`, then passed after adding the first-load non-transition expansion path (`1/1`).
- Device Info Sync follow-up: `SettingsPageControlTransitionTests.DeviceInfoSyncSettingsExpandersStartExpandedWithoutInitialExpansionAnimation` first failed because only the Command Menu expander used the suppression path, then passed after applying it to all three Device Info Sync expanders (`1/1`).
- Fix: the Command Menu and Device Info Sync `SettingsExpander` controls remain expanded on page load while still using the CommunityToolkit control; the app creates them from the Toolkit collapsed default and expands the inner WinUI `Expander` on first load with `VisualStateManager.GoToState(..., useTransitions: false)`, preserving normal user-triggered expand/collapse behavior afterward.
- Initial tray build attempt with `--no-restore` failed in the fresh worktree because `src/OpenHab.Windows.Tray/obj/project.assets.json` did not exist.
- Passed after restore materialized assets: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false` (0 warnings, 0 errors).
- Passed: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-build --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` (`612/612`).
- Initial Release tray build after Device Info Sync follow-up was blocked by local `openHAB.exe` PID 36888 holding `src\OpenHab.Windows.Tray\bin\Release\...\openHAB.exe`, matching the known output-lock caveat; rerunning after the lock cleared passed.
- Passed: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false` (0 warnings, 0 errors).
- Passed: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug --no-restore -p:UseSharedCompilation=false` (0 warnings, 0 errors).
- Post-merge on `main` passed: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` (`612/612`).
- Post-merge on `main` passed: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false` (0 warnings, 0 errors).

2026-05-31 tray flyout first-open preload worktree `fix-tray-preload-content`:

- Red/green regression: `TrayFlyoutShowPlannerTests` first failed because `TrayFlyoutShowPlanner` did not exist, then passed after adding the app-layer preload planner (`4/4`); the shell integration assertion first failed because `App.xaml.cs` did not apply the preload before `flyout.Activate()`, then passed after wiring the hidden render path.
- Baseline caveat: `dotnet test OpenHab.Windows.sln -m:1 -p:UseSharedCompilation=false` ran direct projects successfully (`79/79`, `44/44`, `127/127`, `606/606`) but exited nonzero on the documented standalone SDK `Microsoft.DesktopBridge.props` import failure for `OpenHab.Windows.Package.wapproj`.
- Passed: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~TrayFlyoutShowPlannerTests" --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`4/4`).
- Passed: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` (`610/610`).
- Passed: `dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`79/79`).
- Passed: `dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`44/44`).
- Passed: `dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`127/127`).
- Passed: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false` (0 warnings, 0 errors).

2026-05-29 SonarQube S4143 collection-write remediation:

- Fixed Sonar issue `AZ50seACLi_CR9r889Ad` in `src/OpenHab.App/Runtime/SitemapRuntimeController.cs` by assigning each optimistic switch descriptor row array slot once after the row decision is known.
- Initial targeted App test attempt hit a local parallel build output lock on `OpenHab.Rendering.dll`/`OpenHab.Windows.Notifications.dll`, matching the known transient compiler output-lock caveat; rerunning with serialized MSBuild passed.
- Passed: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~SitemapRuntimeControllerTests" --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` (`43/43`).
- Passed: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` (`606/606`).
- Passed: `dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` (`79/79`).
- Passed: `dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` (`44/44`).
- Passed: `dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` (`127/127`).

2026-05-29 lock switch formatted-state worktree `fix/lock-switch-state-labels`:

- Red/green regressions: formatted lock toggle visual-state tests first failed for missing `SitemapUiLogic.ResolveToggleVisualState`, then passed (`2/2`); formatted switch activation first sent `ON` instead of expected `OFF`, then passed (`1/1`); sitemap navigator formatted switch command first sent `ON` instead of expected `OFF`, then passed (`1/1`); formatted lock SSE event snapshot first published raw `OFF` instead of `LOCKED`, then passed (`2/2`); optimistic formatted lock activation first stayed `UNLOCKED` after a stale immediate reconcile, then passed (`2/2`); non-blocking activation first waited for reconcile, then passed (`1/1`).
- Diagnostics follow-up: real sitemap events showed `SmartLock_01_DoorLock` uses raw `OFF` with label `UNLOCKED` and raw `ON` with label `LOCKED`; lock optimistic display mapping was corrected to match that evidence.
- Display-only toggle follow-up: switch rows now activate through the row button while the `ToggleSwitch` visual remains snapshot-driven, preventing the native control from optimistically flipping before openHAB state/events arrive.
- Optimistic-state follow-up: switch activation now publishes the command target state immediately, returns before background sitemap reconcile completes, and holds the target briefly across stale reconcile/SSE updates until actual state arrives or the hold expires.
- Passed by exit code: `dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore --logger "console;verbosity=normal" -p:UseSharedCompilation=false` (console produced no test-count summary).
- Passed: `dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`44/44`).
- Passed: `dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`127/127`).
- Passed: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`606/606`).
- Passed: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false` (0 warnings, 0 errors) after closing the local app process that had held Release output DLLs; rerun after display-only toggle follow-up also passed (0 warnings, 0 errors); rerun after optimistic-state follow-up also passed (0 warnings, 0 errors); rerun after non-blocking optimistic activation also passed (0 warnings, 0 errors).
- Passed: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug --no-restore -p:UseSharedCompilation=false` (0 warnings, 0 errors).
- Caveat: one parallel App regression run hit a transient compiler output lock on `OpenHab.Sitemaps.dll`; rerunning the test sequentially passed.
- Caveat: Release tray build was briefly blocked by local `openHAB.exe` PID 33664 holding Release output DLLs, matching the known output-lock caveat; rerunning after closing the app passed. A later Release build after optimistic-state follow-up was blocked by another running `openHAB.exe` PID 30060 holding `OpenHab.App.dll`; rerunning after closing the app passed.

2026-05-27 main-window promoted page sidebar visibility worktree `fix/sidebar-promoted-pages-visibility`:

- Red/green regression: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~MainWindowShellAnimationPlannerTests" --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` first failed for missing `ShouldRenderMainUiPagesListItems`, then passed (`26/26`).
- Passed: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`600/600`).
- Passed: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false` (0 warnings, 0 errors).
- Baseline caveat: `dotnet test OpenHab.Windows.sln` hit the known SDK DesktopBridge import failure for `OpenHab.Windows.Package.wapproj` and transient compiler output locks; direct test projects were used per the documented gate.

2026-05-27 Main UI promoted page icon worktree `feature/main-ui-page-icons`:

- Passed: `dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`79/79`).
- Passed: `dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`43/43`).
- Passed: `dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`125/125`).
- Passed: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`595/595`).
- Passed: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false` (0 warnings, 0 errors).

2026-05-27 flyout open-main-window default Main UI behavior worktree `fix/open-main-window-default-main-ui`:

- Passed: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~MainWindowShellControllerTests" --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`8/8`).
- Passed: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`588/588`).
- Passed: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false` (0 warnings, 0 errors).
- Passed by exit code: `dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore --logger "console;verbosity=normal" -p:UseSharedCompilation=false`.
- Passed by exit code: `dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore --logger "console;verbosity=normal" -p:UseSharedCompilation=false`.
- Passed by exit code: `dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --logger "console;verbosity=normal" -p:UseSharedCompilation=false`.

2026-05-24 SonarQube reported-issues remediation worktree `fix/sonarqube-reported-issues`:

- Passed: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release -p:UseSharedCompilation=false` (0 warnings, 0 errors).
- Passed: `dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`79/79`).
- Passed: `dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`43/43`).
- Passed: `dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`125/125`).
- Passed: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --logger "console;verbosity=minimal" -p:UseSharedCompilation=false` (`587/587`).

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
