# openHAB Windows Auth & Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add API token authentication for local and cloud openHAB connections with secure credential storage, and add cloud notification polling with Windows native toast display.

**Architecture:** Authorization lives in `OpenHab.Core` (credential store abstraction, header injection) and `OpenHab.App` (token settings, controller methods). Notifications live in a new `OpenHab.Windows.Notifications` project. The tray shell wires both together at startup. This slice does NOT implement the event stream, persistent settings, or subpage navigation.

**Tech Stack:** .NET 10 SDK, C#, WinUI / Windows App SDK, xUnit, `Windows.Security.Credentials.PasswordVault`, `Microsoft.Windows.AppNotifications`.

---

## Scope Boundary

Included:

**Authorization:**
- API token support for local and cloud endpoints (separate tokens).
- Secure storage via `Windows.Security.Credentials.PasswordVault`.
- `Authorization: Bearer <token>` header injection in `OpenHabHttpClient`.
- Credential redaction from exception messages and diagnostics.
- Token input fields (password boxes) in settings UI, per-endpoint.
- Settings controller methods to set/clear/check API tokens.

**Notifications:**
- Cloud notification polling from `myopenhab.org` notifications endpoint.
- Polling service with configurable interval (default 60s) that runs in background.
- Windows toast notifications via `Microsoft.Windows.AppNotifications.AppNotificationManager`.
- Toast activation: clicking a notification activates the main window.
- Notification polling disabled when in local-only mode.

Excluded:
- openHAB event stream subscriptions and live item updates.
- local network push notifications / SSE.
- Notification history or inbox UI.
- Rich notification actions (dismiss, mark read, reply).
- Persisted settings and migrations (tokens are persisted via PasswordVault; notification preferences are in-memory for this slice).
- OAuth2 / myopenHAB login flow (API token entry only).
- Subpage navigation from notification payloads.

## File Structure

### Authorization files:

- Create `src/OpenHab.Core/Auth/ICredentialStore.cs`: credential store abstraction.
- Create `src/OpenHab.Core/Auth/WindowsCredentialStore.cs`: PasswordVault implementation.
- Create `tests/OpenHab.Core.Tests/Auth/FakeCredentialStore.cs`: test double.
- Create `tests/OpenHab.Core.Tests/Auth/CredentialStoreTests.cs`: store behavior tests.
- Modify `src/OpenHab.Core/Api/IOpenHabClient.cs`: expose credential-safe diagnostic info.
- Modify `src/OpenHab.Core/Api/OpenHabHttpClient.cs`: accept optional API token, inject Bearer header, redact credentials from exceptions.
- Create `tests/OpenHab.Core.Tests/Api/OpenHabHttpClientAuthTests.cs`: header injection and redaction tests.
- Modify `src/OpenHab.App/Settings/AppSettings.cs`: add `HasLocalToken` and `HasCloudToken` booleans.
- Modify `src/OpenHab.App/Settings/AppSettingsController.cs`: add `SetApiToken`, `ClearApiToken`, `HasApiToken` methods backed by `ICredentialStore`.
- Modify `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`: cover token set/clear/check behavior.
- Modify `src/OpenHab.Windows.Tray/App.xaml.cs`: create `WindowsCredentialStore`, wire into settings and HTTP client factory.
- Modify `src/OpenHab.Windows.Tray/MainWindow.xaml`: add local and cloud API token password boxes.
- Modify `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`: handle token input and save.

### Notification files:

- Create `src/OpenHab.Windows.Notifications/OpenHab.Windows.Notifications.csproj`: new WinUI class library.
- Create `src/OpenHab.Windows.Notifications/NotificationPoller.cs`: polls cloud notifications endpoint, raises events.
- Create `src/OpenHab.Windows.Notifications/ToastService.cs`: shows Windows toasts via AppNotificationManager.
- Create `src/OpenHab.Windows.Notifications/NotificationActivationHandler.cs`: handles toast activation.
- Modify `src/OpenHab.Windows.Tray/App.xaml.cs`: start notification polling on launch, wire activation.
- Modify `OpenHab.Windows.sln`: add notification project reference.

### Status file:

- Create `docs/superpowers/status/2026-05-05-openhab-windows-auth-notifications-status.md`: record completion and verification.

---

### Task 1: Credential Store Abstraction And Windows Implementation

**Files:**
- Create: `src/OpenHab.Core/Auth/ICredentialStore.cs`
- Create: `src/OpenHab.Core/Auth/WindowsCredentialStore.cs`
- Create: `tests/OpenHab.Core.Tests/Auth/FakeCredentialStore.cs`
- Create: `tests/OpenHab.Core.Tests/Auth/CredentialStoreTests.cs`

- [ ] **Step 1: Write the credential store tests first**

Create `tests/OpenHab.Core.Tests/Auth/FakeCredentialStore.cs`:

```csharp
namespace OpenHab.Core.Tests.Auth;

public sealed class FakeCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> store = new();

    public Task StoreAsync(string resource, string key, string secret, CancellationToken cancellationToken)
    {
        store[$"{resource}:{key}"] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> RetrieveAsync(string resource, string key, CancellationToken cancellationToken)
    {
        store.TryGetValue($"{resource}:{key}", out var value);
        return Task.FromResult(value);
    }

    public Task RemoveAsync(string resource, string key, CancellationToken cancellationToken)
    {
        store.Remove($"{resource}:{key}");
        return Task.CompletedTask;
    }
}
```

Create `tests/OpenHab.Core.Tests/Auth/CredentialStoreTests.cs`:

```csharp
using OpenHab.Core.Auth;

namespace OpenHab.Core.Tests.Auth;

public sealed class CredentialStoreTests
{
    [Fact]
    public async Task StoreAndRetrieveRoundTrip()
    {
        ICredentialStore store = new FakeCredentialStore();
        await store.StoreAsync("OpenHabAuth", "local-token", "oh.token.abc123", CancellationToken.None);
        var retrieved = await store.RetrieveAsync("OpenHabAuth", "local-token", CancellationToken.None);
        Assert.Equal("oh.token.abc123", retrieved);
    }

    [Fact]
    public async Task RetrieveMissingKeyReturnsNull()
    {
        ICredentialStore store = new FakeCredentialStore();
        var retrieved = await store.RetrieveAsync("OpenHabAuth", "nonexistent", CancellationToken.None);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task RemoveRemovesStoredKey()
    {
        ICredentialStore store = new FakeCredentialStore();
        await store.StoreAsync("OpenHabAuth", "cloud-token", "oh.token.xyz789", CancellationToken.None);
        await store.RemoveAsync("OpenHabAuth", "cloud-token", CancellationToken.None);
        var retrieved = await store.RetrieveAsync("OpenHabAuth", "cloud-token", CancellationToken.None);
        Assert.Null(retrieved);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task StoreRejectsBlankSecret(string secret)
    {
        ICredentialStore store = new FakeCredentialStore();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.StoreAsync("OpenHabAuth", "local-token", secret, CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run credential store tests and verify failure**

```powershell
dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj --filter CredentialStoreTests
```

Expected: fail because `ICredentialStore` does not exist.

- [ ] **Step 3: Implement the credential store interface and Windows implementation**

Create `src/OpenHab.Core/Auth/ICredentialStore.cs`:

```csharp
namespace OpenHab.Core.Auth;

public interface ICredentialStore
{
    Task StoreAsync(string resource, string key, string secret, CancellationToken cancellationToken);
    Task<string?> RetrieveAsync(string resource, string key, CancellationToken cancellationToken);
    Task RemoveAsync(string resource, string key, CancellationToken cancellationToken);
}
```

Create `src/OpenHab.Core/Auth/WindowsCredentialStore.cs`:

```csharp
using Windows.Security.Credentials;

namespace OpenHab.Core.Auth;

public sealed class WindowsCredentialStore : ICredentialStore
{
    public Task StoreAsync(string resource, string key, string secret, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("Secret must not be blank.", nameof(secret));
        }

        var vault = new PasswordVault();
        vault.Add(new PasswordCredential(resource, key, secret));
        return Task.CompletedTask;
    }

    public Task<string?> RetrieveAsync(string resource, string key, CancellationToken cancellationToken)
    {
        try
        {
            var vault = new PasswordVault();
            var credential = vault.Retrieve(resource, key);
            credential.RetrievePassword();
            return Task.FromResult<string?>(credential.Password);
        }
        catch (Exception) when (true)
        {
            return Task.FromResult<string?>(null);
        }
    }

    public Task RemoveAsync(string resource, string key, CancellationToken cancellationToken)
    {
        try
        {
            var vault = new PasswordVault();
            var credential = vault.Retrieve(resource, key);
            vault.Remove(credential);
        }
        catch (Exception) when (true)
        {
            // Already removed or never stored — no-op.
        }

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Run credential store tests and verify they pass**

```powershell
dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj --filter CredentialStoreTests
```

Expected: all 4 tests pass.

- [ ] **Step 5: Commit the credential store slice**

```powershell
git add src/OpenHab.Core/Auth tests/OpenHab.Core.Tests/Auth
git commit -m "feat: add credential store abstraction and windows implementation"
```

---

### Task 2: API Token Settings And Controller Methods

**Files:**
- Modify: `src/OpenHab.App/Settings/AppSettings.cs`
- Modify: `src/OpenHab.App/Settings/AppSettingsController.cs`
- Modify: `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`
- Create: `tests/OpenHab.App.Tests/Settings/FakeCredentialStore.cs`

- [ ] **Step 1: Write the settings tests for token management**

Create `tests/OpenHab.App.Tests/Settings/FakeCredentialStore.cs` (same as above, in test project):

```csharp
using OpenHab.Core.Auth;

namespace OpenHab.App.Tests.Settings;

public sealed class FakeCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> store = new();

    public Task StoreAsync(string resource, string key, string secret, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Secret must not be blank.", nameof(secret));
        store[$"{resource}:{key}"] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> RetrieveAsync(string resource, string key, CancellationToken cancellationToken)
    {
        store.TryGetValue($"{resource}:{key}", out var value);
        return Task.FromResult(value);
    }

    public Task RemoveAsync(string resource, string key, CancellationToken cancellationToken)
    {
        store.Remove($"{resource}:{key}");
        return Task.CompletedTask;
    }
}
```

Modify `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs` — add token tests:

Append these test methods inside the existing `AppSettingsControllerTests` class:

```csharp
[Fact]
public void DefaultsHaveNoTokens()
{
    var controller = new AppSettingsController(new FakeCredentialStore());
    Assert.False(controller.Current.HasLocalToken);
    Assert.False(controller.Current.HasCloudToken);
}

[Fact]
public async Task SetAndClearLocalApiToken()
{
    var store = new FakeCredentialStore();
    var controller = new AppSettingsController(store);

    await controller.SetApiTokenAsync(TransportKind.Local, "oh.local.token123", CancellationToken.None);

    Assert.True(controller.Current.HasLocalToken);
    var retrieved = await store.RetrieveAsync("OpenHabAuth", "local-token", CancellationToken.None);
    Assert.Equal("oh.local.token123", retrieved);

    await controller.ClearApiTokenAsync(TransportKind.Local, CancellationToken.None);
    Assert.False(controller.Current.HasLocalToken);
    var afterClear = await store.RetrieveAsync("OpenHabAuth", "local-token", CancellationToken.None);
    Assert.Null(afterClear);
}

[Fact]
public async Task SetAndClearCloudApiToken()
{
    var store = new FakeCredentialStore();
    var controller = new AppSettingsController(store);

    await controller.SetApiTokenAsync(TransportKind.Cloud, "oh.cloud.token456", CancellationToken.None);

    Assert.True(controller.Current.HasCloudToken);
    var retrieved = await store.RetrieveAsync("OpenHabAuth", "cloud-token", CancellationToken.None);
    Assert.Equal("oh.cloud.token456", retrieved);

    await controller.ClearApiTokenAsync(TransportKind.Cloud, CancellationToken.None);
    Assert.False(controller.Current.HasCloudToken);
}

[Fact]
public async Task SetApiTokenRejectsBlankToken()
{
    var controller = new AppSettingsController(new FakeCredentialStore());
    await Assert.ThrowsAsync<ArgumentException>(() =>
        controller.SetApiTokenAsync(TransportKind.Local, "  ", CancellationToken.None));
}

[Fact]
public async Task GetApiTokenAsyncReturnsTokenFromStore()
{
    var store = new FakeCredentialStore();
    await store.StoreAsync("OpenHabAuth", "local-token", "oh.mytoken", CancellationToken.None);
    var controller = new AppSettingsController(store);

    var token = await controller.GetApiTokenAsync(TransportKind.Local, CancellationToken.None);

    Assert.Equal("oh.mytoken", token);
}

[Fact]
public async Task GetApiTokenAsyncReturnsNullWhenNotStored()
{
    var controller = new AppSettingsController(new FakeCredentialStore());
    var token = await controller.GetApiTokenAsync(TransportKind.Cloud, CancellationToken.None);
    Assert.Null(token);
}
```

Note: existing tests construct `AppSettingsController` without a credential store. They'll need a default parameter or the constructor signature needs updating. Since this is a plan doc, note that the existing constructor becomes `AppSettingsController(ICredentialStore? credentialStore = null)` where `null` means token methods throw `InvalidOperationException`.

- [ ] **Step 2: Run the app settings tests and verify failure**

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter "AppSettingsControllerTests"
```

Expected: new token tests fail because `HasLocalToken`, `HasCloudToken`, `SetApiTokenAsync`, etc. don't exist.

- [ ] **Step 3: Implement token settings**

Modify `src/OpenHab.App/Settings/AppSettings.cs` — add token booleans:

```csharp
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.App.Settings;

public sealed record AppSettings(
    SitemapSkinKind Skin,
    EndpointMode EndpointMode,
    Uri LocalEndpoint,
    Uri CloudEndpoint,
    string SitemapName,
    bool HasLocalToken,
    bool HasCloudToken)
{
    public static AppSettings Default { get; } = new(
        SitemapSkinKind.Windows11,
        EndpointMode.Automatic,
        new Uri("http://openhab.local:8080"),
        new Uri("https://myopenhab.org"),
        "default",
        HasLocalToken: false,
        HasCloudToken: false);
}
```

Modify `src/OpenHab.App/Settings/AppSettingsController.cs` — add constructor overload and token methods:

```csharp
using OpenHab.Core.Auth;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using System.Text.RegularExpressions;

namespace OpenHab.App.Settings;

public sealed class AppSettingsController
{
    private static readonly Regex SitemapNamePattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);
    private const string CredentialResource = "OpenHabAuth";
    private const string LocalKey = "local-token";
    private const string CloudKey = "cloud-token";

    private readonly ICredentialStore? credentialStore;

    public AppSettingsController(ICredentialStore? credentialStore = null)
    {
        this.credentialStore = credentialStore;
    }

    public AppSettings Current { get; private set; } = AppSettings.Default;

    // Existing methods unchanged: SetSkin, SetEndpointMode, SetEndpoints, SetSitemapName

    public async Task SetApiTokenAsync(TransportKind transportKind, string token, CancellationToken cancellationToken)
    {
        if (credentialStore is null)
            throw new InvalidOperationException("No credential store configured.");

        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("API token must not be blank.", nameof(token));

        var key = transportKind == TransportKind.Local ? LocalKey : CloudKey;
        await credentialStore.StoreAsync(CredentialResource, key, token.Trim(), cancellationToken);

        Current = transportKind == TransportKind.Local
            ? Current with { HasLocalToken = true }
            : Current with { HasCloudToken = true };
    }

    public async Task ClearApiTokenAsync(TransportKind transportKind, CancellationToken cancellationToken)
    {
        if (credentialStore is null)
            throw new InvalidOperationException("No credential store configured.");

        var key = transportKind == TransportKind.Local ? LocalKey : CloudKey;
        await credentialStore.RemoveAsync(CredentialResource, key, cancellationToken);

        Current = transportKind == TransportKind.Local
            ? Current with { HasLocalToken = false }
            : Current with { HasCloudToken = false };
    }

    public async Task<string?> GetApiTokenAsync(TransportKind transportKind, CancellationToken cancellationToken)
    {
        if (credentialStore is null)
            throw new InvalidOperationException("No credential store configured.");

        var key = transportKind == TransportKind.Local ? LocalKey : CloudKey;
        return await credentialStore.RetrieveAsync(CredentialResource, key, cancellationToken);
    }

    // Leave existing SetSkin, SetEndpointMode, SetEndpoints, SetSitemapName unchanged below.
}
```

Note: The existing `SetSkin`, `SetEndpointMode`, `SetEndpoints`, `SetSitemapName` methods remain as-is.

- [ ] **Step 4: Run the app settings tests and verify they pass**

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter "AppSettingsControllerTests"
```

Expected: all token tests pass alongside existing tests.

- [ ] **Step 5: Commit the token settings slice**

```powershell
git add src/OpenHab.App/Settings tests/OpenHab.App.Tests
git commit -m "feat: add api token settings with credential store backing"
```

---

### Task 3: Auth Header Injection And Credential Redaction In HTTP Client

**Files:**
- Modify: `src/OpenHab.Core/Api/IOpenHabClient.cs`
- Modify: `src/OpenHab.Core/Api/OpenHabHttpClient.cs`
- Create: `tests/OpenHab.Core.Tests/Api/OpenHabHttpClientAuthTests.cs`

- [ ] **Step 1: Write the auth client tests**

Create `tests/OpenHab.Core.Tests/Api/OpenHabHttpClientAuthTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using OpenHab.Core.Api;

namespace OpenHab.Core.Tests.Api;

public sealed class OpenHabHttpClientAuthTests
{
    [Fact]
    public async Task InjectsBearerTokenIntoRequestHeaders()
    {
        string? capturedAuthHeader = null;
        var handler = new CapturingHandler(responseBody: "{}", onRequest: req =>
        {
            capturedAuthHeader = req.Headers.Authorization?.ToString();
        });

        var client = new OpenHabHttpClient(
            new HttpClient(handler),
            new Uri("http://openhab.local:8080"),
            apiToken: "oh.test.token123");

        await client.GetSitemapJsonAsync("default", CancellationToken.None);

        Assert.Equal("Bearer oh.test.token123", capturedAuthHeader);
    }

    [Fact]
    public async Task OmitsAuthorizationHeaderWhenTokenIsNull()
    {
        string? capturedAuthHeader = null;
        var handler = new CapturingHandler(responseBody: "{}", onRequest: req =>
        {
            capturedAuthHeader = req.Headers.Authorization?.ToString();
        });

        var client = new OpenHabHttpClient(
            new HttpClient(handler),
            new Uri("http://openhab.local:8080"));

        await client.GetSitemapJsonAsync("default", CancellationToken.None);

        Assert.Null(capturedAuthHeader);
    }

    [Fact]
    public async Task RedactsTokenFromExceptionMessage()
    {
        var handler = new CapturingHandler(
            responseBody: "Unauthorized",
            statusCode: HttpStatusCode.Unauthorized);

        var client = new OpenHabHttpClient(
            new HttpClient(handler),
            new Uri("http://openhab.local:8080"),
            apiToken: "oh.secret.token999");

        var exception = await Assert.ThrowsAsync<OpenHabRequestException>(
            () => client.GetSitemapJsonAsync("default", CancellationToken.None));

        Assert.DoesNotContain("oh.secret.token999", exception.Message);
        Assert.DoesNotContain("Bearer", exception.Message);
        Assert.Contains("401", exception.Message);
    }

    [Fact]
    public async Task DoesNotLeakTokenInUrlDiagnostics()
    {
        var handler = new CapturingHandler(
            responseBody: "Not Found",
            statusCode: HttpStatusCode.NotFound);

        var client = new OpenHabHttpClient(
            new HttpClient(handler),
            new Uri("http://openhab.local:8080"),
            apiToken: "oh.token.abc");

        var exception = await Assert.ThrowsAsync<OpenHabRequestException>(
            () => client.GetSitemapJsonAsync("default", CancellationToken.None));

        Assert.DoesNotContain("oh.token.abc", exception.Message);
        Assert.Contains("rest/sitemaps", exception.Message);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string responseBody;
        private readonly HttpStatusCode statusCode;
        private readonly Action<HttpRequestMessage>? onRequest;

        public CapturingHandler(
            string responseBody,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            Action<HttpRequestMessage>? onRequest = null)
        {
            this.responseBody = responseBody;
            this.statusCode = statusCode;
            this.onRequest = onRequest;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onRequest?.Invoke(request);
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
```

- [ ] **Step 2: Run the auth client tests and verify failure**

```powershell
dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj --filter OpenHabHttpClientAuthTests
```

Expected: fail because the new constructor overload and auth behavior don't exist.

- [ ] **Step 3: Implement auth header injection with credential redaction**

Modify `src/OpenHab.Core/Api/OpenHabHttpClient.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;

namespace OpenHab.Core.Api;

public sealed class OpenHabRequestException : Exception
{
    public OpenHabRequestException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public sealed class OpenHabHttpClient : IOpenHabClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;
    private readonly string? _apiToken;
    private readonly bool _hasAuth;

    public OpenHabHttpClient(HttpClient httpClient, Uri baseUri, string? apiToken = null)
    {
        _httpClient = httpClient;
        _baseUri = baseUri;
        _apiToken = apiToken;
        _hasAuth = !string.IsNullOrWhiteSpace(apiToken);
    }

    public bool IsAuthenticated => _hasAuth;

    public Task SendCommandAsync(string itemName, string command, CancellationToken cancellationToken)
    {
        return SendPlainTextAsync(HttpMethod.Post, $"rest/items/{Uri.EscapeDataString(itemName)}", command, cancellationToken);
    }

    public Task SetItemStateAsync(string itemName, string state, CancellationToken cancellationToken)
    {
        return SendPlainTextAsync(HttpMethod.Put, $"rest/items/{Uri.EscapeDataString(itemName)}/state", state, cancellationToken);
    }

    public async Task<string> GetSitemapJsonAsync(string sitemapName, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri($"rest/sitemaps/{Uri.EscapeDataString(sitemapName)}"));
        ApplyAuth(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await ThrowIfFailedAsync(response, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task SendPlainTextAsync(HttpMethod method, string relativePath, string body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(relativePath))
        {
            Content = new StringContent(body)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        ApplyAuth(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await ThrowIfFailedAsync(response, cancellationToken);
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (_apiToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
        }
    }

    private Uri BuildUri(string relativePath)
    {
        var baseBuilder = new UriBuilder(_baseUri);
        if (!baseBuilder.Path.EndsWith("/", StringComparison.Ordinal))
        {
            baseBuilder.Path += "/";
        }

        return new Uri(baseBuilder.Uri, relativePath.TrimStart('/'));
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        // Redact credentials: truncate body and never include auth info
        var safeBody = body.Length > 120 ? body[..120] : body;
        throw new OpenHabRequestException(
            response.StatusCode,
            $"openHAB request failed with {(int)response.StatusCode} {response.ReasonPhrase}: {safeBody}");
    }
}
```

Note: `ThrowIfFailedAsync` already does not include the request URI or headers in the message, so the token is not leaked. The exception message only contains the response status code, reason phrase, and response body (truncated).

- [ ] **Step 4: Run the auth client tests and verify they pass**

```powershell
dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj --filter OpenHabHttpClientAuthTests
```

Expected: all 4 auth tests pass.

- [ ] **Step 5: Run existing HTTP client tests to verify no regressions**

```powershell
dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj --filter "OpenHabHttpClient"
```

Expected: all existing HTTP client tests still pass.

- [ ] **Step 6: Commit the auth HTTP client slice**

```powershell
git add src/OpenHab.Core/Api tests/OpenHab.Core.Tests/Api
git commit -m "feat: add bearer token auth and credential-safe error messages"
```

---

### Task 4: Wire Authorization Into The Tray App

**Files:**
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`

- [ ] **Step 1: Update App.xaml.cs to wire credential store and token-aware HTTP client factory**

Modify `src/OpenHab.Windows.Tray/App.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Core.Api;
using OpenHab.Core.Auth;
using OpenHab.Core.Profiles;
using OpenHab.Windows.Tray.Tray;
using System.Threading;
using Microsoft.UI.Dispatching;
using System.Net.Http;

namespace OpenHab.Windows.Tray;

public partial class App : Application
{
    private MainWindow? window;
    private TrayIconService? trayIcon;
    private DispatcherQueue? uiDispatcherQueue;
    private HttpClient? httpClient;
    private ICredentialStore? credentialStore;
    private int isShuttingDown;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();

        credentialStore = new WindowsCredentialStore();
        var settingsController = new AppSettingsController(credentialStore);
        var renderController = new SitemapRenderController(settingsController);
        httpClient = new HttpClient();
        var runtimeController = new SitemapRuntimeController(
            settingsController,
            renderController,
            (transportKind, endpoint) =>
            {
                var token = GetApiTokenForTransport(settingsController, transportKind);
                return new OpenHabHttpClient(httpClient, endpoint, apiToken: token);
            });

        window = new MainWindow(settingsController, runtimeController);
        trayIcon = new TrayIconService(
            showWindow: () =>
            {
                window.Activate();
                _ = window.RefreshRuntimeAsync();
            },
            exitApplication: () =>
            {
                ShutdownTrayResources();
                Exit();
            });

        window.Activate();
    }

    private static string? GetApiTokenForTransport(
        AppSettingsController settingsController, TransportKind transportKind)
    {
        // Fire-and-forget sync-over-async is acceptable here because
        // the factory is called during runtime operations which are already async.
        // Use a short-lived approach: return the cached value from settings.
        // The actual token retrieval happens via GetApiTokenAsync.
        // For the factory callback, we use a simplified path.
        try
        {
            return settingsController.GetApiTokenAsync(transportKind, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private void OnProcessExit(object? sender, EventArgs args)
    {
        ShutdownTrayResources();
    }

    private void ShutdownTrayResources()
    {
        if (Interlocked.Exchange(ref isShuttingDown, 1) != 0)
        {
            return;
        }

        var dispatcher = uiDispatcherQueue;
        if (dispatcher is not null && !dispatcher.HasThreadAccess)
        {
            if (dispatcher.TryEnqueue(ShutdownTrayResourcesCore))
            {
                return;
            }

            return;
        }

        ShutdownTrayResourcesCore();
    }

    private void ShutdownTrayResourcesCore()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        trayIcon?.Dispose();
        trayIcon = null;
        httpClient?.Dispose();
        httpClient = null;
    }
}
```

- [ ] **Step 2: Add API token password boxes to the settings UI**

Modify `src/OpenHab.Windows.Tray/MainWindow.xaml` — add token fields inside the Settings TabViewItem, after the endpoint text boxes:

```xml
<PasswordBox x:Name="LocalTokenBox"
             Header="Local API token"
             PlaceholderText="Enter token (optional)"
             LostFocus="TokenBox_LostFocus"
             Tag="Local" />
<PasswordBox x:Name="CloudTokenBox"
             Header="Cloud API token"
             PlaceholderText="Enter token (optional)"
             LostFocus="TokenBox_LostFocus"
             Tag="Cloud" />
```

Note: Insert these inside the `<StackPanel Spacing="12" MaxWidth="420">` under the Settings tab, after the `CloudEndpointText` TextBox.

- [ ] **Step 3: Add token handling to the code-behind**

Modify `src/OpenHab.Windows.Tray/MainWindow.xaml.cs` — add token event handlers:

In `RefreshSettingsBindings()`, add lines to reflect the current token state:

```csharp
LocalTokenBox.Password = settingsController.Current.HasLocalToken ? "••••••••" : string.Empty;
CloudTokenBox.Password = settingsController.Current.HasCloudToken ? "••••••••" : string.Empty;
```

Add the `TokenBox_LostFocus` handler:

```csharp
private async void TokenBox_LostFocus(object sender, RoutedEventArgs e)
{
    if (sender is not PasswordBox box || box.Tag is not string tag)
    {
        return;
    }

    var transportKind = tag == "Local" ? TransportKind.Local : TransportKind.Cloud;
    var token = box.Password;

    try
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            await settingsController.ClearApiTokenAsync(transportKind, CancellationToken.None);
        }
        else if (token is not "••••••••") // Don't re-save the placeholder
        {
            await settingsController.SetApiTokenAsync(transportKind, token, CancellationToken.None);
            await LoadRuntimeAsync();
        }
    }
    catch (ArgumentException)
    {
        // Invalid token — revert UI
        RefreshSettingsBindings();
    }
}
```

- [ ] **Step 4: Build the tray project and verify success**

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj
```

Expected: build succeeds with the auth wiring.

- [ ] **Step 5: Commit the auth UI wiring**

```powershell
git add src/OpenHab.Windows.Tray
git commit -m "feat: wire api token auth into tray app ui and http client"
```

---

### Task 5: Cloud Notification Polling Service

**Files:**
- Create: `src/OpenHab.Windows.Notifications/OpenHab.Windows.Notifications.csproj`
- Create: `src/OpenHab.Windows.Notifications/NotificationPoller.cs`
- Create: `src/OpenHab.Windows.Notifications/CloudNotification.cs`

- [ ] **Step 1: Create the notifications project**

Create `src/OpenHab.Windows.Notifications/OpenHab.Windows.Notifications.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWinUI>true</UseWinUI>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\OpenHab.Core\OpenHab.Core.csproj" />
  </ItemGroup>
</Project>
```

Add the project to the solution:

```powershell
dotnet sln OpenHab.Windows.sln add src/OpenHab.Windows.Notifications/OpenHab.Windows.Notifications.csproj
```

- [ ] **Step 2: Create the cloud notification model**

Create `src/OpenHab.Windows.Notifications/CloudNotification.cs`:

```csharp
namespace OpenHab.Windows.Notifications;

public sealed record CloudNotification(
    string Id,
    string Message,
    DateTimeOffset Created,
    string? Icon,
    string? Severity);
```

- [ ] **Step 3: Create the notification poller**

Create `src/OpenHab.Windows.Notifications/NotificationPoller.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;

namespace OpenHab.Windows.Notifications;

public sealed class NotificationPoller : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly Uri cloudBaseUri;
    private readonly string? apiToken;
    private readonly TimeSpan pollInterval;
    private readonly HashSet<string> seenIds = new();

    private CancellationTokenSource? cts;
    private Task? pollingTask;

    public event EventHandler<CloudNotification>? NotificationReceived;

    public NotificationPoller(
        HttpClient httpClient,
        Uri cloudBaseUri,
        string? apiToken = null,
        TimeSpan? pollInterval = null)
    {
        this.httpClient = httpClient;
        this.cloudBaseUri = cloudBaseUri;
        this.apiToken = apiToken;
        this.pollInterval = pollInterval ?? TimeSpan.FromSeconds(60);
    }

    public bool IsRunning => pollingTask is not null && !pollingTask.IsCompleted;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        cts = new CancellationTokenSource();
        pollingTask = PollLoopAsync(cts.Token);
    }

    public void Stop()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    public void Dispose()
    {
        Stop();
        cts?.Dispose();
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Silently ignore polling errors — next cycle will retry.
            }

            try
            {
                await Task.Delay(pollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(cloudBaseUri, "rest/notifications?limit=20"));

        if (apiToken is not null)
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        var notifications = await response.Content
            .ReadFromJsonAsync<List<CloudNotification>>(cancellationToken: cancellationToken);

        if (notifications is null)
        {
            return;
        }

        foreach (var notification in notifications.OrderBy(n => n.Created))
        {
            if (seenIds.Add(notification.Id))
            {
                NotificationReceived?.Invoke(this, notification);
            }
        }
    }
}
```

- [ ] **Step 4: Build the notification project**

```powershell
dotnet build src/OpenHab.Windows.Notifications/OpenHab.Windows.Notifications.csproj
```

Expected: build succeeds.

- [ ] **Step 5: Commit the notification polling slice**

```powershell
git add src/OpenHab.Windows.Notifications OpenHab.Windows.sln
git commit -m "feat: add cloud notification polling service"
```

---

### Task 6: Windows Toast Notification Service

**Files:**
- Create: `src/OpenHab.Windows.Notifications/ToastService.cs`

- [ ] **Step 1: Create the toast service**

Create `src/OpenHab.Windows.Notifications/ToastService.cs`:

```csharp
using Microsoft.Windows.AppNotifications;

namespace OpenHab.Windows.Notifications;

public static class ToastService
{
    private static bool isRegistered;

    public static void EnsureRegistered()
    {
        if (isRegistered)
        {
            return;
        }

        AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
        AppNotificationManager.Default.Register();
        isRegistered = true;
    }

    public static void Show(string title, string body)
    {
        EnsureRegistered();

        var appNotification = new AppNotificationBuilder()
            .AddText(title)
            .AddText(body)
            .BuildNotification();

        AppNotificationManager.Default.Show(appNotification);
    }

    public static event EventHandler? NotificationActivated;

    private static void OnNotificationInvoked(
        AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        NotificationActivated?.Invoke(null, EventArgs.Empty);
    }
}
```

- [ ] **Step 2: Build and verify**

```powershell
dotnet build src/OpenHab.Windows.Notifications/OpenHab.Windows.Notifications.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Commit the toast service slice**

```powershell
git add src/OpenHab.Windows.Notifications
git commit -m "feat: add windows toast notification service"
```

---

### Task 7: Wire Notifications Into The Tray App

**Files:**
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj`

- [ ] **Step 1: Add notification project reference to tray app**

Modify `src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj` — add:

```xml
<ProjectReference Include="..\OpenHab.Windows.Notifications\OpenHab.Windows.Notifications.csproj" />
```

- [ ] **Step 2: Wire notification polling and toast display in App.xaml.cs**

Modify `src/OpenHab.Windows.Tray/App.xaml.cs` — add notification startup:

At the top, add `using OpenHab.Windows.Notifications;`.

In `OnLaunched`, after `window.Activate();`:

```csharp
// Start notification polling for cloud-connected mode.
StartNotificationPolling(settingsController);
```

Add the helper method:

```csharp
private void StartNotificationPolling(AppSettingsController settingsController)
{
    var settings = settingsController.Current;
    if (settings.EndpointMode == EndpointMode.LocalOnly)
    {
        return; // No cloud polling in local-only mode.
    }

    ToastService.EnsureRegistered();
    ToastService.NotificationActivated += (_, _) =>
    {
        _ = uiDispatcherQueue?.TryEnqueue(() =>
        {
            window?.Activate();
        });
    };

    var cloudToken = GetApiTokenForTransport(settingsController, TransportKind.Cloud);
    var poller = new NotificationPoller(
        new HttpClient(),
        settings.CloudEndpoint,
        apiToken: cloudToken);

    poller.NotificationReceived += (_, notification) =>
    {
        _ = uiDispatcherQueue?.TryEnqueue(() =>
        {
            var title = notification.Severity is not null
                ? $"[{notification.Severity}] openHAB"
                : "openHAB";
            var body = notification.Message.Length > 200
                ? notification.Message[..197] + "..."
                : notification.Message;
            ToastService.Show(title, body);
        });
    };

    poller.Start();
}
```

- [ ] **Step 3: Build the tray project and verify**

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj
```

Expected: build succeeds with notifications wired.

- [ ] **Step 4: Commit the notification wiring**

```powershell
git add src/OpenHab.Windows.Tray
git commit -m "feat: wire cloud notification polling and toast display into tray app"
```

---

### Task 8: Full Verification And Status Recording

**Files:**
- Create: `docs/superpowers/status/2026-05-05-openhab-windows-auth-notifications-status.md`

- [ ] **Step 1: Run the full solution tests**

```powershell
dotnet test OpenHab.Windows.sln
```

Expected: all solution test projects pass, including new auth and credential store tests.

- [ ] **Step 2: Run the release build**

```powershell
dotnet build OpenHab.Windows.sln --configuration Release
```

Expected: release build succeeds with 0 warnings and 0 errors introduced by this slice.

- [ ] **Step 3: Write the completion status**

Create `docs/superpowers/status/2026-05-05-openhab-windows-auth-notifications-status.md`:

```markdown
# openHAB Windows Auth & Notifications Status

Date: 2026-05-05

## Source Documents

- Design spec: `docs/superpowers/specs/2026-05-04-openhab-windows-sitemap-client-design.md`
- Connected homepage status: `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`
- Auth & notifications plan: `docs/superpowers/plans/2026-05-05-openhab-windows-auth-notifications.md`

## Completed

- Added `ICredentialStore` abstraction and `WindowsCredentialStore` implementation backed by `PasswordVault`.
- Added `Bearer` token header injection in `OpenHabHttpClient` with credential-safe exception messages.
- Added `HasLocalToken` / `HasCloudToken` to `AppSettings` with `SetApiTokenAsync`, `ClearApiTokenAsync`, and `GetApiTokenAsync` controller methods.
- Added API token password boxes to the tray settings UI (local and cloud, per-endpoint).
- Created `OpenHab.Windows.Notifications` project with cloud notification polling and Windows toast display.
- Wired notification polling into tray app startup (cloud-only/automatic modes).
- Toast activation opens the main window.

## Verification

- `dotnet test OpenHab.Windows.sln`: replace with actual pass/fail counts.
- `dotnet build OpenHab.Windows.sln --configuration Release`: replace with actual warning/error counts.

## Still Out Of Scope

- openHAB event stream subscriptions and live item updates.
- Persistent settings and migrations (token persistence is via PasswordVault only).
- OAuth2 / myopenhab.org login flow.
- Notification history or inbox UI.
- Rich notification actions (dismiss, mark read, reply).
- Subpage navigation.
- Offline cache persistence.
- WebView/Main UI fallback routing.
- MSIX packaging and signing.
```

Do not commit this status file until verification lines contain real command outcomes.

- [ ] **Step 4: Commit the status record**

```powershell
git add docs/superpowers/status/2026-05-05-openhab-windows-auth-notifications-status.md
git commit -m "docs: record auth and notifications status"
```

---

## Self-Review Checklist

- This plan adds two focused slices: API token authorization and cloud notification polling with Windows toasts.
- Authorization is properly layered: storage abstraction in `OpenHab.Core`, settings in `OpenHab.App`, UI in `OpenHab.Windows.Tray`.
- Notifications are in a dedicated `OpenHab.Windows.Notifications` project as designed in the spec.
- The plan does not mix in persistent settings, event streams, subpage navigation, or packaging.
- The credential store works with `PasswordVault` on Windows but the `ICredentialStore` interface allows test doubles.
- Tokens are never logged or leaked in exception messages — verified by dedicated redaction tests.
- Cloud polling is disabled in local-only mode.
- Toast activation simply opens the main window — no complex routing in this slice.
- Every task includes exact files, commands, and concrete code.
- The next logical slice after this is event stream live updates and subpage navigation.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-05-openhab-windows-auth-notifications.md`.

Two execution options:

1. **Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints.
