using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Core.Api;
using OpenHab.Core.Events;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using System.IO;
using System.Reflection;

namespace OpenHab.App.Tests.Runtime;

public sealed class SitemapRuntimeControllerTests
{
    private readonly string settingsFilePath = Path.Combine(
        Path.GetTempPath(),
        "OpenHab.App.Tests",
        Guid.NewGuid().ToString("N"),
        "settings.json");

    private AppSettingsController CreateSettingsController()
    {
        return new AppSettingsController(settingsFilePath: settingsFilePath);
    }

    [Fact]
    public async Task LoadUsesLocalEndpointInAutomaticModeWhenLocalSucceeds()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        var cloudClient = new FakeOpenHabClient();
        var controller = CreateRuntimeController(settings, localClient, cloudClient);

        await controller.LoadAsync();

        Assert.Equal(ConnectionState.Online, controller.Current.ConnectionState);
        Assert.False(controller.Current.HasError);
        Assert.Equal(TransportKind.Local, controller.Current.ActiveTransport);
        Assert.Single(localClient.RequestedSitemaps);
        Assert.Equal("default", localClient.RequestedSitemaps[0]);
        Assert.Empty(cloudClient.RequestedSitemaps);
        Assert.NotNull(controller.Current.Descriptor);
    }

    [Fact]
    public async Task LoadUsesLocalClientInLocalOnlyMode()
    {
        var settings = CreateSettingsController();
        settings.SetEndpointMode(EndpointMode.LocalOnly);
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        var cloudClient = new FakeOpenHabClient();
        var controller = CreateRuntimeController(settings, localClient, cloudClient);

        await controller.LoadAsync();

        Assert.Equal(TransportKind.Local, controller.Current.ActiveTransport);
        Assert.Single(localClient.RequestedSitemaps);
        Assert.Empty(cloudClient.RequestedSitemaps);
    }

    [Fact]
    public async Task LoadUsesCloudClientInCloudOnlyMode()
    {
        var settings = CreateSettingsController();
        settings.SetEndpointMode(EndpointMode.CloudOnly);
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        var cloudClient = new FakeOpenHabClient();
        cloudClient.EnqueueSitemapJson(HomepageJson("OFF"));
        var controller = CreateRuntimeController(settings, localClient, cloudClient);

        await controller.LoadAsync();

        Assert.Equal(TransportKind.Cloud, controller.Current.ActiveTransport);
        Assert.Empty(localClient.RequestedSitemaps);
        Assert.Single(cloudClient.RequestedSitemaps);
    }

    [Fact]
    public async Task LoadFallsBackToCloudWhenLocalFailsInAutomaticMode()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapFailure(new InvalidOperationException("Local unavailable"));
        var cloudClient = new FakeOpenHabClient();
        cloudClient.EnqueueSitemapJson(HomepageJson("ON"));
        var controller = CreateRuntimeController(settings, localClient, cloudClient);

        await controller.LoadAsync();

        Assert.Equal(ConnectionState.Online, controller.Current.ConnectionState);
        Assert.Equal(TransportKind.Cloud, controller.Current.ActiveTransport);
        Assert.False(controller.Current.HasError);
        Assert.Single(localClient.RequestedSitemaps);
        Assert.Single(cloudClient.RequestedSitemaps);
        Assert.NotNull(controller.Current.Descriptor);
    }

    [Fact]
    public async Task ActivateSwitchRowSendsCommandAndReloadsHomepage()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        localClient.EnqueueSitemapJson(HomepageJson("ON"));
        var cloudClient = new FakeOpenHabClient();
        var controller = CreateRuntimeController(settings, localClient, cloudClient);

        await controller.LoadAsync();
        var activated = await controller.ActivateRowAsync(0);

        Assert.True(activated);
        var command = Assert.Single(localClient.CommandsSent);
        Assert.Equal("LivingRoom_Light", command.ItemName);
        Assert.Equal("ON", command.Command);
        Assert.Equal(2, localClient.RequestedSitemaps.Count);
    }

    [Fact]
    public async Task RefreshFailurePreservesLastDescriptorAndReportsOfflineState()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        localClient.EnqueueSitemapFailure(new InvalidOperationException("Server down"));
        var cloudClient = new FakeOpenHabClient();
        cloudClient.EnqueueSitemapFailure(new InvalidOperationException("Cloud down"));
        var controller = CreateRuntimeController(settings, localClient, cloudClient);

        await controller.LoadAsync();
        var firstDescriptor = controller.Current.Descriptor;
        await controller.RefreshAsync();

        Assert.Equal(firstDescriptor, controller.Current.Descriptor);
        Assert.Equal(ConnectionState.Offline, controller.Current.ConnectionState);
        Assert.True(controller.Current.HasError);
        Assert.Contains("failed", controller.Current.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshCancellationIsPropagatedAndNotConvertedToOfflineError()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        localClient.EnqueueSitemapFailure(new OperationCanceledException("Canceled"));
        var cloudClient = new FakeOpenHabClient();
        var controller = CreateRuntimeController(settings, localClient, cloudClient);

        await controller.LoadAsync();
        var descriptorBeforeCancel = controller.Current.Descriptor;

        await Assert.ThrowsAsync<OperationCanceledException>(() => controller.RefreshAsync());

        Assert.Equal(descriptorBeforeCancel, controller.Current.Descriptor);
        Assert.Equal(ConnectionState.Online, controller.Current.ConnectionState);
        Assert.False(controller.Current.HasError);
        Assert.False(controller.Current.IsBusy);
    }

    [Fact]
    public async Task NavigationUpdatesBreadcrumbTrailAndSupportsBreadcrumbJump()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageWithChildJson());
        var cloudClient = new FakeOpenHabClient();
        var controller = CreateRuntimeController(settings, localClient, cloudClient);

        await controller.LoadAsync();
        Assert.Equal(["Home"], controller.Current.Breadcrumbs);

        var navigated = await controller.NavigateToChildAsync(0);
        Assert.True(navigated);
        Assert.Equal(["Home", "Kitchen"], controller.Current.Breadcrumbs);

        var jumped = controller.NavigateToBreadcrumb(0);
        Assert.True(jumped);
        Assert.Equal(["Home"], controller.Current.Breadcrumbs);
        Assert.False(controller.CanGoBack);
    }

    [Fact]
    public async Task ApplySearchQueryBuildsVirtualDescriptorWithoutChangingBreadcrumbs()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageWithChildJson());
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());
        await controller.LoadAsync();

        var breadcrumbsBefore = controller.Current.Breadcrumbs.ToArray();
        var descriptorBefore = controller.Current.Descriptor;
        var snapshotChangedCount = 0;
        controller.SnapshotChanged += (_, _) => snapshotChangedCount++;

        controller.ApplySearchQuery("Temperature");

        Assert.NotNull(controller.Current.Descriptor);
        Assert.NotEqual(descriptorBefore, controller.Current.Descriptor);
        Assert.Equal("__search__", controller.Current.Descriptor!.PageId);
        Assert.Equal(breadcrumbsBefore, controller.Current.Breadcrumbs);
        Assert.True(controller.Current.IsSearchActive);
        Assert.Equal("Temperature", controller.Current.SearchQuery);
        Assert.True(controller.Current.SearchResultCount > 0);
        Assert.True(snapshotChangedCount > 0);
    }

    [Fact]
    public async Task ClearSearchRestoresNormalDescriptor()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageWithChildJson());
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());
        await controller.LoadAsync();

        var normalDescriptor = controller.Current.Descriptor;
        controller.ApplySearchQuery("Temperature");
        Assert.Equal("__search__", controller.Current.Descriptor!.PageId);

        var snapshotChangedCount = 0;
        controller.SnapshotChanged += (_, _) => snapshotChangedCount++;
        controller.ClearSearch();

        Assert.NotNull(controller.Current.Descriptor);
        Assert.Equal(normalDescriptor!.PageId, controller.Current.Descriptor!.PageId);
        Assert.Equal(normalDescriptor.Rows.Count, controller.Current.Descriptor.Rows.Count);
        Assert.NotEqual("__search__", controller.Current.Descriptor.PageId);
        Assert.False(controller.Current.IsSearchActive);
        Assert.Equal(string.Empty, controller.Current.SearchQuery);
        Assert.Equal(0, controller.Current.SearchResultCount);
        Assert.True(snapshotChangedCount > 0);
    }

    [Fact]
    public async Task EmptySearchQueryRestoresNormalDescriptor()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageWithChildJson());
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());
        await controller.LoadAsync();

        var normalDescriptor = controller.Current.Descriptor;
        controller.ApplySearchQuery("Temperature");
        Assert.Equal("__search__", controller.Current.Descriptor!.PageId);

        controller.ApplySearchQuery("   ");

        Assert.NotNull(controller.Current.Descriptor);
        Assert.Equal(normalDescriptor!.PageId, controller.Current.Descriptor!.PageId);
        Assert.Equal(normalDescriptor.Rows.Count, controller.Current.Descriptor.Rows.Count);
        Assert.NotEqual("__search__", controller.Current.Descriptor.PageId);
        Assert.False(controller.Current.IsSearchActive);
        Assert.Equal(string.Empty, controller.Current.SearchQuery);
        Assert.Equal(0, controller.Current.SearchResultCount);
    }

    [Fact]
    public async Task NavigationClearsActiveSearch()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageWithChildJson());
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());
        await controller.LoadAsync();

        controller.ApplySearchQuery("Kitchen");
        Assert.True(controller.Current.IsSearchActive);

        var navigated = await controller.NavigateToChildAsync(0);
        Assert.True(navigated);
        Assert.False(controller.Current.IsSearchActive);
        Assert.Equal(string.Empty, controller.Current.SearchQuery);
        Assert.Equal(0, controller.Current.SearchResultCount);

        controller.ApplySearchQuery("Temperature");
        Assert.True(controller.Current.IsSearchActive);

        var navigatedBack = controller.NavigateBack();
        Assert.True(navigatedBack);
        Assert.False(controller.Current.IsSearchActive);
        Assert.Equal(string.Empty, controller.Current.SearchQuery);

        controller.ApplySearchQuery("Kitchen");
        var jumped = controller.NavigateToBreadcrumb(0);
        Assert.True(jumped);
        Assert.False(controller.Current.IsSearchActive);
        Assert.Equal(string.Empty, controller.Current.SearchQuery);
    }

    [Fact]
    public async Task RefreshWhileSearchActiveKeepsQueryAndRecomputesDescriptor()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        localClient.EnqueueSitemapJson(HomepageJson("ON"));
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());
        await controller.LoadAsync();

        controller.ApplySearchQuery("Living Room Light");
        var searchDescriptorBeforeRefresh = controller.Current.Descriptor;

        await controller.RefreshAsync();

        Assert.True(controller.Current.IsSearchActive);
        Assert.Equal("Living Room Light", controller.Current.SearchQuery);
        Assert.NotNull(controller.Current.Descriptor);
        Assert.Equal("__search__", controller.Current.Descriptor!.PageId);
        Assert.NotEqual(searchDescriptorBeforeRefresh, controller.Current.Descriptor);
        Assert.Equal("ON", controller.Current.Descriptor.Rows[1].State);
    }

    [Fact]
    public void ChangedRowsIncludeSearchMetadataChanges()
    {
        var oldDescriptor = new SitemapRenderDescriptor(
            SitemapSkinKind.Windows11,
            "search",
            "Search results",
            [
                new SitemapRowDescriptor(
                    "Lampka nocna",
                    "OFF",
                    RenderControlKind.Toggle,
                    RenderActionKind.SendCommand,
                    RenderDensity.Comfortable,
                    [],
                    SearchResultKey: "search:widget:old",
                    SourcePageId: "home",
                    SourceWidgetId: "old")
            ]);
        var newDescriptor = oldDescriptor with
        {
            Rows =
            [
                oldDescriptor.Rows[0] with
                {
                    SearchResultKey = "search:widget:new",
                    SourcePageId = "lights",
                    SourceWidgetId = "new"
                }
            ]
        };

        var changed = InvokeComputeChangedRowIndices(oldDescriptor, newDescriptor);

        Assert.Equal([0], changed);
    }

    private static SitemapRuntimeController CreateRuntimeController(
        AppSettingsController settings,
        FakeOpenHabClient localClient,
        FakeOpenHabClient cloudClient)
    {
        var renderController = new SitemapRenderController(settings);
        return new SitemapRuntimeController(
            settings,
            renderController,
            (kind, _) => kind == TransportKind.Local ? localClient : cloudClient);
    }

    private static string HomepageJson(string lightState)
    {
        return $$"""
            {
              "homepage": {
                "id": "home",
                "title": "Home",
                "widgets": [
                  {
                    "type": "Switch",
                    "label": "Living Room Light [{{lightState}}]",
                    "item": {
                      "name": "LivingRoom_Light",
                      "state": "{{lightState}}"
                    },
                    "visibility": true
                  },
                  {
                    "type": "Text",
                    "label": "Hallway Temperature [21.4 C]",
                    "item": {
                      "name": "Hallway_Temperature",
                      "state": "21.4 C"
                    },
                    "visibility": true
                  }
                ]
              }
            }
            """;
    }

    private static string HomepageWithChildJson()
    {
        return """
            {
              "homepage": {
                "id": "home",
                "title": "Home",
                "widgets": [
                  {
                    "type": "Group",
                    "label": "Kitchen",
                    "linkedPage": {
                      "id": "kitchen",
                      "title": "Kitchen",
                      "widgets": [
                        {
                          "type": "Text",
                          "label": "Temperature [21.7 C]",
                          "item": {
                            "name": "Kitchen_Temp",
                            "state": "21.7 C"
                          }
                        }
                      ]
                    }
                  }
                ]
              }
            }
            """;
    }

    // ── Event stream tests ──────────────────────────────────────────

    [Fact]
    public async Task StartSitemapEventStreamAllowsRetryAfterSubscribeFailure()
    {
        var settings = CreateSettingsController();
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
        var settings = CreateSettingsController();
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

    [Fact]
    public async Task SitemapEventStreamStaleFailureDoesNotResetNewerSuccessfulStart()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var staleConnect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventClient = new FakeEventStreamClient();
        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        eventClient.ConnectResults.Enqueue(staleConnect.Task);
        var staleStart = controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "kitchen");
        await WaitUntilAsync(() => eventClient.ConnectCalls == 2);

        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "living");
        staleConnect.SetException(new InvalidOperationException("stale connect failed"));
        await staleStart;

        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "living");

        Assert.Equal(ConnectionState.Online, controller.Current.ConnectionState);
        Assert.Equal(3, eventClient.ConnectCalls);
    }

    [Fact]
    public async Task SitemapEventStreamFailureDegradesOnlineSnapshotAndRaisesSnapshotChanged()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        var cloudClient = new FakeOpenHabClient();
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, cloudClient, eventClient);
        var snapshotChanges = 0;
        controller.SnapshotChanged += (_, _) => snapshotChanges++;

        await controller.LoadAsync();
        var snapshotChangesAfterLoad = snapshotChanges;
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "kitchen");

        eventClient.ConnectFailure = new InvalidOperationException("connect failed");
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");

        Assert.Equal(ConnectionState.Degraded, controller.Current.ConnectionState);
        Assert.Contains("Live updates unavailable", controller.Current.StatusText, StringComparison.Ordinal);
        Assert.True(snapshotChanges > snapshotChangesAfterLoad);
    }

    [Fact]
    public async Task SitemapEventStreamNullSubscriptionDegradesOnlineSnapshotAndRaisesSnapshotChanged()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        var cloudClient = new FakeOpenHabClient();
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, cloudClient, eventClient);
        var snapshotChanges = 0;
        controller.SnapshotChanged += (_, _) => snapshotChanges++;

        await controller.LoadAsync();
        var snapshotChangesAfterLoad = snapshotChanges;
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "kitchen");

        eventClient.SubscriptionId = null;
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");

        Assert.Equal(ConnectionState.Degraded, controller.Current.ConnectionState);
        Assert.Contains("Live updates unavailable", controller.Current.StatusText, StringComparison.Ordinal);
        Assert.True(snapshotChanges > snapshotChangesAfterLoad);
    }

    [Fact]
    public async Task SitemapEventStreamCancellationResetsAndPropagates()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var eventClient = new FakeEventStreamClient
        {
            SubscribeFailure = new OperationCanceledException("canceled")
        };
        var controller = CreateRuntimeController(settings, new FakeOpenHabClient(), new FakeOpenHabClient(), eventClient);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home"));

        eventClient.SubscribeFailure = null;
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");

        Assert.Equal(2, eventClient.SubscribeCalls);
        Assert.Equal(1, eventClient.ConnectCalls);
    }

    [Fact]
    public async Task WidgetEventUpdatesWidgetStateInSnapshot()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        var cloudClient = new FakeOpenHabClient();
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, cloudClient, eventClient);

        await controller.LoadAsync();

        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");

        // Verify initial state
        Assert.Equal("OFF", controller.Current.Descriptor!.Rows[0].State);

        // Simulate sitemap widget SSE event: LivingRoom_Light changed to ON
        eventClient.FireWidgetEvent(new SitemapWidgetEvent(
            WidgetId: "w1",
            Label: null,
            Icon: null,
            Visibility: true,
            ItemName: "LivingRoom_Light",
            ItemState: "ON",
            SitemapName: "default",
            PageId: "home",
            DescriptionChanged: false));

        // Assert delta indices
        Assert.Equal(new[] { 0 }, controller.Current.ChangedRowIndices);

        // Assert descriptor shows updated state
        Assert.Equal("ON", controller.Current.Descriptor!.Rows[0].State);

        // Hallway_Temperature should be unchanged
        Assert.Equal("21.4 C", controller.Current.Descriptor!.Rows[1].State);
    }

    [Fact]
    public async Task WidgetEventNoChangeIsIgnored()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        var cloudClient = new FakeOpenHabClient();
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, cloudClient, eventClient);

        await controller.LoadAsync();
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");

        // Fire a widget event with the same state — no change expected
        eventClient.FireWidgetEvent(new SitemapWidgetEvent(
            WidgetId: "w1",
            Label: null,
            Icon: null,
            Visibility: true,
            ItemName: "LivingRoom_Light",
            ItemState: "OFF",
            SitemapName: "default",
            PageId: "home",
            DescriptionChanged: false));

        // Should be empty (no actual change)
        Assert.Empty(controller.Current.ChangedRowIndices);
    }

    [Fact]
    public async Task WidgetEventTriggersSitemapRefreshForCorrectVisibilityAndState()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        localClient.EnqueueSitemapJson(HomepageJson("ON"));
        var cloudClient = new FakeOpenHabClient();
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, cloudClient, eventClient);

        await controller.LoadAsync();
        Assert.Equal("OFF", controller.Current.Descriptor!.Rows[0].State);

        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
        eventClient.FireWidgetEvent(new SitemapWidgetEvent(
            WidgetId: "w1",
            Label: null,
            Icon: null,
            Visibility: true,
            ItemName: "LivingRoom_Light",
            ItemState: "ON",
            SitemapName: "default",
            PageId: "home",
            DescriptionChanged: false));

        for (var i = 0; i < 20 && controller.Current.Descriptor!.Rows[0].State != "ON"; i++)
        {
            await Task.Delay(20);
        }

        Assert.Equal("ON", controller.Current.Descriptor!.Rows[0].State);
        Assert.True(localClient.RequestedSitemaps.Count >= 1);
    }

    private static SitemapRuntimeController CreateRuntimeController(
        AppSettingsController settings,
        FakeOpenHabClient localClient,
        FakeOpenHabClient cloudClient,
        FakeEventStreamClient? eventClient = null)
    {
        var renderController = new SitemapRenderController(settings);
        return new SitemapRuntimeController(
            settings,
            renderController,
            (kind, _) => kind == TransportKind.Local ? localClient : cloudClient,
            eventClient);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(condition());
    }

    private static IReadOnlyList<int> InvokeComputeChangedRowIndices(
        SitemapRenderDescriptor? oldDescriptor,
        SitemapRenderDescriptor newDescriptor)
    {
        var method = typeof(SitemapRuntimeController).GetMethod(
            "ComputeChangedRowIndices",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return (IReadOnlyList<int>)method.Invoke(null, [oldDescriptor, newDescriptor])!;
    }
}

public sealed class FakeEventStreamClient : IOpenHabEventStreamClient
{
    public event EventHandler<OpenHabEvent>? EventReceived;
    public event EventHandler<SitemapWidgetEvent>? WidgetEventReceived;
    public event EventHandler<string>? ConnectionStateChanged;
    public Exception? SubscribeFailure { get; set; }
    public Exception? ConnectFailure { get; set; }
    public string? SubscriptionId { get; set; } = "fake-subscription-id";
    public int SubscribeCalls { get; private set; }
    public int ConnectCalls { get; private set; }
    public Queue<Task> ConnectResults { get; } = new();
    public bool IsConnected { get; private set; }

    public void FireEvent(OpenHabEvent e)
    {
        EventReceived?.Invoke(this, e);
    }

    public void FireWidgetEvent(SitemapWidgetEvent e)
    {
        WidgetEventReceived?.Invoke(this, e);
    }

    public void FireConnectionState(string state)
    {
        ConnectionStateChanged?.Invoke(this, state);
    }

    public async Task ConnectAsync(Uri baseUri, CancellationToken cancellationToken = default)
    {
        ConnectCalls++;
        if (ConnectResults.Count > 0)
        {
            await ConnectResults.Dequeue();
            IsConnected = true;
            return;
        }

        if (ConnectFailure is not null)
        {
            throw ConnectFailure;
        }

        IsConnected = true;
    }

    public Task<string?> SubscribeToSitemapEventsAsync(Uri baseUri, CancellationToken cancellationToken = default)
    {
        SubscribeCalls++;
        if (SubscribeFailure is not null)
        {
            return Task.FromException<string?>(SubscribeFailure);
        }

        return Task.FromResult(SubscriptionId);
    }

    public void Dispose() { }
}
