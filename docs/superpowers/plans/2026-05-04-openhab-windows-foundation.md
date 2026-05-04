# openHAB Windows Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the testable foundation for a Windows native openHAB sitemap client: solution structure, endpoint routing, openHAB HTTP commands, sitemap runtime models, skin descriptors, and Windows device telemetry mapping.

**Architecture:** Start with UI-independent .NET libraries so the WinUI tray app can be planned separately on stable contracts. `OpenHab.Core` owns profiles, transport selection, HTTP calls, and device telemetry writes. `OpenHab.Sitemaps` owns sitemap-compatible models, normalization, navigation, and interaction intents. `OpenHab.Rendering` maps normalized widgets to skin-neutral render descriptors for Basic and Windows 11 skins.

**Tech Stack:** .NET 10 SDK, C# class libraries, xUnit tests, `Microsoft.NET.Test.Sdk`, `System.Net.Http.Json`, `System.Text.Json`.

---

## Scope Boundary

This plan implements the foundation vertical slice only. It intentionally does not create the WinUI tray shell, WebView2 fallback window, Windows notification platform, secure credential vault, or MSIX packaging. Those are separate implementation plans once these contracts exist.

## File Structure

- Create `OpenHab.Windows.sln`: solution file.
- Create `src/OpenHab.Core/OpenHab.Core.csproj`: core transport, profile, HTTP, telemetry library.
- Create `src/OpenHab.Core/Profiles/EndpointMode.cs`: endpoint routing mode enum.
- Create `src/OpenHab.Core/Profiles/ServerProfile.cs`: local/cloud profile model.
- Create `src/OpenHab.Core/Profiles/TransportSelection.cs`: selected transport result.
- Create `src/OpenHab.Core/Profiles/EndpointSelector.cs`: local/cloud/automatic selection logic.
- Create `src/OpenHab.Core/Api/IOpenHabClient.cs`: core openHAB client contract.
- Create `src/OpenHab.Core/Api/OpenHabHttpClient.cs`: HTTP implementation for item commands, state updates, and sitemap JSON fetches.
- Create `src/OpenHab.Core/DeviceState/DeviceStateSnapshot.cs`: raw Windows state snapshot model.
- Create `src/OpenHab.Core/DeviceState/DeviceStateMapping.cs`: configured Item names.
- Create `src/OpenHab.Core/DeviceState/DeviceStateUpdate.cs`: item/state pair to send.
- Create `src/OpenHab.Core/DeviceState/DeviceStateMapper.cs`: maps Windows snapshots to openHAB item state updates.
- Create `src/OpenHab.Sitemaps/OpenHab.Sitemaps.csproj`: sitemap runtime library.
- Create `src/OpenHab.Sitemaps/Models/SitemapModels.cs`: sitemap/page/widget/item models.
- Create `src/OpenHab.Sitemaps/Runtime/SitemapNormalizer.cs`: filters visibility and marks unsupported widgets.
- Create `src/OpenHab.Sitemaps/Runtime/SitemapNavigator.cs`: navigation stack over normalized pages.
- Create `src/OpenHab.Sitemaps/Runtime/SitemapIntent.cs`: interaction intent records.
- Create `src/OpenHab.Rendering/OpenHab.Rendering.csproj`: renderer descriptor library.
- Create `src/OpenHab.Rendering/Descriptors/RenderDescriptors.cs`: skin-neutral render tree.
- Create `src/OpenHab.Rendering/Skins/ISitemapSkin.cs`: skin contract.
- Create `src/OpenHab.Rendering/Skins/BasicSitemapSkin.cs`: Basic-style descriptor mapper.
- Create `src/OpenHab.Rendering/Skins/Windows11SitemapSkin.cs`: Windows 11 descriptor mapper.
- Create `tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj`: xUnit tests for core.
- Create `tests/OpenHab.Core.Tests/EndpointSelectorTests.cs`: endpoint mode tests.
- Create `tests/OpenHab.Core.Tests/OpenHabHttpClientTests.cs`: command/state request tests.
- Create `tests/OpenHab.Core.Tests/DeviceStateMapperTests.cs`: telemetry mapping tests.
- Create `tests/OpenHab.Sitemaps.Tests/OpenHab.Sitemaps.Tests.csproj`: xUnit tests for sitemap runtime.
- Create `tests/OpenHab.Sitemaps.Tests/SitemapNormalizerTests.cs`: visibility/support tests.
- Create `tests/OpenHab.Sitemaps.Tests/SitemapNavigatorTests.cs`: subpage navigation tests.
- Create `tests/OpenHab.Rendering.Tests/OpenHab.Rendering.Tests.csproj`: xUnit tests for skin descriptors.
- Create `tests/OpenHab.Rendering.Tests/SitemapSkinTests.cs`: Basic/W11 descriptor tests.
- Create `tests/TestSupport/FakeHttpMessageHandler.cs`: deterministic HTTP handler for client tests.

---

### Task 1: Scaffold Solution And Test Projects

**Files:**
- Create: `OpenHab.Windows.sln`
- Create: `src/OpenHab.Core/OpenHab.Core.csproj`
- Create: `src/OpenHab.Sitemaps/OpenHab.Sitemaps.csproj`
- Create: `src/OpenHab.Rendering/OpenHab.Rendering.csproj`
- Create: `tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj`
- Create: `tests/OpenHab.Sitemaps.Tests/OpenHab.Sitemaps.Tests.csproj`
- Create: `tests/OpenHab.Rendering.Tests/OpenHab.Rendering.Tests.csproj`

- [ ] **Step 1: Create the solution and projects**

Run:

```powershell
dotnet new sln -n OpenHab.Windows
dotnet new classlib -n OpenHab.Core -o src/OpenHab.Core --framework net10.0
dotnet new classlib -n OpenHab.Sitemaps -o src/OpenHab.Sitemaps --framework net10.0
dotnet new classlib -n OpenHab.Rendering -o src/OpenHab.Rendering --framework net10.0
dotnet new xunit -n OpenHab.Core.Tests -o tests/OpenHab.Core.Tests --framework net10.0
dotnet new xunit -n OpenHab.Sitemaps.Tests -o tests/OpenHab.Sitemaps.Tests --framework net10.0
dotnet new xunit -n OpenHab.Rendering.Tests -o tests/OpenHab.Rendering.Tests --framework net10.0
dotnet sln OpenHab.Windows.sln add src/OpenHab.Core/OpenHab.Core.csproj
dotnet sln OpenHab.Windows.sln add src/OpenHab.Sitemaps/OpenHab.Sitemaps.csproj
dotnet sln OpenHab.Windows.sln add src/OpenHab.Rendering/OpenHab.Rendering.csproj
dotnet sln OpenHab.Windows.sln add tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj
dotnet sln OpenHab.Windows.sln add tests/OpenHab.Sitemaps.Tests/OpenHab.Sitemaps.Tests.csproj
dotnet sln OpenHab.Windows.sln add tests/OpenHab.Rendering.Tests/OpenHab.Rendering.Tests.csproj
dotnet add tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj reference src/OpenHab.Core/OpenHab.Core.csproj
dotnet add tests/OpenHab.Sitemaps.Tests/OpenHab.Sitemaps.Tests.csproj reference src/OpenHab.Sitemaps/OpenHab.Sitemaps.csproj
dotnet add src/OpenHab.Rendering/OpenHab.Rendering.csproj reference src/OpenHab.Sitemaps/OpenHab.Sitemaps.csproj
dotnet add tests/OpenHab.Rendering.Tests/OpenHab.Rendering.Tests.csproj reference src/OpenHab.Rendering/OpenHab.Rendering.csproj
dotnet add tests/OpenHab.Rendering.Tests/OpenHab.Rendering.Tests.csproj reference src/OpenHab.Sitemaps/OpenHab.Sitemaps.csproj
```

Expected: all commands exit with code `0`.

- [ ] **Step 2: Remove template classes**

Delete:

```text
src/OpenHab.Core/Class1.cs
src/OpenHab.Sitemaps/Class1.cs
src/OpenHab.Rendering/Class1.cs
tests/OpenHab.Core.Tests/UnitTest1.cs
tests/OpenHab.Sitemaps.Tests/UnitTest1.cs
tests/OpenHab.Rendering.Tests/UnitTest1.cs
```

- [ ] **Step 3: Run the empty test suite**

Run:

```powershell
dotnet test OpenHab.Windows.sln
```

Expected: `Passed!` with three test projects discovered and zero or template tests after deletion.

- [ ] **Step 4: Commit scaffold**

Run:

```powershell
git add OpenHab.Windows.sln src tests
git commit -m "chore: scaffold foundation projects"
```

---

### Task 2: Endpoint Profile And Transport Selection

**Files:**
- Create: `src/OpenHab.Core/Profiles/EndpointMode.cs`
- Create: `src/OpenHab.Core/Profiles/ServerProfile.cs`
- Create: `src/OpenHab.Core/Profiles/TransportSelection.cs`
- Create: `src/OpenHab.Core/Profiles/EndpointSelector.cs`
- Test: `tests/OpenHab.Core.Tests/EndpointSelectorTests.cs`

- [ ] **Step 1: Write failing endpoint selector tests**

Create `tests/OpenHab.Core.Tests/EndpointSelectorTests.cs`:

```csharp
using OpenHab.Core.Profiles;

namespace OpenHab.Core.Tests;

public sealed class EndpointSelectorTests
{
    [Fact]
    public void LocalOnlyUsesLocalEndpoint()
    {
        var profile = new ServerProfile("home", new Uri("http://openhab:8080"), new Uri("https://myopenhab.org"), EndpointMode.LocalOnly);

        var result = EndpointSelector.Select(profile, localReachable: false);

        Assert.Equal(TransportKind.Local, result.Kind);
        Assert.Equal(new Uri("http://openhab:8080"), result.BaseUri);
    }

    [Fact]
    public void CloudOnlyUsesCloudEndpoint()
    {
        var profile = new ServerProfile("home", new Uri("http://openhab:8080"), new Uri("https://myopenhab.org"), EndpointMode.CloudOnly);

        var result = EndpointSelector.Select(profile, localReachable: true);

        Assert.Equal(TransportKind.Cloud, result.Kind);
        Assert.Equal(new Uri("https://myopenhab.org"), result.BaseUri);
    }

    [Fact]
    public void AutomaticPrefersLocalWhenReachable()
    {
        var profile = new ServerProfile("home", new Uri("http://openhab:8080"), new Uri("https://myopenhab.org"), EndpointMode.Automatic);

        var result = EndpointSelector.Select(profile, localReachable: true);

        Assert.Equal(TransportKind.Local, result.Kind);
    }

    [Fact]
    public void AutomaticFallsBackToCloudWhenLocalIsNotReachable()
    {
        var profile = new ServerProfile("home", new Uri("http://openhab:8080"), new Uri("https://myopenhab.org"), EndpointMode.Automatic);

        var result = EndpointSelector.Select(profile, localReachable: false);

        Assert.Equal(TransportKind.Cloud, result.Kind);
    }

    [Fact]
    public void CloudOnlyRequiresCloudEndpoint()
    {
        var profile = new ServerProfile("home", new Uri("http://openhab:8080"), cloudEndpoint: null, EndpointMode.CloudOnly);

        var error = Assert.Throws<InvalidOperationException>(() => EndpointSelector.Select(profile, localReachable: true));

        Assert.Equal("Profile 'home' is CloudOnly but has no cloud endpoint.", error.Message);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj --filter EndpointSelectorTests
```

Expected: compile failure because `OpenHab.Core.Profiles` types do not exist.

- [ ] **Step 3: Add endpoint model implementation**

Create `src/OpenHab.Core/Profiles/EndpointMode.cs`:

```csharp
namespace OpenHab.Core.Profiles;

public enum EndpointMode
{
    Automatic,
    LocalOnly,
    CloudOnly
}
```

Create `src/OpenHab.Core/Profiles/ServerProfile.cs`:

```csharp
namespace OpenHab.Core.Profiles;

public sealed record ServerProfile(
    string Name,
    Uri? LocalEndpoint,
    Uri? CloudEndpoint,
    EndpointMode EndpointMode);
```

Create `src/OpenHab.Core/Profiles/TransportSelection.cs`:

```csharp
namespace OpenHab.Core.Profiles;

public enum TransportKind
{
    Local,
    Cloud
}

public sealed record TransportSelection(TransportKind Kind, Uri BaseUri);
```

Create `src/OpenHab.Core/Profiles/EndpointSelector.cs`:

```csharp
namespace OpenHab.Core.Profiles;

public static class EndpointSelector
{
    public static TransportSelection Select(ServerProfile profile, bool localReachable)
    {
        return profile.EndpointMode switch
        {
            EndpointMode.LocalOnly => SelectRequired(profile.Name, TransportKind.Local, profile.LocalEndpoint, "LocalOnly", "local"),
            EndpointMode.CloudOnly => SelectRequired(profile.Name, TransportKind.Cloud, profile.CloudEndpoint, "CloudOnly", "cloud"),
            EndpointMode.Automatic when localReachable && profile.LocalEndpoint is not null => new TransportSelection(TransportKind.Local, profile.LocalEndpoint),
            EndpointMode.Automatic => SelectRequired(profile.Name, TransportKind.Cloud, profile.CloudEndpoint, "Automatic", "cloud"),
            _ => throw new InvalidOperationException($"Profile '{profile.Name}' has unsupported endpoint mode '{profile.EndpointMode}'.")
        };
    }

    private static TransportSelection SelectRequired(string profileName, TransportKind kind, Uri? endpoint, string mode, string endpointName)
    {
        if (endpoint is null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' is {mode} but has no {endpointName} endpoint.");
        }

        return new TransportSelection(kind, endpoint);
    }
}
```

- [ ] **Step 4: Run endpoint tests**

Run:

```powershell
dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj --filter EndpointSelectorTests
```

Expected: `Passed!`.

- [ ] **Step 5: Commit endpoint selection**

Run:

```powershell
git add src/OpenHab.Core/Profiles tests/OpenHab.Core.Tests/EndpointSelectorTests.cs
git commit -m "feat: add endpoint transport selection"
```

---

### Task 3: openHAB HTTP Client Commands And State Updates

**Files:**
- Create: `src/OpenHab.Core/Api/IOpenHabClient.cs`
- Create: `src/OpenHab.Core/Api/OpenHabHttpClient.cs`
- Create: `tests/TestSupport/FakeHttpMessageHandler.cs`
- Test: `tests/OpenHab.Core.Tests/OpenHabHttpClientTests.cs`

- [ ] **Step 1: Write failing HTTP client tests**

Create `tests/TestSupport/FakeHttpMessageHandler.cs`:

```csharp
using System.Net;

namespace TestSupport;

public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    public void Enqueue(HttpStatusCode statusCode, string body = "")
    {
        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body)
        });
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_responses.Count == 0
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : _responses.Dequeue());
    }
}
```

Create `tests/OpenHab.Core.Tests/OpenHabHttpClientTests.cs`:

```csharp
using System.Net;
using OpenHab.Core.Api;
using TestSupport;

namespace OpenHab.Core.Tests;

public sealed class OpenHabHttpClientTests
{
    [Fact]
    public async Task SendCommandPostsPlainTextToItemEndpoint()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK);
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        await client.SendCommandAsync("LivingRoom_Light", "ON", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://openhab:8080/rest/items/LivingRoom_Light", request.RequestUri!.ToString());
        Assert.Equal("ON", await request.Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task SetItemStatePutsPlainTextToStateEndpoint()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK);
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("https://myopenhab.org"));

        await client.SetItemStateAsync("PcLockedState", "ON", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("https://myopenhab.org/rest/items/PcLockedState/state", request.RequestUri!.ToString());
        Assert.Equal("ON", await request.Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetSitemapJsonUsesSitemapEndpoint()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"name":"home"}""");
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var json = await client.GetSitemapJsonAsync("home", CancellationToken.None);

        Assert.Equal("""{"name":"home"}""", json);
        Assert.Equal("http://openhab:8080/rest/sitemaps/home", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task FailedCommandThrowsRedactedOpenHabRequestException()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, "bad token");
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("https://token:secret@myopenhab.org"));

        var error = await Assert.ThrowsAsync<OpenHabRequestException>(() => client.SendCommandAsync("Light", "OFF", CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.DoesNotContain("secret", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj --filter OpenHabHttpClientTests
```

Expected: compile failure because `OpenHabHttpClient` and `OpenHabRequestException` do not exist.

- [ ] **Step 3: Add HTTP client implementation**

Create `src/OpenHab.Core/Api/IOpenHabClient.cs`:

```csharp
namespace OpenHab.Core.Api;

public interface IOpenHabClient
{
    Task SendCommandAsync(string itemName, string command, CancellationToken cancellationToken);
    Task SetItemStateAsync(string itemName, string state, CancellationToken cancellationToken);
    Task<string> GetSitemapJsonAsync(string sitemapName, CancellationToken cancellationToken);
}
```

Create `src/OpenHab.Core/Api/OpenHabHttpClient.cs`:

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

    public OpenHabHttpClient(HttpClient httpClient, Uri baseUri)
    {
        _httpClient = httpClient;
        _baseUri = baseUri;
    }

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
        using var response = await _httpClient.GetAsync(BuildUri($"rest/sitemaps/{Uri.EscapeDataString(sitemapName)}"), cancellationToken);
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

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await ThrowIfFailedAsync(response, cancellationToken);
    }

    private Uri BuildUri(string relativePath)
    {
        return new Uri(_baseUri, relativePath);
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var safeBody = body.Length > 120 ? body[..120] : body;
        throw new OpenHabRequestException(response.StatusCode, $"openHAB request failed with {(int)response.StatusCode} {response.ReasonPhrase}: {safeBody}");
    }
}
```

- [ ] **Step 4: Run HTTP client tests**

Run:

```powershell
dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj --filter OpenHabHttpClientTests
```

Expected: `Passed!`.

- [ ] **Step 5: Commit HTTP client**

Run:

```powershell
git add src/OpenHab.Core/Api tests/TestSupport tests/OpenHab.Core.Tests/OpenHabHttpClientTests.cs
git commit -m "feat: add openHAB HTTP client"
```

---

### Task 4: Sitemap Models And Normalization

**Files:**
- Create: `src/OpenHab.Sitemaps/Models/SitemapModels.cs`
- Create: `src/OpenHab.Sitemaps/Runtime/SitemapNormalizer.cs`
- Test: `tests/OpenHab.Sitemaps.Tests/SitemapNormalizerTests.cs`

- [ ] **Step 1: Write failing normalizer tests**

Create `tests/OpenHab.Sitemaps.Tests/SitemapNormalizerTests.cs`:

```csharp
using OpenHab.Sitemaps.Models;
using OpenHab.Sitemaps.Runtime;

namespace OpenHab.Sitemaps.Tests;

public sealed class SitemapNormalizerTests
{
    [Fact]
    public void RemovesInvisibleWidgets()
    {
        var page = new SitemapPage("root", "Home", [
            new SitemapWidget("Light", SitemapWidgetType.Switch, "Light", "ON", [], true, []),
            new SitemapWidget("Hidden", SitemapWidgetType.Text, "Hidden", "OFF", [], false, [])
        ]);

        var normalized = SitemapNormalizer.Normalize(page);

        Assert.Single(normalized.Widgets);
        Assert.Equal("Light", normalized.Widgets[0].Label);
    }

    [Fact]
    public void MarksUnsupportedWidgetsWithFallback()
    {
        var page = new SitemapPage("root", "Home", [
            new SitemapWidget("Camera", SitemapWidgetType.Video, "FrontCamera", "", [], true, [])
        ]);

        var normalized = SitemapNormalizer.Normalize(page);

        Assert.True(normalized.Widgets[0].RequiresFallback);
        Assert.Equal(SitemapFallbackKind.MainUiOrBrowser, normalized.Widgets[0].FallbackKind);
    }

    [Fact]
    public void PreservesMappingsForSwitchAndSelectionWidgets()
    {
        var page = new SitemapPage("root", "Home", [
            new SitemapWidget("Mode", SitemapWidgetType.Selection, "Mode", "AUTO", [
                new SitemapMapping("AUTO", "Auto"),
                new SitemapMapping("MANUAL", "Manual")
            ], true, [])
        ]);

        var normalized = SitemapNormalizer.Normalize(page);

        Assert.Equal(["AUTO", "MANUAL"], normalized.Widgets[0].Mappings.Select(mapping => mapping.Command).ToArray());
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test tests/OpenHab.Sitemaps.Tests/OpenHab.Sitemaps.Tests.csproj --filter SitemapNormalizerTests
```

Expected: compile failure because sitemap models do not exist.

- [ ] **Step 3: Add sitemap models**

Create `src/OpenHab.Sitemaps/Models/SitemapModels.cs`:

```csharp
namespace OpenHab.Sitemaps.Models;

public sealed record SitemapPage(string Id, string Label, IReadOnlyList<SitemapWidget> Widgets);

public sealed record SitemapWidget(
    string Label,
    SitemapWidgetType Type,
    string? ItemName,
    string? State,
    IReadOnlyList<SitemapMapping> Mappings,
    bool IsVisible,
    IReadOnlyList<SitemapPage> Children);

public sealed record NormalizedSitemapPage(string Id, string Label, IReadOnlyList<NormalizedSitemapWidget> Widgets);

public sealed record NormalizedSitemapWidget(
    string Label,
    SitemapWidgetType Type,
    string? ItemName,
    string? State,
    IReadOnlyList<SitemapMapping> Mappings,
    bool CanNavigate,
    bool RequiresFallback,
    SitemapFallbackKind FallbackKind,
    IReadOnlyList<SitemapPage> Children);

public sealed record SitemapMapping(string Command, string Label);

public enum SitemapWidgetType
{
    Default,
    Text,
    Group,
    Switch,
    Selection,
    Setpoint,
    Slider,
    Colorpicker,
    Colortemperaturepicker,
    Input,
    Buttongrid,
    Button,
    Webview,
    Mapview,
    Image,
    Video,
    Chart
}

public enum SitemapFallbackKind
{
    None,
    MainUiOrBrowser
}
```

Create `src/OpenHab.Sitemaps/Runtime/SitemapNormalizer.cs`:

```csharp
using OpenHab.Sitemaps.Models;

namespace OpenHab.Sitemaps.Runtime;

public static class SitemapNormalizer
{
    private static readonly HashSet<SitemapWidgetType> NativeTypes =
    [
        SitemapWidgetType.Default,
        SitemapWidgetType.Text,
        SitemapWidgetType.Group,
        SitemapWidgetType.Switch,
        SitemapWidgetType.Selection,
        SitemapWidgetType.Setpoint,
        SitemapWidgetType.Slider,
        SitemapWidgetType.Colorpicker,
        SitemapWidgetType.Colortemperaturepicker,
        SitemapWidgetType.Input,
        SitemapWidgetType.Buttongrid,
        SitemapWidgetType.Button,
        SitemapWidgetType.Image
    ];

    public static NormalizedSitemapPage Normalize(SitemapPage page)
    {
        var widgets = page.Widgets
            .Where(widget => widget.IsVisible)
            .Select(NormalizeWidget)
            .ToArray();

        return new NormalizedSitemapPage(page.Id, page.Label, widgets);
    }

    private static NormalizedSitemapWidget NormalizeWidget(SitemapWidget widget)
    {
        var supported = NativeTypes.Contains(widget.Type);
        return new NormalizedSitemapWidget(
            widget.Label,
            widget.Type,
            widget.ItemName,
            widget.State,
            widget.Mappings,
            widget.Children.Count > 0,
            !supported,
            supported ? SitemapFallbackKind.None : SitemapFallbackKind.MainUiOrBrowser,
            widget.Children);
    }
}
```

- [ ] **Step 4: Run normalizer tests**

Run:

```powershell
dotnet test tests/OpenHab.Sitemaps.Tests/OpenHab.Sitemaps.Tests.csproj --filter SitemapNormalizerTests
```

Expected: `Passed!`.

- [ ] **Step 5: Commit sitemap models**

Run:

```powershell
git add src/OpenHab.Sitemaps tests/OpenHab.Sitemaps.Tests/SitemapNormalizerTests.cs
git commit -m "feat: add sitemap normalization models"
```

---

### Task 5: Sitemap Navigation And Intents

**Files:**
- Create: `src/OpenHab.Sitemaps/Runtime/SitemapIntent.cs`
- Create: `src/OpenHab.Sitemaps/Runtime/SitemapNavigator.cs`
- Test: `tests/OpenHab.Sitemaps.Tests/SitemapNavigatorTests.cs`

- [ ] **Step 1: Write failing navigator tests**

Create `tests/OpenHab.Sitemaps.Tests/SitemapNavigatorTests.cs`:

```csharp
using OpenHab.Sitemaps.Models;
using OpenHab.Sitemaps.Runtime;

namespace OpenHab.Sitemaps.Tests;

public sealed class SitemapNavigatorTests
{
    [Fact]
    public void NavigateToChildPushesChildPage()
    {
        var child = new SitemapPage("living", "Living Room", []);
        var root = new SitemapPage("root", "Home", [
            new SitemapWidget("Living Room", SitemapWidgetType.Text, null, null, [], true, [child])
        ]);
        var navigator = new SitemapNavigator(root);

        var intent = navigator.ActivateWidget(0);

        Assert.Equal(new NavigateIntent("living"), intent);
        Assert.Equal("Living Room", navigator.CurrentPage.Label);
    }

    [Fact]
    public void BackReturnsToPreviousPage()
    {
        var child = new SitemapPage("living", "Living Room", []);
        var root = new SitemapPage("root", "Home", [
            new SitemapWidget("Living Room", SitemapWidgetType.Text, null, null, [], true, [child])
        ]);
        var navigator = new SitemapNavigator(root);

        navigator.ActivateWidget(0);
        var moved = navigator.Back();

        Assert.True(moved);
        Assert.Equal("Home", navigator.CurrentPage.Label);
    }

    [Fact]
    public void SwitchWidgetCreatesSendCommandIntent()
    {
        var root = new SitemapPage("root", "Home", [
            new SitemapWidget("Light", SitemapWidgetType.Switch, "Light", "OFF", [], true, [])
        ]);
        var navigator = new SitemapNavigator(root);

        var intent = navigator.ActivateWidget(0);

        Assert.Equal(new SendCommandIntent("Light", "ON"), intent);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test tests/OpenHab.Sitemaps.Tests/OpenHab.Sitemaps.Tests.csproj --filter SitemapNavigatorTests
```

Expected: compile failure because `SitemapNavigator` and intent records do not exist.

- [ ] **Step 3: Add intent and navigator implementation**

Create `src/OpenHab.Sitemaps/Runtime/SitemapIntent.cs`:

```csharp
namespace OpenHab.Sitemaps.Runtime;

public abstract record SitemapIntent;

public sealed record SendCommandIntent(string ItemName, string Command) : SitemapIntent;

public sealed record NavigateIntent(string PageId) : SitemapIntent;

public sealed record OpenFallbackIntent(string PageOrWidgetLabel) : SitemapIntent;

public sealed record NoOpIntent : SitemapIntent;
```

Create `src/OpenHab.Sitemaps/Runtime/SitemapNavigator.cs`:

```csharp
using OpenHab.Sitemaps.Models;

namespace OpenHab.Sitemaps.Runtime;

public sealed class SitemapNavigator
{
    private readonly Stack<SitemapPage> _backStack = new();

    public SitemapNavigator(SitemapPage rootPage)
    {
        CurrentPage = rootPage;
    }

    public SitemapPage CurrentPage { get; private set; }

    public SitemapIntent ActivateWidget(int widgetIndex)
    {
        var widget = CurrentPage.Widgets[widgetIndex];
        if (widget.Children.Count > 0)
        {
            _backStack.Push(CurrentPage);
            CurrentPage = widget.Children[0];
            return new NavigateIntent(CurrentPage.Id);
        }

        if (widget.Type == SitemapWidgetType.Switch && widget.ItemName is not null)
        {
            var command = string.Equals(widget.State, "ON", StringComparison.OrdinalIgnoreCase) ? "OFF" : "ON";
            return new SendCommandIntent(widget.ItemName, command);
        }

        if (IsFallbackWidget(widget.Type))
        {
            return new OpenFallbackIntent(widget.Label);
        }

        return new NoOpIntent();
    }

    public bool Back()
    {
        if (_backStack.Count == 0)
        {
            return false;
        }

        CurrentPage = _backStack.Pop();
        return true;
    }

    private static bool IsFallbackWidget(SitemapWidgetType type)
    {
        return type is SitemapWidgetType.Webview or SitemapWidgetType.Mapview or SitemapWidgetType.Video or SitemapWidgetType.Chart;
    }
}
```

- [ ] **Step 4: Run navigator tests**

Run:

```powershell
dotnet test tests/OpenHab.Sitemaps.Tests/OpenHab.Sitemaps.Tests.csproj --filter SitemapNavigatorTests
```

Expected: `Passed!`.

- [ ] **Step 5: Commit navigator**

Run:

```powershell
git add src/OpenHab.Sitemaps/Runtime/SitemapIntent.cs src/OpenHab.Sitemaps/Runtime/SitemapNavigator.cs tests/OpenHab.Sitemaps.Tests/SitemapNavigatorTests.cs
git commit -m "feat: add sitemap navigation intents"
```

---

### Task 6: Skin Descriptor Contract

**Files:**
- Create: `src/OpenHab.Rendering/Descriptors/RenderDescriptors.cs`
- Create: `src/OpenHab.Rendering/Skins/ISitemapSkin.cs`
- Create: `src/OpenHab.Rendering/Skins/BasicSitemapSkin.cs`
- Create: `src/OpenHab.Rendering/Skins/Windows11SitemapSkin.cs`
- Test: `tests/OpenHab.Rendering.Tests/SitemapSkinTests.cs`

- [ ] **Step 1: Write failing skin tests**

Create `tests/OpenHab.Rendering.Tests/SitemapSkinTests.cs`:

```csharp
using OpenHab.Rendering.Descriptors;
using OpenHab.Rendering.Skins;
using OpenHab.Sitemaps.Models;

namespace OpenHab.Rendering.Tests;

public sealed class SitemapSkinTests
{
    [Fact]
    public void BasicSkinKeepsRowsCompact()
    {
        var page = Page();
        var skin = new BasicSitemapSkin();

        var descriptor = skin.Render(page);

        Assert.Equal(SitemapSkinKind.Basic, descriptor.Skin);
        Assert.All(descriptor.Rows, row => Assert.Equal(RenderDensity.Compact, row.Density));
    }

    [Fact]
    public void Windows11SkinUsesComfortableRows()
    {
        var page = Page();
        var skin = new Windows11SitemapSkin();

        var descriptor = skin.Render(page);

        Assert.Equal(SitemapSkinKind.Windows11, descriptor.Skin);
        Assert.All(descriptor.Rows, row => Assert.Equal(RenderDensity.Comfortable, row.Density));
    }

    [Fact]
    public void SkinDescriptorsExposeFallbackAction()
    {
        var page = new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget("Camera", SitemapWidgetType.Video, "Camera", "", [], false, true, SitemapFallbackKind.MainUiOrBrowser, [])
        ]);

        var descriptor = new Windows11SitemapSkin().Render(page);

        Assert.Equal(RenderActionKind.OpenFallback, descriptor.Rows[0].Action);
    }

    private static NormalizedSitemapPage Page()
    {
        return new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget("Light", SitemapWidgetType.Switch, "Light", "ON", [], false, false, SitemapFallbackKind.None, [])
        ]);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test tests/OpenHab.Rendering.Tests/OpenHab.Rendering.Tests.csproj --filter SitemapSkinTests
```

Expected: compile failure because rendering descriptors and skins do not exist.

- [ ] **Step 3: Add render descriptors and skins**

Create `src/OpenHab.Rendering/Descriptors/RenderDescriptors.cs`:

```csharp
namespace OpenHab.Rendering.Descriptors;

public sealed record SitemapRenderDescriptor(
    SitemapSkinKind Skin,
    string PageId,
    string Title,
    IReadOnlyList<SitemapRowDescriptor> Rows);

public sealed record SitemapRowDescriptor(
    string Label,
    string? State,
    RenderControlKind Control,
    RenderActionKind Action,
    RenderDensity Density);

public enum SitemapSkinKind
{
    Basic,
    Windows11
}

public enum RenderControlKind
{
    Text,
    Toggle,
    Slider,
    Selection,
    Fallback
}

public enum RenderActionKind
{
    None,
    SendCommand,
    Navigate,
    OpenFallback
}

public enum RenderDensity
{
    Compact,
    Comfortable
}
```

Create `src/OpenHab.Rendering/Skins/ISitemapSkin.cs`:

```csharp
using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;

namespace OpenHab.Rendering.Skins;

public interface ISitemapSkin
{
    SitemapRenderDescriptor Render(NormalizedSitemapPage page);
}
```

Create `src/OpenHab.Rendering/Skins/BasicSitemapSkin.cs`:

```csharp
using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;

namespace OpenHab.Rendering.Skins;

public sealed class BasicSitemapSkin : ISitemapSkin
{
    public SitemapRenderDescriptor Render(NormalizedSitemapPage page)
    {
        return new SitemapRenderDescriptor(SitemapSkinKind.Basic, page.Id, page.Label, page.Widgets.Select(ToRow).ToArray());
    }

    private static SitemapRowDescriptor ToRow(NormalizedSitemapWidget widget)
    {
        return new SitemapRowDescriptor(widget.Label, widget.State, ControlFor(widget), ActionFor(widget), RenderDensity.Compact);
    }

    private static RenderControlKind ControlFor(NormalizedSitemapWidget widget)
    {
        if (widget.RequiresFallback)
        {
            return RenderControlKind.Fallback;
        }

        return widget.Type switch
        {
            SitemapWidgetType.Switch => RenderControlKind.Toggle,
            SitemapWidgetType.Slider or SitemapWidgetType.Setpoint => RenderControlKind.Slider,
            SitemapWidgetType.Selection => RenderControlKind.Selection,
            _ => RenderControlKind.Text
        };
    }

    private static RenderActionKind ActionFor(NormalizedSitemapWidget widget)
    {
        if (widget.RequiresFallback)
        {
            return RenderActionKind.OpenFallback;
        }

        if (widget.CanNavigate)
        {
            return RenderActionKind.Navigate;
        }

        return widget.Type is SitemapWidgetType.Switch or SitemapWidgetType.Slider or SitemapWidgetType.Selection
            ? RenderActionKind.SendCommand
            : RenderActionKind.None;
    }
}
```

Create `src/OpenHab.Rendering/Skins/Windows11SitemapSkin.cs`:

```csharp
using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;

namespace OpenHab.Rendering.Skins;

public sealed class Windows11SitemapSkin : ISitemapSkin
{
    public SitemapRenderDescriptor Render(NormalizedSitemapPage page)
    {
        var basicRows = page.Widgets.Select(ToRow).ToArray();
        return new SitemapRenderDescriptor(SitemapSkinKind.Windows11, page.Id, page.Label, basicRows);
    }

    private static SitemapRowDescriptor ToRow(NormalizedSitemapWidget widget)
    {
        if (widget.RequiresFallback)
        {
            return new SitemapRowDescriptor(widget.Label, widget.State, RenderControlKind.Fallback, RenderActionKind.OpenFallback, RenderDensity.Comfortable);
        }

        var control = widget.Type switch
        {
            SitemapWidgetType.Switch => RenderControlKind.Toggle,
            SitemapWidgetType.Slider or SitemapWidgetType.Setpoint => RenderControlKind.Slider,
            SitemapWidgetType.Selection => RenderControlKind.Selection,
            _ => RenderControlKind.Text
        };

        var action = widget.CanNavigate
            ? RenderActionKind.Navigate
            : control is RenderControlKind.Toggle or RenderControlKind.Slider or RenderControlKind.Selection
                ? RenderActionKind.SendCommand
                : RenderActionKind.None;

        return new SitemapRowDescriptor(widget.Label, widget.State, control, action, RenderDensity.Comfortable);
    }
}
```

- [ ] **Step 4: Run skin tests**

Run:

```powershell
dotnet test tests/OpenHab.Rendering.Tests/OpenHab.Rendering.Tests.csproj --filter SitemapSkinTests
```

Expected: `Passed!`.

- [ ] **Step 5: Commit skin contract**

Run:

```powershell
git add src/OpenHab.Rendering tests/OpenHab.Rendering.Tests/SitemapSkinTests.cs
git commit -m "feat: add sitemap skin descriptors"
```

---

### Task 7: Device State Telemetry Mapping

**Files:**
- Create: `src/OpenHab.Core/DeviceState/DeviceStateSnapshot.cs`
- Create: `src/OpenHab.Core/DeviceState/DeviceStateMapping.cs`
- Create: `src/OpenHab.Core/DeviceState/DeviceStateUpdate.cs`
- Create: `src/OpenHab.Core/DeviceState/DeviceStateMapper.cs`
- Test: `tests/OpenHab.Core.Tests/DeviceStateMapperTests.cs`

- [ ] **Step 1: Write failing telemetry mapping tests**

Create `tests/OpenHab.Core.Tests/DeviceStateMapperTests.cs`:

```csharp
using OpenHab.Core.DeviceState;

namespace OpenHab.Core.Tests;

public sealed class DeviceStateMapperTests
{
    [Fact]
    public void MapsBatteryChargingLockAndSessionState()
    {
        var mapping = new DeviceStateMapping("PcBatteryLevel", "PcChargingState", "PcLockedState", "PcSessionState");
        var snapshot = new DeviceStateSnapshot(87, true, true, "locked");

        var updates = DeviceStateMapper.Map(snapshot, mapping);

        Assert.Equal([
            new DeviceStateUpdate("PcBatteryLevel", "87"),
            new DeviceStateUpdate("PcChargingState", "ON"),
            new DeviceStateUpdate("PcLockedState", "ON"),
            new DeviceStateUpdate("PcSessionState", "locked")
        ], updates);
    }

    [Fact]
    public void OmitsUnmappedTelemetryItems()
    {
        var mapping = new DeviceStateMapping(BatteryLevelItem: null, ChargingStateItem: null, LockedStateItem: "PcLockedState", SessionStateItem: null);
        var snapshot = new DeviceStateSnapshot(50, false, false, "active");

        var updates = DeviceStateMapper.Map(snapshot, mapping);

        Assert.Equal([new DeviceStateUpdate("PcLockedState", "OFF")], updates);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj --filter DeviceStateMapperTests
```

Expected: compile failure because device state types do not exist.

- [ ] **Step 3: Add telemetry mapping implementation**

Create `src/OpenHab.Core/DeviceState/DeviceStateSnapshot.cs`:

```csharp
namespace OpenHab.Core.DeviceState;

public sealed record DeviceStateSnapshot(
    int? BatteryLevelPercent,
    bool? IsCharging,
    bool? IsLocked,
    string? SessionState);
```

Create `src/OpenHab.Core/DeviceState/DeviceStateMapping.cs`:

```csharp
namespace OpenHab.Core.DeviceState;

public sealed record DeviceStateMapping(
    string? BatteryLevelItem,
    string? ChargingStateItem,
    string? LockedStateItem,
    string? SessionStateItem);
```

Create `src/OpenHab.Core/DeviceState/DeviceStateUpdate.cs`:

```csharp
namespace OpenHab.Core.DeviceState;

public sealed record DeviceStateUpdate(string ItemName, string State);
```

Create `src/OpenHab.Core/DeviceState/DeviceStateMapper.cs`:

```csharp
namespace OpenHab.Core.DeviceState;

public static class DeviceStateMapper
{
    public static IReadOnlyList<DeviceStateUpdate> Map(DeviceStateSnapshot snapshot, DeviceStateMapping mapping)
    {
        var updates = new List<DeviceStateUpdate>();

        if (mapping.BatteryLevelItem is not null && snapshot.BatteryLevelPercent is not null)
        {
            updates.Add(new DeviceStateUpdate(mapping.BatteryLevelItem, snapshot.BatteryLevelPercent.Value.ToString()));
        }

        if (mapping.ChargingStateItem is not null && snapshot.IsCharging is not null)
        {
            updates.Add(new DeviceStateUpdate(mapping.ChargingStateItem, snapshot.IsCharging.Value ? "ON" : "OFF"));
        }

        if (mapping.LockedStateItem is not null && snapshot.IsLocked is not null)
        {
            updates.Add(new DeviceStateUpdate(mapping.LockedStateItem, snapshot.IsLocked.Value ? "ON" : "OFF"));
        }

        if (mapping.SessionStateItem is not null && snapshot.SessionState is not null)
        {
            updates.Add(new DeviceStateUpdate(mapping.SessionStateItem, snapshot.SessionState));
        }

        return updates;
    }
}
```

- [ ] **Step 4: Run telemetry mapping tests**

Run:

```powershell
dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj --filter DeviceStateMapperTests
```

Expected: `Passed!`.

- [ ] **Step 5: Commit telemetry mapping**

Run:

```powershell
git add src/OpenHab.Core/DeviceState tests/OpenHab.Core.Tests/DeviceStateMapperTests.cs
git commit -m "feat: map Windows device state to openHAB items"
```

---

### Task 8: Full Foundation Verification

**Files:**
- Modify: no source changes expected.
- Test: all test projects.

- [ ] **Step 1: Run the full test suite**

Run:

```powershell
dotnet test OpenHab.Windows.sln
```

Expected: all test projects pass.

- [ ] **Step 2: Build the solution in Release**

Run:

```powershell
dotnet build OpenHab.Windows.sln --configuration Release
```

Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 3: Inspect git status**

Run:

```powershell
git status --short
```

Expected: no tracked foundation files are modified. Existing untracked `.docs/` may remain from the design references and should not be included unless the user explicitly requests it.

- [ ] **Step 4: Commit any verification-only fixes**

If Step 1 or Step 2 required a small compile/test fix, commit only those files:

```powershell
git add src tests OpenHab.Windows.sln
git commit -m "test: verify foundation solution"
```

Expected: a commit is created only if fixes were needed. If no files changed, skip this commit.

---

## Plan Self-Review

Spec coverage:

- Native sitemap foundation: covered by Tasks 4, 5, and 6.
- Basic and Windows 11 skins: covered by Task 6.
- Local/cloud/automatic connection mode: covered by Task 2.
- Item commands and state updates: covered by Task 3.
- Device state reporting: covered by Task 7.
- Cached/offline UI, WinUI tray, WebView fallback, notifications, secure credential storage, and packaging: intentionally deferred to follow-up plans after this foundation exists.

Red-flag scan:

- No task contains unfinished-marker text or unspecified implementation instructions.
- Code snippets define the referenced types before later tasks use them.

Type consistency:

- `SitemapWidget`, `NormalizedSitemapWidget`, `SitemapIntent`, `OpenHabHttpClient`, and device state records use consistent names across tests and implementation steps.
