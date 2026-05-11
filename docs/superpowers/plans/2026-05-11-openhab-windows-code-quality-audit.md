# openHAB Windows Code Quality Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce an evidence-based code quality audit report and prioritized backlog for the openHAB Windows companion app.

**Architecture:** This plan is documentation-first and non-mutating for production code. It gathers baseline evidence, reviews the highest-risk source and documentation areas, writes a single audit report, and records a follow-up task for a consolidated project tracker.

**Tech Stack:** .NET 10, WinUI/Windows App SDK, xUnit, PowerShell, Git, Markdown.

---

## File Structure

Create:

- `docs/superpowers/audits/2026-05-11-openhab-windows-code-quality-audit.md`

Read only:

- `AGENTS.md`
- `docs/superpowers/specs/2026-05-04-openhab-windows-sitemap-client-design.md`
- `docs/superpowers/specs/2026-05-11-openhab-windows-code-quality-audit-design.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-foundation-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`
- `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
- `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`
- `src/OpenHab.App/Settings/AppSettingsController.cs`
- `src/OpenHab.Core/Api/OpenHabHttpClient.cs`
- `src/OpenHab.Core/Events/OpenHabEventStreamClient.cs`
- `src/OpenHab.Core/Auth/WindowsCredentialStore.cs`
- `src/OpenHab.Windows.Notifications/NotificationPoller.cs`
- Relevant tests under `tests/`

Do not modify production code, project files, `AGENTS.md`, the original design spec, status files, package artifacts, or generated output during this audit execution.

---

### Task 1: Establish Baseline And Report Skeleton

**Files:**
- Create: `docs/superpowers/audits/2026-05-11-openhab-windows-code-quality-audit.md`
- Read: `AGENTS.md`
- Read: `docs/superpowers/specs/2026-05-11-openhab-windows-code-quality-audit-design.md`
- Read: `docs/superpowers/status/2026-05-05-openhab-windows-foundation-status.md`
- Read: `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`
- Read: `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`

- [ ] **Step 1: Capture current repository state**

Run:

```powershell
git status --short
```

Expected: output may include existing package-related modified/untracked files. Record the output in the audit report under `Baseline`.

- [ ] **Step 2: Capture recent commits**

Run:

```powershell
git log --oneline -10
```

Expected: ten recent commit summaries. Record them under `Baseline`.

- [ ] **Step 3: Capture project file inventory**

Run:

```powershell
rg --files src tests docs\superpowers\specs docs\superpowers\status docs\superpowers\plans
```

Expected: source, test, spec, status, and plan file paths. Use this to identify audit coverage; do not paste the full output unless it exposes an unexpected file category.

- [ ] **Step 4: Create the audit report skeleton**

Create `docs/superpowers/audits/2026-05-11-openhab-windows-code-quality-audit.md` with this initial content:

```markdown
# openHAB Windows Code Quality Audit

Date: 2026-05-11

## Scope

This audit reviews maintainability, flyout/main sitemap behavior, reliability, security/privacy, runtime efficiency, automation, repository instructions, and project tracking. It does not implement production code changes.

## Baseline

### Git State

Paste summarized `git status --short` output here, preserving unrelated package artifacts as observed state.

### Recent Commits

Paste `git log --oneline -10` output here.

### Documents Reviewed

- `AGENTS.md`
- `docs/superpowers/specs/2026-05-04-openhab-windows-sitemap-client-design.md`
- `docs/superpowers/specs/2026-05-11-openhab-windows-code-quality-audit-design.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-foundation-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`

## Executive Summary

Complete this after all findings are ranked.

## Findings

Findings are ordered by priority: maintainability first, reliability/correctness second, runtime efficiency third. Security/privacy findings are escalated when severity warrants it.

## Flyout/Main Sitemap Behavior Comparison

| Behavior | Flyout | Main Window | Assessment | Evidence |
| --- | --- | --- | --- | --- |

## Security And Privacy Review

## Runtime Efficiency Review

## Repository Instructions And Design Status Review

## Consolidated Tracker Recommendation

## Backlog

## Verification
```

- [ ] **Step 5: Commit the skeleton**

Run:

```powershell
git add docs\superpowers\audits\2026-05-11-openhab-windows-code-quality-audit.md
git commit -m "docs: start code quality audit report"
```

Expected: a commit containing only the new audit report skeleton.

---

### Task 2: Audit Maintainability And Flyout/Main Behavior

**Files:**
- Modify: `docs/superpowers/audits/2026-05-11-openhab-windows-code-quality-audit.md`
- Read: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
- Read: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- Read: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- Read: `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`
- Read: `src/OpenHab.App/Sitemaps/SitemapRenderController.cs`
- Read: `tests/OpenHab.App.Tests/SitemapControlFactoryTests.cs`
- Read: `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`

- [ ] **Step 1: Measure large source files**

Run:

```powershell
Get-ChildItem -Path src,tests -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } | ForEach-Object { $lines=(Get-Content -LiteralPath $_.FullName).Count; [PSCustomObject]@{ Lines=$lines; Path=$_.FullName.Substring((Get-Location).Path.Length+1) } } | Sort-Object Lines -Descending | Select-Object -First 25 | Format-Table -AutoSize
```

Expected: the largest hand-authored C# files. Record the top files and whether size correlates with mixed responsibilities.

- [ ] **Step 2: Search for duplicated sitemap concepts**

Run:

```powershell
rg -n "RefreshRuntimeBindings|CreateRowElementForIndex|ButtonGrid|NavigateBackWithAnimationAsync|OnRowNavigateAsync|ResolveIconAuth|GetApiTokenSync|GetCloudCredentialsSync|ChangedRowIndices|Breadcrumb" src\OpenHab.Windows.Tray src\OpenHab.App tests
```

Expected: references in flyout, main window, runtime, rendering factory, and tests. Use the output to populate maintainability findings.

- [ ] **Step 3: Compare flyout and main window behavior**

Read `FlyoutWindow.xaml.cs` and `MainWindow.xaml.cs`. Fill the comparison table with these rows:

```markdown
| Initial runtime load |  |  |  |  |
| Manual refresh |  |  |  |  |
| Row-level delta update |  |  |  |  |
| Structural row reconciliation |  |  |  |  |
| Button-grid merge behavior |  |  |  |  |
| Command routing by row index or row key |  |  |  |  |
| Navigate forward |  |  |  |  |
| Navigate back |  |  |  |  |
| Breadcrumb/header behavior |  |  |  |  |
| Icon auth resolution |  |  |  |  |
| Chart/icon quality options |  |  |  |  |
| Animation state during snapshot updates |  |  |  |  |
| Error display |  |  |  |  |
```

For each row, classify `Assessment` as one of: `shared`, `duplicated-equivalent`, `duplicated-divergent`, `flyout-only`, or `main-only`.

- [ ] **Step 4: Write maintainability findings**

For each finding, use this exact format. The title must name the actual issue, such as `Duplicated sitemap row rendering between flyout and main window`, and the evidence line must cite exact paths and methods.

```markdown
### M1: Duplicated sitemap row rendering between flyout and main window

- **Severity:** Medium
- **Priority:** Maintainability
- **Evidence:** `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs` and `src/OpenHab.Windows.Tray/MainWindow.xaml.cs` both build sitemap rows and route commands, with differences recorded in the behavior comparison table.
- **Impact:** Explain the future maintenance or regression risk in one paragraph.
- **Suggested direction:** Describe a bounded direction such as extracting a shared sitemap surface coordinator, shared row diff adapter, or shared icon-auth resolver.
- **Affected files/layers:** List exact files and whether they belong to `OpenHab.App` or `OpenHab.Windows.Tray`.
- **Verification needed:** Name the tests or manual checks that should exist before a future refactor.
```

Use `M1`, `M2`, `M3` numbering. Add only findings with concrete evidence.

- [ ] **Step 5: Commit maintainability audit updates**

Run:

```powershell
git add docs\superpowers\audits\2026-05-11-openhab-windows-code-quality-audit.md
git commit -m "docs: audit sitemap maintainability"
```

Expected: a commit containing only audit report updates.

---

### Task 3: Audit Reliability, Security, And Runtime Efficiency

**Files:**
- Modify: `docs/superpowers/audits/2026-05-11-openhab-windows-code-quality-audit.md`
- Read: `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`
- Read: `src/OpenHab.Core/Events/OpenHabEventStreamClient.cs`
- Read: `src/OpenHab.Core/Api/OpenHabHttpClient.cs`
- Read: `src/OpenHab.App/Settings/AppSettingsController.cs`
- Read: `src/OpenHab.Core/Auth/WindowsCredentialStore.cs`
- Read: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
- Read: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- Read: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- Read: `src/OpenHab.Windows.Notifications/NotificationPoller.cs`
- Read: relevant tests under `tests/OpenHab.Core.Tests`, `tests/OpenHab.App.Tests`

- [ ] **Step 1: Search reliability patterns**

Run:

```powershell
rg -n "async void|Task\.Run|_ = |CancellationToken\.None|OperationCanceledException|DispatcherQueue\.TryEnqueue|Dispose|event .*\\+=|Task\.Delay|Interlocked|Volatile|Thread\.Sleep" src tests
```

Expected: async, cancellation, background task, event handler, and concurrency references. Use this output to inspect the surrounding code before writing findings.

- [ ] **Step 2: Search security and privacy patterns**

Run:

```powershell
rg -n "password|token|credential|Authorization|Basic|Bearer|secret|Log|DiagnosticLogger|StatusText|ProcessStartInfo|TemporaryKey|pfx|Package.appxmanifest|AppPackages|BundleArtifacts" src tests AGENTS.md docs
```

Expected: credential, logging, packaging, and generated-artifact references. Record only findings where there is evidence of risk or a documentation gap.

- [ ] **Step 3: Search efficiency patterns**

Run:

```powershell
rg -n "HttpClient|Children\.Clear|new BitmapImage|SvgImageSource|ReadAsByteArrayAsync|ResponseContentRead|ResponseHeadersRead|Timer|PeriodicTimer|Poll|Debounce|Refresh|Reconcile|StartSitemapEventStreamAsync|ConnectAsync" src tests
```

Expected: network, UI rebuild, polling, refresh, and icon/chart loading references. Inspect surrounding code before writing findings.

- [ ] **Step 4: Write reliability findings**

Add findings with this format:

```markdown
### R1: Fire-and-forget runtime operation can hide failures

- **Severity:** High
- **Priority:** Reliability
- **Evidence:** Cite exact file paths and methods.
- **Impact:** Explain the user-visible or correctness risk.
- **Suggested direction:** Describe the smallest future change that would reduce the risk.
- **Affected files/layers:** List exact files and layers.
- **Verification needed:** List the exact unit or integration behavior that should be tested.
```

Use `R1`, `R2`, `R3` numbering. Set severity to `High`, `Medium`, or `Low` based on evidence.

- [ ] **Step 5: Write security/privacy findings**

Add findings with this format:

```markdown
### S1: Package artifacts and certificates need release hygiene review

- **Severity:** High
- **Priority:** Security/Privacy
- **Evidence:** Cite exact file paths and methods or artifact paths.
- **Impact:** Explain whether the risk is user-facing, local-only, repository hygiene, or release-blocking.
- **Suggested direction:** Describe a bounded future change or documentation action.
- **Affected files/layers:** List exact files and layers.
- **Verification needed:** List a test, manual check, or release checklist item.
```

Use `S1`, `S2`, `S3` numbering. Escalate severity only when evidence supports it.

- [ ] **Step 6: Write runtime-efficiency findings**

Add findings with this format:

```markdown
### E1: Repeated icon loading can increase network and memory use

- **Severity:** Medium
- **Priority:** Runtime efficiency
- **Evidence:** Cite exact file paths and methods.
- **Impact:** Explain likely power, memory, network, or redraw impact.
- **Suggested direction:** Describe a bounded future optimization.
- **Affected files/layers:** List exact files and layers.
- **Verification needed:** Describe how to measure improvement or prevent regression.
```

Use `E1`, `E2`, `E3` numbering.

- [ ] **Step 7: Commit reliability, security, and efficiency audit updates**

Run:

```powershell
git add docs\superpowers\audits\2026-05-11-openhab-windows-code-quality-audit.md
git commit -m "docs: audit reliability security efficiency"
```

Expected: a commit containing only audit report updates.

---

### Task 4: Review Project Instructions, Original Design, And Status Tracking

**Files:**
- Modify: `docs/superpowers/audits/2026-05-11-openhab-windows-code-quality-audit.md`
- Read: `AGENTS.md`
- Read: `docs/superpowers/specs/2026-05-04-openhab-windows-sitemap-client-design.md`
- Read: `docs/superpowers/status/2026-05-05-openhab-windows-foundation-status.md`
- Read: `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`
- Read: `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`
- Read: `docs/superpowers/plans/*.md`

- [ ] **Step 1: Compare `AGENTS.md` to current files**

Run:

```powershell
rg --files AGENTS.md src tests docs\superpowers\specs docs\superpowers\status docs\superpowers\plans
```

Expected: enough file coverage to verify key files and status/spec paths named by `AGENTS.md`.

- [ ] **Step 2: Review original sitemap design against current status**

Read the original sitemap design and the three status documents. In the audit report, add this table:

```markdown
| Original design area | Current state | Evidence | Next action |
| --- | --- | --- | --- |
| Endpoint selection and profiles |  |  |  |
| openHAB HTTP client |  |  |  |
| Sitemap parsing and normalization |  |  |  |
| Render descriptors and skins |  |  |  |
| Tray shell and flyout |  |  |  |
| Main window path |  |  |  |
| Settings and credentials |  |  |  |
| Event stream live updates |  |  |  |
| Notifications |  |  |  |
| Device-state mapping |  |  |  |
| Offline/cache behavior |  |  |  |
| Packaging/release workflow |  |  |  |
| UI automation |  |  |  |
```

Use `Current state` values: `shipped`, `partial`, `pending`, `obsolete`, or `historical reference`.

- [ ] **Step 3: Write repository-instruction findings**

Add findings with this format:

```markdown
### D1: Status docs are too fragmented for reliable project orientation

- **Severity:** Low
- **Priority:** Documentation/Tracking
- **Evidence:** Cite `AGENTS.md`, design spec, or status docs.
- **Impact:** Explain how a future agent or maintainer could be misled.
- **Suggested direction:** Describe the documentation update or tracker rule.
- **Affected files/layers:** List exact documentation paths.
- **Verification needed:** Describe a review check, such as confirming every linked status/spec path exists.
```

Use `D1`, `D2`, `D3` numbering.

- [ ] **Step 4: Write consolidated tracker recommendation**

Add this section to the report:

```markdown
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
```

- [ ] **Step 5: Commit documentation/tracking audit updates**

Run:

```powershell
git add docs\superpowers\audits\2026-05-11-openhab-windows-code-quality-audit.md
git commit -m "docs: audit project tracking documentation"
```

Expected: a commit containing only audit report updates.

---

### Task 5: Build Prioritized Backlog And Verify

**Files:**
- Modify: `docs/superpowers/audits/2026-05-11-openhab-windows-code-quality-audit.md`

- [ ] **Step 1: Add executive summary**

At the top of the audit report, complete `Executive Summary` with:

```markdown
## Executive Summary

The highest-priority quality risk is duplicated sitemap behavior between flyout and main window, because it makes future sitemap changes likely to drift unless a shared coordinator or adapter is introduced.

The next reliability concern is runtime lifecycle behavior around background operations, live updates, and UI dispatcher refreshes; the exact top issue is identified by the highest-severity `R` finding in this report.

The most practical runtime-efficiency opportunity is reducing repeated network and UI work in icon/chart loading, row rebuilding, refresh, or reconcile paths; the exact top issue is identified by the highest-severity `E` finding in this report.

The project documentation needs a consolidated tracker at `docs/superpowers/status/openhab-windows-current-state.md` so future status does not depend on reading multiple historical pages.
```

If the completed findings identify a different top issue, rewrite the affected sentence with the exact finding ID and evidence.

- [ ] **Step 2: Add prioritized backlog**

Add backlog items in this exact format:

```markdown
### B1: Create consolidated project tracker

- **Priority:** P0
- **Problem:** One paragraph describing the problem.
- **Evidence:** Link to finding IDs, such as `M1`, `R2`, or `D1`, and cite files.
- **Suggested change:** One paragraph describing the future implementation scope.
- **Affected files/layers:** Exact paths and layers.
- **Verification:** Exact tests, build commands, or manual checks.
- **Risk/notes:** One paragraph describing sequencing or migration risk.
```

Use these priority meanings:

- `P0`: unblock future work or prevent major drift/security risk.
- `P1`: high-value cleanup or reliability work with bounded scope.
- `P2`: optimization, tooling, or documentation improvement that can follow P0/P1 work.

Include at least these backlog themes when supported by findings:

- Create consolidated tracker and update `AGENTS.md` to point to it.
- Unify flyout/main sitemap behavior behind a shared coordinator or adapter.
- Add tests around shared sitemap row update and navigation behavior before refactoring.
- Harden runtime/SSE lifecycle, cancellation, and background reconcile behavior.
- Review credential/logging/package-artifact hygiene.
- Add practical quality gates where local tooling already supports them.
- Improve icon/chart loading, caching, and idle refresh behavior where evidence supports it.

- [ ] **Step 3: Run full tests if practical**

Run:

```powershell
dotnet test OpenHab.Windows.sln
```

Expected: tests pass. If this fails because of existing package/project state or environment limitations, record the failure summary and continue to the build step.

- [ ] **Step 4: Run release build if practical**

Run:

```powershell
dotnet build OpenHab.Windows.sln --configuration Release
```

Expected: build passes. If this fails because of existing package/project state or environment limitations, record the failure summary in `Verification`.

- [ ] **Step 5: Record verification**

Add a verification section with this format:

```markdown
## Verification

- `git status --short`: recorded baseline; package-related modified/untracked files existed before audit execution.
- `dotnet test OpenHab.Windows.sln`: record pass, fail, or not run with a concrete reason.
- `dotnet build OpenHab.Windows.sln --configuration Release`: record pass, fail, or not run with a concrete reason.
- Audit report self-review: checked for unsupported claims, missing evidence, and backlog items without findings.
```

Replace the generic verification wording with actual command results before committing the final report.

- [ ] **Step 6: Self-review the audit report**

Run:

```powershell
rg -n "record pass, fail, or not run|One paragraph describing|Cite exact|Explain the|Describe a|List exact|Name the tests|Use concrete" docs\superpowers\audits\2026-05-11-openhab-windows-code-quality-audit.md
```

Expected: no matches. If matches exist, replace them with concrete content.

- [ ] **Step 7: Commit final audit report**

Run:

```powershell
git add docs\superpowers\audits\2026-05-11-openhab-windows-code-quality-audit.md
git commit -m "docs: complete code quality audit backlog"
```

Expected: a commit containing only final audit report updates.

- [ ] **Step 8: Report completion**

Send a final summary that includes:

```markdown
Audit report complete: `docs/superpowers/audits/2026-05-11-openhab-windows-code-quality-audit.md`

Top backlog item: quote the title of backlog item `B1`.

Verification:
- `dotnet test OpenHab.Windows.sln`: quote the result recorded in the audit report.
- `dotnet build OpenHab.Windows.sln --configuration Release`: quote the result recorded in the audit report.

Unrelated worktree state left untouched: package project changes and generated package artifacts observed in baseline.
```
