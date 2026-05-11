# openHAB Quality Session 1 Docs Gates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create the consolidated current-state tracker and practical verification gate docs that future agents read first.

**Architecture:** This session is documentation-only. It does not touch production code and can run first in parallel with all other sessions, then merge first.

**Tech Stack:** Markdown, repository docs, PowerShell verification commands.

---

## Session Assignment

- **Codex instance:** Session 1, docs/gates.
- **Recommended model:** `gpt-5.4-mini` with high reasoning, or default Codex 5.3 Medium for consistency.
- **Worktree:** `.worktrees\quality-docs`
- **Branch:** `quality/current-state-gates`
- **Must not edit:** `src/**`, `tests/**`, packaging files, app code.

## Dependencies

- **Depends on:** No other remediation session.
- **Can run in parallel with:** Sessions 2, 3, and 4.
- **Should merge:** First. This improves repo orientation for all later work.
- **Expected conflicts:** Low. Possible conflict only in `AGENTS.md` if another branch edits instructions.

## Files

- Create: `docs/superpowers/status/openhab-windows-current-state.md`
- Create: `docs/superpowers/verification/openhab-windows-quality-gates.md`
- Modify: `AGENTS.md`

## Task 1: Prepare Worktree

- [ ] **Step 1: Verify clean source worktree before creating branch**

Run from `D:\Source\Openhab\openhab-win-app`:

```powershell
git status --short
```

Expected: no tracked source changes that belong to this session. If package artifacts are already present, leave them alone; Session 2 owns artifact hygiene.

- [ ] **Step 2: Create or enter docs worktree**

Run:

```powershell
git worktree add .worktrees\quality-docs -b quality/current-state-gates
```

Expected: `.worktrees\quality-docs` exists. If it already exists, run future commands from that directory.

- [ ] **Step 3: Confirm branch**

Run:

```powershell
git branch --show-current
```

Expected: `quality/current-state-gates`.

## Task 2: Add Consolidated Current-State Tracker

- [ ] **Step 1: Write current-state doc**

Create `docs/superpowers/status/openhab-windows-current-state.md`:

```markdown
# openHAB Windows Current State

Date: 2026-05-11

## Purpose

Read this file before implementation. Older dated status files remain useful as historical evidence, but this page summarizes the current product shape, release blockers, and verification gates.

## Shipped Product Shape

- Windows 11 tray app with compact flyout and larger main window.
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

## Historical Status References

- `docs/superpowers/status/2026-05-05-openhab-windows-foundation-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`
```

- [ ] **Step 2: Verify current-state doc exists**

Run:

```powershell
Test-Path docs\superpowers\status\openhab-windows-current-state.md
```

Expected: `True`.

## Task 3: Add Quality Gates Doc

- [ ] **Step 1: Create verification directory**

Run:

```powershell
New-Item -ItemType Directory -Force docs\superpowers\verification
```

Expected: directory exists.

- [ ] **Step 2: Write gate doc**

Create `docs/superpowers/verification/openhab-windows-quality-gates.md`:

````markdown
# openHAB Windows Quality Gates

Date: 2026-05-11

## Direct Test Gate

Use this for everyday logic changes when the packaging project cannot load because DesktopBridge targets are unavailable.

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj
```

Expected: all direct test projects pass.

## Full Solution Gate

Use this before claiming release readiness or after package, manifest, Windows shell, or project-file changes.

```powershell
dotnet test OpenHab.Windows.sln
dotnet build OpenHab.Windows.sln --configuration Release
```

Expected: both commands pass when `Microsoft.DesktopBridge.props` is available under the installed .NET SDK or Visual Studio build targets.

## Known Packaging Prerequisite

`src\OpenHab.Windows.Package\OpenHab.Windows.Package.wapproj` imports DesktopBridge targets. If those targets are missing, direct test projects still provide useful logic coverage, but the full gate remains blocked by environment setup.
````

- [ ] **Step 3: Verify markdown file inventory**

Run:

```powershell
rg --files docs\superpowers\verification docs\superpowers\status
```

Expected: both new docs appear.

## Task 4: Update AGENTS.md Pointers

- [ ] **Step 1: Update Current Status section**

In `AGENTS.md`, replace the current three-file status list with:

```markdown
Use the status docs as the source of truth for what is finished, what was verified, and what remains out of scope. Read this consolidated tracker first:

- `docs/superpowers/status/openhab-windows-current-state.md`

Historical status references:

- `docs/superpowers/status/2026-05-05-openhab-windows-foundation-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`
```

- [ ] **Step 2: Add verification doc to useful docs**

Add this bullet to the useful project docs list in `AGENTS.md`:

```markdown
- `docs/superpowers/verification/openhab-windows-quality-gates.md`
```

- [ ] **Step 3: Verify all linked paths**

Run:

```powershell
rg --files AGENTS.md docs\superpowers\specs docs\superpowers\status docs\superpowers\plans docs\superpowers\verification
```

Expected: `AGENTS.md`, the new current-state doc, and the new verification doc appear.

## Task 5: Commit

- [ ] **Step 1: Check diff**

Run:

```powershell
git diff -- AGENTS.md docs\superpowers\status\openhab-windows-current-state.md docs\superpowers\verification\openhab-windows-quality-gates.md
```

Expected: only docs changes.

- [ ] **Step 2: Commit**

Run:

```powershell
git add AGENTS.md docs/superpowers/status/openhab-windows-current-state.md docs/superpowers/verification/openhab-windows-quality-gates.md
git commit -m "docs: add current state and quality gates"
```

Expected: commit succeeds.

## Handoff

After this branch passes review, merge it before other branches if practical. Other sessions do not need to wait for it to finish implementation, but they should rebase or merge it before final integration so `AGENTS.md` points to the new tracker.
