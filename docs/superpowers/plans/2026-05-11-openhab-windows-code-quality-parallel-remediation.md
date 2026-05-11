# openHAB Windows Code Quality Parallel Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remediate the 2026-05-11 code quality audit by splitting independent documentation, security, app-runtime, and Windows sitemap work across separate worktrees.

**Architecture:** Keep UI-independent runtime fixes in `OpenHab.App` and `OpenHab.Core`; keep WinUI row reconciliation, dispatcher replay, and icon-auth adapters inside `OpenHab.Windows.Tray`. Integrate through small shared components with tests before replacing duplicated window code.

**Tech Stack:** .NET 10, WinUI/Windows App SDK, xUnit, existing openHAB app/core/sitemap/rendering layers.

---

## Source Inputs

- Audit: `docs/superpowers/audits/2026-05-11-openhab-windows-code-quality-audit.md`
- Status: `docs/superpowers/status/2026-05-05-openhab-windows-foundation-status.md`
- Status: `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`
- Status: `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`
- Repository instructions: `AGENTS.md`

## Parallel Worktree Strategy

Create the worktrees from the same clean base branch immediately before implementation. Use `superpowers:using-git-worktrees` at execution time.

| Lane | Worktree | Branch | Owner scope | Merge order |
| --- | --- | --- | --- | --- |
| Docs and gates | `.worktrees\quality-docs` | `quality/current-state-gates` | `AGENTS.md`, status docs, verification docs | 1 |
| Security and release hygiene | `.worktrees\quality-security` | `quality/security-redaction` | Core request redaction, packaging artifact hygiene, ignore/release docs | 2 |
| App lifecycle | `.worktrees\quality-app-lifecycle` | `quality/app-lifecycle` | `OpenHab.App`, `OpenHab.Core.Events`, app/core runtime tests | 3 |
| Windows sitemap surface | `.worktrees\quality-windows-sitemap` | `quality/windows-sitemap-coordinator` | `OpenHab.Windows.Tray`, row planner/renderer, dispatcher replay, Windows-layer tests | 4 |
| Chart/icon performance | same as Windows sitemap lane after Task 4 | same branch or follow-up branch | `SitemapControlFactory` chart/icon cache behavior | 5 |

Do not run two parallel sessions that both edit `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs` or `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`. Dispatcher replay, icon auth consolidation, row reconciliation, and chart loading all touch the same Windows files, so they belong in one lane.

## File Structure

### Docs and Gates Lane

- Create: `docs/superpowers/status/openhab-windows-current-state.md`
  - Single current-state tracker that summarizes shipped behavior, release blockers, verification gates, and known out-of-scope items.
- Create: `docs/superpowers/verification/openhab-windows-quality-gates.md`
  - Two-tier verification contract: direct test projects for everyday logic changes; full solution build/test when DesktopBridge prerequisites exist.
- Modify: `AGENTS.md`
  - Make the consolidated current-state tracker the first status source.

### Security and Release Hygiene Lane

- Create: `src/OpenHab.Core/Diagnostics/SensitiveTextRedactor.cs`
  - Shared redaction helper for server-provided error text before it reaches exceptions, status text, or diagnostics.
- Modify: `src/OpenHab.Core/Api/OpenHabHttpClient.cs`
  - Use `SensitiveTextRedactor` in failed response messages.
- Modify: `tests/OpenHab.Core.Tests/OpenHabHttpClientTests.cs`
  - Add response-body redaction tests.
- Modify: `.gitignore`
  - Ignore local signing keys, user publish metadata, app packages, and bundle artifacts.
- Modify: `src/OpenHab.Windows.Package/OpenHab.Windows.Package.wapproj`
  - Remove hard dependency on checked-in temporary certificate path.
- Remove from Git index after explicit owner approval during execution:
  - `src/OpenHab.Windows.Package/OpenHab.Windows.Package_TemporaryKey.pfx`
  - `src/OpenHab.Windows.Tray/OpenHab_TemporaryKey.pfx`
  - `src/OpenHab.Windows.Tray/*.csproj.user`
  - `src/OpenHab.Windows.Tray/Properties/PublishProfiles/*.pubxml.user`
  - `src/OpenHab.Windows.Package/AppPackages/**`
  - `src/OpenHab.Windows.Package/BundleArtifacts/**`

### App Lifecycle Lane

- Modify: `src/OpenHab.App/Settings/AppSettingsController.cs`
  - Replace fire-and-forget saves with serialized observable persistence and `FlushAsync`.
- Modify: `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`
  - Add ordered-write, flush, and save-failure tests; remove retry-loop dependency where possible.
- Modify: `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`
  - Track event stream start outcomes, reset started state on failure, and surface degraded live-update state deterministically.
- Modify: `src/OpenHab.Core/Events/OpenHabEventStreamClient.cs`
  - Make `ConnectAsync` complete only after the first connection attempt succeeds, fails, or is canceled while keeping reconnects in the background.
- Modify: `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`
  - Add stream subscription failure, connect failure, cancellation, and retry tests.
- Modify: `tests/OpenHab.Core.Tests/Events/OpenHabEventStreamClientTests.cs`
  - Add first-connect outcome tests.

### Windows Sitemap Surface Lane

- Create: `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapIconAuthResolver.cs`
  - Shared Windows-layer credential-to-icon-auth adapter.
- Create: `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapVisualRow.cs`
  - Pure visual-row model for descriptor row index, effective row, and skipped ButtonGrid child rows.
- Create: `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapRowPlanner.cs`
  - Pure ButtonGrid merge, visual-row count, changed-index expansion, row-key lookup, and visual-state key helpers.
- Create: `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapSurfaceRenderer.cs`
  - WinUI `StackPanel` reconciler that uses `SitemapControlFactory` and row planner for both windows.
- Create: `src/OpenHab.Windows.Tray/Rendering/DispatcherRefreshGate.cs`
  - Small dispatcher adapter that records missed refreshes and drains them once enqueue works.
- Create: `tests/OpenHab.App.Tests/SitemapSurface/SitemapRowPlannerTests.cs`
  - Pure tests for ButtonGrid merging, hidden child rows, changed-index expansion, row-key lookup, and visual-state rebuild decisions.
- Create: `tests/OpenHab.App.Tests/SitemapSurface/DispatcherRefreshGateTests.cs`
  - Pure tests for rejected enqueue followed by exactly one replay.
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
  - Replace local row planner, icon-auth, and dispatcher replay logic with shared components.
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
  - Replace divergent direct-index row update path with shared renderer.
- Modify: `tests/OpenHab.App.Tests/SitemapControlFactoryTests.cs`
  - Keep factory helper tests; add any helper contract tests needed by `SitemapRowPlanner`.

## Task 0: Worktree Setup and Ownership

**Files:**
- No source edits in this task.

- [ ] **Step 1: Verify clean base**

Run:

```powershell
git status --short
```

Expected: no tracked or untracked entries. Existing global `safe.directory` warnings are not a blocker.

- [ ] **Step 2: Create docs lane worktree**

Run:

```powershell
git worktree add .worktrees\quality-docs -b quality/current-state-gates
```

Expected: worktree created at `.worktrees\quality-docs`.

- [ ] **Step 3: Create security lane worktree**

Run:

```powershell
git worktree add .worktrees\quality-security -b quality/security-redaction
```

Expected: worktree created at `.worktrees\quality-security`.

- [ ] **Step 4: Create app lifecycle lane worktree**

Run:

```powershell
git worktree add .worktrees\quality-app-lifecycle -b quality/app-lifecycle
```

Expected: worktree created at `.worktrees\quality-app-lifecycle`.

- [ ] **Step 5: Create Windows sitemap lane worktree**

Run:

```powershell
git worktree add .worktrees\quality-windows-sitemap -b quality/windows-sitemap-coordinator
```

Expected: worktree created at `.worktrees\quality-windows-sitemap`.

- [ ] **Step 6: Commit worktree setup note only if repository policy wants it**

Do not commit anything for this task unless a local tracking note is explicitly desired. Worktree creation itself does not change tracked repository files.

## Task 1: Consolidated Current State and Verification Gates

**Files:**
- Create: `docs/superpowers/status/openhab-windows-current-state.md`
- Create: `docs/superpowers/verification/openhab-windows-quality-gates.md`
- Modify: `AGENTS.md`

- [ ] **Step 1: Create the current-state status document**

Write `docs/superpowers/status/openhab-windows-current-state.md` with this structure:

```markdown
# openHAB Windows Current State

Date: 2026-05-11

## Purpose

This file is the first status document to read before implementation. Older dated status files remain useful as historical evidence, but this page summarizes the current product shape, release blockers, and verification gates.

## Shipped Product Shape

- Windows 11 tray app with a compact flyout and larger main window.
- Native sitemap rendering through `OpenHab.Rendering` descriptors and `OpenHab.Windows.Tray.Rendering.SitemapControlFactory`.
- Connected sitemap homepage loading through `OpenHab.App.Runtime.SitemapRuntimeController`.
- Subpage navigation, breadcrumbs, ButtonGrid command dispatch, event-stream widget updates, notifications, settings persistence, credentials, packaging, startup integration, and UI polish have later evidence in dated plans/status files and source.

## Current High-Priority Backlog

- P0: Review tracked temporary signing certificates, user publish metadata, package artifacts, and release signing inputs.
- P0: Ensure server-provided response bodies are redacted before they reach logs, status text, or diagnostics.
- P1: Add shared Windows-layer sitemap row planning before unifying flyout and main window behavior.
- P1: Track sitemap event-stream connect/start outcomes instead of discarding tasks.
- P1: Replace fire-and-forget settings saves with observable serialized persistence.
- P1: Add dispatcher refresh replay when `DispatcherQueue.TryEnqueue` rejects work.
- P2: Add explicit direct-test and full-solution quality gates while DesktopBridge prerequisites remain environment-specific.
- P2: Improve chart/icon loading and cache policy after row reconciliation is shared.

## Verification Gates

- Everyday logic gate: run direct test projects listed in `docs/superpowers/verification/openhab-windows-quality-gates.md`.
- Full gate: run `dotnet test OpenHab.Windows.sln` and `dotnet build OpenHab.Windows.sln --configuration Release` when the Windows DesktopBridge package targets are installed.
- Known environment issue: `OpenHab.Windows.Package.wapproj` imports `Microsoft.DesktopBridge.props`; environments without that target can still run direct test projects.

## Historical Status References

- `docs/superpowers/status/2026-05-05-openhab-windows-foundation-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`
```

- [ ] **Step 2: Create the verification gate document**

Write `docs/superpowers/verification/openhab-windows-quality-gates.md`:

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
````

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
```

- [ ] **Step 3: Update AGENTS status pointers**

In `AGENTS.md`, replace the `Current Status` list with:

```markdown
Use the status docs as the source of truth for what is finished, what was verified, and what remains out of scope. Read this consolidated tracker first:

- `docs/superpowers/status/openhab-windows-current-state.md`

Historical status references:

- `docs/superpowers/status/2026-05-05-openhab-windows-foundation-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`
- `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`
```

Also add `docs/superpowers/verification/openhab-windows-quality-gates.md` to the useful project docs list.

- [ ] **Step 4: Verify links**

Run:

```powershell
rg --files AGENTS.md docs\superpowers\specs docs\superpowers\status docs\superpowers\plans docs\superpowers\verification
```

Expected: the new current-state and verification files appear in the output.

- [ ] **Step 5: Commit docs lane**

Run:

```powershell
git add AGENTS.md docs/superpowers/status/openhab-windows-current-state.md docs/superpowers/verification/openhab-windows-quality-gates.md
git commit -m "docs: add current state and quality gates"
```

Expected: commit succeeds on `quality/current-state-gates`.

## Task 2: Redaction and Release Artifact Hygiene

**Files:**
- Create: `src/OpenHab.Core/Diagnostics/SensitiveTextRedactor.cs`
- Modify: `src/OpenHab.Core/Api/OpenHabHttpClient.cs`
- Modify: `tests/OpenHab.Core.Tests/OpenHabHttpClientTests.cs`
- Modify: `.gitignore`
- Modify: `src/OpenHab.Windows.Package/OpenHab.Windows.Package.wapproj`
- Remove from Git index after approval: tracked `.pfx`, `.user`, `AppPackages`, and `BundleArtifacts` paths listed in the file structure section.

- [ ] **Step 1: Write failing redaction tests**

Append these tests to `tests/OpenHab.Core.Tests/OpenHabHttpClientTests.cs`:

```csharp
[Theory]
[InlineData("Authorization: Bearer oh.secret.token", "oh.secret.token")]
[InlineData("{\"password\":\"p@ssw0rd\",\"error\":\"bad\"}", "p@ssw0rd")]
[InlineData("{\"token\":\"abc123\",\"message\":\"bad token\"}", "abc123")]
[InlineData("https://user:pass@example.org/rest/items", "user:pass@example.org")]
[InlineData("Basic dXNlcjpwYXNz", "dXNlcjpwYXNz")]
public async Task FailedRequestRedactsSensitiveResponseBodies(string responseBody, string sensitiveText)
{
    var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
    {
        ReasonPhrase = "Bad Request",
        Content = new StringContent(responseBody)
    });
    var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

    var error = await Assert.ThrowsAsync<OpenHabRequestException>(
        () => client.GetSitemapJsonAsync("default", CancellationToken.None));

    Assert.DoesNotContain(sensitiveText, error.Message, StringComparison.Ordinal);
    Assert.Contains("[redacted]", error.Message, StringComparison.OrdinalIgnoreCase);
}
```

If `OpenHabHttpClientTests.cs` does not already contain `StubHandler`, add this helper at the bottom of the file:

```csharp
private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(handler(request));
    }
}
```

- [ ] **Step 2: Run redaction tests and verify failure**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --filter FailedRequestRedactsSensitiveResponseBodies
```

Expected: fail because raw response body content is still included in `OpenHabRequestException.Message`.

- [ ] **Step 3: Add shared redactor**

Create `src/OpenHab.Core/Diagnostics/SensitiveTextRedactor.cs`:

```csharp
using System.Text.RegularExpressions;

namespace OpenHab.Core;

public static partial class SensitiveTextRedactor
{
    public static string Redact(string? value, int maxLength = 120)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var redacted = value;
        redacted = AuthorizationHeaderPattern().Replace(redacted, "$1 [redacted]");
        redacted = JsonSecretPattern().Replace(redacted, "$1[redacted]$3");
        redacted = QuerySecretPattern().Replace(redacted, "$1=[redacted]");
        redacted = UrlCredentialPattern().Replace(redacted, "$1[redacted]@");

        if (redacted.Length > maxLength)
        {
            redacted = redacted[..maxLength];
        }

        return redacted;
    }

    [GeneratedRegex(@"(?i)\b(authorization\s*:\s*(?:bearer|basic))\s+[A-Za-z0-9._~+/=-]+")]
    private static partial Regex AuthorizationHeaderPattern();

    [GeneratedRegex(@"(?i)(""?(?:password|passwd|token|secret|authorization|apikey|api_key)""?\s*[:=]\s*""?)([^"",\s}]+)(""?)")]
    private static partial Regex JsonSecretPattern();

    [GeneratedRegex(@"(?i)\b(password|passwd|token|secret|authorization|apikey|api_key)=([^&\s]+)")]
    private static partial Regex QuerySecretPattern();

    [GeneratedRegex(@"(?i)(https?://)[^/\s:@]+:[^/\s@]+@")]
    private static partial Regex UrlCredentialPattern();
}
```

- [ ] **Step 4: Use redactor in HTTP failure messages**

In `src/OpenHab.Core/Api/OpenHabHttpClient.cs`, replace:

```csharp
var safeBody = body.Length > 120 ? body[..120] : body;
throw new OpenHabRequestException(response.StatusCode, $"openHAB request failed with {(int)response.StatusCode} {response.ReasonPhrase}: {safeBody}");
```

with:

```csharp
var safeBody = SensitiveTextRedactor.Redact(body);
throw new OpenHabRequestException(response.StatusCode, $"openHAB request failed with {(int)response.StatusCode} {response.ReasonPhrase}: {safeBody}");
```

- [ ] **Step 5: Run redaction tests and core tests**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --filter FailedRequestRedactsSensitiveResponseBodies
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
```

Expected: all selected and core tests pass.

- [ ] **Step 6: Add artifact ignore rules**

Append these lines to `.gitignore` if equivalent rules do not already exist:

```gitignore
# Local Windows packaging/signing artifacts
*.pfx
*.csproj.user
*.pubxml.user
**/AppPackages/
**/BundleArtifacts/
```

- [ ] **Step 7: Make package certificate input external**

In `src/OpenHab.Windows.Package/OpenHab.Windows.Package.wapproj`, replace the direct certificate key file property:

```xml
<PackageCertificateKeyFile>OpenHab.Windows.Package_TemporaryKey.pfx</PackageCertificateKeyFile>
```

with:

```xml
<PackageCertificateKeyFile Condition="'$(PackageCertificateKeyFile)' != ''">$(PackageCertificateKeyFile)</PackageCertificateKeyFile>
<PackageCertificateThumbprint Condition="'$(PackageCertificateThumbprint)' != ''">$(PackageCertificateThumbprint)</PackageCertificateThumbprint>
```

If the project file already has one of these properties elsewhere, keep one copy only.

- [ ] **Step 8: Remove tracked local artifacts from Git after owner approval**

Ask the repository owner to confirm that the tracked temporary signing certificates and generated packages are not intentional release artifacts. After approval, run:

```powershell
git rm --cached src/OpenHab.Windows.Package/OpenHab.Windows.Package_TemporaryKey.pfx
git rm --cached src/OpenHab.Windows.Tray/OpenHab_TemporaryKey.pfx
git rm --cached src/OpenHab.Windows.Tray/*.csproj.user
git rm --cached src/OpenHab.Windows.Tray/Properties/PublishProfiles/*.pubxml.user
git rm -r --cached src/OpenHab.Windows.Package/AppPackages
git rm -r --cached src/OpenHab.Windows.Package/BundleArtifacts
```

Expected: files are removed from Git tracking while local working copies can remain ignored.

- [ ] **Step 9: Verify artifact inventory**

Run:

```powershell
git ls-files 'src/**.user' 'src/**.pfx' 'src/**/AppPackages/**' 'src/**/BundleArtifacts/**'
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
```

Expected: `git ls-files` returns no tracked local artifact paths; core tests pass.

- [ ] **Step 10: Commit security lane**

Run:

```powershell
git add .gitignore src/OpenHab.Core src/OpenHab.Windows.Package tests/OpenHab.Core.Tests
git commit -m "fix: redact failed request details and ignore local artifacts"
```

Expected: commit succeeds on `quality/security-redaction`.

## Task 3: Observable Settings Persistence and Event Stream Lifecycle

**Files:**
- Modify: `src/OpenHab.App/Settings/AppSettingsController.cs`
- Modify: `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`
- Modify: `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`
- Modify: `src/OpenHab.Core/Events/OpenHabEventStreamClient.cs`
- Modify: `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`
- Modify: `tests/OpenHab.Core.Tests/Events/OpenHabEventStreamClientTests.cs`

- [ ] **Step 1: Write failing settings flush tests**

Add tests to `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`:

```csharp
[Fact]
public async Task FlushAsyncPersistsLatestQueuedSetting()
{
    var controller = new AppSettingsController();

    controller.SetSitemapName("first");
    controller.SetSitemapName("second");
    await controller.FlushAsync();

    var reloaded = new AppSettingsController();
    Assert.Equal("second", reloaded.Current.SitemapName);
}

[Fact]
public async Task FlushAsyncCompletesWhenNoSaveIsQueued()
{
    var controller = new AppSettingsController();

    await controller.FlushAsync();

    Assert.NotNull(controller.Current);
}
```

- [ ] **Step 2: Run settings tests and verify failure**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "FlushAsync"
```

Expected: fail because `AppSettingsController.FlushAsync` does not exist.

- [ ] **Step 3: Serialize settings writes**

In `src/OpenHab.App/Settings/AppSettingsController.cs`, add fields:

```csharp
private readonly object saveSyncRoot = new();
private Task queuedSaveTask = Task.CompletedTask;
```

Add this public method:

```csharp
public Task FlushAsync()
{
    lock (saveSyncRoot)
    {
        return queuedSaveTask;
    }
}
```

Replace every `_ = SaveAsync();` call with:

```csharp
QueueSave();
```

Add the queue method:

```csharp
private void QueueSave()
{
    AppSettings snapshot;
    lock (syncRoot)
    {
        snapshot = Current;
    }

    lock (saveSyncRoot)
    {
        queuedSaveTask = queuedSaveTask
            .ContinueWith(
                _ => SaveAsync(snapshot),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default)
            .Unwrap();
    }
}
```

Change `private async Task SaveAsync()` to:

```csharp
private async Task SaveAsync(AppSettings snapshot)
{
    try
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(SettingsFilePath, json);
    }
    catch (Exception ex)
    {
        DiagnosticLogger.Warn($"Settings save failed: {ex.GetType().Name}: {ex.Message}");
    }
}
```

- [ ] **Step 4: Update tests that relied on retry loops**

In these test constructors, replace the retry-loop comment with a flush-aware cleanup path where a controller is available, and keep `File.Delete(SettingsFilePath)` only as setup cleanup:

- `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`
- `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`
- `tests/OpenHab.App.Tests/SitemapRenderControllerTests.cs`

For newly added tests, always call:

```csharp
await controller.FlushAsync();
```

before constructing a second controller that reads `settings.json`.

- [ ] **Step 5: Run app settings tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter AppSettingsControllerTests
```

Expected: app settings tests pass.

- [ ] **Step 6: Write failing event stream lifecycle tests**

In `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`, extend `FakeEventStreamClient`:

```csharp
public Exception? SubscribeFailure { get; set; }
public Exception? ConnectFailure { get; set; }
public int SubscribeCalls { get; private set; }
public int ConnectCalls { get; private set; }

public Task ConnectAsync(Uri baseUri, CancellationToken cancellationToken = default)
{
    ConnectCalls++;
    if (ConnectFailure is not null)
    {
        return Task.FromException(ConnectFailure);
    }

    IsConnected = true;
    return Task.CompletedTask;
}

public Task<string?> SubscribeToSitemapEventsAsync(Uri baseUri, CancellationToken cancellationToken = default)
{
    SubscribeCalls++;
    if (SubscribeFailure is not null)
    {
        return Task.FromException<string?>(SubscribeFailure);
    }

    return Task.FromResult<string?>("fake-subscription-id");
}
```

Add these tests:

```csharp
[Fact]
public async Task StartSitemapEventStreamAllowsRetryAfterSubscribeFailure()
{
    var settings = new AppSettingsController();
    settings.SetSitemapName("default");
    var eventClient = new FakeEventStreamClient
    {
        SubscribeFailure = new InvalidOperationException("subscribe failed")
    };
    var controller = CreateRuntimeController(settings, new FakeOpenHabClient(), new FakeOpenHabClient(), eventClient);

    await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
    eventClient.SubscribeFailure = null;
    await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");

    Assert.Equal(2, eventClient.SubscribeCalls);
    Assert.Equal(1, eventClient.ConnectCalls);
}

[Fact]
public async Task StartSitemapEventStreamAllowsRetryAfterConnectFailure()
{
    var settings = new AppSettingsController();
    settings.SetSitemapName("default");
    var eventClient = new FakeEventStreamClient
    {
        ConnectFailure = new InvalidOperationException("connect failed")
    };
    var controller = CreateRuntimeController(settings, new FakeOpenHabClient(), new FakeOpenHabClient(), eventClient);

    await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
    eventClient.ConnectFailure = null;
    await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");

    Assert.Equal(2, eventClient.ConnectCalls);
    Assert.True(eventClient.IsConnected);
}
```

- [ ] **Step 7: Run lifecycle tests and verify failure**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "StartSitemapEventStreamAllowsRetry"
```

Expected: at least one test fails because stream started state is not reset on connect failure and connect is discarded.

- [ ] **Step 8: Track stream start result in runtime controller**

In `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`, replace the body of `StartSitemapEventStreamAsync` with:

```csharp
public async Task StartSitemapEventStreamAsync(Uri localBaseUri, string sitemapName, string pageId, CancellationToken ct = default)
{
    if (sitemapEventStreamClient is null) return;

    if (_sitemapEventStreamStarted && _sitemapEventStreamSitemapName == sitemapName && _sitemapEventStreamPageId == pageId)
    {
        return;
    }

    _sitemapEventStreamStarted = true;
    _sitemapEventStreamSitemapName = sitemapName;
    _sitemapEventStreamPageId = pageId;

    try
    {
        DiagnosticLogger.Info($"Starting sitemap event stream to {localBaseUri} for sitemap '{sitemapName}' page '{pageId}'");
        EnsureSitemapEventHandlersAttached();

        _subscriptionId = await sitemapEventStreamClient.SubscribeToSitemapEventsAsync(localBaseUri, ct);
        if (_subscriptionId is null)
        {
            DiagnosticLogger.Warn("Failed to subscribe to sitemap events — no subscription ID returned");
            ResetSitemapEventStreamStart();
            return;
        }

        DiagnosticLogger.Info($"Sitemap event subscription created: {_subscriptionId}");
        var sseUrl = new Uri(localBaseUri, $"rest/sitemaps/events/{_subscriptionId}?sitemap={Uri.EscapeDataString(sitemapName)}&pageid={Uri.EscapeDataString(pageId)}");
        await sitemapEventStreamClient.ConnectAsync(sseUrl, ct);
    }
    catch (OperationCanceledException)
    {
        ResetSitemapEventStreamStart();
        throw;
    }
    catch (Exception ex)
    {
        DiagnosticLogger.Warn($"Sitemap event stream start failed: {ex.GetType().Name}: {ex.Message}");
        ResetSitemapEventStreamStart();
        Current = Current with
        {
            ConnectionState = Current.ConnectionState == ConnectionState.Online
                ? ConnectionState.Degraded
                : Current.ConnectionState,
            StatusText = Current.ConnectionState == ConnectionState.Online
                ? "Live updates unavailable. Refresh manually."
                : Current.StatusText,
            ChangedRowIndices = []
        };
    }
}

private void ResetSitemapEventStreamStart()
{
    _sitemapEventStreamStarted = false;
    _sitemapEventStreamSitemapName = null;
    _sitemapEventStreamPageId = null;
}
```

Leave `RefreshAsyncInternal` page-load behavior online when sitemap loading succeeds; the event-stream catch above only downgrades live updates to degraded.

- [ ] **Step 9: Make first connect outcome observable in core event stream client**

In `src/OpenHab.Core/Events/OpenHabEventStreamClient.cs`, replace `ConnectAsync` with:

```csharp
public async Task ConnectAsync(Uri sseUri, CancellationToken cancellationToken = default)
{
    var uriChanged = _sseUri is null || _sseUri != sseUri;
    if (IsConnected && !uriChanged)
    {
        return;
    }

    _sseUri = sseUri;

    var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var previous = Interlocked.Exchange(ref _internalCts, cts);
    previous?.Cancel();
    previous?.Dispose();

    var firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    _ = Task.Run(() => ReadLoopAsync(sseUri, cts, firstAttempt), CancellationToken.None);

    await firstAttempt.Task.WaitAsync(cancellationToken);
}
```

Change `ReadLoopAsync` signature:

```csharp
private async Task ReadLoopAsync(Uri sseUri, CancellationTokenSource cts, TaskCompletionSource firstAttempt)
```

Inside the success path after `response.EnsureSuccessStatusCode();`, set:

```csharp
Interlocked.Exchange(ref _isConnected, 1);
firstAttempt.TrySetResult();
```

Inside the non-cancellation catch, before logging retry:

```csharp
firstAttempt.TrySetException(ex);
```

At the end of the method, before returning after cancellation:

```csharp
firstAttempt.TrySetCanceled(ct);
```

- [ ] **Step 10: Run app/core lifecycle tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "StartSitemapEventStreamAllowsRetry|FlushAsync|AppSettingsControllerTests"
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --filter OpenHabEventStreamClientTests
```

Expected: selected app and core tests pass.

- [ ] **Step 11: Run full direct app/core tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
```

Expected: both projects pass.

- [ ] **Step 12: Commit app lifecycle lane**

Run:

```powershell
git add src/OpenHab.App src/OpenHab.Core tests/OpenHab.App.Tests tests/OpenHab.Core.Tests
git commit -m "fix: make settings and sitemap event lifecycle observable"
```

Expected: commit succeeds on `quality/app-lifecycle`.

## Task 4: Shared Windows Sitemap Surface Coordinator

**Files:**
- Create: `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapIconAuthResolver.cs`
- Create: `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapVisualRow.cs`
- Create: `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapRowPlanner.cs`
- Create: `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapSurfaceRenderer.cs`
- Create: `src/OpenHab.Windows.Tray/Rendering/DispatcherRefreshGate.cs`
- Create: `tests/OpenHab.App.Tests/SitemapSurface/SitemapRowPlannerTests.cs`
- Create: `tests/OpenHab.App.Tests/SitemapSurface/DispatcherRefreshGateTests.cs`
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`

- [ ] **Step 1: Write row planner tests**

Create `tests/OpenHab.App.Tests/SitemapSurface/SitemapRowPlannerTests.cs`:

```csharp
using OpenHab.Rendering.Descriptors;
using OpenHab.Windows.Tray.Rendering.SitemapSurface;

namespace OpenHab.App.Tests.SitemapSurface;

public sealed class SitemapRowPlannerTests
{
    [Fact]
    public void VisualRowsSkipButtonChildrenAndMergeVisibleButtonGridOptions()
    {
        var rows = new[]
        {
            Grid("Mode"),
            Button("Auto", "AUTO", visible: true, sourceIndex: 1),
            Button("Manual", "MANUAL", visible: false, sourceIndex: 2),
            Text("Temperature")
        };

        var visualRows = SitemapRowPlanner.BuildVisualRows(rows);

        Assert.Equal([0, 3], visualRows.Select(row => row.RowIndex).ToArray());
        var grid = visualRows[0].Row;
        Assert.Single(grid.SelectionOptions);
        Assert.Equal("Auto", grid.SelectionOptions[0].Label);
        Assert.Equal(1, grid.SelectionOptions[0].SourceRowIndex);
    }

    [Fact]
    public void ExpandChangedIndicesMapsButtonChildToOwningGrid()
    {
        var rows = new[] { Grid("Mode"), Button("Auto", "AUTO", true, 1), Button("Manual", "MANUAL", true, 2), Text("Temperature") };

        var expanded = SitemapRowPlanner.ExpandChangedIndices([2], rows);

        Assert.Equal([0], expanded);
    }

    [Fact]
    public void TryResolveRowIndexFindsCurrentRowByStableKey()
    {
        var rows = new[] { Text("Kitchen", widgetId: "w-kitchen"), Text("Hall", widgetId: "w-hall") };
        var key = SitemapControlFactory.BuildRowIdentityKey(rows[1]);

        var found = SitemapRowPlanner.TryResolveRowIndex(rows, key, out var rowIndex);

        Assert.True(found);
        Assert.Equal(1, rowIndex);
    }

    private static SitemapRowDescriptor Text(string label, string? widgetId = null) =>
        new(label, null, RenderControlKind.Text, RenderActionKind.None, RenderDensity.Compact, [], WidgetId: widgetId);

    private static SitemapRowDescriptor Grid(string label) =>
        new(label, null, RenderControlKind.ButtonGrid, RenderActionKind.SendCommand, RenderDensity.Compact, []);

    private static SitemapRowDescriptor Button(string label, string command, bool visible, int sourceIndex) =>
        new(label, command, RenderControlKind.Button, RenderActionKind.SendCommand, RenderDensity.Compact, [],
            Command: command,
            IsVisible: visible,
            SelectionOptions: [new SitemapMapOption(command, label, null, null, false, command, null, false, sourceIndex)]);
}
```

- [ ] **Step 2: Run row planner tests and verify failure**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter SitemapRowPlannerTests
```

Expected: fail because `SitemapRowPlanner` does not exist.

- [ ] **Step 3: Add pure row model**

Create `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapVisualRow.cs`:

```csharp
using OpenHab.Rendering.Descriptors;

namespace OpenHab.Windows.Tray.Rendering.SitemapSurface;

public sealed record SitemapVisualRow(
    int RowIndex,
    SitemapRowDescriptor Row,
    int NextDescriptorIndex);
```

- [ ] **Step 4: Add row planner**

Create `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapRowPlanner.cs`:

```csharp
using OpenHab.Rendering.Descriptors;

namespace OpenHab.Windows.Tray.Rendering.SitemapSurface;

public static class SitemapRowPlanner
{
    public static IReadOnlyList<SitemapVisualRow> BuildVisualRows(IReadOnlyList<SitemapRowDescriptor> rows)
    {
        var visualRows = new List<SitemapVisualRow>();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.Control == RenderControlKind.Button)
            {
                continue;
            }

            if (row.Control == RenderControlKind.ButtonGrid)
            {
                var merged = BuildMergedButtonGridRow(index, rows, out var nextIndex);
                visualRows.Add(new SitemapVisualRow(index, merged, nextIndex));
                index = nextIndex - 1;
                continue;
            }

            visualRows.Add(new SitemapVisualRow(index, row, index + 1));
        }

        return visualRows;
    }

    public static IReadOnlyList<int> ExpandChangedIndices(IReadOnlyList<int> changedIndices, IReadOnlyList<SitemapRowDescriptor> rows)
    {
        var set = new SortedSet<int>();
        foreach (var index in changedIndices)
        {
            if (index < 0 || index >= rows.Count)
            {
                continue;
            }

            var effective = index;
            if (rows[index].Control == RenderControlKind.Button)
            {
                for (var scan = index - 1; scan >= 0; scan--)
                {
                    if (rows[scan].Control == RenderControlKind.ButtonGrid)
                    {
                        effective = scan;
                        break;
                    }

                    if (rows[scan].Control != RenderControlKind.Button)
                    {
                        break;
                    }
                }
            }

            if (rows[effective].Control != RenderControlKind.Button)
            {
                set.Add(effective);
            }
        }

        return set.ToArray();
    }

    public static bool TryResolveRowIndex(IReadOnlyList<SitemapRowDescriptor>? rows, string rowKey, out int rowIndex)
    {
        if (rows is null)
        {
            rowIndex = -1;
            return false;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            if (string.Equals(SitemapControlFactory.BuildRowIdentityKey(rows[index]), rowKey, StringComparison.Ordinal))
            {
                rowIndex = index;
                return true;
            }
        }

        rowIndex = -1;
        return false;
    }

    public static int CountVisualRows(IReadOnlyList<SitemapRowDescriptor> rows) => BuildVisualRows(rows).Count;

    public static SitemapRowDescriptor BuildMergedButtonGridRow(int gridIndex, IReadOnlyList<SitemapRowDescriptor> rows)
    {
        return BuildMergedButtonGridRow(gridIndex, rows, out _);
    }

    private static SitemapRowDescriptor BuildMergedButtonGridRow(int gridIndex, IReadOnlyList<SitemapRowDescriptor> rows, out int nextIndex)
    {
        var row = rows[gridIndex];
        var childOptions = new List<SitemapMapOption>();
        var scan = gridIndex + 1;
        while (scan < rows.Count && rows[scan].Control == RenderControlKind.Button)
        {
            var child = rows[scan];
            var command = child.Command ?? child.RawItemState ?? child.RawState ?? child.State ?? string.Empty;
            var isActive = string.Equals(child.RawItemState ?? child.RawState ?? child.State, "ON", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(child.Command, "ON", StringComparison.OrdinalIgnoreCase);
            childOptions.Add(new SitemapMapOption(
                command,
                child.Label,
                child.GridRow,
                child.GridColumn,
                isActive,
                child.Command,
                child.ReleaseCommand,
                child.Stateless,
                scan));
            scan++;
        }

        nextIndex = scan;
        var visibleChildOptions = childOptions.Where(option => option.SourceRowIndex.HasValue && rows[option.SourceRowIndex.Value].IsVisible).ToList();
        if (visibleChildOptions.Count > 0)
        {
            childOptions = visibleChildOptions;
        }

        return childOptions.Count > 0 ? row with { SelectionOptions = childOptions } : row;
    }
}
```

- [ ] **Step 5: Run row planner tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter SitemapRowPlannerTests
```

Expected: row planner tests pass.

- [ ] **Step 6: Add shared icon auth resolver**

Create `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapIconAuthResolver.cs`:

```csharp
using OpenHab.App.Settings;
using OpenHab.Core.Profiles;

namespace OpenHab.Windows.Tray.Rendering.SitemapSurface;

public sealed class SitemapIconAuthResolver(AppSettingsController settingsController)
{
    public SitemapControlFactory.IconAuthContext Resolve(TransportKind transportKind)
    {
        if (transportKind == TransportKind.Local)
        {
            return new SitemapControlFactory.IconAuthContext(
                ApiToken: GetApiToken(TransportKind.Local),
                BasicUserName: null,
                BasicPassword: null,
                TransportKind: transportKind);
        }

        var cloudCredentials = GetCloudCredentials();
        return new SitemapControlFactory.IconAuthContext(
            ApiToken: null,
            BasicUserName: cloudCredentials?.UserName,
            BasicPassword: cloudCredentials?.Password,
            TransportKind: transportKind);
    }

    private string? GetApiToken(TransportKind kind)
    {
        try { return settingsController.GetApiTokenAsync(kind, CancellationToken.None).GetAwaiter().GetResult(); }
        catch { return null; }
    }

    private CloudCredentials? GetCloudCredentials()
    {
        try { return settingsController.GetCloudCredentialsAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch { return null; }
    }
}
```

- [ ] **Step 7: Add dispatcher replay tests**

Create `tests/OpenHab.App.Tests/SitemapSurface/DispatcherRefreshGateTests.cs`:

```csharp
using OpenHab.Windows.Tray.Rendering;

namespace OpenHab.App.Tests.SitemapSurface;

public sealed class DispatcherRefreshGateTests
{
    [Fact]
    public void RequestRecordsPendingRefreshWhenEnqueueFails()
    {
        var gate = new DispatcherRefreshGate(_ => false);
        var refreshes = 0;

        gate.Request(() => refreshes++);

        Assert.Equal(0, refreshes);
        Assert.True(gate.HasPendingRefresh);
    }

    [Fact]
    public void DrainRunsOnePendingRefresh()
    {
        var queue = new Queue<Action>();
        var gate = new DispatcherRefreshGate(action =>
        {
            queue.Enqueue(action);
            return true;
        });
        var refreshes = 0;

        gate.Request(() => refreshes++);
        gate.Drain(() => refreshes++);
        while (queue.TryDequeue(out var action))
        {
            action();
        }

        Assert.Equal(1, refreshes);
        Assert.False(gate.HasPendingRefresh);
    }
}
```

- [ ] **Step 8: Add dispatcher replay gate**

Create `src/OpenHab.Windows.Tray/Rendering/DispatcherRefreshGate.cs`:

```csharp
namespace OpenHab.Windows.Tray.Rendering;

public sealed class DispatcherRefreshGate(Func<Action, bool> tryEnqueue)
{
    private int pendingRefresh;

    public bool HasPendingRefresh => Interlocked.CompareExchange(ref pendingRefresh, 0, 0) == 1;

    public void Request(Action refresh)
    {
        if (!tryEnqueue(() =>
            {
                Interlocked.Exchange(ref pendingRefresh, 0);
                refresh();
            }))
        {
            Interlocked.Exchange(ref pendingRefresh, 1);
        }
    }

    public void Drain(Action refresh)
    {
        if (Interlocked.Exchange(ref pendingRefresh, 0) == 0)
        {
            return;
        }

        Request(refresh);
    }
}
```

- [ ] **Step 9: Add shared surface renderer skeleton**

Create `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapSurfaceRenderer.cs` with the public API first:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.Windows.Tray.Rendering.SitemapSurface;

public sealed class SitemapSurfaceRenderer(
    AppSettingsController settingsController,
    SitemapIconAuthResolver iconAuthResolver,
    Func<string, Task> activateByRowKey,
    Func<string, Task> navigateByRowKey,
    Func<string, string, Task> sendCommandByRowKey,
    Func<int, string, Task> sendCommandByRowIndex)
{
    private sealed record RenderedRowTag(int RowIndex, string RowKey, string VisualStateKey);
    private sealed record ExistingRenderedRow(FrameworkElement Element, int ChildIndex);

    public void Refresh(StackPanel rowsPanel, SitemapRuntimeSnapshot snapshot)
    {
        var rows = snapshot.Descriptor?.Rows;
        if (rows is null)
        {
            rowsPanel.Children.Clear();
            return;
        }

        if (snapshot.ChangedRowIndices is { Count: > 0 })
        {
            RefreshChangedRows(rowsPanel, rows, snapshot);
            return;
        }

        if (rowsPanel.Children.Count == SitemapRowPlanner.CountVisualRows(rows))
        {
            return;
        }

        rowsPanel.Children.Clear();
        foreach (var visualRow in SitemapRowPlanner.BuildVisualRows(rows))
        {
            var element = CreateRowElement(visualRow.RowIndex, visualRow.Row, snapshot);
            rowsPanel.Children.Add(element);
            SitemapControlFactory.SetVisibility(element, visualRow.Row.IsVisible);
        }
    }

    private void RefreshChangedRows(StackPanel rowsPanel, IReadOnlyList<SitemapRowDescriptor> rows, SitemapRuntimeSnapshot snapshot)
    {
        foreach (var index in SitemapRowPlanner.ExpandChangedIndices(snapshot.ChangedRowIndices, rows))
        {
            if (!TryFindRenderedRow(rowsPanel, index, out var existing, out var childIndex))
            {
                continue;
            }

            var row = rows[index].Control == RenderControlKind.ButtonGrid
                ? SitemapRowPlanner.BuildMergedButtonGridRow(index, rows)
                : rows[index];

            if (ShouldRebuild(existing, row, index))
            {
                rowsPanel.Children.RemoveAt(childIndex);
                rowsPanel.Children.Insert(childIndex, CreateRowElement(index, row, snapshot));
                continue;
            }

            SitemapControlFactory.UpdateState(existing, row);
            SetRenderedRowTag(existing, index, row);
        }
    }

    private FrameworkElement CreateRowElement(int index, SitemapRowDescriptor row, SitemapRuntimeSnapshot snapshot)
    {
        var iconTransport = snapshot.ActiveTransport ?? TransportKind.Local;
        var iconBaseUri = iconTransport == TransportKind.Local
            ? settingsController.Current.LocalEndpoint
            : settingsController.Current.CloudEndpoint;
        var iconAuth = iconAuthResolver.Resolve(iconTransport);

        if (row.Control == RenderControlKind.ButtonGrid)
        {
            Func<SitemapMapOption, bool, Task> sendGridCommand = (option, isRelease) =>
            {
                var expectedCommand = isRelease ? option.ReleaseCommand : option.ClickCommand ?? option.Command;
                if (string.IsNullOrWhiteSpace(expectedCommand) ||
                    string.Equals(expectedCommand, "NULL", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.CompletedTask;
                }

                return option.SourceRowIndex.HasValue
                    ? sendCommandByRowIndex(option.SourceRowIndex.Value, expectedCommand)
                    : sendCommandByRowIndex(index, expectedCommand);
            };

            var element = SitemapControlFactory.Create(
                row,
                activateRow: null,
                sendCommand: null,
                iconBaseUri,
                settingsController.Current.UseWindows11Icons,
                iconAuth,
                chartDpi: (int)settingsController.Current.ChartQuality,
                sendButtonGridCommand: sendGridCommand);
            SetRenderedRowTag(element, index, row);
            return element;
        }

        var rowKey = SitemapControlFactory.BuildRowIdentityKey(row);
        Func<Task>? activateRow = row.Action switch
        {
            RenderActionKind.Navigate => () => navigateByRowKey(rowKey),
            RenderActionKind.SendCommand when row.Control == RenderControlKind.Toggle => () => activateByRowKey(rowKey),
            _ => null
        };
        Func<string, Task>? sendCommand = row.Action == RenderActionKind.SendCommand
            ? command => sendCommandByRowKey(rowKey, command)
            : null;

        var created = SitemapControlFactory.Create(
            row,
            activateRow,
            sendCommand,
            iconBaseUri,
            settingsController.Current.UseWindows11Icons,
            iconAuth,
            chartDpi: (int)settingsController.Current.ChartQuality);
        SetRenderedRowTag(created, index, row);
        return created;
    }

    private static bool TryFindRenderedRow(StackPanel rowsPanel, int rowIndex, out FrameworkElement element, out int childIndex)
    {
        for (var i = 0; i < rowsPanel.Children.Count; i++)
        {
            if (rowsPanel.Children[i] is FrameworkElement candidate &&
                candidate.Tag is RenderedRowTag tag &&
                tag.RowIndex == rowIndex)
            {
                element = candidate;
                childIndex = i;
                return true;
            }
        }

        element = null!;
        childIndex = -1;
        return false;
    }

    private static void SetRenderedRowTag(FrameworkElement element, int rowIndex, SitemapRowDescriptor row)
    {
        element.Tag = new RenderedRowTag(
            rowIndex,
            SitemapControlFactory.BuildRowIdentityKey(row),
            SitemapControlFactory.BuildRowVisualStateKey(row, rowIndex));
    }

    private static bool ShouldRebuild(FrameworkElement element, SitemapRowDescriptor row, int rowIndex)
    {
        return element.Tag is RenderedRowTag tag &&
               !string.Equals(tag.VisualStateKey, SitemapControlFactory.BuildRowVisualStateKey(row, rowIndex), StringComparison.Ordinal);
    }
}
```

This first renderer is intentionally narrower than flyout's full structural reconcile. After both windows use it, add a second commit that ports flyout's existing `ReconcileStructuralRows` reuse/removal behavior into `SitemapSurfaceRenderer` if manual smoke tests show insertion/removal flicker.

- [ ] **Step 10: Wire flyout to shared components**

In `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`:

Add field:

```csharp
private readonly SitemapSurfaceRenderer sitemapSurfaceRenderer;
private readonly DispatcherRefreshGate snapshotRefreshGate;
private readonly DispatcherRefreshGate notificationRefreshGate;
```

In the constructor after assignments:

```csharp
var iconAuthResolver = new SitemapIconAuthResolver(settingsController);
sitemapSurfaceRenderer = new SitemapSurfaceRenderer(
    settingsController,
    iconAuthResolver,
    activateByRowKey: OnRowActivatedByKeyAsync,
    navigateByRowKey: OnRowNavigateByKeyAsync,
    sendCommandByRowKey: SendCommandForRowKeyAsync,
    sendCommandByRowIndex: (rowIndex, command) => runtimeController.SendCommandForRowAsync(rowIndex, command));
snapshotRefreshGate = new DispatcherRefreshGate(action => DispatcherQueue.TryEnqueue(action));
notificationRefreshGate = new DispatcherRefreshGate(action => DispatcherQueue.TryEnqueue(action));
```

Replace the `runtimeController.SnapshotChanged` enqueue block with:

```csharp
runtimeController.SnapshotChanged += (_, _) =>
{
    if (_suppressNextSnapshotRefresh)
    {
        _suppressNextSnapshotRefresh = false;
        return;
    }

    snapshotRefreshGate.Request(() => RefreshRuntimeBindings(targetRows: null));
};
```

Replace notification enqueue with:

```csharp
notificationRefreshGate.Request(RefreshNotificationBadge);
```

Replace `RefreshRuntimeBindings` row-body logic with:

```csharp
internal void RefreshRuntimeBindings(StackPanel? targetRows = null)
{
    var rowsPanel = targetRows ?? ActiveRows;
    var snapshot = runtimeController.Current;
    RefreshChromeBindings(snapshot);
    sitemapSurfaceRenderer.Refresh(rowsPanel, snapshot);
}
```

Delete the duplicate flyout methods after compilation succeeds:

- `ResolveIconAuth`
- `GetApiTokenSync`
- `GetCloudCredentialsSync`
- `ExpandChangedIndicesForMergedRows`
- `ReconcileStructuralRows`
- `CountVisualRows`
- `BuildMergedButtonGridRow`
- `CreateRowElementForIndex`
- `AddRenderedRow`
- `TryFindRenderedRow`
- `TryGetRenderedRowIndex`
- `SetRenderedRowTag`
- `ShouldRebuildRow`

Keep `TryResolveCurrentRowIndex`, but replace its loop with:

```csharp
return SitemapRowPlanner.TryResolveRowIndex(runtimeController.Current.Descriptor?.Rows, rowKey, out rowIndex);
```

- [ ] **Step 11: Wire main window to shared components**

In `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`:

Add fields:

```csharp
private readonly SitemapSurfaceRenderer sitemapSurfaceRenderer;
private readonly DispatcherRefreshGate snapshotRefreshGate;
private readonly DispatcherRefreshGate notificationRefreshGate;
```

In the constructor after assignments:

```csharp
var iconAuthResolver = new SitemapIconAuthResolver(settingsController);
sitemapSurfaceRenderer = new SitemapSurfaceRenderer(
    settingsController,
    iconAuthResolver,
    activateByRowKey: OnRowActivatedByKeyAsync,
    navigateByRowKey: OnRowNavigateByKeyAsync,
    sendCommandByRowKey: SendCommandForRowKeyAsync,
    sendCommandByRowIndex: (rowIndex, command) => runtimeController.SendCommandForRowAsync(rowIndex, command));
snapshotRefreshGate = new DispatcherRefreshGate(action => DispatcherQueue.TryEnqueue(action));
notificationRefreshGate = new DispatcherRefreshGate(action => DispatcherQueue.TryEnqueue(action));
```

Add these helper methods by adapting the flyout key-based methods:

```csharp
private Task OnRowActivatedByKeyAsync(string rowKey)
{
    return SitemapRowPlanner.TryResolveRowIndex(runtimeController.Current.Descriptor?.Rows, rowKey, out var rowIndex)
        ? OnRowActivatedAsync(rowIndex)
        : Task.CompletedTask;
}

private Task OnRowNavigateByKeyAsync(string rowKey)
{
    return SitemapRowPlanner.TryResolveRowIndex(runtimeController.Current.Descriptor?.Rows, rowKey, out var rowIndex)
        ? OnRowNavigateAsync(rowIndex)
        : Task.CompletedTask;
}

private Task SendCommandForRowKeyAsync(string rowKey, string command)
{
    return SitemapRowPlanner.TryResolveRowIndex(runtimeController.Current.Descriptor?.Rows, rowKey, out var rowIndex)
        ? runtimeController.SendCommandForRowAsync(rowIndex, command)
        : Task.CompletedTask;
}
```

Replace notification enqueue with:

```csharp
notificationRefreshGate.Request(RefreshNotificationList);
```

Replace the `runtimeController.SnapshotChanged` enqueue failure branch with:

```csharp
snapshotRefreshGate.Request(() => RefreshRuntimeBindings(targetRows: null));
```

Replace `RefreshRuntimeBindings` row-body logic with:

```csharp
internal void RefreshRuntimeBindings(StackPanel? targetRows = null)
{
    var rowsPanel = targetRows ?? ActiveRows;
    var snapshot = runtimeController.Current;
    RefreshChromeBindings(snapshot);
    sitemapSurfaceRenderer.Refresh(rowsPanel, snapshot);
}
```

Delete duplicate main-window methods after compilation succeeds:

- `ResolveIconAuth`
- `GetApiTokenSync`
- `GetCloudCredentialsSync`
- inline ButtonGrid merge and direct child-index changed-row update logic inside old `RefreshRuntimeBindings`

- [ ] **Step 12: Drain pending dispatcher refreshes at safe lifecycle points**

In both `RefreshRuntimeBindings` methods after `sitemapSurfaceRenderer.Refresh(...)`, add:

```csharp
snapshotRefreshGate.Drain(() => RefreshRuntimeBindings(targetRows: null));
```

In `RefreshNotificationBadge` and `RefreshNotificationList`, add the corresponding drain at the end:

```csharp
notificationRefreshGate.Drain(RefreshNotificationBadge);
```

or:

```csharp
notificationRefreshGate.Drain(RefreshNotificationList);
```

- [ ] **Step 13: Run Windows-layer tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "SitemapRowPlannerTests|DispatcherRefreshGateTests|SitemapControlFactoryTests"
```

Expected: selected tests pass.

- [ ] **Step 14: Build tray project**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj
```

Expected: build succeeds.

- [ ] **Step 15: Manual smoke check**

Run the app using the existing local run method used by this repo. Verify:

- Flyout loads the configured sitemap.
- Main window loads the same sitemap.
- ButtonGrid rows render as one visual row with child buttons.
- Toggle rows dispatch commands.
- Navigation rows move forward and back in both windows.
- Widget visibility changes do not leave stale direct-index row updates in main window.
- Dispatcher enqueue rejection logs are replaced by pending replay behavior.

- [ ] **Step 16: Commit Windows sitemap lane**

Run:

```powershell
git add src/OpenHab.Windows.Tray tests/OpenHab.App.Tests
git commit -m "refactor: share sitemap surface row coordination"
```

Expected: commit succeeds on `quality/windows-sitemap-coordinator`.

## Task 5: Chart and Icon Loading Policy After Shared Row Coordination

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- Modify: `tests/OpenHab.App.Tests/SitemapControlFactoryTests.cs`
- Optional create: `src/OpenHab.Windows.Tray/Rendering/SitemapImageCache.cs`

- [ ] **Step 1: Write chart URL cache policy tests**

Add tests to `tests/OpenHab.App.Tests/SitemapControlFactoryTests.cs`:

```csharp
[Fact]
public void BuildChartUrl_UsesStableUrlWhenCacheBustDisabled()
{
    var row = new SitemapRowDescriptor(
        "Power", "12", RenderControlKind.Chart, RenderActionKind.None, RenderDensity.Compact,
        [], ItemName: "Weather_Temperature", Period: "D");
    var baseUri = new Uri("http://localhost:8080/");

    var first = SitemapControlFactory.BuildChartUrl(row, baseUri, chartDpi: 192, cacheBust: false);
    var second = SitemapControlFactory.BuildChartUrl(row, baseUri, chartDpi: 192, cacheBust: false);

    Assert.Equal(first, second);
    Assert.DoesNotContain("random=", first!.ToString());
}
```

- [ ] **Step 2: Run chart test and verify failure**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter BuildChartUrl_UsesStableUrlWhenCacheBustDisabled
```

Expected: fail because `BuildChartUrl` has no `cacheBust` parameter.

- [ ] **Step 3: Add explicit chart cache-bust parameter**

In `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`, change `BuildChartUrl` signature to:

```csharp
public static Uri? BuildChartUrl(SitemapRowDescriptor row, Uri? baseUri, int chartDpi = 96, bool cacheBust = true)
```

When building the query, append `random=` only when `cacheBust` is true:

```csharp
if (cacheBust)
{
    query.Add($"random={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
}
```

Keep all existing callers using the default `cacheBust: true`.

- [ ] **Step 4: Run chart/factory tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter SitemapControlFactoryTests
```

Expected: factory tests pass.

- [ ] **Step 5: Commit chart policy lane**

Run:

```powershell
git add src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs tests/OpenHab.App.Tests/SitemapControlFactoryTests.cs
git commit -m "refactor: make chart cache busting explicit"
```

Expected: commit succeeds on the Windows sitemap branch or its follow-up branch.

## Task 6: Integration and Final Verification

**Files:**
- Merge commits from all lane branches.
- Resolve conflicts only in files owned by the conflicting lane; do not rewrite unrelated local changes.

- [ ] **Step 1: Merge docs lane**

Run from the integration branch:

```powershell
git merge quality/current-state-gates
```

Expected: no code conflicts.

- [ ] **Step 2: Merge security lane**

Run:

```powershell
git merge quality/security-redaction
```

Expected: possible `.gitignore` or package-project conflict only if other packaging work landed first.

- [ ] **Step 3: Merge app lifecycle lane**

Run:

```powershell
git merge quality/app-lifecycle
```

Expected: possible test conflict only if new app tests were added in parallel; keep all tests.

- [ ] **Step 4: Merge Windows sitemap lane**

Run:

```powershell
git merge quality/windows-sitemap-coordinator
```

Expected: possible conflicts in `MainWindow.xaml.cs`, `FlyoutWindow.xaml.cs`, and `SitemapControlFactoryTests.cs`; preserve shared renderer and key-based command routing.

- [ ] **Step 5: Run direct test gate**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj
```

Expected: all direct test projects pass.

- [ ] **Step 6: Run full solution gate when DesktopBridge is available**

Run:

```powershell
dotnet test OpenHab.Windows.sln
dotnet build OpenHab.Windows.sln --configuration Release
```

Expected: both pass when `Microsoft.DesktopBridge.props` exists. If they fail only because DesktopBridge targets are missing, record that exact failure and do not claim full solution verification.

- [ ] **Step 7: Manual Windows smoke check**

Run the packaged or unpackaged app with a configured openHAB endpoint. Verify:

- Flyout and main window show the same current page.
- Flyout and main window ButtonGrid rows dispatch to the expected source row.
- Forward navigation, back navigation, and breadcrumbs work in both windows.
- SSE widget update changes one visible row without full-window flicker.
- A hidden ButtonGrid child does not render as a visible option.
- Local token and cloud credentials still load icons.
- Failed HTTP response text shown in status/logs does not include token, password, Authorization header value, or URL embedded credentials.
- Settings changes survive restart after `FlushAsync`-backed saves.

- [ ] **Step 8: Final status update**

Create a dated status file:

```powershell
New-Item -ItemType File docs\superpowers\status\2026-05-11-openhab-windows-code-quality-remediation-status.md
```

Write:

```markdown
# openHAB Windows Code Quality Remediation Status

Date: 2026-05-11

## Completed

- Consolidated current-state tracker and quality gates.
- Redacted server-provided HTTP failure bodies.
- Removed tracked local package/signing artifacts after owner approval.
- Serialized settings persistence with observable flush.
- Made sitemap event stream start/connect outcomes observable.
- Shared Windows sitemap row planning and rendering between flyout and main window.
- Added dispatcher refresh replay.
- Made chart cache busting explicit.

## Verification

- Direct test gate: record exact command output counts.
- Full solution gate: record pass/fail and DesktopBridge prerequisite status.
- Manual smoke check: record checked sitemap, endpoint mode, and observed flyout/main behavior.

## Remaining Work

- Device/connectivity telemetry configuration and sender remain a separate product feature.
- Additional UI automation remains a separate test-infrastructure effort.
```

- [ ] **Step 9: Commit integration status**

Run:

```powershell
git add docs/superpowers/status/2026-05-11-openhab-windows-code-quality-remediation-status.md
git commit -m "docs: record code quality remediation status"
```

Expected: commit succeeds.

## Split Notes

- `B1` and `B6` are safe to run immediately in the docs lane.
- `B2` is independent except for possible `.gitignore` conflicts; merge it early so later branches inherit artifact rules.
- `R1` and `R2` should stay together because both touch app runtime tests and asynchronous lifecycle semantics.
- `B3`, `B4`, `M1`, `M2`, `M3`, and `R3` should stay together in the Windows sitemap lane because they all touch `FlyoutWindow.xaml.cs` and `MainWindow.xaml.cs`.
- `B7` should wait until after shared row coordination because repeated full row rebuilds distort chart/icon loading measurements.
- `B8` device/connectivity telemetry is not included in this remediation wave. It should be planned as a separate feature because it needs settings UI, Windows collectors, sender scheduling, and privacy decisions.

## Self-Review

- Spec coverage: audit backlog `B1` through `B7` is covered. `B8` is explicitly split out as a separate feature plan.
- Placeholder scan: this plan uses concrete files, commands, test names, and code snippets. Owner approval is required only for removing tracked signing/package artifacts.
- Type consistency: new Windows sitemap types live under `OpenHab.Windows.Tray.Rendering.SitemapSurface`; tests import that namespace; `SitemapSurfaceRenderer` depends on existing `SitemapControlFactory`, `SitemapRuntimeSnapshot`, `AppSettingsController`, and `TransportKind`.
