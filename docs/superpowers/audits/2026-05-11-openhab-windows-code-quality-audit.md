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

### Project Inventory Note

`rg --files src tests docs\superpowers\specs docs\superpowers\status docs\superpowers\plans` showed the expected source, test, spec, status, and plan coverage for `OpenHab.Core`, `OpenHab.Sitemaps`, `OpenHab.Rendering`, `OpenHab.App`, `OpenHab.Windows.Tray`, `OpenHab.Windows.Notifications`, and `OpenHab.Windows.Package`. The inventory also exposed package/signing and user-specific artifact categories to include in later security and repository hygiene review, including `.pfx`, `.csproj.user`, and `.pubxml.user` files under `src/`.

### Baseline Verification Note

Controller-provided baseline verification: direct test projects passed `266/266` in this worktree. `dotnet test OpenHab.Windows.sln` restored packages when run escalated but exited nonzero because `src/OpenHab.Windows.Package/OpenHab.Windows.Package.wapproj` imports missing `C:\Program Files\dotnet\sdk\10.0.203\Microsoft\DesktopBridge\Microsoft.DesktopBridge.props`.

## Executive Summary

Pending completion after audit findings are collected and ranked. Task 1 establishes the report skeleton, repository baseline, reviewed documents, and known verification constraints.

## Findings

Findings will be ordered by priority: maintainability first, reliability/correctness second, runtime efficiency third. Security/privacy findings will be escalated when severity warrants it.

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

Pending Task 3 evidence collection. Baseline inventory already identifies package/signing and user-specific artifact categories that should be reviewed for repository hygiene.

## Runtime Efficiency Review

Pending Task 3 evidence collection.

## Repository Instructions And Design Status Review

Pending Task 4 evidence collection. `AGENTS.md` currently directs readers to the status docs as source of truth and warns that design/spec docs describe intended direction rather than shipped behavior.

## Consolidated Tracker Recommendation

Pending Task 4 evidence collection. The code quality audit design asks the audit to recommend a single active tracking document for shipped behavior, partial work, remaining work, out-of-scope work, active risks, and historical references.

## Backlog

Pending Task 5 after findings are ranked.

## Verification

- `git status --short`: recorded under Baseline; no tracked or untracked entries were shown, but safe.directory warnings were emitted.
- `git status --short --ignored`: recorded summary under Baseline; only ignored `bin/` and `obj/` output under `src/` and `tests/` was observed.
- `git log --oneline -10`: recorded under Baseline.
- `rg --files src tests docs\superpowers\specs docs\superpowers\status docs\superpowers\plans`: used to identify audit coverage and artifact categories; full output not pasted because it was routine inventory.
- `Get-ChildItem -Path src,tests -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } | ... | Select-Object -First 25`: recorded Task 2 large-file metric under `Task 2 Maintainability Evidence Notes`; top relevant files were `SitemapControlFactory.cs` (2462 lines), `FlyoutWindow.xaml.cs` (1396), `MainWindow.xaml.cs` (1091), and `SitemapRuntimeController.cs` (907).
- `rg -n "RefreshRuntimeBindings|CreateRowElementForIndex|ButtonGrid|NavigateBackWithAnimationAsync|OnRowNavigateAsync|ResolveIconAuth|GetApiTokenSync|GetCloudCredentialsSync|ChangedRowIndices|Breadcrumb" src\OpenHab.Windows.Tray src\OpenHab.App tests`: recorded Task 2 duplicate sitemap concept observations and used them to inspect surrounding code.
- Tests were not rerun for Task 1. Controller-provided baseline verification is recorded in `Baseline Verification Note`.
