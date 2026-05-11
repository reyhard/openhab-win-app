# openHAB Windows Code Quality Audit Design

Date: 2026-05-11

## Goal

Prepare an evidence-based code quality audit for the openHAB Windows companion app and turn the results into a prioritized implementation backlog. The audit does not implement fixes directly. It identifies maintainability, reliability, security, and runtime-efficiency issues with enough evidence that later implementation plans can address them safely.

The priority order is:

1. Maintainability and future change safety.
2. Reliability and correctness.
3. Runtime efficiency, including power, memory, network, and redraw behavior.

Security and privacy findings are escalated above this order when they expose credentials, endpoint details, unsafe generated artifacts, or user-sensitive data.

## Current Context

The app is intentionally layered:

- `OpenHab.Core`: endpoint selection, HTTP, credentials, events, and device-state mapping.
- `OpenHab.Sitemaps`: sitemap models, parsing, normalization, and navigation intents.
- `OpenHab.Rendering`: skin-neutral descriptors and skin mapping.
- `OpenHab.App`: settings, notifications, tray/runtime controllers, and UI-independent state.
- `OpenHab.Windows.Tray`: WinUI shell, flyout, main window, tray integration, and rendering controls.
- `OpenHab.Windows.Notifications`: Windows notification support.
- `tests/*`: xUnit coverage for core, sitemap, rendering, and app behavior.

Latest status docs show that the foundation, UI slice, and connected homepage are implemented, with later work such as deeper fallback surfaces, persistent offline cache, and broader UI automation still out of scope.

Initial inspection found likely audit hotspots:

- `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
- `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`
- `src/OpenHab.App/Settings/AppSettingsController.cs`
- `AGENTS.md`

The worktree may contain package/build artifacts and package project changes. The audit should record them as repository hygiene evidence, not delete or rewrite them unless a later task explicitly approves that cleanup.

## Deliverables

The audit should produce:

1. A prioritized findings report with file references, evidence, severity, and impact.
2. A flyout/main sitemap behavior comparison.
3. A proposed direction for unifying shared sitemap behavior between flyout and main window.
4. A security/privacy review covering credentials, logs, auth headers, generated artifacts, temporary certificates, and diagnostics.
5. A runtime-efficiency review covering idle behavior, refresh/reconcile loops, icon/chart loading, UI rebuilds, timers, and polling.
6. A repository-instructions review for `AGENTS.md`.
7. A backlog of scoped follow-up tasks suitable for later implementation planning.
8. A verification summary listing commands run and results.

## Audit Lanes

### Maintainability

Inspect large files, duplicated rendering and navigation paths, row identity/state update behavior, helper extraction opportunities, layering boundaries, and testability. The main focus is whether future sitemap work can be added without editing multiple divergent UI paths.

Expected hotspots:

- `FlyoutWindow.xaml.cs`
- `MainWindow.xaml.cs`
- `SitemapControlFactory.cs`
- `SitemapRuntimeController.cs`

### Flyout/Main Unification

Compare flyout and main window behavior feature by feature:

- Initial load and manual refresh.
- Row-level deltas.
- Structural row reconciliation.
- Navigation and back behavior.
- Breadcrumb/header behavior.
- Button-grid merging and command routing.
- Icon and chart loading.
- Icon authentication.
- Animation state.
- Error handling.

The output should identify a target shared abstraction, such as a shared sitemap surface coordinator or renderer adapter, but should not implement it during the audit.

### Reliability

Inspect async and event-driven behavior:

- Fire-and-forget tasks.
- Cancellation propagation.
- Background reconcile loops.
- SSE subscription, reconnect, and disposal lifetime.
- UI dispatcher failures.
- Stale updates and race conditions.
- Event handler cleanup.
- App shutdown and tray/flyout close paths.

Findings should distinguish between proven bugs, plausible risks, and areas that need targeted tests before a fix is designed.

### Security And Privacy

Inspect sensitive data handling:

- Credential storage and retrieval.
- Auth header construction.
- Password/token UI behavior.
- Logging and diagnostic redaction.
- Generated package artifacts.
- Temporary certificates.
- Endpoint and credential leakage in errors or status text.

Security findings should include whether the issue is user-facing, local-only, repository hygiene, or release-blocking.

### Runtime Efficiency

Inspect power, memory, network, and redraw behavior:

- Idle behavior while the app is minimized, hidden to tray, or disconnected.
- SSE reconnect and reconciliation debounce behavior.
- Notification polling cadence.
- Icon/chart HTTP usage and cache opportunities.
- Static `HttpClient` usage.
- UI control rebuilds versus state updates.
- Retained UI state and event handlers.
- Avoidable allocations in hot rendering paths.

Efficiency findings should prioritize practical improvements for a tray app: low idle CPU/network activity, bounded memory growth, and avoiding visible flicker.

### Automation

Review available quality gates and propose additions only where they would catch meaningful regressions:

- Existing `dotnet test OpenHab.Windows.sln`.
- Existing `dotnet build OpenHab.Windows.sln --configuration Release`.
- Nullable and compiler diagnostics.
- .NET analyzers and style rules.
- Dependency vulnerability checks if available locally.
- Code metrics for unusually large files or complex methods.
- Targeted unit tests for shared sitemap behavior and runtime races.

The audit should not install tooling or mutate project configuration without separate approval.

### Repository Instructions

Review `AGENTS.md` against current repository reality:

- Project summary accuracy.
- Key file list accuracy.
- Verification commands.
- Status-doc guidance.
- Generated artifact guidance.
- Layering and security rules.
- Subagent guidance.
- Missing or stale instructions that could mislead future agents.

Recommended documentation updates should be backlog items unless the user separately asks to edit `AGENTS.md`.

## Workflow

1. Establish the baseline:
   - `git status --short`
   - recent commits
   - latest status docs
   - current project/test structure
   - current `AGENTS.md`

2. Collect evidence by lane:
   - Search for hotspots such as `TODO`, `FIXME`, credentials, tokens, auth, `async void`, `Task.Run`, fire-and-forget calls, cancellation, disposal, timers, polling, and dispatcher enqueue failures.
   - Read the major runtime and UI files.
   - Measure source file size and identify unusually large classes.
   - Review tests around runtime, rendering, sitemap parsing, credentials, notifications, and tray behavior.

3. Cross-check flyout/main sitemap behavior:
   - Build a comparison table.
   - Mark behavior as shared, duplicated-equivalent, duplicated-divergent, flyout-only, or main-only.
   - Identify the smallest shared abstraction that would remove drift without pushing WinUI concerns into lower layers.

4. Rank findings:
   - Use the user priority order: maintainability, reliability, efficiency.
   - Escalate security/privacy findings when severity justifies it.
   - Separate proven issues from speculative improvements.

5. Build the backlog:
   - Convert findings into small, sequenced tasks.
   - Keep architecture-sensitive work separate from quick wins.
   - Include affected layers, verification, and risk notes for each task.

6. Record verification:
   - List commands run and their results.
   - If full build or full test is impractical, explain why and list completed checks.

## Finding Format

Each finding should use this structure:

- **Title**
- **Severity**
- **Priority**
- **Evidence**
- **Impact**
- **Suggested direction**
- **Affected files/layers**
- **Verification needed**

Severity levels:

- **Critical**: credential exposure, release-blocking security issue, data corruption, or repeatable crash.
- **High**: likely user-visible correctness issue, major race, severe maintenance blocker, or high idle resource use.
- **Medium**: clear maintainability problem, plausible reliability risk, or inefficient behavior with bounded impact.
- **Low**: cleanup, documentation, analyzer, or minor consistency issue.

## Backlog Item Format

Each backlog item should use this structure:

- **Title**
- **Priority**
- **Problem**
- **Evidence**
- **Suggested change**
- **Affected files/layers**
- **Verification**
- **Risk/notes**

Backlog tasks should be small enough to become separate implementation-plan tasks. Broad refactors should be decomposed into preparatory tests, shared abstractions, migration steps, and cleanup.

## Boundaries

The audit may run non-mutating commands such as:

- `dotnet test OpenHab.Windows.sln`
- `dotnet build OpenHab.Windows.sln --configuration Release`
- `rg`
- `git status`
- file-size and code-metrics inspection
- dependency/analyzer inspection if already available locally

The audit must not:

- Rewrite production code.
- Delete generated package artifacts.
- Install new tools.
- Change project files.
- Rewrite `AGENTS.md`.
- Commit implementation changes.

Those actions become backlog recommendations unless separately approved.

## Expected Follow-Up

After the audit report is accepted, use a separate implementation plan to address selected backlog items. The first likely implementation theme is flyout/main sitemap behavior unification, because it aligns with the top maintainability priority and reduces later reliability risk.
