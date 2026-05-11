# openHAB Windows Code Quality Audit

Date: 2026-05-11

## Scope

This audit reviews maintainability, flyout/main sitemap behavior, reliability, security/privacy, runtime efficiency, automation, repository instructions, and project tracking. It does not implement production code changes.

## Baseline

### Git State

`git status --short` produced no tracked or untracked file entries. The command did emit existing global Git configuration warnings:

```text
warning: safe.directory ''*'' not absolute
warning: safe.directory ''%(prefix)///192.168.1.175/opt/housedb'' not absolute
warning: safe.directory '`%(prefix)///192.168.1.175/opt/housedb`' not absolute
```

`git status --short --ignored` reported only ignored `bin/` and `obj/` output under `src/` and `tests/`, matching the controller-provided baseline that this isolated worktree has clean tracked state except generated ignored output from baseline tests.

### Recent Commits

`git log --oneline -10` output:

```text
2b83d9c docs: add code quality audit plan
81ec867 docs: expand code quality audit scope
b452638 docs: add code quality audit design
62575f5 Tune widget visibility animation
3e6ccbc Fix sitemap event row updates
95ef625 Bump package manifest version to 1.0.22.0
e7e4c0a Fix compiler diagnostics and stabilize packaged WinUI build
25bed3f Fix first subpage slide transition and unify sitemap page animations
e4a5c68 Fix ButtonGrid command dispatch and hover styling
81a8cf2 Fix sitemap startup selection fallback behavior
warning: safe.directory ''*'' not absolute
warning: safe.directory ''%(prefix)///192.168.1.175/opt/housedb'' not absolute
warning: safe.directory '`%(prefix)///192.168.1.175/opt/housedb`' not absolute
```

### Documents Reviewed

- `AGENTS.md`
- `docs/superpowers/specs/2026-05-04-openhab-windows-sitemap-client-design.md`
- `docs/superpowers/specs/2026-05-11-openhab-windows-code-quality-audit-design.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-foundation-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`
- `docs/superpowers/plans/2026-05-11-openhab-windows-code-quality-audit.md`
- `docs/superpowers/plans/*.md`

### Project Inventory Note

`rg --files src tests docs\superpowers\specs docs\superpowers\status docs\superpowers\plans` showed the expected source, test, spec, status, and plan coverage for `OpenHab.Core`, `OpenHab.Sitemaps`, `OpenHab.Rendering`, `OpenHab.App`, `OpenHab.Windows.Tray`, `OpenHab.Windows.Notifications`, and `OpenHab.Windows.Package`. The inventory also exposed package/signing and user-specific artifact categories to include in later security and repository hygiene review, including `.pfx`, `.csproj.user`, and `.pubxml.user` files under `src/`.

### Baseline Verification Note

Controller-provided baseline verification: direct test projects passed `266/266` in this worktree. `dotnet test OpenHab.Windows.sln` restored packages when run escalated but exited nonzero because `src/OpenHab.Windows.Package/OpenHab.Windows.Package.wapproj` imports missing `C:\Program Files\dotnet\sdk\10.0.203\Microsoft\DesktopBridge\Microsoft.DesktopBridge.props`.

## Executive Summary

Pending completion after audit findings are collected and ranked. Task 1 establishes the report skeleton, repository baseline, reviewed documents, and known verification constraints.

## Findings

Findings are ordered by priority within each completed audit task. Security/privacy findings are escalated when severity warrants it.

### Task 2 Maintainability Evidence Notes

Large-file metric command:

```powershell
Get-ChildItem -Path src,tests -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } | ForEach-Object { $lines=(Get-Content -LiteralPath $_.FullName).Count; [PSCustomObject]@{ Lines=$lines; Path=$_.FullName.Substring((Get-Location).Path.Length+1) } } | Sort-Object Lines -Descending | Select-Object -First 25 | Format-Table -AutoSize
```

Top relevant hand-authored files from the metric:

| Lines | Path | Maintainability note |
| ---: | --- | --- |
| 2462 | `src\OpenHab.Windows.Tray\Rendering\SitemapControlFactory.cs` | Large Windows-layer factory owns row creation, state updates, icon loading/cache, chart URL/image loading, visibility animation, ButtonGrid controls, and multiple pure helper contracts. |
| 1396 | `src\OpenHab.Windows.Tray\FlyoutWindow.xaml.cs` | Flyout shell mixes window behavior, runtime binding, row diff/reconcile, navigation animation, icon auth resolution, notification badge, and flyout-specific chrome. |
| 1091 | `src\OpenHab.Windows.Tray\MainWindow.xaml.cs` | Main window mixes settings, notifications, runtime binding, row creation, navigation animation, icon auth resolution, and shell commands. |
| 907 | `src\OpenHab.App\Runtime\SitemapRuntimeController.cs` | Shared runtime controller owns load/refresh, command dispatch, navigation stack, breadcrumbs, SSE deltas, and reconciliation. |
| 748 | `src\OpenHab.Windows.Tray\App.xaml.cs` | Startup and tray orchestration is also large, but not inspected for Task 2 beyond duplicate-search output. |
| 445 | `src\OpenHab.App\Settings\AppSettingsController.cs` | Settings and credentials controller is sizable but outside the Task 2 behavior comparison. |
| 439 | `tests\OpenHab.App.Tests\Runtime\SitemapRuntimeControllerTests.cs` | Runtime tests cover load/fallback, navigation breadcrumbs, widget events, and changed row indices. |
| 344 | `tests\OpenHab.App.Tests\SitemapControlFactoryTests.cs` | Factory tests cover icon helpers, chart URL construction, row identity keys, visual-state keys, and exposure of `UpdateState`. |

The duplicated sitemap concept search was run as prescribed:

```powershell
rg -n "RefreshRuntimeBindings|CreateRowElementForIndex|ButtonGrid|NavigateBackWithAnimationAsync|OnRowNavigateAsync|ResolveIconAuth|GetApiTokenSync|GetCloudCredentialsSync|ChangedRowIndices|Breadcrumb" src\OpenHab.Windows.Tray src\OpenHab.App tests
```

Relevant observations from inspecting surrounding code:

- `src\OpenHab.Windows.Tray\FlyoutWindow.xaml.cs` and `src\OpenHab.Windows.Tray\MainWindow.xaml.cs` both subscribe to `SitemapRuntimeController.SnapshotChanged`, both expose `LoadRuntimeAsync`/`RefreshRuntimeAsync`, and both implement `RunRuntimeOperationAsync`.
- Both windows implement `RefreshRuntimeBindings`, `ResolveIconAuth`, `GetApiTokenSync`, `GetCloudCredentialsSync`, `OnRowNavigateAsync`, and `NavigateBackWithAnimationAsync`.
- Flyout adds row identity tags, row-key command routing, ButtonGrid delta expansion, and structural reconciliation in `RefreshRuntimeBindings`, `ExpandChangedIndicesForMergedRows`, `ReconcileStructuralRows`, `BuildMergedButtonGridRow`, and `CreateRowElementForIndex`.
- Main window keeps its ButtonGrid merge and command routing inline in `RefreshRuntimeBindings`; row deltas index directly into `rowsPanel.Children`.
- Shared behavior exists in `src\OpenHab.App\Runtime\SitemapRuntimeController.cs` for load/refresh, row commands, navigation, breadcrumbs, changed-row index computation, SSE widget events, and reconciliation.
- `tests\OpenHab.App.Tests\Runtime\SitemapRuntimeControllerTests.cs` covers runtime breadcrumbs and `ChangedRowIndices`; `tests\OpenHab.App.Tests\SitemapControlFactoryTests.cs` covers factory helpers, but the assigned tests do not assert parity between flyout and main window `RefreshRuntimeBindings` behavior.

### M1: Duplicated sitemap row rendering between flyout and main window

- **Severity:** Medium
- **Priority:** Maintainability
- **Evidence:** `src\OpenHab.Windows.Tray\FlyoutWindow.xaml.cs` implements row binding and creation through `RefreshRuntimeBindings`, `BuildMergedButtonGridRow`, and `CreateRowElementForIndex`. `src\OpenHab.Windows.Tray\MainWindow.xaml.cs` implements a separate `RefreshRuntimeBindings` with inline ButtonGrid merge, icon auth setup, command delegates, and row creation. Both ultimately call `SitemapControlFactory.Create`, but the pre-factory orchestration is duplicated.
- **Impact:** Future sitemap behavior changes must be applied in both windows. The current code already has flyout-only row identity tags and structural reconciliation while the main window does direct child-index delta updates and full rebuilds, so behavior can drift even when both windows use the same runtime snapshot and factory.
- **Suggested direction:** Extract a shared Windows-layer sitemap surface coordinator or row adapter that converts `SitemapRuntimeSnapshot` plus settings into row operations, ButtonGrid merge data, icon auth context, and command delegates. Keep WinUI element creation in `OpenHab.Windows.Tray` and keep `OpenHab.App` UI-independent.
- **Affected files/layers:** `src\OpenHab.Windows.Tray\FlyoutWindow.xaml.cs` (`OpenHab.Windows.Tray`), `src\OpenHab.Windows.Tray\MainWindow.xaml.cs` (`OpenHab.Windows.Tray`), `src\OpenHab.Windows.Tray\Rendering\SitemapControlFactory.cs` (`OpenHab.Windows.Tray`), and shared snapshot inputs from `src\OpenHab.App\Runtime\SitemapRuntimeController.cs` (`OpenHab.App`).
- **Verification needed:** Add tests or harness coverage that exercises the shared row adapter for normal rows, hidden rows, ButtonGrid rows with child buttons, command routing, navigation rows, and changed-row snapshots before replacing the window-local implementations.

### M2: Divergent row-delta mapping and structural reconciliation between flyout and main window

- **Severity:** Medium
- **Priority:** Maintainability
- **Evidence:** Flyout `RefreshRuntimeBindings` expands changed indices through `ExpandChangedIndicesForMergedRows`, finds rendered rows by `RenderedRowTag`, rebuilds ButtonGrid or visual-state changed rows, and uses `ReconcileStructuralRows` when row counts or structure change. Main `RefreshRuntimeBindings` applies each changed index directly to `rowsPanel.Children[index]` and otherwise clears and rebuilds all rows.
- **Impact:** The same `ChangedRowIndices` snapshot from `SitemapRuntimeController` is interpreted differently by the two windows. Main window updates are harder to reason about when descriptor rows include skipped `Button` rows, merged ButtonGrid children, or visibility-driven structural changes. Flyout has more complex logic but also owns that complexity privately, so fixes there do not improve the main window.
- **Suggested direction:** Move descriptor-row to visual-row mapping, changed-index expansion, and structural reconciliation policy into one reusable Windows-layer component. The component should expose deterministic operations that can be unit-tested without a live `Window`.
- **Affected files/layers:** `src\OpenHab.Windows.Tray\FlyoutWindow.xaml.cs` (`OpenHab.Windows.Tray`), `src\OpenHab.Windows.Tray\MainWindow.xaml.cs` (`OpenHab.Windows.Tray`), `src\OpenHab.Windows.Tray\Rendering\SitemapControlFactory.cs` (`OpenHab.Windows.Tray`), and `tests\OpenHab.App.Tests\SitemapControlFactoryTests.cs` / `tests\OpenHab.App.Tests\Runtime\SitemapRuntimeControllerTests.cs` as current adjacent coverage.
- **Verification needed:** Add cases for `ChangedRowIndices` after ButtonGrid child changes, hidden row changes, unchanged visual row count with descriptor changes, row insertion/removal, and stale row-key prevention. Existing runtime tests assert `ChangedRowIndices`, and factory tests assert row identity helpers, but they do not verify flyout/main visual-row parity.

### M3: Duplicated icon-auth adapter logic in flyout and main window

- **Severity:** Low
- **Priority:** Maintainability
- **Evidence:** `src\OpenHab.Windows.Tray\FlyoutWindow.xaml.cs` and `src\OpenHab.Windows.Tray\MainWindow.xaml.cs` both implement `ResolveIconAuth`, `GetApiTokenSync`, and `GetCloudCredentialsSync` with equivalent local-token versus cloud-basic mapping for `SitemapControlFactory.IconAuthContext`.
- **Impact:** Any future change to icon authentication, credential fallback, or error handling must be duplicated in both windows. The current synchronous wrapper pattern is also repeated in UI classes, which makes it easier for future code to copy the blocking pattern instead of centralizing the boundary.
- **Suggested direction:** Extract a small shared Windows-layer icon auth resolver that depends on `AppSettingsController` and returns `SitemapControlFactory.IconAuthContext`. Keep credential storage behavior in `OpenHab.App.Settings`; only consolidate the UI adapter.
- **Affected files/layers:** `src\OpenHab.Windows.Tray\FlyoutWindow.xaml.cs` (`OpenHab.Windows.Tray`), `src\OpenHab.Windows.Tray\MainWindow.xaml.cs` (`OpenHab.Windows.Tray`), and `src\OpenHab.Windows.Tray\Rendering\SitemapControlFactory.cs` (`OpenHab.Windows.Tray`).
- **Verification needed:** Add resolver tests for local token auth, cloud username/password auth, missing credential fallback, and exception-to-null behavior before deleting the duplicated methods.

### R1: Fire-and-forget sitemap event stream lifecycle can lose start and connect failures

- **Severity:** Medium
- **Priority:** Reliability
- **Evidence:** `src\OpenHab.App\Runtime\SitemapRuntimeController.cs` discards `StartSitemapEventStreamAsync` from `RefreshAsyncInternal` after primary and fallback loads, and `ReconnectForPage` discards it with `CancellationToken.None`. `StartSitemapEventStreamAsync` sets `_sitemapEventStreamStarted` before awaiting `SubscribeToSitemapEventsAsync`, then discards `sitemapEventStreamClient.ConnectAsync`. `src\OpenHab.Core\Events\OpenHabEventStreamClient.cs` implements `ConnectAsync` by starting `ReadLoopAsync` with `Task.Run` and returning `Task.CompletedTask`.
- **Impact:** Live sitemap updates can fail after a successful page load without a caller observing the failure. A subscription failure can also leave `_sitemapEventStreamStarted` set before the connection is actually usable, making a later same-page start eligible for the "already started" skip path. The user-facing symptom is stale sitemap state until manual refresh or navigation.
- **Suggested direction:** Track the event-stream start/connect task inside `SitemapRuntimeController`, reset stream-start state if subscription fails, and expose a deterministic state transition when connect fails or is canceled. Keep reconnect retry behavior in `OpenHabEventStreamClient`, but make the first subscription/start outcome observable to the runtime controller.
- **Affected files/layers:** `src\OpenHab.App\Runtime\SitemapRuntimeController.cs` (`OpenHab.App`), `src\OpenHab.Core\Events\OpenHabEventStreamClient.cs` (`OpenHab.Core`), and `tests\OpenHab.App.Tests\Runtime\SitemapRuntimeControllerTests.cs` / `tests\OpenHab.Core.Tests\Events\OpenHabEventStreamClientTests.cs` (tests).
- **Verification needed:** Add tests where `SubscribeToSitemapEventsAsync` throws, where `ConnectAsync` fails or is canceled, where same-page retry is attempted after failure, and where `RefreshAsync` does not report online live updates unless the stream start outcome is known.

### R2: Fire-and-forget settings persistence hides failed saves and can race callers

- **Severity:** Medium
- **Priority:** Reliability
- **Evidence:** `src\OpenHab.App\Settings\AppSettingsController.cs` calls `_ = SaveAsync()` from settings mutators such as `SetSkin`, `SetEndpoints`, `SetSitemapName`, `SetNotificationPollInterval`, `SetApiTokenAsync`, and `SetCloudCredentialsAsync`. `SaveAsync` catches all exceptions and silently discards them. `tests\OpenHab.App.Tests\AppSettingsControllerTests.cs`, `tests\OpenHab.App.Tests\Runtime\SitemapRuntimeControllerTests.cs`, and `tests\OpenHab.App.Tests\SitemapRenderControllerTests.cs` include constructor retry loops with the comment "fire-and-forget SaveAsync from a previous test may still be writing."
- **Impact:** A settings change can update in-memory state while persistence fails or is still in flight, so the app can appear configured but revert after restart. The test retry loops show the asynchronous write can outlive the operation that triggered it, creating observable file-system races.
- **Suggested direction:** Make persistence completion observable for callers that need durability, at least by returning or exposing a save task from mutators or by queueing serialized writes with a flush method for tests and shutdown. Log save failures without including sensitive settings values.
- **Affected files/layers:** `src\OpenHab.App\Settings\AppSettingsController.cs` (`OpenHab.App`) and related settings/runtime/render tests under `tests\OpenHab.App.Tests`.
- **Verification needed:** Add tests for ordered consecutive settings writes, simulated write failure reporting, and a flush/shutdown path that guarantees `settings.json` is stable before test cleanup or app exit.

### R3: Dispatcher enqueue failure drops UI refresh work without replay

- **Severity:** Low
- **Priority:** Reliability
- **Evidence:** `src\OpenHab.Windows.Tray\FlyoutWindow.xaml.cs` handles `SnapshotChanged` and notification changes by calling `DispatcherQueue.TryEnqueue`; when it returns false, the code only logs warnings such as "UI update lost" or "badge update lost." `src\OpenHab.Windows.Tray\MainWindow.xaml.cs` uses the same pattern for notification-list and sitemap snapshot refreshes.
- **Impact:** If the dispatcher rejects enqueue during shutdown, startup race, or window lifetime transitions, the runtime snapshot can advance while the visible UI remains stale. Main window has transition-specific pending refresh handling, but neither window records a retry when `TryEnqueue` itself fails.
- **Suggested direction:** Treat enqueue failure as a pending refresh on the owning window or route snapshot refresh through a small dispatcher adapter that can record and drain missed updates when the window is active again.
- **Affected files/layers:** `src\OpenHab.Windows.Tray\FlyoutWindow.xaml.cs` (`OpenHab.Windows.Tray`) and `src\OpenHab.Windows.Tray\MainWindow.xaml.cs` (`OpenHab.Windows.Tray`).
- **Verification needed:** Add a dispatcher-adapter test or window harness that simulates rejected enqueue, then verifies the next successful dispatcher cycle refreshes sitemap rows and notification indicators exactly once.

### S1: Tracked temporary signing certificates and user publish metadata need release hygiene review

- **Severity:** High
- **Priority:** Security/Privacy
- **Evidence:** `git ls-files 'src/**.user' 'src/**.pfx' 'src/**/AppPackages/**' 'src/**/BundleArtifacts/**'` reports tracked files `src/OpenHab.Windows.Package/OpenHab.Windows.Package_TemporaryKey.pfx`, `src/OpenHab.Windows.Tray/OpenHab_TemporaryKey.pfx`, `src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj.user`, `src/OpenHab.Windows.Tray/Properties/PublishProfiles/ClickOnceProfile.pubxml.user`, and `src/OpenHab.Windows.Tray/Properties/PublishProfiles/FolderProfile.pubxml.user`. `src\OpenHab.Windows.Package\OpenHab.Windows.Package.wapproj` enables app package signing and points `PackageCertificateKeyFile` at `OpenHab.Windows.Package_TemporaryKey.pfx`.
- **Impact:** This is repository hygiene and release-blocking until reviewed. If the `.pfx` files contain usable private keys, anyone with repository access can sign packages with those temporary identities. The `.user` files also capture local publish/debug metadata and should not be treated as intentional project configuration without an explicit decision.
- **Suggested direction:** Move signing material out of source control, rotate any certificate that may have been shared, add ignore rules for generated signing/user publish files, and document the supported local developer signing flow versus release signing flow.
- **Affected files/layers:** `src/OpenHab.Windows.Package/OpenHab.Windows.Package_TemporaryKey.pfx`, `src/OpenHab.Windows.Tray/OpenHab_TemporaryKey.pfx`, `src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj.user`, `src/OpenHab.Windows.Tray/Properties/PublishProfiles/*.pubxml.user`, and `src\OpenHab.Windows.Package\OpenHab.Windows.Package.wapproj` (`OpenHab.Windows.Package` / repository configuration).
- **Verification needed:** Add a release checklist item that confirms no private signing keys or `.user` files are tracked, run `git ls-files` for `.pfx`, `.user`, `AppPackages`, and `BundleArtifacts`, and verify package signing still works from documented local or CI-provided certificate inputs.

### S2: Server error bodies are copied into logs and status text without a redaction contract

- **Severity:** Medium
- **Priority:** Security/Privacy
- **Evidence:** `src\OpenHab.Core\Api\OpenHabHttpClient.cs` reads failed response bodies and includes the first 120 characters in `OpenHabRequestException.Message`. `src\OpenHab.App\Runtime\SitemapRuntimeController.cs` logs exception messages and copies them into `StatusText` for connection failures and fallback failures. `src\OpenHab.Windows.Tray\FlyoutWindow.xaml.cs` and `src\OpenHab.Windows.Tray\MainWindow.xaml.cs` also display caught exception messages in `StatusText`. Existing `tests\OpenHab.Core.Tests\Api\OpenHabHttpClientAuthTests.cs` and `tests\OpenHab.Core.Tests\OpenHabHttpClientTests.cs` assert configured tokens and URI credentials are not included, but they do not cover sensitive server-provided response bodies.
- **Impact:** This is local-only and user-facing through the app status area and diagnostics log. It is not evidence of current credential leakage, but it is a privacy risk if an openHAB server, proxy, or cloud endpoint returns sensitive details in an error body.
- **Suggested direction:** Define a redaction contract for user-visible and logged request failures: keep status code and reason phrase, but avoid raw body text by default or pass it through a shared redactor with tests for token-like, credential-like, URL, and authorization-like values.
- **Affected files/layers:** `src\OpenHab.Core\Api\OpenHabHttpClient.cs` (`OpenHab.Core`), `src\OpenHab.App\Runtime\SitemapRuntimeController.cs` (`OpenHab.App`), `src\OpenHab.Windows.Tray\FlyoutWindow.xaml.cs` and `src\OpenHab.Windows.Tray\MainWindow.xaml.cs` (`OpenHab.Windows.Tray`), plus related API/runtime tests.
- **Verification needed:** Add API tests where response bodies contain token-like strings, basic-auth-looking strings, URLs with credentials, and item/user names; add runtime/UI-adjacent tests that verify `StatusText` uses sanitized request failure messages.

### E1: Chart image loading bypasses cache on every render

- **Severity:** Medium
- **Priority:** Runtime efficiency
- **Evidence:** `src\OpenHab.Windows.Tray\Rendering\SitemapControlFactory.cs` builds chart URLs in `BuildChartUrl` with a random query parameter, creates a new `Image`, then fire-and-forget calls `LoadChartImageWithAuthAsync`. That method fetches with `HttpCompletionOption.ResponseContentRead` and reads the whole response with `ReadAsByteArrayAsync`. No chart cache or cancellation token is used.
- **Impact:** Every chart row render is forced to issue a fresh network request and decode a full image, even when the same chart is recreated by refresh, navigation, or a full row rebuild. On battery-powered systems or slow openHAB links, this can increase network, CPU, memory, and redraw cost.
- **Suggested direction:** Replace unconditional random cache-busting with a bounded refresh policy, pass a cancellation token tied to the row/window lifetime, and consider a short-lived chart image cache keyed by item, period, dpi, endpoint, and auth mode.
- **Affected files/layers:** `src\OpenHab.Windows.Tray\Rendering\SitemapControlFactory.cs` (`OpenHab.Windows.Tray`) and windows that trigger row rendering from `FlyoutWindow.xaml.cs` / `MainWindow.xaml.cs`.
- **Verification needed:** Add a chart URL test that proves cache-busting only changes when required, plus a measured scenario comparing network requests and image decode count across repeated refreshes of a page with chart widgets.

### E2: Main window full row rebuilds create avoidable UI and image work

- **Severity:** Medium
- **Priority:** Runtime efficiency
- **Evidence:** `src\OpenHab.Windows.Tray\MainWindow.xaml.cs` `RefreshRuntimeBindings` applies `ChangedRowIndices` when present, but otherwise calls `rowsPanel.Children.Clear()` and recreates every visible row. `src\OpenHab.Windows.Tray\FlyoutWindow.xaml.cs` has additional no-op and structural reconciliation paths that avoid full rebuild when visual row count is unchanged or rows can be reconciled.
- **Impact:** Main window refreshes can recreate WinUI controls and re-trigger icon/chart loading even when the page structure did not materially change. This can cause flicker, extra allocations, and repeated network/image work, especially on pages with charts, images, and many widgets.
- **Suggested direction:** Share the flyout structural reconcile policy or introduce the shared row adapter from `M2`, then use the same visual-row diffing path for both windows.
- **Affected files/layers:** `src\OpenHab.Windows.Tray\MainWindow.xaml.cs`, `src\OpenHab.Windows.Tray\FlyoutWindow.xaml.cs`, and `src\OpenHab.Windows.Tray\Rendering\SitemapControlFactory.cs` (`OpenHab.Windows.Tray`).
- **Verification needed:** Add parity tests or a UI harness that counts created row elements and image loads for no-op refresh, changed-row refresh, ButtonGrid child changes, and row insert/remove scenarios.

### E3: Icon source cache has no eviction or size guard

- **Severity:** Low
- **Priority:** Runtime efficiency
- **Evidence:** `src\OpenHab.Windows.Tray\Rendering\SitemapControlFactory.cs` stores loaded icon `ImageSource` instances in a static `ConcurrentDictionary<string, ImageSource>` keyed by absolute icon URI, color, and auth mode. `TryLoadIconForFormatAsync` loads response content into a byte array before decoding, then keeps successful image sources for the lifetime of the process. The audit did not find a maximum size, expiration policy, or cache-clear path.
- **Impact:** The positive cache reduces repeat network requests, but long-running sessions that visit many endpoints, icon states, colors, or sitemaps can retain decoded image sources indefinitely. This is a bounded risk for small installations and a memory-growth risk for large or frequently changing sitemap sets.
- **Suggested direction:** Add a small bounded cache policy or cache-clear hook tied to endpoint/profile changes, and measure memory use before choosing an eviction size.
- **Affected files/layers:** `src\OpenHab.Windows.Tray\Rendering\SitemapControlFactory.cs` (`OpenHab.Windows.Tray`).
- **Verification needed:** Add a focused cache test or diagnostic counter for cache entry count, then measure memory after loading a representative large sitemap with many icon/color/state combinations.

## Flyout/Main Sitemap Behavior Comparison

| Behavior | Flyout | Main Window | Assessment | Evidence |
| --- | --- | --- | --- | --- |
| Initial runtime load | `LoadRuntimeAsync` calls `RunRuntimeOperationAsync(ct => runtimeController.LoadAsync(ct))`; constructor defers initial load until startup resolution. | Same `LoadRuntimeAsync` pattern and deferred constructor comment. | shared | Shared runtime load is `SitemapRuntimeController.LoadAsync` -> `RefreshAsyncInternal("load")`; window wrappers are equivalent. |
| Manual refresh | `RefreshRuntimeAsync` and `RefreshButton_Click` call the runtime refresh path through `RunRuntimeOperationAsync`. | Same refresh wrapper and refresh button path. | duplicated-equivalent | Both windows duplicate `RunRuntimeOperationAsync` and call shared `SitemapRuntimeController.RefreshAsync` -> `RefreshAsyncInternal("manual")`. |
| Row-level delta update | Expands changed descriptor indices for merged ButtonGrid rows, locates visual rows by `RenderedRowTag`, rebuilds ButtonGrid/visual-state changed rows, otherwise calls `SitemapControlFactory.UpdateState`. | Iterates `ChangedRowIndices` and calls `SitemapControlFactory.UpdateState` on `rowsPanel.Children[index]` when the child exists. | duplicated-divergent | `FlyoutWindow.RefreshRuntimeBindings`, `ExpandChangedIndicesForMergedRows`, `TryFindRenderedRow`, `ShouldRebuildRow`; `MainWindow.RefreshRuntimeBindings`; shared source is `SitemapRuntimeController.ComputeChangedRowIndices`. |
| Structural row reconciliation | Uses `ReconcileStructuralRows` keyed by `SitemapControlFactory.BuildRowIdentityKey`, reuses/rebuilds/inserts/removes rows, and calls `CollapseAndRemove` for disappearing rows. | Clears `rowsPanel.Children` and rebuilds all non-button visual rows when there are no changed indices. | flyout-only | Flyout `ReconcileStructuralRows`, `ExistingRenderedRow`, `PendingRowUpdate`; main `RefreshRuntimeBindings` has no equivalent structural reconcile method. |
| Button-grid merge behavior | `BuildMergedButtonGridRow` collects following `Button` rows, filters to visible child options when available, stores source row index, and `CreateRowElementForIndex` routes commands from source row index or grid index. | Inline merge in `RefreshRuntimeBindings` collects following `Button` rows, filters visible child options, stores source row index, and has an additional fallback scan by expected command. | duplicated-divergent | `FlyoutWindow.BuildMergedButtonGridRow` and `CreateRowElementForIndex`; `MainWindow.RefreshRuntimeBindings`; factory endpoint is shared `SitemapControlFactory.CreateButtonGrid`. |
| Command routing by row index or row key | Non-grid commands capture a stable row key and resolve current row index with `TryResolveCurrentRowIndex`; ButtonGrid uses source row index when provided. | Non-grid commands capture descriptor `rowIndex` directly; ButtonGrid uses source row index or scans child rows by command. | duplicated-divergent | `FlyoutWindow.OnRowActivatedByKeyAsync`, `OnRowNavigateByKeyAsync`, `SendCommandForRowKeyAsync`, `TryResolveCurrentRowIndex`; `MainWindow.RefreshRuntimeBindings` command delegates. |
| Navigate forward | Suppresses next snapshot refresh, calls `runtimeController.NavigateToChildAsync`, renders inactive rows, refreshes chrome, runs overlap animation, swaps slots, resets slot opacity/z-index. | Suppresses next snapshot refresh, sets page-transition flag, calls `NavigateToChildAsync`, renders inactive rows, refreshes settings, runs overlap animation, swaps slots, drains pending snapshot refresh. | duplicated-divergent | `FlyoutWindow.OnRowNavigateAsync`; `MainWindow.OnRowNavigateAsync`; shared navigation state is `SitemapRuntimeController.NavigateToChildAsync`. |
| Navigate back | Flyout button checks `CanGoBack` before fire-and-forget call; `NavigateBackWithAnimationAsync` refreshes chrome, runs overlap animation, swaps slots, resets opacity/z-index. | Back button, GoBack key, and XButton1 pointer route to `NavigateBackWithAnimationAsync`; method sets page-transition flag and drains pending snapshot refresh. | duplicated-divergent | `FlyoutWindow.NavigateBack_Click`, `NavigateBackWithAnimationAsync`; `MainWindow.NavigateBack_Click`, `MainContent_KeyDown`, `MainContent_PointerPressed`, `NavigateBackWithAnimationAsync`; shared runtime method is `SitemapRuntimeController.NavigateBack`. |
| Breadcrumb/header behavior | Title is pinned to root breadcrumb or sitemap name; `BreadcrumbBar` shows home icon plus text items and supports breadcrumb jumps via `runtimeController.NavigateToBreadcrumb`. | Title uses current descriptor title; back button visibility reflects `CanGoBack`; no breadcrumb bar behavior in `MainWindow.xaml.cs`. | duplicated-divergent | `FlyoutWindow.RefreshChromeBindings`, `BreadcrumbBar_ItemClicked`, `BreadcrumbDisplayItem`; `MainWindow.RefreshChromeBindings`; shared breadcrumbs come from `SitemapRuntimeController.BuildBreadcrumbTrail` and `NavigateToBreadcrumb`. |
| Icon auth resolution | `ResolveIconAuth` maps local transport to token auth and cloud transport to username/password, using synchronous settings calls. | Same method names and same local/cloud mapping with synchronous settings calls. | duplicated-equivalent | `FlyoutWindow.ResolveIconAuth`, `GetApiTokenSync`, `GetCloudCredentialsSync`; `MainWindow.ResolveIconAuth`, `GetApiTokenSync`, `GetCloudCredentialsSync`; target type is `SitemapControlFactory.IconAuthContext`. |
| Chart/icon quality options | Passes `UseWindows11Icons` and `(int)ChartQuality` to `SitemapControlFactory.Create` from `CreateRowElementForIndex`. | Passes `UseWindows11Icons` to sitemap rows but does not pass `chartDpi`, so factory default `192` is used; `MainWindow.xaml.cs` has no `ChartQuality` reference. | duplicated-divergent | `FlyoutWindow.CreateRowElementForIndex`; `MainWindow.RefreshRuntimeBindings`; factory default is `SitemapControlFactory.Create(..., int chartDpi = 192)` and chart URL uses `BuildChartUrl`. |
| Animation state during snapshot updates | Snapshot handler suppresses one refresh but otherwise enqueues `RefreshRuntimeBindings`; page navigation/back animations do not track a pending snapshot flag. | Snapshot handler defers updates while `_isPageTransitionRunning` and drains `_pendingSnapshotRefresh` after navigate/back animation. | main-only | `FlyoutWindow` constructor `SnapshotChanged` handler and navigation methods; `MainWindow` constructor `SnapshotChanged` handler plus `_isPageTransitionRunning` / `_pendingSnapshotRefresh` handling in navigation methods. |
| Error display | `RunRuntimeOperationAsync` and `OnRowNavigateAsync` catch exceptions and set `StatusText.Text = $"Error: {ex.Message}"`; breadcrumb/back paths have limited or no catch. | `RunRuntimeOperationAsync`, `OnRowNavigateAsync`, settings save paths, log opening, and credential paths set `StatusText` on caught exceptions. | duplicated-divergent | `FlyoutWindow.RunRuntimeOperationAsync`, `OnRowNavigateAsync`; `MainWindow.RunRuntimeOperationAsync`, `OnRowNavigateAsync`, credential/settings handlers; shared runtime also sets snapshot `StatusText` and `HasError`. |

## Security And Privacy Review

Task 3 found one release-hygiene issue and one local privacy risk. The highest security priority is `S1`: tracked `.pfx` temporary signing certificates and `.user` publish metadata should be reviewed before any distributable package is treated as release-ready. Runtime credential handling has positive evidence: `OpenHabHttpClient` and `NotificationPoller` inject `Bearer` or `Basic` headers without logging header values, credentials are stored through `ICredentialStore` / `WindowsCredentialStore`, and existing HTTP-client tests assert configured tokens and URI credentials are not included in exception messages.

The remaining privacy gap is `S2`: failed openHAB response bodies are copied into exception messages, then into runtime logs and status text. This is local-only and user-facing rather than proof of current credential leakage, but it needs a redaction contract because server-provided bodies are outside the app's control.

## Runtime Efficiency Review

The main runtime-efficiency opportunities are image/network churn and row rebuild churn. `E1` is the clearest network issue: chart URLs include a random query value and chart images are fetched and decoded on every render. `E2` shows the main window recreates all row controls when no changed-row delta is available, while the flyout already has more selective no-op and structural reconciliation paths. `E3` is a lower-priority memory risk: icon caching avoids repeated fetches, but successful `ImageSource` entries have no eviction or size guard.

The notification poller uses the configured interval with `Task.Delay` and a cancellation token, and the sitemap runtime debounces widget-event reconcile refreshes. Those are reasonable baseline controls; future efficiency work should measure request counts, created row elements, image decode counts, and icon cache size before changing behavior.

## Repository Instructions And Design Status Review

`AGENTS.md` gives accurate high-level repository rules: preserve the layer split, reuse the sitemap/runtime/rendering pipeline, avoid exposing credentials in diagnostics, and treat status documents as the source of truth over design documents. The key paths named by `AGENTS.md` were verified with `rg --files AGENTS.md src tests docs\superpowers\specs docs\superpowers\status docs\superpowers\plans`; the command returned `AGENTS.md`, the cited sitemap design spec, the three cited status pages, the listed source entry points, and current test projects.

The main documentation weakness is not a broken link. It is that `AGENTS.md` still points future readers to three 2026-05-05 status pages as the best shipped-state summary, while the repository also contains later plan/status files for auth and notifications, tray shell behavior, UI polish, event stream live updates, notification debugging, flyout animation, merge-conflict fallout, and flyout retoggle issues. The three AGENTS-linked status pages remain valuable historical anchors, but they can now understate current work in areas such as credentials, notifications, the main window path, and event-stream support.

The original sitemap design remains useful as a product and architecture reference. It should not be treated as a completion checklist without a current-state overlay: several design areas are shipped in foundation form, some are implemented only partially, and others remain pending or release-blocked. The table below maps the original design areas to the three AGENTS-linked status documents plus the current known repo inventory from this audit.

| Original design area | Current state | Evidence | Next action |
| --- | --- | --- | --- |
| Endpoint selection and profiles | shipped | Foundation status says Task 2 added endpoint profiles and local/cloud/automatic transport selection; `rg --files` shows `src\OpenHab.Core\Profiles\EndpointSelector.cs`, `ServerProfile.cs`, `EndpointMode.cs`, and `tests\OpenHab.Core.Tests\EndpointSelectorTests.cs`. | Keep as current foundation behavior; track future changes in the consolidated current-state document. |
| openHAB HTTP client | shipped | Foundation status says Task 3 added the openHAB HTTP client for commands, state updates, sitemap JSON loading, diagnostics, URI construction, and cancellation-aware tests; `rg --files` shows `src\OpenHab.Core\Api\OpenHabHttpClient.cs` and HTTP client tests. | Keep the client as the shared API surface; address `S2` before relying on raw server error bodies in logs or status text. |
| Sitemap parsing and normalization | shipped | Foundation status says Task 4 added sitemap models and normalization; connected homepage status says sitemap REST JSON parsing from the openHAB homepage payload was added; `rg --files` shows `OpenHabSitemapJsonParser.cs`, `SitemapNormalizer.cs`, and related tests. | Continue compatibility coverage as new widget types and fallback behavior are added. |
| Render descriptors and skins | shipped | Foundation status says Task 6 added render descriptors, skin contract, Basic skin, Windows 11 skin, and shared row mapping; UI slice status says the sample sitemap rendered through those descriptors. | Preserve the skin-neutral layer and avoid moving WinUI concerns into `OpenHab.Rendering`; use `M1` and `M2` to reduce Windows-layer duplication. |
| Tray shell and flyout | partial | UI slice status says a tray icon and compact WinUI flyout host were added; `rg --files` shows `TrayIconService.cs`, `FlyoutWindow.xaml`, and flyout tests. Later plan/status inventory includes tray-shell, polish, animation, and retoggle follow-ups, showing this area is active rather than settled. | Record exact current flyout behavior and known caveats in the consolidated tracker before more UI work. |
| Main window path | partial | Foundation status originally listed main window and settings UI out of scope; current repo inventory shows `src\OpenHab.Windows.Tray\MainWindow.xaml` and `.xaml.cs`, and Task 2 findings show duplicated sitemap behavior between main window and flyout. | Treat the main window as implemented but not behaviorally unified with flyout; follow `M1` and `M2`. |
| Settings and credentials | partial | UI slice status added app settings; connected homepage status still listed persisted settings and credentials out of scope; current repo inventory shows `AppSettingsController.cs`, `AppSettings.cs`, `WindowsCredentialStore.cs`, credential tests, and Task 3 finding `R2` on fire-and-forget persistence. | Track current persisted settings and credential behavior explicitly; harden save durability and release hygiene before calling this complete. |
| Event stream live updates | partial | The three AGENTS-linked status pages list event stream live updates as out of scope, but current repo inventory shows `OpenHabEventStreamClient.cs`, SSE tests, and `docs/superpowers/status/2026-05-07-openhab-windows-event-stream-status.md`; Task 3 finding `R1` records lifecycle risk. | Mark as implemented with reliability caveats in the consolidated tracker; fix `R1` before treating live updates as stable. |
| Notifications | partial | UI slice and connected homepage statuses list native notifications out of scope; current repo inventory shows `OpenHab.Windows.Notifications`, `NotificationPoller.cs`, toast service files, notification tests, and later notification status/debugging docs. | Document current polling/toast degradation behavior and keep packaging/toast constraints visible. |
| Device-state mapping | shipped | Foundation status says Task 7 added Windows device state telemetry mapping from battery, charging, lock, and session snapshots to configured openHAB Item state updates; `rg --files` shows `DeviceStateMapper.cs` and `DeviceStateMapperTests.cs`. | Keep the mapping as shipped foundation scope; separately track real Windows collection and scheduled/event-triggered telemetry sending if still pending. |
| Offline/cache behavior | pending | The sitemap design calls for cached offline browsing; foundation and connected homepage statuses list cached offline sitemap state or offline cache persistence as out of scope. Current audit evidence found icon caching but no shipped offline sitemap persistence. | Add explicit offline/cache backlog after Task 5; do not imply cached browsing is shipped. |
| Packaging/release workflow | partial | Foundation and UI slice statuses list MSIX packaging/signing out of scope; current repo inventory includes `src\OpenHab.Windows.Package`, manifests, `.pfx` files, and publish metadata; baseline verification notes the `.wapproj` DesktopBridge import issue, and `S1` records signing artifact risk. | Treat packaging as not release-ready; resolve `S1` and document supported developer versus release packaging. |
| UI automation | pending | The original design asks for small real UI tests; foundation status lists UI automation tests as left from the original design. Current test inventory is unit/integration-style xUnit coverage under `tests/*`, with no UI automation project found by `rg --files`. | Add UI automation only after the tracker clarifies stable flyout/main behaviors and packaging/run prerequisites. |

### D1: Status docs are too fragmented for reliable project orientation

- **Severity:** Low
- **Priority:** Documentation/Tracking
- **Evidence:** `AGENTS.md` directs readers to `docs/superpowers/status/2026-05-05-openhab-windows-foundation-status.md`, `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`, and `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md` as the source of truth. `rg --files docs\superpowers\status docs\superpowers\plans` also shows later status and plan files for auth/notifications, tray shell behavior, UI polish, event stream live updates, notification debugging, flyout animation, merge-conflict resolution, and flyout retoggle issues.
- **Impact:** A maintainer or future agent who follows `AGENTS.md` literally can conclude that event stream live updates, persisted credentials, notifications, main-window behavior, and packaging constraints are still only pending, even though the current repo contains implementation and later status evidence for several of those areas.
- **Suggested direction:** Create one current-state tracker that links to historical status pages and classifies each feature as shipped, partial, pending, obsolete, or historical reference.
- **Affected files/layers:** `AGENTS.md`, `docs/superpowers/status/*.md`, `docs/superpowers/plans/*.md`, and the proposed `docs/superpowers/status/openhab-windows-current-state.md`.
- **Verification needed:** Run `rg --files AGENTS.md docs\superpowers\specs docs\superpowers\status docs\superpowers\plans` and confirm every status/spec/plan path linked from `AGENTS.md` and the tracker exists.

### D2: AGENTS status guidance omits later current-state evidence

- **Severity:** Low
- **Priority:** Documentation/Tracking
- **Evidence:** `AGENTS.md` says to use the three 2026-05-05 status docs as the best summary of finished behavior. Those pages say event stream live updates, notifications, credentials, WebView/Main UI fallback, offline cache persistence, and packaging are out of scope at that point. Current file inventory includes `src\OpenHab.Core\Events\OpenHabEventStreamClient.cs`, `src\OpenHab.Windows.Notifications\NotificationPoller.cs`, credential store files, main/flyout windows, and later status pages such as `2026-05-07-openhab-windows-event-stream-status.md` and `2026-05-06-openhab-windows-notification-debugging.md`.
- **Impact:** The repository instructions are conservative, but now incomplete as an orientation path. They correctly warn that designs are not shipped behavior, yet they do not provide a single place that reconciles shipped code with later status pages and active audit findings.
- **Suggested direction:** After the consolidated tracker exists, update `AGENTS.md` to name that tracker as the first status document, with the three 2026-05-05 pages and later status pages listed as historical references.
- **Affected files/layers:** `AGENTS.md`, `docs/superpowers/status/2026-05-05-openhab-windows-*.md`, later `docs/superpowers/status/*.md`, and `docs/superpowers/status/openhab-windows-current-state.md`.
- **Verification needed:** Review `AGENTS.md` after Task 5 follow-up work and confirm it no longer requires readers to infer current state from multiple historical pages.

### D3: Original design needs an explicit historical-reference label and current-state overlay

- **Severity:** Low
- **Priority:** Documentation/Tracking
- **Evidence:** `docs/superpowers/specs/2026-05-04-openhab-windows-sitemap-client-design.md` describes MVP goals including cached offline browsing, active transport status, secure credential storage, live item updates, unsupported-widget fallback, telemetry sending, packaging, and UI tests. The three AGENTS-linked status pages show some foundation/UI/connectivity items complete, while offline cache persistence, WebView/Main UI fallback routing, notifications, telemetry sending, packaging, and UI automation remain out of scope as of those checkpoints. Current repo state has moved some areas forward after those checkpoints, which is not visible from the design spec alone.
- **Impact:** The design spec is still useful, but without a current-state table it can be read either as a shipped MVP checklist or as stale direction. That ambiguity makes audit backlog and implementation planning easier to mis-rank.
- **Suggested direction:** Treat the design spec as a historical product/architecture reference, and keep a separate current-state tracker with a design-area table like the one in this audit.
- **Affected files/layers:** `docs/superpowers/specs/2026-05-04-openhab-windows-sitemap-client-design.md`, `docs/superpowers/status/*.md`, and `docs/superpowers/status/openhab-windows-current-state.md`.
- **Verification needed:** When the tracker is created, compare each original design area against source files, tests, and status docs, then leave the design spec unchanged unless a future product-direction update is explicitly requested.

## Consolidated Tracker Recommendation

Recommended path: `docs/superpowers/status/openhab-windows-current-state.md`

Purpose: this file should be the first document read after `AGENTS.md`. It should replace scattered status-page reading as the active source of truth while preserving older status pages as historical evidence.

Recommended sections:

- Current shipped behavior
- Partial behavior and known limitations
- Remaining planned work
- Out-of-scope work
- Active risks and quality backlog
- Historical reference documents
- Update rule: update this tracker whenever a plan completes or an audit changes project priorities

First backlog item: create `docs/superpowers/status/openhab-windows-current-state.md` from the accepted audit report and update `AGENTS.md` to point to it as the primary status document.

## Backlog

Pending Task 5 after findings are ranked.

## Verification

- `git status --short`: recorded under Baseline; no tracked or untracked entries were shown, but safe.directory warnings were emitted.
- `git status --short --ignored`: recorded summary under Baseline; only ignored `bin/` and `obj/` output under `src/` and `tests/` was observed.
- `git log --oneline -10`: recorded under Baseline.
- `rg --files src tests docs\superpowers\specs docs\superpowers\status docs\superpowers\plans`: used to identify audit coverage and artifact categories; full output not pasted because it was routine inventory.
- `Get-ChildItem -Path src,tests -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } | ... | Select-Object -First 25`: recorded Task 2 large-file metric under `Task 2 Maintainability Evidence Notes`; top relevant files were `SitemapControlFactory.cs` (2462 lines), `FlyoutWindow.xaml.cs` (1396), `MainWindow.xaml.cs` (1091), and `SitemapRuntimeController.cs` (907).
- `rg -n "RefreshRuntimeBindings|CreateRowElementForIndex|ButtonGrid|NavigateBackWithAnimationAsync|OnRowNavigateAsync|ResolveIconAuth|GetApiTokenSync|GetCloudCredentialsSync|ChangedRowIndices|Breadcrumb" src\OpenHab.Windows.Tray src\OpenHab.App tests`: recorded Task 2 duplicate sitemap concept observations and used them to inspect surrounding code.
- `rg -n "async void|Task\.Run|_ = |CancellationToken\.None|OperationCanceledException|DispatcherQueue\.TryEnqueue|Dispose|event .*\\+=|Task\.Delay|Interlocked|Volatile|Thread\.Sleep" src tests`: run for Task 3 reliability review. Key hits inspected included `SitemapRuntimeController` fire-and-forget stream/reconcile work, `OpenHabEventStreamClient.ConnectAsync`, dispatcher enqueue paths in both windows, settings persistence `_ = SaveAsync()`, and tests using retry loops or `Task.Delay`.
- `rg -n "password|token|credential|Authorization|Basic|Bearer|secret|Log|DiagnosticLogger|StatusText|ProcessStartInfo|TemporaryKey|pfx|Package.appxmanifest|AppPackages|BundleArtifacts" src tests AGENTS.md docs`: run for Task 3 security/privacy review. Key hits inspected included HTTP auth header injection, credential store paths, diagnostic/status text propagation, package manifest/signing configuration, tracked `.pfx` files, and tracked `.user` publish metadata.
- `rg -n "HttpClient|Children\.Clear|new BitmapImage|SvgImageSource|ReadAsByteArrayAsync|ResponseContentRead|ResponseHeadersRead|Timer|PeriodicTimer|Poll|Debounce|Refresh|Reconcile|StartSitemapEventStreamAsync|ConnectAsync" src tests`: run for Task 3 efficiency review. Key hits inspected included icon/chart loading in `SitemapControlFactory`, main/flyout row refresh behavior, notification polling, SSE connection setup, and widget-event reconcile debounce.
- `git ls-files 'src/**.user' 'src/**.pfx' 'src/**/AppPackages/**' 'src/**/BundleArtifacts/**'`: found tracked temporary signing certificates and user publish metadata recorded in `S1`.
- `rg --files AGENTS.md src tests docs\superpowers\specs docs\superpowers\status docs\superpowers\plans`: run for Task 4. Key result: verified `AGENTS.md`, the AGENTS-named source entry points, the AGENTS-named sitemap design and three status docs, and the current source/test/spec/status/plan inventory all exist.
- `Select-String -Path docs\superpowers\plans\*.md -Pattern '^# |^## |^### |Goal:|Still Out Of Scope|Out of scope|Verification|MSIX|package|UI automation|Event stream|Notifications|credentials|cache|offline|main window|flyout|tray|WebView|telemetry'`: run for Task 4 plan review. Key result: confirmed the plan inventory spans foundation, auth/notifications, connected homepage, tray shell behavior, UI bugfix/polish/icon polish, packaging/notifications migration, notification inbox actions, flyout animation/corners/light-dismiss, and this audit plan.
- `Select-String -Path docs\superpowers\status\*.md -Pattern '^# |^## |Completed|Still Out Of Scope|Verification|Event stream|Notifications|credentials|cache|offline|packaging|UI automation|Main|flyout|tray|MSIX|WebView|telemetry'`: run as supporting Task 4 current-state review. Key result: later status docs contain evidence for auth/notifications, tray shell behavior, UI polish, event stream live updates, notification debugging, flyout animation, merge-conflict fallout, and flyout retoggle issues beyond the three AGENTS-linked status docs.
- Placeholder/self-review search for pending Task 3 text and template instructions: no matches after Task 3 edits.
- `git diff --check -- docs\superpowers\audits\2026-05-11-openhab-windows-code-quality-audit.md`: no whitespace errors; Git emitted the existing global safe.directory warnings and an LF-to-CRLF warning for the audit file.
- Task 4 placeholder/template self-review search: no leftover Task 4 placeholders after edits.
- `git diff --name-only`: only `docs/superpowers/audits/2026-05-11-openhab-windows-code-quality-audit.md` was modified for Task 4.
- Tests were not rerun for Task 1. Controller-provided baseline verification is recorded in `Baseline Verification Note`.
- Tests were not rerun for Task 3 because the task modified documentation only and required search/inspection verification rather than production behavior changes.
- Tests were not rerun for Task 4 because the task modified documentation only and required path/status/spec/plan review rather than production behavior changes.
