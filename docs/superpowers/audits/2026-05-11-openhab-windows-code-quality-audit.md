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

## Flyout/Main Sitemap Behavior Comparison

| Behavior | Flyout | Main Window | Assessment | Evidence |
| --- | --- | --- | --- | --- |
| Pending comparison | Pending Task 2 source review | Pending Task 2 source review | Pending | Evidence not collected in Task 1 |

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
- Tests were not rerun for Task 1. Controller-provided baseline verification is recorded in `Baseline Verification Note`.
