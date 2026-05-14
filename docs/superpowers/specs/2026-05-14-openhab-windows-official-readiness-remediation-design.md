# openHAB Windows Official-Readiness Remediation Design

Date: 2026-05-14

## Purpose

This design splits the official-readiness work into two implementation plans so the fast governance work can land without losing the later engineering hardening items.

The reviewed findings being addressed are:

- Finding 1: missing repository governance and public contribution surface.
- Finding 4: privacy and logging hardening.
- Finding 5: `OpenHab.App.Tests` completes assertions but the VSTest host does not exit cleanly.
- Finding 6: targeted maintainability cleanup around large files and touched code.

## Current Context

The repository is a Windows 11 openHAB companion app with layered projects:

- `OpenHab.Core`
- `OpenHab.Sitemaps`
- `OpenHab.Rendering`
- `OpenHab.App`
- `OpenHab.Windows.Tray`
- `OpenHab.Windows.Notifications`
- `OpenHab.Windows.Package`

The current state tracker is `docs/superpowers/status/openhab-windows-current-state.md`.

The repository already has `LICENSE` with EPL-2.0. It currently lacks the public governance files expected for an official openHAB-adjacent repository: root `README.md`, `CONTRIBUTING.md`, `NOTICE`, `SECURITY.md`, and an active CI workflow.

Direct test verification during the review showed:

- `OpenHab.Core.Tests`: passed `68/68`.
- `OpenHab.Sitemaps.Tests`: passed `39/39`.
- `OpenHab.Rendering.Tests`: passed `31/31`.
- `OpenHab.App.Tests`: passed `333/333` assertions, then the test host stayed alive until blame-hang aborted the run after 30 seconds.

Large-file inventory shows the main maintainability hotspots:

- `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`
- `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`
- `src/OpenHab.Windows.Tray/App.xaml.cs`
- `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
- `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`

## Plan Split

### Plan A: Fast Official-Readiness Shell

Plan A addresses the governance and public review surface first. It is intentionally small, documentation-heavy, and suitable for quick review.

Plan A creates:

- `README.md`
- `CONTRIBUTING.md`
- `NOTICE`
- `SECURITY.md`
- `.github/workflows/ci.yml`
- Updates to status or verification docs where needed

Plan A documents known limitations instead of fixing them:

- App test host shutdown issue.
- DesktopBridge packaging prerequisite.
- Privacy/logging hardening backlog.
- Targeted maintainability backlog.
- Release signing and package identity ownership still needing maintainer decisions.

Plan A does not change product behavior, logging behavior, test host lifetime, package signing, localization, or large-file structure.

### Plan B: Privacy, Test Shutdown, And Targeted Maintainability

Plan B preserves the engineering hardening work as a separate follow-up plan.

Plan B covers:

- Privacy-safe diagnostics and user-visible status messages.
- Root-cause investigation and fix for the `OpenHab.App.Tests` host shutdown hang.
- Targeted helper extraction only where it supports privacy hardening or test-host cleanup.
- Verification gate updates after the App test host exits cleanly.

Plan B does not include Microsoft Store identity, official release signing automation, localization, or broad architecture cleanup.

## Plan A Design

### README

The README should be accurate and conservative. It should describe the app as a Windows companion app for openHAB and clearly state the current product shape:

- Windows 11 tray app.
- Compact flyout.
- Main window with embedded openHAB Main UI.
- Native sitemap rendering available as a secondary pane.
- Notifications and settings surfaces.

The README should include:

- Current status and maturity.
- Requirements: Windows, .NET SDK, Visual Studio/MSIX tooling for package builds.
- Repository layout.
- Direct test commands.
- Tray build command.
- Package build command and DesktopBridge caveat.
- Local data/log paths under `%LocalAppData%\OpenHab.WinApp`.
- Explicit note that release signing and official distribution require openHAB maintainer-owned infrastructure.

### CONTRIBUTING

The contribution guide should align with openHAB expectations while staying practical for this .NET/WinUI repository.

It should include:

- Discuss larger changes before implementation.
- Prefer issue-backed branches.
- Keep commits focused.
- Sign commits with DCO `Signed-off-by`.
- Run direct tests before opening a PR.
- Run full/package gates when packaging or Windows shell behavior changes.
- Update docs/status when behavior changes.
- Do not include credentials, private logs, signing keys, or user-specific package metadata.
- Preserve the layered architecture from `AGENTS.md`.
- Keep WinUI code out of lower layers.

### NOTICE

The NOTICE file should:

- Identify the project and EPL-2.0 licensing.
- State that openHAB names and marks belong to the openHAB project.
- Record that third-party dependencies are managed through project files.
- Point maintainers to package references for the current dependency list.

It should not invent license conclusions for every dependency. A future dependency/license scan can expand it.

### SECURITY

The security policy should:

- Explain that credentials, tokens, endpoint URLs, notification payloads, and logs can contain sensitive data.
- Ask reporters not to post secrets or full logs publicly.
- Direct security reports to the openHAB security process if this becomes an official repo, or to the repository maintainers while unofficial.
- State that unsupported/local builds may not receive production security handling until ownership is finalized.

### CI

The initial CI workflow should run on Windows and execute the direct test gate:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --blame-hang --blame-hang-timeout 30s
```

The App test command may fail until Plan B fixes test-host shutdown. The workflow should make that state visible rather than silently skipping App tests. If the implementation chooses to keep CI green before Plan B, the workflow must explicitly document the temporary compromise in the verification docs.

## Plan B Design

### Privacy-Safe Diagnostics

Plan B should add one small sanitization boundary rather than scattered string replacements.

The boundary should support:

- Sanitizing exception messages before user-visible status text.
- Sanitizing diagnostic log messages when they include server-provided data.
- Preserving useful error categories such as HTTP status, timeout, cancellation, and unavailable service.

Initial targets:

- `OpenHabHttpClient` request failure messages.
- `SitemapRuntimeController` connection and fallback status text.
- `FlyoutWindow` and `MainWindow` direct `ex.Message` status surfaces.
- Settings credential save failure status.
- SSE raw/unparsed data logging.
- Notification and toast exception logging where payload or activation arguments can contain sensitive values.

Tests should cover:

- Authorization headers.
- Bearer tokens.
- Basic credentials.
- URLs with user info.
- Token-like query parameters.
- Server response bodies.
- Benign messages that should remain readable.

### App Test Host Shutdown

Plan B must start with root-cause investigation. It should not paper over the issue by only increasing timeouts.

Investigation should use:

- `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --blame-hang --blame-hang-timeout 30s`
- Targeted test filters around the listed active tests from blame-hang.
- Inspection of WinUI/static resources, dispatchers, timers, background tasks, event subscriptions, and undisposed services referenced by the App tests.

Success criteria:

- `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore` exits cleanly.
- No hanging VSTest host.
- No abandoned background tasks or dispatcher work from tests.
- The direct test gate can run without blame-hang in normal local development.

### Targeted Maintainability Extraction

Plan B may extract helpers only where the privacy or test-host work touches code.

Allowed examples:

- A status/error-message formatter for runtime/UI status text.
- A diagnostics redaction helper or wrapper around `DiagnosticLogger`.
- A small notification/toast log formatter if notification logging is touched.
- A narrow sitemap/media helper if privacy work touches icon/chart/server payload logging.

Out of scope:

- Full split of `SitemapControlFactory.cs`.
- Full split of `SettingsPageControl.xaml.cs`.
- Full split of `App.xaml.cs`.
- Reworking flyout/main architecture beyond touched surfaces.

## Verification Strategy

Plan A verification:

- `git status --short` before and after.
- Validate docs for clear scope and no placeholders.
- Run direct tests if practical.
- At minimum, run formatting/self-review checks on new Markdown and YAML files.

Plan B verification:

- `dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore`
- `dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore`
- `dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore`
- `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore`
- `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore`

Full package verification remains environment-dependent because `OpenHab.Windows.Package.wapproj` needs DesktopBridge/MSIX targets.

## Risks

- Plan A can make the repo look more official than the code currently is. The README and SECURITY policy must state the current maturity and known blockers clearly.
- Plan B can grow into a broad refactor if maintainability cleanup is not constrained. Keep extraction tied to privacy or test-host work.
- CI may expose the known App test host hang immediately. That is acceptable if documented, but the final CI behavior should be deliberate.
- Release signing and package identity need openHAB maintainer decisions. Do not claim official release readiness until ownership is settled.

## Non-Goals

- Official Microsoft Store release.
- Production signing automation.
- Localization infrastructure.
- Full analyzer/style overhaul.
- Complete dependency/license audit.
- Broad UI or sitemap rendering refactor.
