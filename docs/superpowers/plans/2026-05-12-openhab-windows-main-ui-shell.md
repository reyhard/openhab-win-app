# openHAB Windows Main UI Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Main Window default to openHAB Main UI in WebView2, add a Windows-style left rail with promoted Main UI pages/settings/notifications, and keep the native sitemap as an independently visible split pane.

**Architecture:** `OpenHab.Core` adds a REST contract for Main UI page components. `OpenHab.App` filters promoted pages, caches discovery results, and stores shell state. `OpenHab.Windows.Tray` owns WebView2 hosting, left rail navigation, settings/notification native pages, and sitemap pane composition while reusing the existing sitemap runtime/renderer.

**Tech Stack:** .NET 10, C#, WinUI 3 / Windows App SDK, WebView2, xUnit, `System.Text.Json`, existing `AppSettingsController`, `SitemapRuntimeController`, `NotificationStore`, and `SitemapSurfaceRenderer`.

---

## Scope Boundary

This plan implements the approved design in `docs/superpowers/specs/2026-05-12-openhab-windows-main-ui-shell-design.md`.

Included:

- Main Window shell layout.
- Main UI WebView2 default center surface.
- Promoted Main UI page discovery through `GET /rest/ui/components/ui:page`.
- Collapsible `Main UI Pages` left rail section.
- Settings and notifications as app-owned center pages.
- Native sitemap right split pane hidden by default and independently visible across center pages.
- WebView same-host/external-host routing rules and myopenHAB host rewrite.

Excluded:

- Tray flyout redesign.
- Native Main UI widget rendering.
- Windows Widgets.
- New notification transports.
- Full visual polish beyond working Windows 11-style layout.

## File Structure

- Modify `src/OpenHab.Core/Api/IOpenHabClient.cs`: add Main UI page component contract and method.
- Modify `src/OpenHab.Core/Api/OpenHabHttpClient.cs`: fetch `rest/ui/components/ui:page`.
- Create `src/OpenHab.Core/Ui/MainUiPageComponent.cs`: raw UI page component DTO.
- Create `src/OpenHab.App/MainUi/MainUiPageLink.cs`: app-ready navigation link.
- Create `src/OpenHab.App/MainUi/MainUiPageDiscoveryService.cs`: filters promoted pages and builds routes.
- Modify `src/OpenHab.App/Settings/AppSettings.cs`: persist shell/discovery settings.
- Modify `src/OpenHab.App/Settings/AppSettingsController.cs`: setters and normalization for shell/discovery settings.
- Create `src/OpenHab.App/MainUi/MainUiUrlBuilder.cs`: endpoint route construction and myopenHAB rewrite.
- Create `src/OpenHab.App/MainUi/MainUiNavigationRequest.cs`: navigation request model.
- Create `src/OpenHab.Windows.Tray/MainUi/MainUiWebViewHost.xaml`: WebView2 host user control.
- Create `src/OpenHab.Windows.Tray/MainUi/MainUiWebViewHost.xaml.cs`: WebView lifecycle, navigation, external browser handoff.
- Create `src/OpenHab.App/Shell/MainWindowShellState.cs`: shell selection and sitemap visibility model.
- Create `src/OpenHab.App/Shell/MainWindowShellController.cs`: pure shell state transitions.
- Create `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml`: reusable settings page content.
- Create `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`: moved settings behavior from `MainWindow`.
- Create `src/OpenHab.Windows.Tray/Notifications/NotificationsPageControl.xaml`: reusable notification page content.
- Create `src/OpenHab.Windows.Tray/Notifications/NotificationsPageControl.xaml.cs`: moved notification behavior from `MainWindow`.
- Modify `src/OpenHab.Windows.Tray/MainWindow.xaml`: replace two-column sitemap/pivot layout with left rail, center presenter, right sitemap pane.
- Modify `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`: compose shell controls, discovery, WebView navigation, sitemap visibility, settings/notifications routing.
- Modify `src/OpenHab.Windows.Tray/App.xaml.cs`: construct page discovery client and route flyout requests to shell pages.
- Create `tests/OpenHab.Core.Tests/Api/OpenHabHttpClientMainUiPageTests.cs`: Core HTTP tests.
- Create `tests/OpenHab.App.Tests/MainUi/MainUiPageDiscoveryServiceTests.cs`: promoted filtering and route tests.
- Modify `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`: shell setting persistence tests.
- Create `tests/OpenHab.App.Tests/MainUi/MainUiPageCacheTests.cs`: cache normalization tests if cache lives in settings.
- Create `tests/OpenHab.App.Tests/MainUi/MainUiUrlBuilderTests.cs`: route construction tests.
- Create `tests/OpenHab.App.Tests/Shell/MainWindowShellControllerTests.cs`: pure shell transition tests.

## Implementation Notes

- The REST endpoint for Main UI page components is `rest/ui/components/ui:page`. Community evidence and openHAB UI component docs show this endpoint returns page components and individual pages live under `rest/ui/components/ui:page/{uid}`.
- Treat a page as promoted when its `config.sidebar` value is JSON boolean `true` or string `"true"`.
- Build promoted page routes as `/page/{escapedUid}`.
- Sort promoted page links by numeric `config.order` when present, then by label, then UID.
- Preserve `MainWindow.xaml.cs` behavior while extracting; do not rewrite unrelated settings/notification logic.
- Do not delete `.superpowers/` or unrelated untracked packaging files.

---

### Task 1: Add Core Main UI Page Component Fetching

**Files:**
- Modify: `src/OpenHab.Core/Api/IOpenHabClient.cs`
- Modify: `src/OpenHab.Core/Api/OpenHabHttpClient.cs`
- Create: `src/OpenHab.Core/Ui/MainUiPageComponent.cs`
- Create: `tests/OpenHab.Core.Tests/Api/OpenHabHttpClientMainUiPageTests.cs`

- [ ] **Step 1: Write failing Core HTTP tests**

Create `tests/OpenHab.Core.Tests/Api/OpenHabHttpClientMainUiPageTests.cs`:

```csharp
using System.Net;
using OpenHab.Core.Api;

namespace OpenHab.Core.Tests.Api;

public sealed class OpenHabHttpClientMainUiPageTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string ResponseBody { get; set; } = "[]";
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? AuthHeaderValue { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                LastRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            AuthHeaderValue = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody)
            });
        }
    }

    [Fact]
    public async Task GetMainUiPageComponentsAsync_UsesUiPageEndpointAndAuth()
    {
        var handler = new CapturingHandler
        {
            ResponseBody = """
            [
              {
                "uid": "energy",
                "component": "oh-layout-page",
                "config": { "label": "Energy", "sidebar": true, "order": "20", "icon": "f7:bolt" }
              }
            ]
            """
        };
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"), apiToken: "token");

        var pages = await client.GetMainUiPageComponentsAsync(CancellationToken.None);

        Assert.Equal(new Uri("http://openhab:8080/rest/ui/components/ui:page"), handler.LastRequest!.RequestUri);
        Assert.Equal("Bearer token", handler.AuthHeaderValue);
        var page = Assert.Single(pages);
        Assert.Equal("energy", page.Uid);
        Assert.Equal("oh-layout-page", page.Component);
        Assert.Equal("Energy", page.GetConfigString("label"));
        Assert.True(page.GetConfigBoolean("sidebar"));
        Assert.Equal(20, page.GetConfigInt32("order"));
        Assert.Equal("f7:bolt", page.GetConfigString("icon"));
    }

    [Fact]
    public async Task GetMainUiPageComponentsAsync_ReturnsEmptyForBlankArray()
    {
        var handler = new CapturingHandler { ResponseBody = "[]" };
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

        var pages = await client.GetMainUiPageComponentsAsync(CancellationToken.None);

        Assert.Empty(pages);
    }

    [Fact]
    public async Task GetMainUiPageComponentsAsync_ThrowsRedactedRequestExceptionOnFailure()
    {
        var handler = new CapturingHandler
        {
            StatusCode = HttpStatusCode.InternalServerError,
            ResponseBody = "token=secret"
        };
        var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"), apiToken: "secret");

        var exception = await Assert.ThrowsAsync<OpenHabRequestException>(
            () => client.GetMainUiPageComponentsAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj --filter "FullyQualifiedName~OpenHabHttpClientMainUiPageTests"
```

Expected: build fails because `GetMainUiPageComponentsAsync` and `MainUiPageComponent` do not exist.

- [ ] **Step 3: Add raw page component model**

Create `src/OpenHab.Core/Ui/MainUiPageComponent.cs`:

```csharp
using System.Globalization;
using System.Text.Json;

namespace OpenHab.Core.Ui;

public sealed record MainUiPageComponent(
    string Uid,
    string Component,
    IReadOnlyDictionary<string, JsonElement> Config)
{
    public string? GetConfigString(string key)
    {
        if (!Config.TryGetValue(key, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    public bool GetConfigBoolean(string key)
    {
        if (!Config.TryGetValue(key, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => false
        };
    }

    public int? GetConfigInt32(string key)
    {
        if (!Config.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
```

- [ ] **Step 4: Extend `IOpenHabClient`**

Modify `src/OpenHab.Core/Api/IOpenHabClient.cs`:

```csharp
using OpenHab.Core.Ui;

namespace OpenHab.Core.Api;

public sealed record SitemapInfo(string Name, string Label);

public interface IOpenHabClient
{
    Task SendCommandAsync(string itemName, string command, CancellationToken cancellationToken);
    Task SetItemStateAsync(string itemName, string state, CancellationToken cancellationToken);
    Task<string> GetSitemapJsonAsync(string sitemapName, CancellationToken cancellationToken);
    Task<IReadOnlyList<SitemapInfo>> GetSitemapsAsync(CancellationToken ct);
    Task<IReadOnlyList<MainUiPageComponent>> GetMainUiPageComponentsAsync(CancellationToken ct);
}
```

- [ ] **Step 5: Implement page fetch in `OpenHabHttpClient`**

Add `using OpenHab.Core.Ui;` to `src/OpenHab.Core/Api/OpenHabHttpClient.cs`.

Add this method inside `OpenHabHttpClient`:

```csharp
public async Task<IReadOnlyList<MainUiPageComponent>> GetMainUiPageComponentsAsync(CancellationToken ct)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri("rest/ui/components/ui:page"));
    ApplyAuth(request);

    using var response = await _httpClient.SendAsync(request, ct);
    await ThrowIfFailedAsync(response, ct);

    using var stream = await response.Content.ReadAsStreamAsync(ct);
    using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

    if (json.RootElement.ValueKind != JsonValueKind.Array)
    {
        throw new FormatException("Main UI page component response must be a JSON array.");
    }

    var pages = new List<MainUiPageComponent>();
    foreach (var element in json.RootElement.EnumerateArray())
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            continue;
        }

        var uid = ReadString(element, "uid");
        if (string.IsNullOrWhiteSpace(uid))
        {
            continue;
        }

        var component = ReadString(element, "component") ?? string.Empty;
        var config = ReadConfig(element);
        pages.Add(new MainUiPageComponent(uid, component, config));
    }

    return pages;
}
```

Add these helper methods inside `OpenHabHttpClient`:

```csharp
private static string? ReadString(JsonElement element, string propertyName)
{
    return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
        ? property.GetString()
        : null;
}

private static IReadOnlyDictionary<string, JsonElement> ReadConfig(JsonElement element)
{
    if (!element.TryGetProperty("config", out var configElement) || configElement.ValueKind != JsonValueKind.Object)
    {
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    }

    var config = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    foreach (var property in configElement.EnumerateObject())
    {
        config[property.Name] = property.Value.Clone();
    }

    return config;
}
```

- [ ] **Step 6: Run Core tests**

Run:

```powershell
dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj --filter "FullyQualifiedName~OpenHabHttpClientMainUiPageTests|FullyQualifiedName~OpenHabHttpClientAuthTests"
```

Expected: all selected tests pass.

- [ ] **Step 7: Commit Core API contract**

Run:

```powershell
git add src/OpenHab.Core/Api/IOpenHabClient.cs src/OpenHab.Core/Api/OpenHabHttpClient.cs src/OpenHab.Core/Ui/MainUiPageComponent.cs tests/OpenHab.Core.Tests/Api/OpenHabHttpClientMainUiPageTests.cs
git commit -m "Add Main UI page component API"
```

---

### Task 2: Add App Discovery, Filtering, Cache, And Shell Settings

**Files:**
- Create: `src/OpenHab.App/MainUi/MainUiPageLink.cs`
- Create: `src/OpenHab.App/MainUi/MainUiPageDiscoveryService.cs`
- Modify: `src/OpenHab.App/Settings/AppSettings.cs`
- Modify: `src/OpenHab.App/Settings/AppSettingsController.cs`
- Create: `tests/OpenHab.App.Tests/MainUi/MainUiPageDiscoveryServiceTests.cs`
- Modify: `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`

- [ ] **Step 1: Write failing discovery service tests**

Create `tests/OpenHab.App.Tests/MainUi/MainUiPageDiscoveryServiceTests.cs`:

```csharp
using System.Text.Json;
using OpenHab.App.MainUi;
using OpenHab.Core.Ui;

namespace OpenHab.App.Tests.MainUi;

public sealed class MainUiPageDiscoveryServiceTests
{
    [Fact]
    public void BuildPromotedLinks_ReturnsOnlySidebarPagesSortedByOrder()
    {
        var pages = new[]
        {
            Page("security", "Security", sidebar: true, order: "30", icon: "f7:shield"),
            Page("hidden", "Hidden", sidebar: false, order: "10", icon: null),
            Page("energy", "Energy", sidebar: true, order: "10", icon: "f7:bolt")
        };

        var links = MainUiPageDiscoveryService.BuildPromotedLinks(pages);

        Assert.Collection(
            links,
            first =>
            {
                Assert.Equal("energy", first.Uid);
                Assert.Equal("Energy", first.Label);
                Assert.Equal("/page/energy", first.Route);
                Assert.Equal("f7:bolt", first.Icon);
                Assert.Equal(10, first.Order);
            },
            second =>
            {
                Assert.Equal("security", second.Uid);
                Assert.Equal("Security", second.Label);
                Assert.Equal("/page/security", second.Route);
                Assert.Equal("f7:shield", second.Icon);
                Assert.Equal(30, second.Order);
            });
    }

    [Fact]
    public void BuildPromotedLinks_UsesUidWhenLabelIsMissing()
    {
        var pages = new[] { Page("page_without_label", null, sidebar: true, order: null, icon: null) };

        var link = Assert.Single(MainUiPageDiscoveryService.BuildPromotedLinks(pages));

        Assert.Equal("page_without_label", link.Label);
        Assert.Equal("/page/page_without_label", link.Route);
    }

    [Fact]
    public void BuildPromotedLinks_EscapesUidInRoute()
    {
        var pages = new[] { Page("Floor Plan", "Floor Plan", sidebar: true, order: null, icon: null) };

        var link = Assert.Single(MainUiPageDiscoveryService.BuildPromotedLinks(pages));

        Assert.Equal("/page/Floor%20Plan", link.Route);
    }

    private static MainUiPageComponent Page(string uid, string? label, bool sidebar, string? order, string? icon)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (label is not null)
        {
            values["label"] = JsonDocument.Parse(JsonSerializer.Serialize(label)).RootElement.Clone();
        }
        values["sidebar"] = JsonDocument.Parse(sidebar ? "true" : "false").RootElement.Clone();
        if (order is not null)
        {
            values["order"] = JsonDocument.Parse(JsonSerializer.Serialize(order)).RootElement.Clone();
        }
        if (icon is not null)
        {
            values["icon"] = JsonDocument.Parse(JsonSerializer.Serialize(icon)).RootElement.Clone();
        }

        return new MainUiPageComponent(uid, "oh-layout-page", values);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter "FullyQualifiedName~MainUiPageDiscoveryServiceTests"
```

Expected: build fails because `OpenHab.App.MainUi` types do not exist.

- [ ] **Step 3: Add app-ready link model**

Create `src/OpenHab.App/MainUi/MainUiPageLink.cs`:

```csharp
namespace OpenHab.App.MainUi;

public sealed record MainUiPageLink(
    string Uid,
    string Label,
    string Route,
    string? Icon,
    string? Type,
    int? Order);
```

- [ ] **Step 4: Add discovery service**

Create `src/OpenHab.App/MainUi/MainUiPageDiscoveryService.cs`:

```csharp
using OpenHab.Core.Api;
using OpenHab.Core.Ui;

namespace OpenHab.App.MainUi;

public sealed class MainUiPageDiscoveryService(IOpenHabClient client)
{
    public async Task<IReadOnlyList<MainUiPageLink>> DiscoverPromotedLinksAsync(CancellationToken cancellationToken)
    {
        var pages = await client.GetMainUiPageComponentsAsync(cancellationToken);
        return BuildPromotedLinks(pages);
    }

    public static IReadOnlyList<MainUiPageLink> BuildPromotedLinks(IEnumerable<MainUiPageComponent> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        return pages
            .Where(page => page.GetConfigBoolean("sidebar"))
            .Select(ToLink)
            .OrderBy(link => link.Order ?? int.MaxValue)
            .ThenBy(link => link.Label, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(link => link.Uid, StringComparer.Ordinal)
            .ToArray();
    }

    private static MainUiPageLink ToLink(MainUiPageComponent page)
    {
        var label = page.GetConfigString("label");
        if (string.IsNullOrWhiteSpace(label))
        {
            label = page.Uid;
        }

        return new MainUiPageLink(
            page.Uid,
            label.Trim(),
            "/page/" + Uri.EscapeDataString(page.Uid),
            page.GetConfigString("icon"),
            page.Component,
            page.GetConfigInt32("order"));
    }
}
```

- [ ] **Step 5: Add shell/cache settings tests**

Append these tests to `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`:

```csharp
[Fact]
public async Task CanPersistMainUiShellState()
{
    var controller = CreateController();

    controller.SetMainUiPagesExpanded(true);
    controller.SetMainWindowSitemapPaneVisible(true);
    controller.SetCachedMainUiPageLinks(new[]
    {
        new OpenHab.App.MainUi.MainUiPageLink("energy", "Energy", "/page/energy", "f7:bolt", "oh-layout-page", 10)
    });
    await controller.FlushAsync();

    var reloaded = CreateController();

    Assert.True(reloaded.Current.MainUiPagesExpanded);
    Assert.True(reloaded.Current.MainWindowSitemapPaneVisible);
    var link = Assert.Single(reloaded.Current.CachedMainUiPageLinks);
    Assert.Equal("energy", link.Uid);
    Assert.Equal("Energy", link.Label);
    Assert.Equal("/page/energy", link.Route);
}

[Fact]
public void SetCachedMainUiPageLinksNormalizesBlankLabelsAndRoutes()
{
    var controller = CreateController();

    controller.SetCachedMainUiPageLinks(new[]
    {
        new OpenHab.App.MainUi.MainUiPageLink("energy", "  ", "page/energy", null, null, null),
        new OpenHab.App.MainUi.MainUiPageLink("", "Ignored", "/page/ignored", null, null, null)
    });

    var link = Assert.Single(controller.Current.CachedMainUiPageLinks);
    Assert.Equal("energy", link.Uid);
    Assert.Equal("energy", link.Label);
    Assert.Equal("/page/energy", link.Route);
}
```

- [ ] **Step 6: Run settings tests to verify they fail**

Run:

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter "FullyQualifiedName~AppSettingsControllerTests.CanPersistMainUiShellState|FullyQualifiedName~AppSettingsControllerTests.SetCachedMainUiPageLinksNormalizesBlankLabelsAndRoutes"
```

Expected: build fails because settings properties and setters do not exist.

- [ ] **Step 7: Extend `AppSettings`**

Modify `src/OpenHab.App/Settings/AppSettings.cs`:

```csharp
using System.Text.Json.Serialization;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using System.Collections.Immutable;
using OpenHab.App.MainUi;
```

Add these parameters before credential-only ignored properties in the `AppSettings` record:

```csharp
    bool MainUiPagesExpanded = false,
    bool MainWindowSitemapPaneVisible = false,
    ImmutableArray<MainUiPageLink> CachedMainUiPageLinks = default,
```

Update `Default` so `CachedMainUiPageLinks` receives an empty array:

```csharp
        ChartQuality: ChartQuality.High,
        DeviceInfoSync: DeviceInfoSyncSettings.Default,
        CachedMainUiPageLinks: []);
```

If named argument ordering requires adjustment, keep every existing default value unchanged and add `CachedMainUiPageLinks: []` as the last non-ignored setting argument.

- [ ] **Step 8: Add settings controller setters and normalization**

Modify `src/OpenHab.App/Settings/AppSettingsController.cs`.

Add `using OpenHab.App.MainUi;`.

Add methods:

```csharp
public void SetMainUiPagesExpanded(bool expanded)
{
    UpdateSettings(settings => settings with { MainUiPagesExpanded = expanded });
}

public void SetMainWindowSitemapPaneVisible(bool visible)
{
    UpdateSettings(settings => settings with { MainWindowSitemapPaneVisible = visible });
}

public void SetCachedMainUiPageLinks(IEnumerable<MainUiPageLink> links)
{
    ArgumentNullException.ThrowIfNull(links);
    UpdateSettings(settings => settings with
    {
        CachedMainUiPageLinks = NormalizeMainUiPageLinks(links)
    });
}
```

Add helper:

```csharp
private static ImmutableArray<MainUiPageLink> NormalizeMainUiPageLinks(IEnumerable<MainUiPageLink>? links)
{
    if (links is null)
    {
        return [];
    }

    return links
        .Where(static link => !string.IsNullOrWhiteSpace(link.Uid))
        .Select(static link =>
        {
            var uid = link.Uid.Trim();
            var label = string.IsNullOrWhiteSpace(link.Label) ? uid : link.Label.Trim();
            var route = string.IsNullOrWhiteSpace(link.Route) ? "/page/" + Uri.EscapeDataString(uid) : link.Route.Trim();
            if (!route.StartsWith("/", StringComparison.Ordinal))
            {
                route = "/" + route;
            }

            return link with
            {
                Uid = uid,
                Label = label,
                Route = route
            };
        })
        .DistinctBy(static link => link.Uid, StringComparer.Ordinal)
        .OrderBy(static link => link.Order ?? int.MaxValue)
        .ThenBy(static link => link.Label, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(static link => link.Uid, StringComparer.Ordinal)
        .ToImmutableArray();
}
```

Update `NormalizeLoadedSettings` return expression:

```csharp
        return settings with
        {
            FlyoutWidth = width,
            NotificationPollIntervalSeconds = interval,
            ImportantNotificationTags = NormalizeImportantNotificationTags(settings.ImportantNotificationTags),
            DeviceInfoSync = settings.DeviceInfoSync?.Normalized() ?? DeviceInfoSyncSettings.Default,
            CachedMainUiPageLinks = NormalizeMainUiPageLinks(settings.CachedMainUiPageLinks)
        };
```

- [ ] **Step 9: Run App tests**

Run:

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter "FullyQualifiedName~MainUiPageDiscoveryServiceTests|FullyQualifiedName~AppSettingsControllerTests"
```

Expected: all selected tests pass.

- [ ] **Step 10: Commit discovery and settings**

Run:

```powershell
git add src/OpenHab.App/MainUi src/OpenHab.App/Settings/AppSettings.cs src/OpenHab.App/Settings/AppSettingsController.cs tests/OpenHab.App.Tests/MainUi/MainUiPageDiscoveryServiceTests.cs tests/OpenHab.App.Tests/AppSettingsControllerTests.cs
git commit -m "Add Main UI page discovery and shell settings"
```

---

### Task 3: Add Main UI URL And Pure Shell Navigation Helpers

**Files:**
- Create: `src/OpenHab.App/MainUi/MainUiUrlBuilder.cs`
- Create: `src/OpenHab.App/MainUi/MainUiNavigationRequest.cs`
- Create: `src/OpenHab.App/Shell/MainWindowShellState.cs`
- Create: `src/OpenHab.App/Shell/MainWindowShellController.cs`
- Create: `tests/OpenHab.App.Tests/MainUi/MainUiUrlBuilderTests.cs`
- Create: `tests/OpenHab.App.Tests/Shell/MainWindowShellControllerTests.cs`

- [ ] **Step 1: Write URL builder tests**

Create `tests/OpenHab.App.Tests/MainUi/MainUiUrlBuilderTests.cs`:

```csharp
using OpenHab.App.MainUi;

namespace OpenHab.App.Tests.MainUi;

public sealed class MainUiUrlBuilderTests
{
    [Fact]
    public void Build_RewritesMyOpenHabRootToHomeHost()
    {
        var uri = MainUiUrlBuilder.Build(new Uri("https://myopenhab.org"), "/");

        Assert.Equal(new Uri("https://home.myopenhab.org/"), uri);
    }

    [Fact]
    public void Build_CombinesLocalEndpointAndRelativeRoute()
    {
        var uri = MainUiUrlBuilder.Build(new Uri("http://openhab:8080/base/"), "/page/energy");

        Assert.Equal(new Uri("http://openhab:8080/page/energy"), uri);
    }

    [Fact]
    public void IsSameHost_ReturnsTrueForSameSchemeHostAndPort()
    {
        Assert.True(MainUiUrlBuilder.IsSameHost(
            new Uri("http://openhab:8080/"),
            new Uri("http://openhab:8080/page/energy")));
    }

    [Fact]
    public void IsSameHost_ReturnsFalseForExternalHost()
    {
        Assert.False(MainUiUrlBuilder.IsSameHost(
            new Uri("http://openhab:8080/"),
            new Uri("https://example.com/")));
    }
}
```

- [ ] **Step 2: Write shell controller tests**

Create `tests/OpenHab.App.Tests/Shell/MainWindowShellControllerTests.cs`:

```csharp
using OpenHab.App.Shell;

namespace OpenHab.App.Tests.Shell;

public sealed class MainWindowShellControllerTests
{
    [Fact]
    public void StartsOnMainUiWithSitemapHidden()
    {
        var controller = new MainWindowShellController(initialSitemapVisible: false);

        Assert.Equal(MainWindowCenterPage.MainUi, controller.Current.CenterPage);
        Assert.False(controller.Current.IsSitemapVisible);
    }

    [Fact]
    public void SitemapVisibilitySurvivesSettingsNavigation()
    {
        var controller = new MainWindowShellController(initialSitemapVisible: false);

        controller.SetSitemapVisible(true);
        controller.SelectCenterPage(MainWindowCenterPage.Settings);

        Assert.Equal(MainWindowCenterPage.Settings, controller.Current.CenterPage);
        Assert.True(controller.Current.IsSitemapVisible);
    }

    [Fact]
    public void PromotedMainUiPageSelectionKeepsSitemapVisibility()
    {
        var controller = new MainWindowShellController(initialSitemapVisible: true);

        controller.SelectPromotedMainUiPage("/page/energy");

        Assert.Equal(MainWindowCenterPage.MainUi, controller.Current.CenterPage);
        Assert.True(controller.Current.IsSitemapVisible);
        Assert.Equal("/page/energy", controller.Current.PendingMainUiRoute);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter "FullyQualifiedName~MainUiUrlBuilderTests|FullyQualifiedName~MainWindowShellControllerTests"
```

Expected: build fails because helper types do not exist.

- [ ] **Step 4: Add navigation request**

Create `src/OpenHab.App/MainUi/MainUiNavigationRequest.cs`:

```csharp
namespace OpenHab.App.MainUi;

public sealed record MainUiNavigationRequest(string Route)
{
    public static MainUiNavigationRequest Root { get; } = new("/");
}
```

- [ ] **Step 5: Add URL builder**

Create `src/OpenHab.App/MainUi/MainUiUrlBuilder.cs`:

```csharp
namespace OpenHab.App.MainUi;

public static class MainUiUrlBuilder
{
    public static Uri Build(Uri endpoint, string? route)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var builder = new UriBuilder(endpoint)
        {
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        };

        if (string.Equals(builder.Host, "myopenhab.org", StringComparison.OrdinalIgnoreCase))
        {
            builder.Host = "home.myopenhab.org";
        }

        var normalizedRoute = string.IsNullOrWhiteSpace(route) ? "/" : route.Trim();
        if (!normalizedRoute.StartsWith("/", StringComparison.Ordinal))
        {
            normalizedRoute = "/" + normalizedRoute;
        }

        return new Uri(builder.Uri, normalizedRoute.TrimStart('/'));
    }

    public static bool IsSameHost(Uri expectedBase, Uri candidate)
    {
        ArgumentNullException.ThrowIfNull(expectedBase);
        ArgumentNullException.ThrowIfNull(candidate);

        return string.Equals(expectedBase.Scheme, candidate.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(expectedBase.Host, candidate.Host, StringComparison.OrdinalIgnoreCase)
            && expectedBase.Port == candidate.Port;
    }
}
```

- [ ] **Step 6: Add shell state/controller**

Create `src/OpenHab.App/Shell/MainWindowShellState.cs`:

```csharp
namespace OpenHab.App.Shell;

public enum MainWindowCenterPage
{
    MainUi,
    Notifications,
    Settings,
    Diagnostics
}

public sealed record MainWindowShellState(
    MainWindowCenterPage CenterPage,
    bool IsSitemapVisible,
    string? PendingMainUiRoute)
{
    public static MainWindowShellState Initial(bool sitemapVisible) =>
        new(MainWindowCenterPage.MainUi, sitemapVisible, "/");
}
```

Create `src/OpenHab.App/Shell/MainWindowShellController.cs`:

```csharp
namespace OpenHab.App.Shell;

public sealed class MainWindowShellController
{
    public MainWindowShellController(bool initialSitemapVisible)
    {
        Current = MainWindowShellState.Initial(initialSitemapVisible);
    }

    public MainWindowShellState Current { get; private set; }

    public event EventHandler? Changed;

    public void SelectCenterPage(MainWindowCenterPage page)
    {
        Current = Current with
        {
            CenterPage = page,
            PendingMainUiRoute = page == MainWindowCenterPage.MainUi ? Current.PendingMainUiRoute : null
        };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SelectPromotedMainUiPage(string route)
    {
        var normalizedRoute = string.IsNullOrWhiteSpace(route) ? "/" : route.Trim();
        if (!normalizedRoute.StartsWith("/", StringComparison.Ordinal))
        {
            normalizedRoute = "/" + normalizedRoute;
        }

        Current = Current with
        {
            CenterPage = MainWindowCenterPage.MainUi,
            PendingMainUiRoute = normalizedRoute
        };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetSitemapVisible(bool visible)
    {
        if (Current.IsSitemapVisible == visible)
        {
            return;
        }

        Current = Current with { IsSitemapVisible = visible };
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
```

- [ ] **Step 7: Run helper tests**

Run:

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter "FullyQualifiedName~MainUiUrlBuilderTests|FullyQualifiedName~MainWindowShellControllerTests"
```

Expected: selected tests pass.

- [ ] **Step 8: Commit helpers**

Run:

```powershell
git add src/OpenHab.App/MainUi/MainUiUrlBuilder.cs src/OpenHab.App/MainUi/MainUiNavigationRequest.cs src/OpenHab.App/Shell tests/OpenHab.App.Tests/MainUi/MainUiUrlBuilderTests.cs tests/OpenHab.App.Tests/Shell/MainWindowShellControllerTests.cs
git commit -m "Add Main UI navigation shell helpers"
```

---

### Task 4: Extract Settings And Notifications Into Reusable Center Pages

**Files:**
- Create: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml`
- Create: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`
- Create: `src/OpenHab.Windows.Tray/Notifications/NotificationsPageControl.xaml`
- Create: `src/OpenHab.Windows.Tray/Notifications/NotificationsPageControl.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`

- [ ] **Step 1: Capture current build before extraction**

Run:

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build succeeds before refactoring. If it fails for an unrelated environment reason, record the exact failure in the task notes and continue only if it is not caused by current source code.

- [ ] **Step 2: Create notification page XAML**

Create `src/OpenHab.Windows.Tray/Notifications/NotificationsPageControl.xaml`:

```xml
<UserControl
    x:Class="OpenHab.Windows.Tray.Notifications.NotificationsPageControl"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Border BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
            BorderThickness="1"
            CornerRadius="8"
            Padding="12">
        <Grid RowSpacing="8">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
            </Grid.RowDefinitions>
            <Grid ColumnSpacing="8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <TextBox x:Name="NotificationSearchBox"
                         PlaceholderText="Search notifications"
                         MinWidth="220"
                         TextChanged="NotificationSearchBox_TextChanged" />
                <ComboBox Grid.Column="1"
                          x:Name="NotificationFilterBox"
                          MinWidth="120"
                          Width="120"
                          SelectedIndex="0"
                          SelectionChanged="NotificationFilterBox_SelectionChanged">
                    <ComboBoxItem Content="Visible" Tag="Visible" />
                    <ComboBoxItem Content="Unread" Tag="Unread" />
                    <ComboBoxItem Content="Read" Tag="Read" />
                    <ComboBoxItem Content="Hidden" Tag="Hidden" />
                    <ComboBoxItem Content="All" Tag="All" />
                </ComboBox>
                <ComboBox Grid.Column="2"
                          x:Name="NotificationSortBox"
                          MinWidth="110"
                          Width="110"
                          SelectedIndex="0"
                          SelectionChanged="NotificationSortBox_SelectionChanged">
                    <ComboBoxItem Content="Newest" Tag="DateDescending" />
                    <ComboBoxItem Content="Oldest" Tag="DateAscending" />
                    <ComboBoxItem Content="Name" Tag="Name" />
                </ComboBox>
                <Border Grid.Column="3"
                        x:Name="UnreadBadge"
                        Background="{ThemeResource AccentFillColorDefaultBrush}"
                        CornerRadius="10"
                        Padding="6,2"
                        HorizontalAlignment="Left"
                        VerticalAlignment="Center"
                        Visibility="Collapsed">
                    <TextBlock x:Name="UnreadCountText" FontSize="11" />
                </Border>
                <Button Grid.Column="4"
                        x:Name="MarkAllReadButton"
                        Content="Mark all read"
                        Click="MarkAllReadButton_Click" />
            </Grid>
            <TextBlock x:Name="LocalOnlyNote"
                       Grid.Row="1"
                       Text="New notifications unavailable in Local-Only mode. Switch to Automatic or Cloud to receive alerts."
                       Foreground="{ThemeResource SystemControlForegroundBaseMediumBrush}"
                       TextWrapping="Wrap"
                       Visibility="Collapsed"
                       Margin="0,8,0,0" />
            <Grid Grid.Row="2">
                <ScrollViewer>
                    <StackPanel x:Name="NotificationRows" Spacing="4" />
                </ScrollViewer>
                <TextBlock x:Name="EmptyNotificationsText"
                           Text="No notifications"
                           Opacity="0.5"
                           HorizontalAlignment="Center"
                           Margin="0,40,0,0"
                           Visibility="Collapsed" />
            </Grid>
        </Grid>
    </Border>
</UserControl>
```

- [ ] **Step 3: Move notification code into code-behind**

Create `src/OpenHab.Windows.Tray/Notifications/NotificationsPageControl.xaml.cs` by moving notification-only fields and methods from `MainWindow.xaml.cs`.

The constructor must be:

```csharp
using Microsoft.UI.Xaml.Controls;
using OpenHab.App.Notifications;
using OpenHab.App.Settings;
using OpenHab.Windows.Tray.Rendering;

namespace OpenHab.Windows.Tray.Notifications;

public sealed partial class NotificationsPageControl : UserControl
{
    private enum NotificationSortOrder
    {
        DateDescending,
        DateAscending,
        Name
    }

    private readonly AppSettingsController settingsController;
    private readonly NotificationStore? notificationStore;
    private readonly DispatcherRefreshGate notificationRefreshGate;
    private bool notificationControlsReady;

    public NotificationsPageControl(AppSettingsController settingsController, NotificationStore? notificationStore)
    {
        this.settingsController = settingsController;
        this.notificationStore = notificationStore;
        notificationRefreshGate = new DispatcherRefreshGate(action => DispatcherQueue.TryEnqueue(() => action()));

        InitializeComponent();
        notificationControlsReady = true;

        if (notificationStore is not null)
        {
            notificationStore.Changed += (_, _) =>
            {
                notificationRefreshGate.Request(RefreshNotificationList);
            };
            RefreshNotificationList();
        }
    }
}
```

Move these methods from `MainWindow.xaml.cs` into the control and adjust references to use control fields:

- `RefreshNotificationList`
- notification row creation helpers used only by `RefreshNotificationList`
- `MarkAllReadButton_Click`
- `NotificationSearchBox_TextChanged`
- `NotificationFilterBox_SelectionChanged`
- `NotificationSortBox_SelectionChanged`

Keep method bodies identical except for namespace and field location. Do not change notification behavior in this task.

- [ ] **Step 4: Create settings page control**

Create `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml` by moving the current `PivotItem Header="Settings"` content from `MainWindow.xaml` into a `UserControl`. Remove the `PivotItem` wrapper. Keep every existing settings element name intact because the moved code-behind will still reference those names.

Use this root wrapper, and put the existing settings `Grid RowSpacing="12"` inside the `Border`. The moved settings grid begins with the settings breadcrumb/header area and contains `SettingsBreadcrumbRootButton`, `SettingsTitleText`, `SettingsSubtitleText`, and `SettingsContent`.

```xml
<UserControl
    x:Class="OpenHab.Windows.Tray.Settings.SettingsPageControl"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Border BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
            BorderThickness="1"
            CornerRadius="8"
            Padding="12" />
</UserControl>
```

Final XAML must not leave the `Border` self-closing; it must contain the moved settings grid.

- [ ] **Step 5: Move settings code into code-behind**

Create `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs` by moving settings-only fields and methods from `MainWindow.xaml.cs`.

Constructor:

```csharp
using Microsoft.UI.Xaml.Controls;
using OpenHab.App.Settings;

namespace OpenHab.Windows.Tray.Settings;

public sealed partial class SettingsPageControl : UserControl
{
    private readonly AppSettingsController settingsController;
    private readonly Func<Task> refreshRuntimeAsync;
    private readonly Action<string> setStatusText;

    public SettingsPageControl(
        AppSettingsController settingsController,
        Func<Task> refreshRuntimeAsync,
        Action<string> setStatusText)
    {
        this.settingsController = settingsController;
        this.refreshRuntimeAsync = refreshRuntimeAsync;
        this.setStatusText = setStatusText;
        InitializeComponent();
        InitializeSettingsControls();
        RefreshSettingsBindings();
    }

    public void ShowRoot()
    {
        NavigateToSettingsPage(SettingsPage.Root);
    }
}
```

Move all settings-specific methods and fields, including:

- `SettingsPage` enum
- settings control fields
- `InitializeSettingsControls`
- `NavigateToSettingsPage`
- `UpdateSettingsBreadcrumb`
- `BuildConnectionSettingsPage`
- `BuildGeneralSettingsPage`
- `BuildAppearanceSettingsPage`
- `BuildDeviceInfoSyncSettingsPage`
- `BuildAboutSettingsPage`
- settings row/group helper methods
- settings event handlers
- `RefreshSettingsBindings`
- `SaveDeviceInfoSyncSettings`
- `ViewLogsButton_Click`

Replace `await RefreshRuntimeAsync();` calls with `await refreshRuntimeAsync();`.

Replace assignments like `StatusText.Text = "...";` with `setStatusText("...");`.

- [ ] **Step 6: Replace old Pivot content with controls**

Modify `src/OpenHab.Windows.Tray/MainWindow.xaml` to remove `SidePanelPivot` content and add named presenters that can host new controls temporarily:

```xml
<Grid x:Name="CenterContentHost" Grid.Row="2" Grid.Column="1" />
```

Keep the sitemap XAML unchanged in this task.

Modify `MainWindow.xaml.cs`:

- Remove notification/settings field declarations moved to controls.
- Add fields:

```csharp
private Notifications.NotificationsPageControl? notificationsPage;
private Settings.SettingsPageControl? settingsPage;
```

- Add helpers:

```csharp
private void ShowNotificationsPage()
{
    notificationsPage ??= new Notifications.NotificationsPageControl(settingsController, notificationStore);
    CenterContentHost.Children.Clear();
    CenterContentHost.Children.Add(notificationsPage);
}

private void ShowSettingsPage()
{
    settingsPage ??= new Settings.SettingsPageControl(settingsController, RefreshRuntimeAsync, text => StatusText.Text = text);
    settingsPage.ShowRoot();
    CenterContentHost.Children.Clear();
    CenterContentHost.Children.Add(settingsPage);
}
```

- Change existing public methods:

```csharp
public void ShowNotificationsTab()
{
    ShowNotificationsPage();
}

public void ShowSettingsTab()
{
    ShowSettingsPage();
}
```

- [ ] **Step 7: Build after extraction**

Run:

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build succeeds.

- [ ] **Step 8: Commit extraction**

Run:

```powershell
git add src/OpenHab.Windows.Tray/MainWindow.xaml src/OpenHab.Windows.Tray/MainWindow.xaml.cs src/OpenHab.Windows.Tray/Settings src/OpenHab.Windows.Tray/Notifications
git commit -m "Extract settings and notifications pages"
```

---

### Task 5: Add WebView Host And Main Window Shell Layout

**Files:**
- Create: `src/OpenHab.Windows.Tray/MainUi/MainUiWebViewHost.xaml`
- Create: `src/OpenHab.Windows.Tray/MainUi/MainUiWebViewHost.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj` if XAML build items require explicit inclusion

- [ ] **Step 1: Add WebView host XAML**

Create `src/OpenHab.Windows.Tray/MainUi/MainUiWebViewHost.xaml`:

```xml
<UserControl
    x:Class="OpenHab.Windows.Tray.MainUi.MainUiWebViewHost"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:Microsoft.UI.Xaml.Controls">
    <Grid>
        <controls:WebView2 x:Name="MainWebView"
                           Visibility="Collapsed" />
        <Grid x:Name="LoadingView"
              Background="{ThemeResource CardBackgroundFillColorDefaultBrush}">
            <StackPanel HorizontalAlignment="Center"
                        VerticalAlignment="Center"
                        Spacing="12">
                <ProgressRing IsActive="True" Width="36" Height="36" />
                <TextBlock Text="Loading openHAB Main UI" Opacity="0.7" />
            </StackPanel>
        </Grid>
        <Border x:Name="ErrorView"
                Visibility="Collapsed"
                Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
                BorderThickness="1"
                CornerRadius="8"
                Padding="20">
            <StackPanel HorizontalAlignment="Center"
                        VerticalAlignment="Center"
                        Spacing="12"
                        MaxWidth="420">
                <FontIcon Glyph="&#xE783;" FontSize="32" />
                <TextBlock x:Name="ErrorTitleText"
                           Text="Main UI could not be loaded"
                           FontSize="18"
                           FontWeight="SemiBold"
                           HorizontalAlignment="Center" />
                <TextBlock x:Name="ErrorMessageText"
                           TextWrapping="Wrap"
                           TextAlignment="Center"
                           Opacity="0.7" />
                <StackPanel Orientation="Horizontal"
                            HorizontalAlignment="Center"
                            Spacing="8">
                    <Button Content="Retry" Click="RetryButton_Click" />
                    <Button Content="Open in browser" Click="OpenInBrowserButton_Click" />
                </StackPanel>
            </StackPanel>
        </Border>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Add WebView host code-behind**

Create `src/OpenHab.Windows.Tray/MainUi/MainUiWebViewHost.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using OpenHab.App.MainUi;
using OpenHab.Core;

namespace OpenHab.Windows.Tray.MainUi;

public sealed partial class MainUiWebViewHost : UserControl
{
    private Uri? currentBaseUri;
    private Uri? currentUri;
    private string pendingRoute = "/";
    private bool initialized;

    public MainUiWebViewHost()
    {
        InitializeComponent();
    }

    public async Task NavigateAsync(Uri endpoint, string? route, CancellationToken cancellationToken = default)
    {
        currentUri = MainUiUrlBuilder.Build(endpoint, route);
        currentBaseUri = new Uri(currentUri.GetLeftPart(UriPartial.Authority));
        pendingRoute = route ?? "/";

        ShowLoading();

        try
        {
            await EnsureInitializedAsync();
            cancellationToken.ThrowIfCancellationRequested();
            MainWebView.Source = currentUri;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Main UI WebView initialization failed: {ex.GetType().Name}: {ex.Message}");
            ShowError("Main UI could not be loaded. WebView2 may be unavailable.");
        }
    }

    public bool CanGoBack => MainWebView.CanGoBack;

    public void GoBack()
    {
        if (MainWebView.CanGoBack)
        {
            MainWebView.GoBack();
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (initialized)
        {
            return;
        }

        await MainWebView.EnsureCoreWebView2Async();
        MainWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        MainWebView.CoreWebView2.Settings.IsScriptEnabled = true;
        MainWebView.NavigationStarting += MainWebView_NavigationStarting;
        MainWebView.NavigationCompleted += MainWebView_NavigationCompleted;
        initialized = true;
    }

    private void MainWebView_NavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || currentBaseUri is null)
        {
            return;
        }

        if (!MainUiUrlBuilder.IsSameHost(currentBaseUri, uri))
        {
            args.Cancel = true;
            OpenExternal(uri);
        }
    }

    private void MainWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (args.IsSuccess)
        {
            MainWebView.Visibility = Visibility.Visible;
            LoadingView.Visibility = Visibility.Collapsed;
            ErrorView.Visibility = Visibility.Collapsed;
            return;
        }

        DiagnosticLogger.Warn($"Main UI navigation failed: webError={args.WebErrorStatus}");
        ShowError("Check the configured endpoint and credentials, then retry.");
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentBaseUri is not null)
        {
            _ = NavigateAsync(currentBaseUri, pendingRoute);
        }
    }

    private void OpenInBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentUri is not null)
        {
            OpenExternal(currentUri);
        }
    }

    private static void OpenExternal(Uri uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Open external URL failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ShowLoading()
    {
        LoadingView.Visibility = Visibility.Visible;
        ErrorView.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        ErrorMessageText.Text = message;
        MainWebView.Visibility = Visibility.Collapsed;
        LoadingView.Visibility = Visibility.Collapsed;
        ErrorView.Visibility = Visibility.Visible;
    }
}
```

- [ ] **Step 3: Replace MainWindow layout with shell grid**

Modify `src/OpenHab.Windows.Tray/MainWindow.xaml`:

- Add namespaces:

```xml
xmlns:mainui="using:OpenHab.Windows.Tray.MainUi"
```

- Replace the root grid body with a shell layout that keeps the existing header and moves sitemap to the right pane.
- Move the existing header elements into row `0`, column `1`, preserving `BackButton`, `TitleText`, `StatusText`, and the command area used by refresh/menu actions.
- Add `ToggleSitemapButton` to the existing header command area and wire `Click="ToggleSitemapButton_Click"`.
- Move the existing dual-slot sitemap grid from the current left column into the right `SitemapContentRoot` pane, preserving `SitemapHeaderArea`, `SitemapPageSlotA`, `SitemapRows`, `SitemapPageSlotB`, and `SitemapRowsB`.
- Create the left navigation rail with the exact button/list names shown below because the code-behind wiring depends on them.

```xml
<Grid Margin="20" RowSpacing="12" ColumnSpacing="0">
    <Grid.Resources>
        <MenuFlyout x:Name="SitemapMenuFlyout" />
    </Grid.Resources>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="Auto" />
        <RowDefinition Height="*" />
    </Grid.RowDefinitions>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="220" />
        <ColumnDefinition Width="*" />
    </Grid.ColumnDefinitions>

    <Border Grid.Row="0"
            Grid.RowSpan="3"
            Grid.Column="0"
            Margin="-20,-20,12,-20"
            Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
            BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
            BorderThickness="0,0,1,0">
        <Grid Padding="12,20,12,12" RowSpacing="10">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>
            <StackPanel Spacing="8">
                <StackPanel Orientation="Horizontal" Spacing="8" Margin="4,0,0,14">
                    <Image Width="28" Height="28">
                        <Image.Source>
                            <SvgImageSource UriSource="ms-appx:///Assets/openhab-icon.svg" />
                        </Image.Source>
                    </Image>
                    <StackPanel>
                        <TextBlock Text="openHAB" FontSize="18" FontWeight="SemiBold" />
                        <TextBlock Text="Windows companion" Opacity="0.6" Style="{StaticResource CaptionTextBlockStyle}" />
                    </StackPanel>
                </StackPanel>
                <Button x:Name="HomeNavButton" HorizontalAlignment="Stretch" HorizontalContentAlignment="Left" Click="HomeNavButton_Click">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <FontIcon Glyph="&#xE80F;" FontSize="14" />
                        <TextBlock Text="Home" />
                    </StackPanel>
                </Button>
                <Button x:Name="MainUiPagesToggleButton" HorizontalAlignment="Stretch" HorizontalContentAlignment="Left" Click="MainUiPagesToggleButton_Click">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <FontIcon x:Name="MainUiPagesChevron" Glyph="&#xE70D;" FontSize="12" />
                        <TextBlock Text="Main UI Pages" />
                    </StackPanel>
                </Button>
                <StackPanel x:Name="MainUiPagesList" Spacing="4" Margin="18,0,0,0" />
                <Button x:Name="NotificationsNavButton" HorizontalAlignment="Stretch" HorizontalContentAlignment="Left" Click="NotificationsNavButton_Click">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <FontIcon Glyph="&#xE7F4;" FontSize="14" />
                        <TextBlock Text="Notifications" />
                    </StackPanel>
                </Button>
                <Button x:Name="SettingsNavButton" HorizontalAlignment="Stretch" HorizontalContentAlignment="Left" Click="SettingsNavButton_Click">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <FontIcon Glyph="&#xE713;" FontSize="14" />
                        <TextBlock Text="Settings" />
                    </StackPanel>
                </Button>
            </StackPanel>
            <StackPanel Grid.Row="2" Spacing="2">
                <TextBlock x:Name="ShellConnectionText" Text="Connecting..." Opacity="0.8" />
                <TextBlock Text="to openHAB server" Opacity="0.55" Style="{StaticResource CaptionTextBlockStyle}" />
            </StackPanel>
        </Grid>
    </Border>

    <Grid Grid.Row="2" Grid.Column="1" ColumnSpacing="12">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition x:Name="SitemapPaneColumn" Width="0" />
        </Grid.ColumnDefinitions>
        <Grid x:Name="CenterContentHost" Grid.Column="0">
            <mainui:MainUiWebViewHost x:Name="MainUiHost" />
        </Grid>
        <Border x:Name="SitemapContentRoot"
                Grid.Column="1"
                Visibility="Collapsed"
                Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
                BorderThickness="1"
                CornerRadius="8"
                Padding="12" />
    </Grid>
</Grid>
```

Final XAML must not leave `SitemapContentRoot` self-closing; it must contain the moved sitemap grid.

- [ ] **Step 4: Wire shell state in MainWindow**

Modify `MainWindow.xaml.cs`:

- Add fields:

```csharp
private readonly OpenHab.App.Shell.MainWindowShellController shellController;
private IReadOnlyList<OpenHab.App.MainUi.MainUiPageLink> promotedMainUiPages = [];
```

- Initialize in constructor after `InitializeComponent()`:

```csharp
shellController = new OpenHab.App.Shell.MainWindowShellController(settingsController.Current.MainWindowSitemapPaneVisible);
shellController.Changed += (_, _) => ApplyMainWindowShellState();
ApplyMainWindowShellState();
```

- Add shell methods:

```csharp
private void ApplyMainWindowShellState()
{
    var state = shellController.Current;
    SitemapPaneColumn.Width = state.IsSitemapVisible ? new GridLength(380) : new GridLength(0);
    SitemapContentRoot.Visibility = state.IsSitemapVisible ? Visibility.Visible : Visibility.Collapsed;
    settingsController.SetMainWindowSitemapPaneVisible(state.IsSitemapVisible);

    if (state.CenterPage == OpenHab.App.Shell.MainWindowCenterPage.MainUi)
    {
        ShowMainUi();
        if (!string.IsNullOrWhiteSpace(state.PendingMainUiRoute))
        {
            _ = NavigateMainUiAsync(state.PendingMainUiRoute);
        }
    }
    else if (state.CenterPage == OpenHab.App.Shell.MainWindowCenterPage.Notifications)
    {
        ShowNotificationsPage();
    }
    else if (state.CenterPage == OpenHab.App.Shell.MainWindowCenterPage.Settings)
    {
        ShowSettingsPage();
    }
}

private void ShowMainUi()
{
    if (!CenterContentHost.Children.Contains(MainUiHost))
    {
        CenterContentHost.Children.Clear();
        CenterContentHost.Children.Add(MainUiHost);
    }
}

private async Task NavigateMainUiAsync(string route)
{
    var settings = settingsController.Current;
    var endpoint = runtimeController.Current.ActiveTransport == TransportKind.Cloud
        ? settings.CloudEndpoint
        : settings.LocalEndpoint;
    await MainUiHost.NavigateAsync(endpoint, route);
}
```

- Add button handlers:

```csharp
private void HomeNavButton_Click(object sender, RoutedEventArgs e)
{
    shellController.SelectPromotedMainUiPage("/");
}

private void NotificationsNavButton_Click(object sender, RoutedEventArgs e)
{
    shellController.SelectCenterPage(OpenHab.App.Shell.MainWindowCenterPage.Notifications);
}

private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
{
    shellController.SelectCenterPage(OpenHab.App.Shell.MainWindowCenterPage.Settings);
}

private void MainUiPagesToggleButton_Click(object sender, RoutedEventArgs e)
{
    settingsController.SetMainUiPagesExpanded(!settingsController.Current.MainUiPagesExpanded);
    RefreshPromotedMainUiPagesList();
}

private void ToggleSitemapButton_Click(object sender, RoutedEventArgs e)
{
    shellController.SetSitemapVisible(!shellController.Current.IsSitemapVisible);
}
```

- Add a `Button x:Name="ToggleSitemapButton"` to the header command area in XAML and wire `Click="ToggleSitemapButton_Click"`.

- [ ] **Step 5: Build shell layout**

Run:

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build succeeds.

- [ ] **Step 6: Commit WebView shell layout**

Run:

```powershell
git add src/OpenHab.Windows.Tray/MainUi src/OpenHab.Windows.Tray/MainWindow.xaml src/OpenHab.Windows.Tray/MainWindow.xaml.cs
git commit -m "Add Main UI WebView shell layout"
```

---

### Task 6: Wire Promoted Page Discovery Into Main Window

**Files:**
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`

- [ ] **Step 1: Add MainWindow discovery entry point**

Modify `MainWindow.xaml.cs` constructor and fields:

```csharp
private readonly Func<TransportKind, Uri, IOpenHabClient> openHabClientFactory;
```

Update constructor signature:

```csharp
public MainWindow(
    AppSettingsController settingsController,
    SitemapRuntimeController runtimeController,
    NotificationStore? notificationStore,
    Action requestHideToTray,
    Func<TransportKind, Uri, IOpenHabClient> openHabClientFactory)
```

Assign:

```csharp
this.openHabClientFactory = openHabClientFactory;
```

Keep existing overloads by forwarding with a simple factory if tests instantiate them:

```csharp
(transportKind, endpoint) => new OpenHabHttpClient(new HttpClient(), endpoint)
```

- [ ] **Step 2: Add discovery method**

Add to `MainWindow.xaml.cs`:

```csharp
public async Task RefreshPromotedMainUiPagesAsync(CancellationToken cancellationToken = default)
{
    var settings = settingsController.Current;
    var transport = runtimeController.Current.ActiveTransport ?? (settings.EndpointMode == EndpointMode.CloudOnly ? TransportKind.Cloud : TransportKind.Local);
    var endpoint = transport == TransportKind.Cloud ? settings.CloudEndpoint : settings.LocalEndpoint;

    try
    {
        var client = openHabClientFactory(transport, endpoint);
        var service = new OpenHab.App.MainUi.MainUiPageDiscoveryService(client);
        promotedMainUiPages = await service.DiscoverPromotedLinksAsync(cancellationToken);
        settingsController.SetCachedMainUiPageLinks(promotedMainUiPages);
        if (promotedMainUiPages.Count > 0 && !settingsController.Current.MainUiPagesExpanded)
        {
            settingsController.SetMainUiPagesExpanded(true);
        }
        RefreshPromotedMainUiPagesList();
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        DiagnosticLogger.Warn($"Main UI promoted page discovery failed: {ex.GetType().Name}: {ex.Message}");
        promotedMainUiPages = settingsController.Current.CachedMainUiPageLinks;
        RefreshPromotedMainUiPagesList(discoveryError: true);
    }
}
```

- [ ] **Step 3: Add left rail list rendering**

Add to `MainWindow.xaml.cs`:

```csharp
private void RefreshPromotedMainUiPagesList(bool discoveryError = false)
{
    MainUiPagesList.Children.Clear();
    var expanded = settingsController.Current.MainUiPagesExpanded;
    MainUiPagesList.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
    MainUiPagesChevron.Glyph = expanded ? "\uE70D" : "\uE76C";

    if (!expanded)
    {
        return;
    }

    if (discoveryError)
    {
        MainUiPagesList.Children.Add(new TextBlock
        {
            Text = "Could not refresh pages. Showing cached links.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.65,
            Margin = new Thickness(8, 4, 8, 4)
        });
    }

    var links = promotedMainUiPages.Count > 0
        ? promotedMainUiPages
        : settingsController.Current.CachedMainUiPageLinks;

    if (links.Count == 0)
    {
        MainUiPagesList.Children.Add(new TextBlock
        {
            Text = "No promoted pages",
            Opacity = 0.65,
            Margin = new Thickness(8, 4, 8, 4)
        });
        return;
    }

    foreach (var link in links)
    {
        var button = new Button
        {
            Content = link.Label,
            Tag = link.Route,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 6, 10, 6)
        };
        button.Click += PromotedMainUiPageButton_Click;
        MainUiPagesList.Children.Add(button);
    }
}

private void PromotedMainUiPageButton_Click(object sender, RoutedEventArgs e)
{
    if (sender is Button { Tag: string route })
    {
        shellController.SelectPromotedMainUiPage(route);
    }
}
```

- [ ] **Step 4: Wire discovery from `App.xaml.cs`**

Modify `App.xaml.cs` MainWindow construction:

```csharp
mainWindow = new MainWindow(
    settingsController,
    runtimeController,
    notificationStore,
    requestHideToTray: () =>
    {
        shellController.HandleWindowCloseRequested(TrayShellSurface.MainWindow);
        _ = ApplyShellStateAsync();
    },
    openHabClientFactory: (transportKind, endpoint) =>
    {
        var auth = ResolveRuntimeAuthSync(settingsController, transportKind);
        return new OpenHabHttpClient(
            httpClient,
            endpoint,
            apiToken: auth.ApiToken,
            basicUserName: auth.BasicUserName,
            basicPassword: auth.BasicPassword);
    });
```

In `CompleteStartupAsync`, after sitemap list processing starts the runtime load, enqueue page discovery:

```csharp
_ = uiDispatcherQueue?.TryEnqueue(() =>
{
    if (mainWindow is not null)
    {
        _ = mainWindow.RefreshPromotedMainUiPagesAsync();
    }
});
```

- [ ] **Step 5: Build and run targeted tests**

Run:

```powershell
dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj --filter "FullyQualifiedName~OpenHabHttpClientMainUiPageTests"
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter "FullyQualifiedName~MainUiPageDiscoveryServiceTests|FullyQualifiedName~AppSettingsControllerTests.CanPersistMainUiShellState"
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: all commands pass.

- [ ] **Step 6: Commit discovery wiring**

Run:

```powershell
git add src/OpenHab.Windows.Tray/MainWindow.xaml.cs src/OpenHab.Windows.Tray/App.xaml.cs
git commit -m "Wire promoted Main UI page discovery"
```

---

### Task 7: Final Verification And Status Update

**Files:**
- Modify: `docs/superpowers/status/openhab-windows-current-state.md` if the implementation is complete and verified.

- [ ] **Step 1: Run direct test projects**

Run:

```powershell
dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj
dotnet test tests/OpenHab.Sitemaps.Tests/OpenHab.Sitemaps.Tests.csproj
dotnet test tests/OpenHab.Rendering.Tests/OpenHab.Rendering.Tests.csproj
```

Expected: all direct tests pass. If WinUI-dependent tests fail with known Windows App Runtime bootstrapping errors, record exact failures and continue only after confirming they are the documented environment issue.

- [ ] **Step 2: Run tray build**

Run:

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build succeeds.

- [ ] **Step 3: Run full solution tests when practical**

Run:

```powershell
dotnet test OpenHab.Windows.sln
```

Expected: full solution tests pass, or fail only on documented DesktopBridge/WinUI environment prerequisites unrelated to this implementation.

- [ ] **Step 4: Manual smoke check**

Run the app from Visual Studio or the existing debug launch path and verify:

- Main Window opens with Main UI visible.
- Sitemap pane is hidden on first launch unless the persisted setting says visible.
- `Show sitemap` displays the native sitemap right pane without resizing the outer window.
- Settings opens in the center and visible sitemap stays visible.
- Notifications opens in the center and visible sitemap stays visible.
- Returning Home restores the WebView without losing its in-window state.
- Promoted pages appear under `Main UI Pages`.
- A promoted page click navigates WebView and does not change sitemap state.
- External WebView links open in the system browser.
- `myopenhab.org` Main UI route navigates to `home.myopenhab.org`.

- [ ] **Step 5: Update current state doc**

If the implementation is complete, add a short dated note to `docs/superpowers/status/openhab-windows-current-state.md` under shipped product shape or status notes:

```markdown
- 2026-05-12: Main Window now defaults to WebView2-hosted openHAB Main UI with a native left rail, promoted page discovery, and an independently toggled native sitemap split pane.
```

- [ ] **Step 6: Commit verification/status**

Run:

```powershell
git add docs/superpowers/status/openhab-windows-current-state.md
git commit -m "Update current state for Main UI shell"
```

Skip this commit if no status file change is made.
