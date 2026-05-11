# openHAB Quality Session 2 Security Release Hygiene Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redact sensitive server-provided failure text and clean up release/package artifact tracking.

**Architecture:** Keep redaction in `OpenHab.Core` so app/runtime/UI status surfaces inherit safe exception messages. Treat signing certificate and generated package removal as a deliberate owner-approved repository hygiene step.

**Tech Stack:** .NET 10, xUnit, PowerShell, Git.

---

## Session Assignment

- **Codex instance:** Session 2, security/release hygiene.
- **Recommended model:** default Codex 5.3 Medium.
- **Worktree:** `.worktrees\quality-security`
- **Branch:** `quality/security-redaction`
- **Must not edit:** `FlyoutWindow.xaml.cs`, `MainWindow.xaml.cs`, `SitemapRuntimeController.cs`, settings persistence.

## Dependencies

- **Depends on:** No implementation dependency.
- **Can run in parallel with:** Sessions 1, 3, and 4.
- **Should merge:** After Session 1 if possible, before Session 4 final integration.
- **Requires human/owner approval:** Removing tracked `.pfx`, `.user`, `AppPackages`, or `BundleArtifacts` from Git.
- **Expected conflicts:** `.gitignore` or `OpenHab.Windows.Package.wapproj` if packaging work is active.

## Files

- Create: `src/OpenHab.Core/Diagnostics/SensitiveTextRedactor.cs`
- Modify: `src/OpenHab.Core/Api/OpenHabHttpClient.cs`
- Modify: `tests/OpenHab.Core.Tests/OpenHabHttpClientTests.cs`
- Modify: `.gitignore`
- Modify: `src/OpenHab.Windows.Package/OpenHab.Windows.Package.wapproj`
- Remove from Git index after approval:
  - `src/OpenHab.Windows.Package/OpenHab.Windows.Package_TemporaryKey.pfx`
  - `src/OpenHab.Windows.Tray/OpenHab_TemporaryKey.pfx`
  - `src/OpenHab.Windows.Tray/*.csproj.user`
  - `src/OpenHab.Windows.Tray/Properties/PublishProfiles/*.pubxml.user`
  - `src/OpenHab.Windows.Package/AppPackages/**`
  - `src/OpenHab.Windows.Package/BundleArtifacts/**`

## Task 1: Prepare Worktree

- [ ] **Step 1: Create or enter worktree**

Run from `D:\Source\Openhab\openhab-win-app`:

```powershell
git worktree add .worktrees\quality-security -b quality/security-redaction
```

Expected: `.worktrees\quality-security` exists. If it already exists, run future commands from that directory.

- [ ] **Step 2: Inventory tracked sensitive/package artifacts**

Run:

```powershell
git ls-files 'src/**.user' 'src/**.pfx' 'src/**/AppPackages/**' 'src/**/BundleArtifacts/**'
```

Expected: current tracked artifacts are listed. Do not remove yet without owner approval.

## Task 2: Add Redaction Tests

- [ ] **Step 1: Add failed-response redaction test**

Append to `tests/OpenHab.Core.Tests/OpenHabHttpClientTests.cs`:

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

If needed, add this helper at the bottom of the same test class:

```csharp
private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(handler(request));
    }
}
```

- [ ] **Step 2: Run focused test and confirm failure**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --filter FailedRequestRedactsSensitiveResponseBodies
```

Expected: failure because raw server response body is currently included in exception text.

## Task 3: Implement Redaction

- [ ] **Step 1: Create redactor**

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

- [ ] **Step 2: Use redactor in HTTP client**

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

- [ ] **Step 3: Run core tests**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
```

Expected: all core tests pass.

## Task 4: Ignore Local Packaging Artifacts

- [ ] **Step 1: Add ignore rules**

Append to `.gitignore` if equivalent rules do not already exist:

```gitignore
# Local Windows packaging/signing artifacts
*.pfx
*.csproj.user
*.pubxml.user
**/AppPackages/
**/BundleArtifacts/
```

- [ ] **Step 2: Make package certificate configurable**

In `src/OpenHab.Windows.Package/OpenHab.Windows.Package.wapproj`, replace:

```xml
<PackageCertificateKeyFile>OpenHab.Windows.Package_TemporaryKey.pfx</PackageCertificateKeyFile>
```

with:

```xml
<PackageCertificateKeyFile Condition="'$(PackageCertificateKeyFile)' != ''">$(PackageCertificateKeyFile)</PackageCertificateKeyFile>
<PackageCertificateThumbprint Condition="'$(PackageCertificateThumbprint)' != ''">$(PackageCertificateThumbprint)</PackageCertificateThumbprint>
```

If the project already defines either property elsewhere, keep one copy only.

## Task 5: Remove Tracked Local Artifacts After Approval

- [ ] **Step 1: Ask for explicit owner approval**

Ask:

```text
Do you approve removing the tracked temporary signing certificates, user publish metadata, AppPackages, and BundleArtifacts from Git tracking while leaving local ignored copies on disk?
```

Expected: proceed only after approval.

- [ ] **Step 2: Remove approved artifacts from Git index**

Run only after approval:

```powershell
git rm --cached src/OpenHab.Windows.Package/OpenHab.Windows.Package_TemporaryKey.pfx
git rm --cached src/OpenHab.Windows.Tray/OpenHab_TemporaryKey.pfx
git rm --cached src/OpenHab.Windows.Tray/*.csproj.user
git rm --cached src/OpenHab.Windows.Tray/Properties/PublishProfiles/*.pubxml.user
git rm -r --cached src/OpenHab.Windows.Package/AppPackages
git rm -r --cached src/OpenHab.Windows.Package/BundleArtifacts
```

Expected: files are staged for deletion from Git tracking.

- [ ] **Step 3: Verify tracked artifact list is empty**

Run:

```powershell
git ls-files 'src/**.user' 'src/**.pfx' 'src/**/AppPackages/**' 'src/**/BundleArtifacts/**'
```

Expected: no tracked artifact paths remain.

## Task 6: Verify and Commit

- [ ] **Step 1: Run verification**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
git diff --check
```

Expected: core tests pass and no whitespace errors.

- [ ] **Step 2: Commit**

Run:

```powershell
git add .gitignore src/OpenHab.Core tests/OpenHab.Core.Tests src/OpenHab.Windows.Package
git add -u src
git commit -m "fix: redact request failures and ignore local package artifacts"
```

Expected: commit succeeds.

## Handoff

Merge after Session 1 if possible. If owner approval for artifact removal is not available, commit redaction and ignore/project-file changes separately, then leave artifact removal as a documented blocker.
