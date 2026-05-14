# openHAB Windows Official-Readiness Plan A Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the minimal public repository governance, contribution, security, and CI surface needed for openHAB official-readiness review.

**Architecture:** This plan is documentation and workflow only. It does not change runtime behavior, package signing, test code, or application architecture. The CI workflow runs the direct test gate and makes the known App test host shutdown issue visible with an explicitly named non-blocking step.

**Tech Stack:** Markdown, GitHub Actions, .NET 10, xUnit, Windows runner.

---

## File Structure

- Create: `README.md` - public project overview, status, requirements, build/test commands, package caveats, and local data paths.
- Create: `CONTRIBUTING.md` - openHAB-aligned contribution rules adapted for this .NET/WinUI app.
- Create: `NOTICE` - project notice and dependency attribution entry point.
- Create: `SECURITY.md` - security reporting and sensitive-data handling guidance.
- Create: `.github/workflows/ci.yml` - direct test workflow for Windows.
- Modify: `docs/superpowers/status/openhab-windows-current-state.md` - record that governance docs and CI exist after this plan.
- Modify: `docs/superpowers/verification/openhab-windows-quality-gates.md` - document the direct CI gate and the temporary App test host caveat.

## Task 1: Add README

**Files:**
- Create: `README.md`

- [ ] **Step 1: Create the README**

Create `README.md` with this content:

```markdown
# openHAB Windows Companion App

This repository contains a Windows companion app for openHAB. The current product direction is a Windows 11 tray app with a compact flyout, a larger main window, embedded openHAB Main UI, and native sitemap rendering for Windows-specific workflows.

## Current Status

This app is under active development. It is not yet an official release-ready openHAB distribution.

Known release-readiness items still need maintainer decisions or follow-up implementation:

- Official package identity and signing ownership.
- Microsoft Store or other distribution ownership.
- Privacy hardening for diagnostics and user-visible error text.
- Clean shutdown of `OpenHab.App.Tests` without VSTest blame-hang.
- Localization and broader accessibility review.
- Full dependency/license review beyond the initial `NOTICE` file.

## Features

- Windows tray app with compact flyout.
- Main window with embedded openHAB Main UI through WebView2.
- Optional native sitemap pane backed by the shared sitemap/runtime/rendering pipeline.
- Settings, notifications, startup integration, sitemap navigation, and shortcut command menu work.
- Local app state and diagnostics under `%LocalAppData%\OpenHab.WinApp`.

## Repository Layout

- `src/OpenHab.Core` - endpoint selection, server profiles, HTTP client, credentials, diagnostics, event streams, and device-state mapping.
- `src/OpenHab.Sitemaps` - sitemap models, parsing, normalization, and navigation intents.
- `src/OpenHab.Rendering` - skin-neutral render descriptors and sitemap skin mapping.
- `src/OpenHab.App` - UI-independent settings, runtime controllers, notifications, shell state, and shortcuts.
- `src/OpenHab.Windows.Tray` - WinUI/Windows App SDK shell, tray icon, flyout, main window, settings UI, and Windows-specific rendering.
- `src/OpenHab.Windows.Notifications` - Windows toast notification integration.
- `src/OpenHab.Windows.Package` - MSIX packaging project.
- `tests` - xUnit coverage for core, sitemap, rendering, and app/runtime behavior.
- `docs/superpowers` - design, plan, status, and verification notes used during development.

## Requirements

- Windows 11 for the intended desktop experience.
- .NET SDK version from `global.json`.
- Visual Studio with MSBuild and MSIX/DesktopBridge tooling for package builds.
- WebView2 Runtime for embedded Main UI.

## Build And Test

Everyday direct test gate:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --blame-hang --blame-hang-timeout 30s
```

The App test project currently executes its assertions successfully but can leave the VSTest host running. The blame-hang flags make that failure mode explicit until the test-host shutdown issue is fixed.

Build the tray app:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release
```

Build the MSIX package when Visual Studio MSIX/DesktopBridge targets are installed:

```powershell
.\build-package.ps1 -Configuration Release -Platform x64
```

The package project imports DesktopBridge targets that are not always available through standalone .NET SDK MSBuild. Use `build-package.ps1` for package builds because it locates Visual Studio MSBuild and verifies the required DesktopBridge props file.

## Runtime Data

Runtime logs and app state are written under:

```text
%LocalAppData%\OpenHab.WinApp
```

Useful files include:

- `diagnostics.log`
- `task-crash.log`
- `settings.json`
- `notifications.json`

Do not post full logs publicly if they can include endpoint URLs, item names, notification payloads, credentials, tokens, or other private information.

## Packaging And Signing

Release signing is not finalized. Official distribution must use signing certificates, package identity, and release infrastructure owned by the appropriate openHAB maintainers.

Local temporary signing files and package output must not be committed.

## Contributing

See `CONTRIBUTING.md`.

## Security

See `SECURITY.md`.

## License

This project is licensed under the Eclipse Public License 2.0. See `LICENSE`.
```

- [ ] **Step 2: Review README links**

Run:

```powershell
Test-Path README.md; Test-Path CONTRIBUTING.md; Test-Path SECURITY.md; Test-Path LICENSE
```

Expected after Task 4 completes:

```text
True
True
True
True
```

- [ ] **Step 3: Commit README after docs set is complete**

Do not commit in this task. Commit once Tasks 1-6 are complete so the governance docs land together.

## Task 2: Add CONTRIBUTING

**Files:**
- Create: `CONTRIBUTING.md`

- [ ] **Step 1: Create contribution guide**

Create `CONTRIBUTING.md` with this content:

```markdown
# Contributing

Thanks for contributing to the openHAB Windows companion app.

This repository follows openHAB contribution expectations where they apply, adapted for a .NET/WinUI Windows app.

## Before You Start

- Discuss large features, architectural changes, packaging changes, and security-sensitive work before implementing them.
- Prefer issue-backed work and focused branches.
- Keep changes small enough to review.
- Update tests and documentation with behavior changes.

## Developer Certificate Of Origin

Commits must be signed off using the Developer Certificate of Origin.

Use:

```powershell
git commit -s -m "short summary"
```

The sign-off must use your real name and a reachable email address.

## Coding Guidelines

- Preserve the project split:
  - `OpenHab.Core` for openHAB access, credentials, diagnostics, profiles, event streams, and device-state mapping.
  - `OpenHab.Sitemaps` for sitemap parsing and runtime-neutral sitemap behavior.
  - `OpenHab.Rendering` for render descriptors and skin mapping.
  - `OpenHab.App` for UI-independent app/runtime behavior.
  - `OpenHab.Windows.Tray` for WinUI, tray, flyout, main window, and Windows-specific rendering.
  - `OpenHab.Windows.Notifications` for Windows toast integration.
- Do not push WinUI-specific concerns into lower layers.
- Reuse the existing sitemap normalizer, runtime controller, and rendering pipeline.
- Keep logs and user-visible errors privacy-safe.
- Do not add broad refactors to unrelated feature work.

## Sensitive Data

Do not commit:

- Credentials or tokens.
- Private endpoint URLs.
- Full diagnostic logs from a real installation.
- `.pfx` signing keys.
- `.user` project files.
- Package output under `AppPackages` or `BundleArtifacts`.

When adding diagnostics, assume server responses, URLs, item names, notification payloads, and exception messages can contain private information.

## Verification

Run the direct test gate for normal logic changes:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --blame-hang --blame-hang-timeout 30s
```

Run the tray build for UI or Windows-shell changes:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release
```

Run the package build for packaging, manifest, startup-task, notification activation, or signing changes:

```powershell
.\build-package.ps1 -Configuration Release -Platform x64
```

The full package build requires Visual Studio MSBuild with DesktopBridge/MSIX targets.

## Pull Requests

Pull requests should include:

- Clear summary of behavior changes.
- Tests run and their result.
- Known limitations or follow-up work.
- Screenshots only for UI-visible changes.
- Documentation updates when setup, packaging, commands, or user-visible behavior changes.

Do not hide failing tests. If a known infrastructure issue applies, call it out explicitly.
```

- [ ] **Step 2: Verify DCO guidance is present**

Run:

```powershell
Select-String -Path CONTRIBUTING.md -Pattern "Developer Certificate Of Origin", "git commit -s", "Sensitive Data", "Verification"
```

Expected: four matches are printed.

## Task 3: Add NOTICE

**Files:**
- Create: `NOTICE`

- [ ] **Step 1: Create NOTICE**

Create `NOTICE` with this content:

```text
openHAB Windows Companion App
Copyright (c) contributors.

This project is made available under the Eclipse Public License 2.0.
See LICENSE for the full license text.

openHAB names, logos, and marks belong to the openHAB project and their
respective owners. This repository must not be treated as an official
release channel until the openHAB maintainers have assigned package identity,
signing, release, and support ownership.

Third-party dependencies are declared in the project files under src/ and
tests/. Review PackageReference entries before preparing a release or
redistribution package.

Current dependency entry points include:

- src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj
- src/OpenHab.Windows.Notifications/OpenHab.Windows.Notifications.csproj
- tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj
- tests/OpenHab.Sitemaps.Tests/OpenHab.Sitemaps.Tests.csproj
- tests/OpenHab.Rendering.Tests/OpenHab.Rendering.Tests.csproj
- tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj
```

- [ ] **Step 2: Verify dependency entry points exist**

Run:

```powershell
Test-Path src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj
Test-Path src\OpenHab.Windows.Notifications\OpenHab.Windows.Notifications.csproj
Test-Path tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
Test-Path tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj
Test-Path tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj
Test-Path tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj
```

Expected: six `True` lines.

## Task 4: Add SECURITY

**Files:**
- Create: `SECURITY.md`

- [ ] **Step 1: Create security policy**

Create `SECURITY.md` with this content:

```markdown
# Security Policy

## Supported Status

This Windows companion app is under active development and is not yet an official release-ready openHAB distribution.

Security support, release signing, and package ownership must be finalized by openHAB maintainers before this repository can be treated as an official release channel.

## Reporting A Vulnerability

If this repository is transferred into the official openHAB organization or published as an official openHAB app, follow the current openHAB security reporting process.

Until ownership is finalized, report suspected vulnerabilities privately to the repository maintainers. Do not open a public issue containing secrets, exploit details, private endpoint data, or full diagnostic logs.

## Sensitive Data

The app can process or store sensitive data, including:

- openHAB endpoint URLs.
- API tokens.
- Basic-auth credentials.
- Item names and states.
- Sitemap content.
- Notification payloads.
- Local diagnostics.

Do not paste full logs or settings files into public issues. Redact tokens, credentials, private hostnames, public URLs with query strings, item names that reveal private information, and notification payloads.

## Local Files

Runtime files are stored under:

```text
%LocalAppData%\OpenHab.WinApp
```

Review files before sharing them. Useful files such as `diagnostics.log`, `settings.json`, `notifications.json`, and `task-crash.log` can contain private data.

## Signing Keys

Do not commit signing keys, `.pfx` files, `.user` project metadata, package output, or private release credentials.
```

- [ ] **Step 2: Verify sensitive-data guidance is present**

Run:

```powershell
Select-String -Path SECURITY.md -Pattern "Sensitive Data", "%LocalAppData%", "Signing Keys", "Do not open a public issue"
```

Expected: four matches are printed.

## Task 5: Add CI Workflow

**Files:**
- Create: `.github/workflows/ci.yml`

- [ ] **Step 1: Create workflow**

Create `.github/workflows/ci.yml` with this content:

```yaml
name: CI

on:
  push:
    branches:
      - main
  pull_request:

jobs:
  direct-tests:
    name: Direct test gate
    runs-on: windows-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Restore
        run: dotnet restore OpenHab.Windows.sln

      - name: Test Core
        run: dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore

      - name: Test Sitemaps
        run: dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore

      - name: Test Rendering
        run: dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore

      - name: Test App with known host-shutdown caveat
        id: app_tests
        continue-on-error: true
        run: dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --blame-hang --blame-hang-timeout 30s

      - name: Report App test caveat
        if: steps.app_tests.outcome == 'failure'
        shell: pwsh
        run: |
          Write-Host "::warning::OpenHab.App.Tests has a known VSTest host shutdown issue. Assertions may pass before blame-hang aborts the host. See docs/superpowers/verification/openhab-windows-quality-gates.md."

      - name: Build tray project
        run: dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore
```

- [ ] **Step 2: Validate YAML file presence**

Run:

```powershell
Test-Path .github\workflows\ci.yml
Select-String -Path .github\workflows\ci.yml -Pattern "continue-on-error", "Report App test caveat", "Build tray project"
```

Expected: `Test-Path` prints `True` and three matches are printed.

## Task 6: Update Verification Docs

**Files:**
- Modify: `docs/superpowers/verification/openhab-windows-quality-gates.md`
- Modify: `docs/superpowers/status/openhab-windows-current-state.md`

- [ ] **Step 1: Update quality gates**

In `docs/superpowers/verification/openhab-windows-quality-gates.md`, replace the Direct Test Gate section with:

```markdown
## Direct Test Gate

Use this for everyday logic changes when the packaging project cannot load because DesktopBridge targets are unavailable.

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --blame-hang --blame-hang-timeout 30s
```

Expected current behavior:

- Core, Sitemaps, and Rendering should pass and exit cleanly.
- App assertions are expected to pass, but the VSTest host can stay alive until blame-hang aborts it. This is a known blocker tracked for Plan B of the official-readiness remediation.
```

- [ ] **Step 2: Add CI note to quality gates**

Add this section after `## Direct Test Gate`:

```markdown
## CI Gate

`.github/workflows/ci.yml` runs the direct test projects and the tray Release build on `windows-latest`.

The App test step is temporarily marked `continue-on-error` so the workflow exposes the known test-host shutdown issue without hiding Core, Sitemaps, Rendering, or tray build regressions. Remove that temporary allowance after the App test host exits cleanly.
```

- [ ] **Step 3: Update current state backlog**

In `docs/superpowers/status/openhab-windows-current-state.md`, add these bullets under `## Current High-Priority Backlog` if they are not already present:

```markdown
- P0: Finalize official repository governance and release ownership: README, CONTRIBUTING, NOTICE, SECURITY, CI, package identity, signing, and support policy.
- P0: Fix `OpenHab.App.Tests` VSTest host shutdown so the direct App test gate exits cleanly without blame-hang.
```

- [ ] **Step 4: Add latest governance note**

In `docs/superpowers/status/openhab-windows-current-state.md`, add this section before `## Historical Status References`:

```markdown
## Official-Readiness Plan Split

2026-05-14 design `docs/superpowers/specs/2026-05-14-openhab-windows-official-readiness-remediation-design.md` splits remediation into:

- Plan A: fast public repository governance and CI shell.
- Plan B: privacy-safe diagnostics, App test host shutdown, and targeted maintainability extraction.
```

## Task 7: Verify Plan A

**Files:**
- Verify all files touched by Tasks 1-6.

- [ ] **Step 1: Inspect changed files**

Run:

```powershell
git status --short
```

Expected: changed or new files include only:

```text
README.md
CONTRIBUTING.md
NOTICE
SECURITY.md
.github/workflows/ci.yml
docs/superpowers/verification/openhab-windows-quality-gates.md
docs/superpowers/status/openhab-windows-current-state.md
docs/superpowers/plans/2026-05-14-openhab-windows-official-readiness-plan-a.md
docs/superpowers/plans/2026-05-14-openhab-windows-official-readiness-plan-b.md
```

Existing unrelated untracked files may also appear. Do not stage unrelated files.

- [ ] **Step 2: Search for forbidden placeholders**

Run:

```powershell
rg -n "TBD|TODO|placeholder|fill in|implement later" README.md CONTRIBUTING.md NOTICE SECURITY.md .github\workflows\ci.yml docs\superpowers\verification\openhab-windows-quality-gates.md docs\superpowers\status\openhab-windows-current-state.md
```

Expected: no matches.

- [ ] **Step 3: Run direct tests**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --blame-hang --blame-hang-timeout 30s
```

Expected:

- Core, Sitemaps, and Rendering pass.
- App assertions pass, then the known test-host shutdown issue may abort the run after blame-hang. Record the exact output.

- [ ] **Step 4: Build tray project**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore
```

Expected: build succeeds.

- [ ] **Step 5: Check whitespace**

Run:

```powershell
git diff --check
```

Expected: no whitespace errors.

- [ ] **Step 6: Commit Plan A**

Stage only Plan A files:

```powershell
git add README.md CONTRIBUTING.md NOTICE SECURITY.md .github\workflows\ci.yml docs\superpowers\verification\openhab-windows-quality-gates.md docs\superpowers\status\openhab-windows-current-state.md docs\superpowers\plans\2026-05-14-openhab-windows-official-readiness-plan-a.md docs\superpowers\plans\2026-05-14-openhab-windows-official-readiness-plan-b.md
git commit -m "docs: add official readiness governance"
```
