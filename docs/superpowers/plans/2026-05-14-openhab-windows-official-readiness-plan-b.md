# openHAB Windows Official-Readiness Plan B Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden privacy-sensitive diagnostics, fix `OpenHab.App.Tests` host shutdown, and extract only the helpers needed for those changes.

**Architecture:** Add a small diagnostics/status sanitization boundary in `OpenHab.Core`, consume it from app/runtime/Windows surfaces that currently expose raw exception messages, then investigate and fix the App test host lifetime issue. Maintainability extraction is limited to helpers directly required by privacy or test-shutdown work.

**Tech Stack:** .NET 10, xUnit, WinUI/Windows App SDK, GitHub Actions, PowerShell.

---

## File Structure

- Create: `src/OpenHab.Core/Diagnostics/SafeDiagnosticText.cs` - reusable exception/status sanitization for logs and user-visible text.
- Modify: `src/OpenHab.Core/DiagnosticLogger.cs` - add safe logging overloads or route exception text through `SafeDiagnosticText` where callers opt in.
- Modify: `src/OpenHab.Core/Api/OpenHabHttpClient.cs` - keep request failure messages structured and redacted.
- Modify: `src/OpenHab.Core/Events/OpenHabEventStreamClient.cs` - remove raw SSE payload logging or sanitize it.
- Modify: `src/OpenHab.App/Runtime/SitemapRuntimeController.cs` - convert raw exception/status propagation to safe status text.
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs` - replace direct `ex.Message` status text.
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs` - replace direct `ex.Message` status text.
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs` - replace credential/log-open direct exception status text.
- Modify: `src/OpenHab.Windows.Notifications/ToastService.cs` - sanitize exception logging that includes activation or toast data.
- Modify: `src/OpenHab.Windows.Notifications/NotificationPoller.cs` - sanitize `LastError`.
- Test: `tests/OpenHab.Core.Tests/Diagnostics/SafeDiagnosticTextTests.cs`
- Test: existing runtime/UI-adjacent tests under `tests/OpenHab.App.Tests`
- Modify: `docs/superpowers/verification/openhab-windows-quality-gates.md` - remove App test caveat after the host exits cleanly.

## Task 1: Add Safe Diagnostic Text Tests

**Files:**
- Create: `tests/OpenHab.Core.Tests/Diagnostics/SafeDiagnosticTextTests.cs`

- [ ] **Step 1: Write redaction tests**

Create `tests/OpenHab.Core.Tests/Diagnostics/SafeDiagnosticTextTests.cs` with:

```csharp
using OpenHab.Core;

namespace OpenHab.Core.Tests.Diagnostics;

public sealed class SafeDiagnosticTextTests
{
    [Theory]
    [InlineData("Authorization: Bearer oh.secret.token", "oh.secret.token")]
    [InlineData("Authorization: Basic dXNlcjpwYXNz", "dXNlcjpwYXNz")]
    [InlineData("https://user:pass@example.com/rest/items", "user:pass")]
    [InlineData("request failed?token=abc123&item=Light", "abc123")]
    [InlineData("{\"password\":\"secret-value\"}", "secret-value")]
    public void ForLog_RedactsSensitiveValues(string input, string forbidden)
    {
        var safe = SafeDiagnosticText.ForLog(input);

        Assert.DoesNotContain(forbidden, safe, StringComparison.Ordinal);
        Assert.Contains("[redacted]", safe, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForUserStatus_PreservesHttpStatusButDropsBody()
    {
        var ex = new OpenHabRequestException(
            System.Net.HttpStatusCode.Unauthorized,
            "openHAB request failed with 401 Unauthorized: {\"token\":\"secret\"}");

        var safe = SafeDiagnosticText.ForUserStatus(ex, "Connection failed.");

        Assert.Contains("Connection failed.", safe, StringComparison.Ordinal);
        Assert.Contains("401", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", safe, StringComparison.Ordinal);
    }

    [Fact]
    public void ForUserStatus_MapsTimeout()
    {
        var safe = SafeDiagnosticText.ForUserStatus(new TimeoutException("server did not answer"), "Connection failed.");

        Assert.Equal("Connection failed. The request timed out.", safe);
    }

    [Fact]
    public void ForUserStatus_MapsGenericExceptionWithoutMessage()
    {
        var safe = SafeDiagnosticText.ForUserStatus(new InvalidOperationException("contains token=abc"), "Connection failed.");

        Assert.Equal("Connection failed. InvalidOperationException.", safe);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore --filter SafeDiagnosticTextTests
```

Expected: build fails because `SafeDiagnosticText` does not exist.

## Task 2: Implement SafeDiagnosticText

**Files:**
- Create: `src/OpenHab.Core/Diagnostics/SafeDiagnosticText.cs`

- [ ] **Step 1: Add implementation**

Create `src/OpenHab.Core/Diagnostics/SafeDiagnosticText.cs` with:

```csharp
namespace OpenHab.Core;

public static class SafeDiagnosticText
{
    public static string ForLog(string? value, int maxLength = 240)
    {
        return SensitiveTextRedactor.Redact(value, maxLength);
    }

    public static string ForLog(Exception exception, int maxLength = 240)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return ForLog($"{exception.GetType().Name}: {exception.Message}", maxLength);
    }

    public static string ForUserStatus(Exception exception, string prefix)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = "Operation failed.";
        }

        prefix = prefix.TrimEnd();

        return exception switch
        {
            OpenHabRequestException requestException =>
                $"{prefix} openHAB returned HTTP {(int)requestException.StatusCode} {requestException.StatusCode}.",
            TimeoutException =>
                $"{prefix} The request timed out.",
            OperationCanceledException =>
                $"{prefix} The operation was canceled.",
            HttpRequestException =>
                $"{prefix} The openHAB endpoint could not be reached.",
            _ =>
                $"{prefix} {exception.GetType().Name}."
        };
    }
}
```

- [ ] **Step 2: Run focused tests**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore --filter SafeDiagnosticTextTests
```

Expected: tests pass.

- [ ] **Step 3: Commit diagnostic boundary**

Run:

```powershell
git add src\OpenHab.Core\Diagnostics\SafeDiagnosticText.cs tests\OpenHab.Core.Tests\Diagnostics\SafeDiagnosticTextTests.cs
git commit -m "test: cover safe diagnostic text"
```

## Task 3: Route Runtime Status Through SafeDiagnosticText

**Files:**
- Modify: `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`
- Test: `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`

- [ ] **Step 1: Add failing runtime status test**

In `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`, add a test near existing refresh failure tests:

```csharp
[Fact]
public async Task RefreshAsync_UsesSafeStatusForRequestFailure()
{
    var client = new FakeOpenHabClient
    {
        Sitemaps = [new SitemapInfo("default", "Default")],
        SitemapJsonException = new OpenHabRequestException(
            System.Net.HttpStatusCode.Unauthorized,
            "openHAB request failed with 401 Unauthorized: {\"token\":\"secret\"}")
    };
    var controller = CreateController(client, settings => settings.SetSitemapName("default"));

    await controller.RefreshAsync(CancellationToken.None);

    Assert.Contains("HTTP 401", controller.Current.StatusText, StringComparison.Ordinal);
    Assert.DoesNotContain("secret", controller.Current.StatusText, StringComparison.Ordinal);
    Assert.DoesNotContain("token", controller.Current.StatusText, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run test to verify failure**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter RefreshAsync_UsesSafeStatusForRequestFailure --blame-hang --blame-hang-timeout 30s
```

Expected: assertion fails because current status includes raw exception message.

- [ ] **Step 3: Update runtime status text**

In `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`, replace failure status assignments that use raw exception messages:

```csharp
StatusText = $"Connection failed: {firstError.Message}; fallback failed: {fallbackError.Message}",
```

with:

```csharp
StatusText = $"{SafeDiagnosticText.ForUserStatus(firstError, "Connection failed.")} {SafeDiagnosticText.ForUserStatus(fallbackError, "Fallback failed.")}",
```

Replace:

```csharp
StatusText = $"Connection failed: {error.Message}",
```

with:

```csharp
StatusText = SafeDiagnosticText.ForUserStatus(error, "Connection failed."),
```

Replace diagnostic log calls that include `{error.Message}`, `{firstError.Message}`, `{fallbackError.Message}`, or `{ex.Message}` in runtime failure paths with `SafeDiagnosticText.ForLog(exception)`.

- [ ] **Step 4: Run focused runtime test**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter RefreshAsync_UsesSafeStatusForRequestFailure --blame-hang --blame-hang-timeout 30s
```

Expected: assertion passes; host shutdown issue can still appear after the test.

## Task 4: Sanitize Windows Status Surfaces

**Files:**
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`

- [ ] **Step 1: Replace direct exception status text**

Replace direct user-visible status text such as:

```csharp
StatusText.Text = $"Error: {ex.Message}";
ShellConnectionText.Text = $"Error: {ex.Message}";
setStatusText($"Failed to save token: {ex.Message}");
setStatusText($"Failed to save cloud credentials: {ex.Message}");
setStatusText($"Could not open logs: {ex.Message}");
```

with:

```csharp
StatusText.Text = SafeDiagnosticText.ForUserStatus(ex, "Error.");
ShellConnectionText.Text = SafeDiagnosticText.ForUserStatus(ex, "Error.");
setStatusText(SafeDiagnosticText.ForUserStatus(ex, "Failed to save token."));
setStatusText(SafeDiagnosticText.ForUserStatus(ex, "Failed to save cloud credentials."));
setStatusText(SafeDiagnosticText.ForUserStatus(ex, "Could not open logs."));
```

- [ ] **Step 2: Sanitize warning logs**

Replace warning logs in those files that include `ex.Message` with:

```csharp
DiagnosticLogger.Warn($"Operation failed: {SafeDiagnosticText.ForLog(ex)}");
```

Use the existing operation-specific prefix from the original message.

- [ ] **Step 3: Build tray project**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore
```

Expected: build succeeds.

## Task 5: Sanitize Event And Notification Logging

**Files:**
- Modify: `src/OpenHab.Core/Events/OpenHabEventStreamClient.cs`
- Modify: `src/OpenHab.Windows.Notifications/NotificationPoller.cs`
- Modify: `src/OpenHab.Windows.Notifications/ToastService.cs`

- [ ] **Step 1: Update SSE logging**

In `OpenHabEventStreamClient`, replace raw SSE data logging:

```csharp
DiagnosticLogger.Info($"SSE raw: {line[..Math.Min(line.Length, 200)]}");
DiagnosticLogger.Warn($"SSE unparsed data line: {line[..Math.Min(line.Length, 200)]}");
```

with:

```csharp
DiagnosticLogger.Info($"SSE raw: {SafeDiagnosticText.ForLog(line, 200)}");
DiagnosticLogger.Warn($"SSE unparsed data line: {SafeDiagnosticText.ForLog(line, 200)}");
```

Replace:

```csharp
DiagnosticLogger.Warn($"SSE event stream error: {ex.GetType().Name}: {ex.Message}");
```

with:

```csharp
DiagnosticLogger.Warn($"SSE event stream error: {SafeDiagnosticText.ForLog(ex)}");
```

- [ ] **Step 2: Update notification poller LastError**

Replace:

```csharp
LastError = ex.Message;
```

with:

```csharp
LastError = SafeDiagnosticText.ForLog(ex);
```

- [ ] **Step 3: Update toast exception logs**

Replace toast exception log interpolations that include `{ex.Message}` or `{ex.StackTrace}` with `SafeDiagnosticText.ForLog(ex)`. Keep sequence numbers and broad operation names.

Example:

```csharp
DiagnosticLogger.Error($"Toast.Show#{seq} FAILED - {SafeDiagnosticText.ForLog(ex)}");
```

- [ ] **Step 4: Run Core and App tests**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --blame-hang --blame-hang-timeout 30s
```

Expected: Core passes. App assertions pass; host shutdown issue may still occur until later tasks.

## Task 6: Investigate App Test Host Shutdown

**Files:**
- No production edits in this task.
- Inspect: `tests/OpenHab.App.Tests`
- Inspect: `src/OpenHab.Windows.Tray`
- Inspect: `src/OpenHab.Windows.Notifications`

- [ ] **Step 1: Reproduce with blame-hang**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --blame-hang --blame-hang-timeout 30s
```

Expected: assertions pass and blame-hang aborts the host, or the test exits cleanly if the issue has already disappeared.

- [ ] **Step 2: Run active-test filters from blame-hang**

Run each filter separately, replacing names with the active tests printed by the current blame-hang output:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapControlFactoryTests --blame-hang --blame-hang-timeout 30s
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~DwmWindowDecorationsTests --blame-hang --blame-hang-timeout 30s
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapRowPlannerTests --blame-hang --blame-hang-timeout 30s
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapSurfaceRendererTests --blame-hang --blame-hang-timeout 30s
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapInputNormalizationTests --blame-hang --blame-hang-timeout 30s
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~DispatcherRefreshGateTests --blame-hang --blame-hang-timeout 30s
```

Expected: identify the smallest test group that leaves the host alive.

- [ ] **Step 3: Search for unmanaged/static lifetime sources**

Run:

```powershell
rg -n "static readonly HttpClient|DispatcherQueue|Task.Run|Timer|PeriodicTimer|SystemEvents|\\.\\+=|WebView2|NotifyIcon|ToastNotificationManager|AppNotificationManager|CancellationTokenSource|Dispose\\(" src tests\OpenHab.App.Tests
```

Expected: collect candidate lifetime sources. Do not edit until the smallest reproducing test group is known.

- [ ] **Step 4: Record investigation result**

Create `docs/superpowers/status/2026-05-14-openhab-windows-app-test-host-shutdown-investigation.md` with this structure and replace the example observations with the command results from Steps 1-3:

```markdown
# openHAB Windows App Test Host Shutdown Investigation

Date: 2026-05-14

## Reproduction

- Full App test command with blame-hang: recorded.
- Result: assertions passed and host shutdown behavior was observed.

## Filter Results

- `SitemapControlFactoryTests`: recorded pass/fail and host-exit behavior.
- `DwmWindowDecorationsTests`: recorded pass/fail and host-exit behavior.
- `SitemapRowPlannerTests`: recorded pass/fail and host-exit behavior.
- `SitemapSurfaceRendererTests`: recorded pass/fail and host-exit behavior.
- `SitemapInputNormalizationTests`: recorded pass/fail and host-exit behavior.
- `DispatcherRefreshGateTests`: recorded pass/fail and host-exit behavior.

## Candidate Lifetime Sources

- Static resources: recorded.
- Event subscriptions: recorded.
- Dispatcher or WinUI resources: recorded.
- Background tasks or cancellation sources: recorded.

## Conclusion

The next implementation step must target the smallest reproducing test group and the concrete lifetime source identified above.
```

Expected: the investigation file names the smallest reproducing group and the suspected lifetime source without changing production code.

## Task 7: Convert Test-Host Investigation Into A Focused Patch

**Files:**
- Create: `docs/superpowers/plans/2026-05-14-openhab-windows-app-test-host-shutdown-root-cause.md`
- Modify after that plan is written: the concrete source file identified in the investigation note.
- Test after that plan is written: the smallest reproducing test group identified in the investigation note.

- [ ] **Step 1: Write the focused root-cause patch plan**

Create `docs/superpowers/plans/2026-05-14-openhab-windows-app-test-host-shutdown-root-cause.md` after Task 6. The plan must include:

- The exact file and type that own the lifetime source.
- The exact test filter that reproduces the host shutdown issue.
- The failing test or verification command to run before the fix.
- The smallest cleanup or disposal change needed.
- The verification command that must exit cleanly without blame-hang.

Expected: the new root-cause patch plan has no generic lifetime categories and can be executed without guessing.

- [ ] **Step 2: Implement only the focused root-cause patch plan**

Use `docs/superpowers/plans/2026-05-14-openhab-windows-app-test-host-shutdown-root-cause.md` as the execution source for the actual App test-host fix. Do not apply generic cleanup outside the exact source identified by Task 6.

- [ ] **Step 3: Run the six known suspect filters without blame-hang**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapControlFactoryTests
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~DwmWindowDecorationsTests
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapRowPlannerTests
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapSurfaceRendererTests
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapInputNormalizationTests
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~DispatcherRefreshGateTests
```

Expected: all six filtered runs pass and exit cleanly.

- [ ] **Step 4: Run full App tests without blame-hang**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore
```

Expected: tests pass and process exits cleanly.

## Task 8: Remove Temporary CI Caveat After Test Host Fix

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `docs/superpowers/verification/openhab-windows-quality-gates.md`
- Modify: `docs/superpowers/status/openhab-windows-current-state.md`

- [ ] **Step 1: Make App tests blocking in CI**

In `.github/workflows/ci.yml`, replace:

```yaml
      - name: Test App with known host-shutdown caveat
        id: app_tests
        continue-on-error: true
        run: dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --blame-hang --blame-hang-timeout 30s

      - name: Report App test caveat
        if: steps.app_tests.outcome == 'failure'
        shell: pwsh
        run: |
          Write-Host "::warning::OpenHab.App.Tests has a known VSTest host shutdown issue. Assertions may pass before blame-hang aborts the host. See docs/superpowers/verification/openhab-windows-quality-gates.md."
```

with:

```yaml
      - name: Test App
        run: dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore
```

- [ ] **Step 2: Update quality gate App test command**

In `docs/superpowers/verification/openhab-windows-quality-gates.md`, replace App test commands using `--blame-hang --blame-hang-timeout 30s` with:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj
```

Replace text describing the known App test host caveat with:

```markdown
Expected: all direct test projects pass and exit cleanly.
```

- [ ] **Step 3: Update current state**

In `docs/superpowers/status/openhab-windows-current-state.md`, remove the high-priority backlog bullet for App test host shutdown and add verification evidence:

```markdown
- Passed: `dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --no-restore`; App tests passed and the VSTest host exited cleanly.
```

## Task 9: Final Verification And Commit

**Files:**
- Verify all files touched in Plan B.

- [ ] **Step 1: Run direct tests**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore
```

Expected: all pass and exit cleanly.

- [ ] **Step 2: Build tray project**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore
```

Expected: build succeeds.

- [ ] **Step 3: Search for raw sensitive status patterns**

Run:

```powershell
rg -n "StatusText\\.Text = \\$\\\"Error: \\{ex\\.Message\\}|ShellConnectionText\\.Text = \\$\\\"Error: \\{ex\\.Message\\}|setStatusText\\(\\$\\\"Failed .*\\{ex\\.Message\\}|SSE raw: \\{line|LastError = ex\\.Message" src tests
```

Expected: no matches.

- [ ] **Step 4: Check whitespace**

Run:

```powershell
git diff --check
```

Expected: no whitespace errors.

- [ ] **Step 5: Commit Plan B**

Run:

```powershell
git add src tests .github\workflows\ci.yml docs\superpowers\verification\openhab-windows-quality-gates.md docs\superpowers\status\openhab-windows-current-state.md
git commit -m "fix: harden diagnostics and app test shutdown"
```
