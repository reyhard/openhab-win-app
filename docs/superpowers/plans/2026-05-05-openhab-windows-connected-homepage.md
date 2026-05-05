# openHAB Windows Connected Homepage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the in-memory sample sitemap with a real openHAB-loaded homepage, surface connection status in the tray window, and support a first live command path for switch rows.

**Architecture:** Keep transport selection, sitemap JSON parsing, runtime state, and refresh logic inside `OpenHab.App` and `OpenHab.Sitemaps`, with `OpenHab.Windows.Tray` remaining a thin WinUI shell. This slice loads exactly one configured sitemap homepage, renders it through the existing skin system, exposes manual refresh and status, and supports switch-command round trips by reloading the homepage after each command. Subpage navigation, live event updates, persisted settings, secure credentials, and WebView fallback remain separate plans.

**Tech Stack:** .NET 10 SDK, C#, xUnit, `System.Text.Json`, existing `OpenHab.Core`, `OpenHab.Sitemaps`, `OpenHab.Rendering`, and `OpenHab.Windows.Tray`.

---

## Scope Boundary

Included:
- Parse the openHAB sitemap REST payload for the homepage and nested linked pages into existing sitemap models.
- Add a runtime controller that loads a sitemap homepage from the configured endpoint mode and reports connected/offline state plus active local/cloud transport.
- Add a configurable sitemap name to app settings.
- Render the loaded homepage in the existing WinUI tray surface.
- Add status text and manual refresh in the tray UI.
- Support switch commands from rendered rows by sending the command and reloading the homepage.

Excluded:
- Persisted settings and migrations.
- Secure credential or API token storage.
- Server profile management beyond the existing local/cloud URIs and endpoint mode.
- Event stream subscriptions and live item updates.
- Cached offline browsing beyond keeping the last in-memory descriptor during a failed refresh.
- Subpage navigation UI.
- Slider, selection, setpoint, and color command entry.
- WebView/Main UI fallback routing.
- Native notifications, device telemetry sending, packaging, startup integration, and signing.

## File Structure

- Create `src/OpenHab.Sitemaps/Parsing/OpenHabSitemapJsonParser.cs`: parses the `/rest/sitemaps/{name}` JSON payload into `SitemapPage`/`SitemapWidget`.
- Create `tests/OpenHab.Sitemaps.Tests/OpenHabSitemapJsonParserTests.cs`: validates homepage parsing, label/state splitting, mappings, and linked pages.
- Modify `src/OpenHab.App/Settings/AppSettings.cs`: add configured sitemap name.
- Modify `src/OpenHab.App/Settings/AppSettingsController.cs`: add sitemap-name mutation with validation.
- Modify `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`: cover default sitemap name and invalid sitemap names.
- Modify `src/OpenHab.App/Sitemaps/SitemapRenderController.cs`: render any normalized page instead of always using the sample page.
- Modify `tests/OpenHab.App.Tests/SitemapRenderControllerTests.cs`: verify rendering from an injected normalized page.
- Create `src/OpenHab.App/Runtime/ConnectionState.cs`: connection-state enum for the first connected slice.
- Create `src/OpenHab.App/Runtime/SitemapRuntimeSnapshot.cs`: immutable snapshot exposed to the UI.
- Create `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`: load/refresh/command runtime over `IOpenHabClient`.
- Create `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`: transport selection, load success, local-to-cloud fallback, and switch command reload behavior.
- Create `tests/OpenHab.App.Tests/Runtime/FakeOpenHabClient.cs`: deterministic test double for runtime tests.
- Modify `src/OpenHab.Windows.Tray/App.xaml.cs`: construct the runtime controller.
- Modify `src/OpenHab.Windows.Tray/MainWindow.xaml`: add connection status, sitemap name, and refresh action.
- Modify `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`: call runtime load/refresh and render runtime snapshots.
- Modify `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`: wire rendered controls to a row-action callback.
- Create `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`: record completion and verification results after implementation.

---

### Task 1: Parse openHAB Sitemap JSON

**Files:**
- Create: `src/OpenHab.Sitemaps/Parsing/OpenHabSitemapJsonParser.cs`
- Create: `tests/OpenHab.Sitemaps.Tests/OpenHabSitemapJsonParserTests.cs`

- [ ] **Step 1: Write the failing parser tests**

Create `tests/OpenHab.Sitemaps.Tests/OpenHabSitemapJsonParserTests.cs`:

```csharp
using OpenHab.Sitemaps.Models;
using OpenHab.Sitemaps.Parsing;

namespace OpenHab.Sitemaps.Tests;

public sealed class OpenHabSitemapJsonParserTests
{
    [Fact]
    public void ParseHomepageBuildsWidgetsAndLinkedPage()
    {
        const string json = """
        {
          "homepage": {
            "id": "home",
            "title": "Home",
            "widgets": [
              {
                "type": "Switch",
                "label": "Living Room Light [ON]",
                "item": {
                  "name": "LivingRoom_Light",
                  "state": "ON"
                },
                "mappings": [
                  { "command": "ON", "label": "On" },
                  { "command": "OFF", "label": "Off" }
                ]
              },
              {
                "type": "Group",
                "label": "Kitchen",
                "linkedPage": {
                  "id": "kitchen",
                  "title": "Kitchen",
                  "widgets": [
                    {
                      "type": "Text",
                      "label": "Temperature [21.4 C]",
                      "item": {
                        "name": "Kitchen_Temperature",
                        "state": "21.4 C"
                      }
                    }
                  ]
                }
              }
            ]
          }
        }
        """;

        var parser = new OpenHabSitemapJsonParser();

        var page = parser.ParseHomepage(json);

        Assert.Equal("home", page.Id);
        Assert.Equal("Home", page.Label);

        Assert.Equal(2, page.Widgets.Count);

        var light = page.Widgets[0];
        Assert.Equal(SitemapWidgetType.Switch, light.Type);
        Assert.Equal("Living Room Light", light.Label);
        Assert.Equal("LivingRoom_Light", light.ItemName);
        Assert.Equal("ON", light.State);
        Assert.Equal(2, light.Mappings.Count);

        var group = page.Widgets[1];
        Assert.Equal(SitemapWidgetType.Group, group.Type);
        Assert.Single(group.Children);
        Assert.Equal("kitchen", group.Children[0].Id);
        Assert.Equal("Kitchen", group.Children[0].Label);
        Assert.Equal("Temperature", group.Children[0].Widgets[0].Label);
        Assert.Equal("21.4 C", group.Children[0].Widgets[0].State);
    }

    [Fact]
    public void ParseHomepageDefaultsMissingStateAndMappings()
    {
        const string json = """
        {
          "homepage": {
            "id": "home",
            "title": "Home",
            "widgets": [
              {
                "type": "Chart",
                "label": "Power Chart"
              }
            ]
          }
        }
        """;

        var parser = new OpenHabSitemapJsonParser();

        var page = parser.ParseHomepage(json);

        var widget = Assert.Single(page.Widgets);
        Assert.Equal(SitemapWidgetType.Chart, widget.Type);
        Assert.Equal("Power Chart", widget.Label);
        Assert.Null(widget.ItemName);
        Assert.Null(widget.State);
        Assert.Empty(widget.Mappings);
        Assert.Empty(widget.Children);
        Assert.True(widget.IsVisible);
    }
}
```

- [ ] **Step 2: Run the tests and verify failure**

Run:

```powershell
dotnet test tests/OpenHab.Sitemaps.Tests/OpenHab.Sitemaps.Tests.csproj --filter OpenHabSitemapJsonParserTests
```

Expected: fail because `OpenHab.Sitemaps.Parsing.OpenHabSitemapJsonParser` does not exist.

- [ ] **Step 3: Implement the sitemap JSON parser**

Create `src/OpenHab.Sitemaps/Parsing/OpenHabSitemapJsonParser.cs`:

```csharp
using System.Text.Json;
using OpenHab.Sitemaps.Models;

namespace OpenHab.Sitemaps.Parsing;

public sealed class OpenHabSitemapJsonParser
{
    public SitemapPage ParseHomepage(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        var homepage = document.RootElement.GetProperty("homepage");
        return ParsePage(homepage);
    }

    private static SitemapPage ParsePage(JsonElement element)
    {
        var id = element.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Sitemap page is missing an id.");
        var label = GetOptionalString(element, "title")
            ?? GetOptionalString(element, "label")
            ?? id;

        var widgets = element.TryGetProperty("widgets", out var widgetsElement)
            ? widgetsElement.EnumerateArray().Select(ParseWidget).ToArray()
            : Array.Empty<SitemapWidget>();

        return new SitemapPage(id, label, widgets);
    }

    private static SitemapWidget ParseWidget(JsonElement element)
    {
        var rawLabel = GetOptionalString(element, "label") ?? string.Empty;
        var (label, stateFromLabel) = SplitLabelAndState(rawLabel);
        var type = ParseWidgetType(GetOptionalString(element, "type"));

        string? itemName = null;
        string? itemState = null;
        if (element.TryGetProperty("item", out var itemElement))
        {
            itemName = GetOptionalString(itemElement, "name");
            itemState = GetOptionalString(itemElement, "state");
        }

        var mappings = element.TryGetProperty("mappings", out var mappingsElement)
            ? mappingsElement.EnumerateArray()
                .Select(mapping => new SitemapMapping(
                    mapping.GetProperty("command").GetString() ?? string.Empty,
                    mapping.GetProperty("label").GetString() ?? string.Empty))
                .ToArray()
            : Array.Empty<SitemapMapping>();

        var children = element.TryGetProperty("linkedPage", out var linkedPage)
            ? new[] { ParsePage(linkedPage) }
            : Array.Empty<SitemapPage>();

        return new SitemapWidget(
            label,
            type,
            itemName,
            itemState ?? stateFromLabel,
            mappings,
            IsVisible: true,
            children);
    }

    private static SitemapWidgetType ParseWidgetType(string? type)
    {
        return Enum.TryParse<SitemapWidgetType>(type, ignoreCase: true, out var parsed)
            ? parsed
            : SitemapWidgetType.Default;
    }

    private static (string Label, string? State) SplitLabelAndState(string rawLabel)
    {
        var markerStart = rawLabel.LastIndexOf(" [", StringComparison.Ordinal);
        if (markerStart < 0 || !rawLabel.EndsWith(']'))
        {
            return (rawLabel, null);
        }

        var label = rawLabel[..markerStart].Trim();
        var state = rawLabel[(markerStart + 2)..^1].Trim();
        return (label, string.IsNullOrWhiteSpace(state) ? null : state);
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
```

- [ ] **Step 4: Run the parser tests and verify they pass**

Run:

```powershell
dotnet test tests/OpenHab.Sitemaps.Tests/OpenHab.Sitemaps.Tests.csproj --filter OpenHabSitemapJsonParserTests
```

Expected: both parser tests pass.

- [ ] **Step 5: Commit the parser slice**

Run:

```powershell
git add src/OpenHab.Sitemaps/Parsing/OpenHabSitemapJsonParser.cs tests/OpenHab.Sitemaps.Tests/OpenHabSitemapJsonParserTests.cs
git commit -m "feat: parse openhab sitemap homepage json"
```

---

### Task 2: Add Connected Homepage Runtime In `OpenHab.App`

**Files:**
- Modify: `src/OpenHab.App/Settings/AppSettings.cs`
- Modify: `src/OpenHab.App/Settings/AppSettingsController.cs`
- Modify: `src/OpenHab.App/Sitemaps/SitemapRenderController.cs`
- Modify: `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`
- Modify: `tests/OpenHab.App.Tests/SitemapRenderControllerTests.cs`
- Create: `src/OpenHab.App/Runtime/ConnectionState.cs`
- Create: `src/OpenHab.App/Runtime/SitemapRuntimeSnapshot.cs`
- Create: `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`
- Create: `tests/OpenHab.App.Tests/Runtime/FakeOpenHabClient.cs`
- Create: `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`

- [ ] **Step 1: Extend the settings tests first**

Modify `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs` to this complete file:

```csharp
using OpenHab.App.Settings;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.App.Tests;

public sealed class AppSettingsControllerTests
{
    [Fact]
    public void DefaultsUseWindows11SkinAutomaticEndpointModeAndDefaultSitemap()
    {
        var controller = new AppSettingsController();

        Assert.Equal(SitemapSkinKind.Windows11, controller.Current.Skin);
        Assert.Equal(EndpointMode.Automatic, controller.Current.EndpointMode);
        Assert.Equal("default", controller.Current.SitemapName);
        Assert.Equal(new Uri("http://openhab.local:8080"), controller.Current.LocalEndpoint);
        Assert.Equal(new Uri("https://myopenhab.org"), controller.Current.CloudEndpoint);
    }

    [Fact]
    public void CanChangeSkinEndpointModeAndSitemapName()
    {
        var controller = new AppSettingsController();

        controller.SetSkin(SitemapSkinKind.Basic);
        controller.SetEndpointMode(EndpointMode.CloudOnly);
        controller.SetSitemapName("home");

        Assert.Equal(SitemapSkinKind.Basic, controller.Current.Skin);
        Assert.Equal(EndpointMode.CloudOnly, controller.Current.EndpointMode);
        Assert.Equal("home", controller.Current.SitemapName);
    }

    [Fact]
    public void RejectsRelativeEndpointUris()
    {
        var controller = new AppSettingsController();

        var exception = Assert.Throws<ArgumentException>(() =>
            controller.SetEndpoints(new Uri("/rest", UriKind.Relative), new Uri("https://myopenhab.org")));

        Assert.Contains("absolute", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsBlankSitemapName(string value)
    {
        var controller = new AppSettingsController();

        Assert.Throws<ArgumentException>(() => controller.SetSitemapName(value));
    }
}
```

Create `tests/OpenHab.App.Tests/Runtime/FakeOpenHabClient.cs`:

```csharp
using OpenHab.Core.Api;

namespace OpenHab.App.Tests.Runtime;

public sealed class FakeOpenHabClient : IOpenHabClient
{
    private readonly Queue<string> sitemapResponses = new();

    public List<(string ItemName, string Command)> Commands { get; } = new();
    public Exception? SitemapException { get; set; }

    public void EnqueueSitemap(string json)
    {
        sitemapResponses.Enqueue(json);
    }

    public Task SendCommandAsync(string itemName, string command, CancellationToken cancellationToken)
    {
        Commands.Add((itemName, command));
        return Task.CompletedTask;
    }

    public Task SetItemStateAsync(string itemName, string state, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<string> GetSitemapJsonAsync(string sitemapName, CancellationToken cancellationToken)
    {
        if (SitemapException is not null)
        {
            throw SitemapException;
        }

        if (sitemapResponses.Count == 0)
        {
            throw new InvalidOperationException("No sitemap response queued.");
        }

        return Task.FromResult(sitemapResponses.Dequeue());
    }
}
```

Create `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`:

```csharp
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Core.Profiles;
using OpenHab.Sitemaps.Parsing;

namespace OpenHab.App.Tests.Runtime;

public sealed class SitemapRuntimeControllerTests
{
    private const string OffHomepageJson = """
    {
      "homepage": {
        "id": "home",
        "title": "Home",
        "widgets": [
          {
            "type": "Switch",
            "label": "Living Room Light [OFF]",
            "item": {
              "name": "LivingRoom_Light",
              "state": "OFF"
            },
            "mappings": [
              { "command": "ON", "label": "On" },
              { "command": "OFF", "label": "Off" }
            ]
          }
        ]
      }
    }
    """;

    private const string OnHomepageJson = """
    {
      "homepage": {
        "id": "home",
        "title": "Home",
        "widgets": [
          {
            "type": "Switch",
            "label": "Living Room Light [ON]",
            "item": {
              "name": "LivingRoom_Light",
              "state": "ON"
            },
            "mappings": [
              { "command": "ON", "label": "On" },
              { "command": "OFF", "label": "Off" }
            ]
          }
        ]
      }
    }
    """;

    [Fact]
    public async Task LoadAsyncUsesLocalEndpointWhenConfigured()
    {
        var settings = new AppSettingsController();
        settings.SetEndpointMode(EndpointMode.LocalOnly);

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemap(OffHomepageJson);

        var controller = new SitemapRuntimeController(
            settings,
            new SitemapRenderController(settings),
            new OpenHabSitemapJsonParser(),
            baseUri => localClient);

        await controller.LoadAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, controller.Current.ConnectionState);
        Assert.Equal(TransportKind.Local, controller.Current.ActiveTransport);
        Assert.Equal("Home", controller.Current.Descriptor!.Title);
    }

    [Fact]
    public async Task LoadAsyncFallsBackToCloudWhenAutomaticLocalLoadFails()
    {
        var settings = new AppSettingsController();
        settings.SetEndpointMode(EndpointMode.Automatic);

        var localClient = new FakeOpenHabClient
        {
            SitemapException = new HttpRequestException("local down")
        };
        var cloudClient = new FakeOpenHabClient();
        cloudClient.EnqueueSitemap(OffHomepageJson);

        var controller = new SitemapRuntimeController(
            settings,
            new SitemapRenderController(settings),
            new OpenHabSitemapJsonParser(),
            baseUri => baseUri.Host.Contains("myopenhab", StringComparison.OrdinalIgnoreCase) ? cloudClient : localClient);

        await controller.LoadAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, controller.Current.ConnectionState);
        Assert.Equal(TransportKind.Cloud, controller.Current.ActiveTransport);
        Assert.Contains("cloud", controller.Current.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ActivateRowAsyncSendsSwitchCommandAndReloadsHomepage()
    {
        var settings = new AppSettingsController();
        settings.SetEndpointMode(EndpointMode.LocalOnly);

        var client = new FakeOpenHabClient();
        client.EnqueueSitemap(OffHomepageJson);
        client.EnqueueSitemap(OnHomepageJson);

        var controller = new SitemapRuntimeController(
            settings,
            new SitemapRenderController(settings),
            new OpenHabSitemapJsonParser(),
            baseUri => client);

        await controller.LoadAsync(CancellationToken.None);
        await controller.ActivateRowAsync(0, CancellationToken.None);

        var command = Assert.Single(client.Commands);
        Assert.Equal("LivingRoom_Light", command.ItemName);
        Assert.Equal("ON", command.Command);
        Assert.Equal("ON", controller.Current.Descriptor!.Rows[0].State);
    }
}
```

- [ ] **Step 2: Run the new tests and verify failure**

Run:

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter "AppSettingsControllerTests|SitemapRuntimeControllerTests"
```

Expected: fail because `ConnectionState`, `SitemapRuntimeSnapshot`, `SitemapRuntimeController`, and `SetSitemapName` do not exist.

- [ ] **Step 3: Implement settings, render-controller, and runtime types**

Modify `src/OpenHab.App/Settings/AppSettings.cs`:

```csharp
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.App.Settings;

public sealed record AppSettings(
    SitemapSkinKind Skin,
    EndpointMode EndpointMode,
    string SitemapName,
    Uri LocalEndpoint,
    Uri CloudEndpoint)
{
    public static AppSettings Default { get; } = new(
        SitemapSkinKind.Windows11,
        EndpointMode.Automatic,
        "default",
        new Uri("http://openhab.local:8080"),
        new Uri("https://myopenhab.org"));
}
```

Modify `src/OpenHab.App/Settings/AppSettingsController.cs`:

```csharp
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.App.Settings;

public sealed class AppSettingsController
{
    public AppSettings Current { get; private set; } = AppSettings.Default;

    public void SetSkin(SitemapSkinKind skin)
    {
        Current = Current with { Skin = skin };
    }

    public void SetEndpointMode(EndpointMode endpointMode)
    {
        Current = Current with { EndpointMode = endpointMode };
    }

    public void SetSitemapName(string sitemapName)
    {
        if (string.IsNullOrWhiteSpace(sitemapName))
        {
            throw new ArgumentException("Sitemap name must not be blank.", nameof(sitemapName));
        }

        Current = Current with { SitemapName = sitemapName.Trim() };
    }

    public void SetEndpoints(Uri localEndpoint, Uri cloudEndpoint)
    {
        ArgumentNullException.ThrowIfNull(localEndpoint);
        ArgumentNullException.ThrowIfNull(cloudEndpoint);

        if (!localEndpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Local endpoint must be an absolute URI.", nameof(localEndpoint));
        }

        if (!cloudEndpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Cloud endpoint must be an absolute URI.", nameof(cloudEndpoint));
        }

        Current = Current with
        {
            LocalEndpoint = localEndpoint,
            CloudEndpoint = cloudEndpoint
        };
    }
}
```

Modify `src/OpenHab.App/Sitemaps/SitemapRenderController.cs`:

```csharp
using OpenHab.App.Settings;
using OpenHab.Rendering.Descriptors;
using OpenHab.Rendering.Skins;
using OpenHab.Sitemaps.Models;

namespace OpenHab.App.Sitemaps;

public sealed class SitemapRenderController
{
    private readonly AppSettingsController settingsController;

    public SitemapRenderController(AppSettingsController settingsController)
    {
        ArgumentNullException.ThrowIfNull(settingsController);
        this.settingsController = settingsController;
    }

    public SitemapRenderDescriptor BuildDescriptor(NormalizedSitemapPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        ISitemapSkin skin = settingsController.Current.Skin switch
        {
            SitemapSkinKind.Basic => new BasicSitemapSkin(),
            SitemapSkinKind.Windows11 => new Windows11SitemapSkin(),
            _ => throw new InvalidOperationException($"Unsupported sitemap skin '{settingsController.Current.Skin}'.")
        };

        return skin.Render(page);
    }

    public SitemapRenderDescriptor BuildCurrentDescriptor()
    {
        return BuildDescriptor(SampleSitemapFactory.CreateHomePage());
    }
}
```

Create `src/OpenHab.App/Runtime/ConnectionState.cs`:

```csharp
namespace OpenHab.App.Runtime;

public enum ConnectionState
{
    Loading,
    Connected,
    Offline
}
```

Create `src/OpenHab.App/Runtime/SitemapRuntimeSnapshot.cs`:

```csharp
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.App.Runtime;

public sealed record SitemapRuntimeSnapshot(
    ConnectionState ConnectionState,
    TransportKind? ActiveTransport,
    bool IsBusy,
    string StatusText,
    string? ErrorMessage,
    SitemapRenderDescriptor? Descriptor)
{
    public static SitemapRuntimeSnapshot Empty { get; } = new(
        ConnectionState.Loading,
        ActiveTransport: null,
        IsBusy: false,
        StatusText: "Not loaded.",
        ErrorMessage: null,
        Descriptor: null);
}
```

Create `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`:

```csharp
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Core.Api;
using OpenHab.Core.Profiles;
using OpenHab.Sitemaps.Models;
using OpenHab.Sitemaps.Parsing;
using OpenHab.Sitemaps.Runtime;

namespace OpenHab.App.Runtime;

public sealed class SitemapRuntimeController
{
    private readonly AppSettingsController settingsController;
    private readonly SitemapRenderController renderController;
    private readonly OpenHabSitemapJsonParser parser;
    private readonly Func<Uri, IOpenHabClient> clientFactory;

    private IOpenHabClient? activeClient;
    private SitemapPage? currentHomepage;

    public SitemapRuntimeController(
        AppSettingsController settingsController,
        SitemapRenderController renderController,
        OpenHabSitemapJsonParser parser,
        Func<Uri, IOpenHabClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(settingsController);
        ArgumentNullException.ThrowIfNull(renderController);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(clientFactory);

        this.settingsController = settingsController;
        this.renderController = renderController;
        this.parser = parser;
        this.clientFactory = clientFactory;
    }

    public SitemapRuntimeSnapshot Current { get; private set; } = SitemapRuntimeSnapshot.Empty;

    public Task LoadAsync(CancellationToken cancellationToken)
    {
        return LoadInternalAsync(isRefresh: false, cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        return LoadInternalAsync(isRefresh: true, cancellationToken);
    }

    public async Task ActivateRowAsync(int rowIndex, CancellationToken cancellationToken)
    {
        if (currentHomepage is null || activeClient is null)
        {
            return;
        }

        if (rowIndex < 0 || rowIndex >= currentHomepage.Widgets.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(rowIndex));
        }

        var widget = currentHomepage.Widgets[rowIndex];
        if (widget.Type != SitemapWidgetType.Switch || string.IsNullOrWhiteSpace(widget.ItemName))
        {
            return;
        }

        var command = string.Equals(widget.State, "ON", StringComparison.OrdinalIgnoreCase) ? "OFF" : "ON";
        await activeClient.SendCommandAsync(widget.ItemName, command, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    private async Task LoadInternalAsync(bool isRefresh, CancellationToken cancellationToken)
    {
        var previousDescriptor = Current.Descriptor;
        Current = Current with
        {
            ConnectionState = ConnectionState.Loading,
            IsBusy = true,
            ErrorMessage = null,
            StatusText = isRefresh ? "Refreshing sitemap..." : "Loading sitemap..."
        };

        var settings = settingsController.Current;
        var attempts = GetAttempts(settings);
        Exception? lastError = null;

        foreach (var attempt in attempts)
        {
            try
            {
                var client = clientFactory(attempt.BaseUri);
                var json = await client.GetSitemapJsonAsync(settings.SitemapName, cancellationToken);
                var homepage = parser.ParseHomepage(json);
                var descriptor = renderController.BuildDescriptor(SitemapNormalizer.Normalize(homepage));

                activeClient = client;
                currentHomepage = homepage;
                Current = new SitemapRuntimeSnapshot(
                    ConnectionState.Connected,
                    attempt.Kind,
                    IsBusy: false,
                    StatusText: $"Connected via {attempt.Kind.ToString().ToLowerInvariant()}.",
                    ErrorMessage: null,
                    Descriptor: descriptor);
                return;
            }
            catch (Exception ex) when (settings.EndpointMode == EndpointMode.Automatic)
            {
                lastError = ex;
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        Current = new SitemapRuntimeSnapshot(
            ConnectionState.Offline,
            ActiveTransport: null,
            IsBusy: false,
            StatusText: "Offline. Showing last loaded sitemap if available.",
            ErrorMessage: lastError?.Message,
            Descriptor: previousDescriptor);
    }

    private static IReadOnlyList<TransportSelection> GetAttempts(AppSettings settings)
    {
        return settings.EndpointMode switch
        {
            EndpointMode.LocalOnly => [new TransportSelection(TransportKind.Local, settings.LocalEndpoint)],
            EndpointMode.CloudOnly => [new TransportSelection(TransportKind.Cloud, settings.CloudEndpoint)],
            EndpointMode.Automatic => [
                new TransportSelection(TransportKind.Local, settings.LocalEndpoint),
                new TransportSelection(TransportKind.Cloud, settings.CloudEndpoint)
            ],
            _ => throw new InvalidOperationException($"Unsupported endpoint mode '{settings.EndpointMode}'.")
        };
    }
}
```

- [ ] **Step 4: Update the render-controller tests for the new signature**

Modify `tests/OpenHab.App.Tests/SitemapRenderControllerTests.cs` to this complete file:

```csharp
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;

namespace OpenHab.App.Tests;

public sealed class SitemapRenderControllerTests
{
    [Fact]
    public void BuildsWindows11DescriptorByDefault()
    {
        var settings = new AppSettingsController();
        var controller = new SitemapRenderController(settings);
        var page = new NormalizedSitemapPage(
            "home",
            "Home",
            [
                new NormalizedSitemapWidget(
                    "Living Room Light",
                    SitemapWidgetType.Switch,
                    "LivingRoom_Light",
                    "ON",
                    [new SitemapMapping("ON", "On"), new SitemapMapping("OFF", "Off")],
                    CanNavigate: false,
                    RequiresFallback: false,
                    SitemapFallbackKind.None,
                    [])
            ]);

        var descriptor = controller.BuildDescriptor(page);

        Assert.Equal(SitemapSkinKind.Windows11, descriptor.Skin);
        Assert.Equal("home", descriptor.PageId);
        Assert.Equal("Home", descriptor.Title);
        Assert.Contains(descriptor.Rows, row => row.Control == RenderControlKind.Toggle && row.Action == RenderActionKind.SendCommand);
    }

    [Fact]
    public void UsesBasicSkinWhenSelected()
    {
        var settings = new AppSettingsController();
        settings.SetSkin(SitemapSkinKind.Basic);
        var controller = new SitemapRenderController(settings);
        var page = new NormalizedSitemapPage(
            "home",
            "Home",
            [
                new NormalizedSitemapWidget(
                    "Kitchen Dimmer",
                    SitemapWidgetType.Slider,
                    "Kitchen_Dimmer",
                    "42",
                    [],
                    CanNavigate: false,
                    RequiresFallback: false,
                    SitemapFallbackKind.None,
                    [])
            ]);

        var descriptor = controller.BuildDescriptor(page);

        Assert.Equal(SitemapSkinKind.Basic, descriptor.Skin);
        Assert.All(descriptor.Rows, row => Assert.Equal(RenderDensity.Compact, row.Density));
    }
}
```

- [ ] **Step 5: Run the app tests and verify they pass**

Run:

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter "AppSettingsControllerTests|SitemapRenderControllerTests|SitemapRuntimeControllerTests"
```

Expected: all selected `OpenHab.App.Tests` pass.

- [ ] **Step 6: Commit the connected runtime slice**

Run:

```powershell
git add src/OpenHab.App tests/OpenHab.App.Tests
git commit -m "feat: add connected homepage runtime"
```

---

### Task 3: Wire The Tray Window To The Connected Runtime

**Files:**
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`

- [ ] **Step 1: Update the tray app startup to build runtime services**

Modify `src/OpenHab.Windows.Tray/App.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Core.Api;
using OpenHab.Sitemaps.Parsing;
using OpenHab.Windows.Tray.Tray;

namespace OpenHab.Windows.Tray;

public partial class App : Application
{
    private MainWindow? window;
    private TrayIconService? trayIcon;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settingsController = new AppSettingsController();
        var renderController = new SitemapRenderController(settingsController);
        var runtimeController = new SitemapRuntimeController(
            settingsController,
            renderController,
            new OpenHabSitemapJsonParser(),
            baseUri => new OpenHabHttpClient(new HttpClient(), baseUri));

        window = new MainWindow(settingsController, runtimeController);
        trayIcon = new TrayIconService(
            showWindow: () =>
            {
                window.Activate();
            },
            exitApplication: () =>
            {
                trayIcon?.Dispose();
                Exit();
            });

        window.Activate();
    }
}
```

- [ ] **Step 2: Add status, sitemap-name, and refresh controls to the window**

Modify `src/OpenHab.Windows.Tray/MainWindow.xaml`:

```xml
<Window
    x:Class="OpenHab.Windows.Tray.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="openHAB">
    <Grid Margin="16" RowSpacing="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <Grid ColumnSpacing="12">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>

            <StackPanel>
                <TextBlock x:Name="TitleText"
                           FontSize="20"
                           FontWeight="SemiBold" />
                <TextBlock x:Name="StatusText"
                           Opacity="0.75" />
            </StackPanel>

            <Button Grid.Column="1"
                    x:Name="RefreshButton"
                    Content="Refresh"
                    Click="RefreshButton_Click" />
        </Grid>

        <TextBlock Grid.Row="1"
                   x:Name="ErrorText"
                   Foreground="IndianRed"
                   TextWrapping="WrapWholeWords" />

        <TabView Grid.Row="2"
                 IsAddTabButtonVisible="False">
            <TabViewItem Header="Sitemap">
                <ScrollViewer>
                    <StackPanel x:Name="SitemapRows" Spacing="8" />
                </ScrollViewer>
            </TabViewItem>
            <TabViewItem Header="Settings">
                <StackPanel Spacing="12" MaxWidth="420">
                    <TextBox x:Name="SitemapNameText"
                             Header="Sitemap name"
                             LostFocus="SitemapNameText_LostFocus" />
                    <ComboBox x:Name="SkinCombo"
                              Header="Skin"
                              SelectionChanged="SkinCombo_SelectionChanged" />
                    <ComboBox x:Name="EndpointModeCombo"
                              Header="Endpoint mode"
                              SelectionChanged="EndpointModeCombo_SelectionChanged" />
                    <TextBox x:Name="LocalEndpointText"
                             Header="Local endpoint"
                             LostFocus="EndpointText_LostFocus" />
                    <TextBox x:Name="CloudEndpointText"
                             Header="Cloud endpoint"
                             LostFocus="EndpointText_LostFocus" />
                </StackPanel>
            </TabViewItem>
        </TabView>
    </Grid>
</Window>
```

- [ ] **Step 3: Bind runtime load, refresh, and row actions in the code-behind**

Modify `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using OpenHab.Windows.Tray.Rendering;

namespace OpenHab.Windows.Tray;

public sealed partial class MainWindow : Window
{
    private readonly AppSettingsController settingsController;
    private readonly SitemapRuntimeController runtimeController;
    private bool isRefreshing;

    public MainWindow(AppSettingsController settingsController, SitemapRuntimeController runtimeController)
    {
        this.settingsController = settingsController;
        this.runtimeController = runtimeController;

        InitializeComponent();
        InitializeSettingsControls();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadAndRenderAsync();
    }

    private async Task LoadAndRenderAsync()
    {
        isRefreshing = true;
        try
        {
            await runtimeController.LoadAsync(CancellationToken.None);
            RenderSnapshot();
        }
        finally
        {
            isRefreshing = false;
        }
    }

    private void RenderSnapshot()
    {
        var snapshot = runtimeController.Current;
        TitleText.Text = snapshot.Descriptor?.Title ?? "openHAB";
        StatusText.Text = snapshot.StatusText;
        ErrorText.Text = snapshot.ErrorMessage ?? string.Empty;
        RefreshButton.IsEnabled = !snapshot.IsBusy;

        SitemapRows.Children.Clear();
        if (snapshot.Descriptor is not null)
        {
            for (var i = 0; i < snapshot.Descriptor.Rows.Count; i++)
            {
                var row = snapshot.Descriptor.Rows[i];
                SitemapRows.Children.Add(SitemapControlFactory.Create(row, i, HandleRowActionAsync));
            }
        }

        SitemapNameText.Text = settingsController.Current.SitemapName;
        SkinCombo.SelectedItem = settingsController.Current.Skin;
        EndpointModeCombo.SelectedItem = settingsController.Current.EndpointMode;
        LocalEndpointText.Text = settingsController.Current.LocalEndpoint.ToString();
        CloudEndpointText.Text = settingsController.Current.CloudEndpoint.ToString();
    }

    private void InitializeSettingsControls()
    {
        SkinCombo.ItemsSource = Enum.GetValues<SitemapSkinKind>();
        EndpointModeCombo.ItemsSource = Enum.GetValues<EndpointMode>();
    }

    private async Task HandleRowActionAsync(int rowIndex)
    {
        await runtimeController.ActivateRowAsync(rowIndex, CancellationToken.None);
        RenderSnapshot();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (isRefreshing)
        {
            return;
        }

        isRefreshing = true;
        try
        {
            await runtimeController.RefreshAsync(CancellationToken.None);
            RenderSnapshot();
        }
        finally
        {
            isRefreshing = false;
        }
    }

    private async void SkinCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRefreshing || SkinCombo.SelectedItem is not SitemapSkinKind skin)
        {
            return;
        }

        settingsController.SetSkin(skin);
        await runtimeController.RefreshAsync(CancellationToken.None);
        RenderSnapshot();
    }

    private async void EndpointModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRefreshing || EndpointModeCombo.SelectedItem is not EndpointMode endpointMode)
        {
            return;
        }

        settingsController.SetEndpointMode(endpointMode);
        await runtimeController.LoadAsync(CancellationToken.None);
        RenderSnapshot();
    }

    private async void SitemapNameText_LostFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            settingsController.SetSitemapName(SitemapNameText.Text);
            await runtimeController.LoadAsync(CancellationToken.None);
            RenderSnapshot();
        }
        catch (ArgumentException)
        {
            SitemapNameText.Text = settingsController.Current.SitemapName;
        }
    }

    private async void EndpointText_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(LocalEndpointText.Text, UriKind.Absolute, out var localEndpoint)
            || !Uri.TryCreate(CloudEndpointText.Text, UriKind.Absolute, out var cloudEndpoint))
        {
            RenderSnapshot();
            return;
        }

        try
        {
            settingsController.SetEndpoints(localEndpoint, cloudEndpoint);
            await runtimeController.LoadAsync(CancellationToken.None);
            RenderSnapshot();
        }
        catch (ArgumentException)
        {
            RenderSnapshot();
        }
    }
}
```

- [ ] **Step 4: Update the control factory to invoke runtime row actions**

Modify `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.Windows.Tray.Rendering;

public static class SitemapControlFactory
{
    public static FrameworkElement Create(
        SitemapRowDescriptor row,
        int rowIndex,
        Func<int, Task> onActivate)
    {
        return row.Control switch
        {
            RenderControlKind.Toggle => CreateToggle(row, rowIndex, onActivate),
            RenderControlKind.Slider => CreateSlider(row),
            RenderControlKind.Selection => CreateSelection(row, rowIndex, onActivate),
            RenderControlKind.Fallback => CreateFallback(row, rowIndex, onActivate),
            _ => CreateText(row)
        };
    }

    private static FrameworkElement CreateText(SitemapRowDescriptor row)
    {
        return CreateRow(row.Label, row.State ?? string.Empty);
    }

    private static FrameworkElement CreateToggle(
        SitemapRowDescriptor row,
        int rowIndex,
        Func<int, Task> onActivate)
    {
        var toggle = new ToggleSwitch
        {
            Header = row.Label,
            IsOn = string.Equals(row.State, "ON", StringComparison.OrdinalIgnoreCase)
        };

        toggle.Toggled += async (_, _) => await onActivate(rowIndex);
        return toggle;
    }

    private static FrameworkElement CreateSlider(SitemapRowDescriptor row)
    {
        var value = double.TryParse(row.State, out var parsed) ? parsed : 0;
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = row.Label },
                new Slider { Minimum = 0, Maximum = 100, Value = value, IsEnabled = false }
            }
        };
    }

    private static FrameworkElement CreateSelection(
        SitemapRowDescriptor row,
        int rowIndex,
        Func<int, Task> onActivate)
    {
        var button = new Button
        {
            Content = string.IsNullOrWhiteSpace(row.State) ? row.Label : $"{row.Label}: {row.State}",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = row.Action == RenderActionKind.SendCommand
        };
        button.Click += async (_, _) => await onActivate(rowIndex);
        return button;
    }

    private static FrameworkElement CreateFallback(
        SitemapRowDescriptor row,
        int rowIndex,
        Func<int, Task> onActivate)
    {
        var button = new Button
        {
            Content = row.Label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = row.Action == RenderActionKind.OpenFallback
        };
        button.Click += async (_, _) => await onActivate(rowIndex);
        return button;
    }

    private static FrameworkElement CreateRow(string label, string state)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });

        var stateText = new TextBlock { Text = state, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(stateText, 1);
        grid.Children.Add(stateText);

        return grid;
    }
}
```

- [ ] **Step 5: Build the tray project and verify success**

Run:

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj
```

Expected: build succeeds with the connected homepage runtime wired into the tray app.

- [ ] **Step 6: Commit the tray runtime wiring**

Run:

```powershell
git add src/OpenHab.Windows.Tray
git commit -m "feat: wire tray ui to connected homepage runtime"
```

---

### Task 4: Full Verification And Status Recording

**Files:**
- Create: `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`

- [ ] **Step 1: Run the full solution tests**

Run:

```powershell
dotnet test OpenHab.Windows.sln
```

Expected: all solution test projects pass, including the new parser and runtime tests.

- [ ] **Step 2: Run the release build**

Run:

```powershell
dotnet build OpenHab.Windows.sln --configuration Release
```

Expected: release build succeeds with no warnings introduced by this slice.

- [ ] **Step 3: Write the completion status with actual verification output**

Create `docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md`:

```markdown
# openHAB Windows Connected Homepage Status

Date: 2026-05-05

## Source Documents

- Design spec: `docs/superpowers/specs/2026-05-04-openhab-windows-sitemap-client-design.md`
- UI slice status: `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`
- Connected homepage plan: `docs/superpowers/plans/2026-05-05-openhab-windows-connected-homepage.md`

## Completed

- Added sitemap REST JSON parsing from the openHAB homepage payload into existing sitemap models.
- Added a connected homepage runtime that selects the configured endpoint mode, loads the configured sitemap, and surfaces connection status.
- Added a configurable sitemap name in app settings.
- Reused the existing skin renderer to display a live homepage instead of the sample page.
- Wired the WinUI tray surface to load, refresh, and display runtime errors/status.
- Added a first live command path for switch rows by sending the command and reloading the homepage.

## Verification

- `dotnet test OpenHab.Windows.sln`: replace this line with the actual pass/fail counts from the completed run.
- `dotnet build OpenHab.Windows.sln --configuration Release`: replace this line with the actual warning/error counts from the completed run.

## Still Out Of Scope

- Subpage navigation.
- Event stream live item updates.
- Persisted settings and credentials.
- Offline cache persistence.
- WebView/Main UI fallback routing.
- Notifications, telemetry sending, and packaging.
```

Do not commit this status file until the two verification lines contain the real command outcomes.

- [ ] **Step 4: Commit the status record**

Run:

```powershell
git add docs/superpowers/status/2026-05-05-openhab-windows-connected-homepage-status.md
git commit -m "docs: record connected homepage status"
```

---

## Self-Review Checklist

- This plan deliberately stays inside one subsystem: replacing the sample homepage with a live connected homepage slice.
- The plan does not mix in persisted settings, secure credentials, event streams, offline cache persistence, WebView fallback routing, or packaging work.
- The JSON parser task feeds directly into the app runtime task; later tasks do not depend on undefined types.
- The tray shell remains thin; transport/runtime logic lives in `OpenHab.App` and parsing lives in `OpenHab.Sitemaps`.
- Every task includes exact files, commands, and concrete code.
- The main remaining product gaps after this plan are subpage navigation, live updates, persistent settings, and secure credentials, which should be separate follow-on plans.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-05-openhab-windows-connected-homepage.md`.

Two execution options:

1. **Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** - execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints.
