using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Core.Api;
using OpenHab.Core.Events;
using OpenHab.Core.Profiles;
using OpenHab.Rendering;
using OpenHab.Rendering.Descriptors;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using OpenHab.Core;

namespace OpenHab.App.Tests.Runtime;

public sealed class SitemapRuntimeControllerTests
{
    private static readonly int[] FirstRowChanged = [0];

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
        for (var i = 0; i < 20 && localClient.RequestedSitemaps.Count < 2; i++)
        {
            await Task.Delay(20);
        }

        Assert.Equal(2, localClient.RequestedSitemaps.Count);
    }

    [Fact]
    public async Task ActivateSwitchRowUsesRawBinaryItemStateForFormattedState()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(FormattedSwitchJson("LOCKED", "ON"));
        localClient.EnqueueSitemapJson(FormattedSwitchJson("UNLOCKED", "OFF"));
        var cloudClient = new FakeOpenHabClient();
        var controller = CreateRuntimeController(settings, localClient, cloudClient);

        await controller.LoadAsync();
        var activated = await controller.ActivateRowAsync(0);

        Assert.True(activated);
        var command = Assert.Single(localClient.CommandsSent);
        Assert.Equal("FrontDoor_Lock", command.ItemName);
        Assert.Equal("OFF", command.Command);
    }

    [Fact]
    public async Task ActivateSwitchRowKeepsOptimisticFormattedStateWhenImmediateReconcileIsStale()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(FormattedSwitchJson("UNLOCKED", "OFF"));
        localClient.EnqueueSitemapJson(FormattedSwitchJson("UNLOCKED", "OFF"));
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());

        await controller.LoadAsync();
        var activated = await controller.ActivateRowAsync(0);

        Assert.True(activated);
        var command = Assert.Single(localClient.CommandsSent);
        Assert.Equal("ON", command.Command);
        var row = controller.Current.Descriptor!.Rows[0];
        var visualState = SitemapUiLogic.ResolveToggleVisualState(row);
        Assert.Equal("LOCKED", row.State);
        Assert.Equal("ON", row.RawItemState);
        Assert.Equal("LOCKED", visualState.DisplayText);
        Assert.True(visualState.IsOn);
    }

    [Fact]
    public async Task ActivateSwitchRowReturnsAfterOptimisticStateBeforeReconcileCompletes()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var reconcileStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowReconcile = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(FormattedSwitchJson("UNLOCKED", "OFF"));
        localClient.EnqueueSitemapResponse(async (_, _) =>
        {
            reconcileStarted.SetResult();
            await allowReconcile.Task;
            return FormattedSwitchJson("LOCKED", "ON");
        });
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());

        await controller.LoadAsync();
        var activationTask = controller.ActivateRowAsync(0);
        await reconcileStarted.Task;

        Assert.True(activationTask.IsCompleted);
        Assert.True(await activationTask);
        var row = controller.Current.Descriptor!.Rows[0];
        Assert.Equal("LOCKED", row.State);
        Assert.Equal("ON", row.RawItemState);

        allowReconcile.SetResult();
    }

    [Fact]
    public async Task WidgetEventDuringOptimisticFormattedStateHoldIgnoresStaleRawState()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(FormattedSwitchJson("UNLOCKED", "OFF"));
        localClient.EnqueueSitemapJson(FormattedSwitchJson("UNLOCKED", "OFF"));
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
        await controller.ActivateRowAsync(0);

        eventClient.FireWidgetEvent(new SitemapWidgetEvent(
            WidgetId: "front-door-lock",
            Label: null,
            Icon: null,
            Visibility: true,
            ItemName: "FrontDoor_Lock",
            ItemState: "OFF",
            SitemapName: "default",
            PageId: "home",
            DescriptionChanged: false));

        var row = controller.Current.Descriptor!.Rows[0];
        var visualState = SitemapUiLogic.ResolveToggleVisualState(row);
        Assert.Equal("LOCKED", row.State);
        Assert.Equal("ON", row.RawItemState);
        Assert.True(visualState.IsOn);
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
    public async Task RefreshAsync_UsesSafeStatusForRequestFailure()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        settings.SetEndpointMode(EndpointMode.LocalOnly);

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        localClient.EnqueueSitemapFailure(
            new OpenHabRequestException(
                System.Net.HttpStatusCode.Unauthorized,
                "openHAB request failed with 401 Unauthorized: {\"token\":\"secret\"}"));

        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());
        await controller.LoadAsync();

        await controller.RefreshAsync(CancellationToken.None);

        Assert.Contains("HTTP 401", controller.Current.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", controller.Current.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("token", controller.Current.StatusText, StringComparison.OrdinalIgnoreCase);
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
    public async Task ApplySearchQueryPreservesTrailingSpaceForSearchInput()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageWithChildJson());
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());
        await controller.LoadAsync();

        controller.ApplySearchQuery("Temperature ");

        Assert.True(controller.Current.IsSearchActive);
        Assert.Equal("Temperature ", controller.Current.SearchQuery);
        Assert.Equal("__search__", controller.Current.Descriptor!.PageId);

        await controller.RefreshAsync();

        Assert.True(controller.Current.IsSearchActive);
        Assert.Equal("Temperature ", controller.Current.SearchQuery);
    }

    [Fact]
    public async Task ApplySearchQueryAsyncKeepsLatestSubmittedQuery()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());
        await controller.LoadAsync();

        var first = controller.ApplySearchQueryAsync("Hallway");
        var second = controller.ApplySearchQueryAsync("Living");

        await Task.WhenAll(first, second);

        Assert.True(controller.Current.IsSearchActive);
        Assert.Equal("Living", controller.Current.SearchQuery);
        Assert.Equal("__search__", controller.Current.Descriptor!.PageId);
        Assert.Contains(controller.Current.Descriptor.Rows, row => row.Label == "Living Room Light");
        Assert.DoesNotContain(controller.Current.Descriptor.Rows, row => row.Label == "Hallway Temperature");
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
    public async Task RefreshWhileSearchActiveRecomputesFromLatestSitemapOrder()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageSearchActionJson("OFF"));
        localClient.EnqueueSitemapJson(HomepageSearchActionJson("OFF", reversed: true));
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());
        await controller.LoadAsync();

        controller.ApplySearchQuery("Lampka");
        await controller.RefreshAsync();

        var labels = controller.Current.Descriptor!.Rows
            .Where(row => row.Label.StartsWith("Lampka", StringComparison.Ordinal))
            .Select(row => row.Label)
            .ToArray();
        Assert.Equal(["Lampka mobilna", "Lampka nocna"], labels);
    }

    [Fact]
    public async Task ActivateSearchResultSendsCommandToSourceWidget()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageSearchActionJson("OFF"));
        localClient.EnqueueSitemapJson(HomepageSearchActionJson("ON"));
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());
        await controller.LoadAsync();

        controller.ApplySearchQuery("Lampka nocna");
        var row = Assert.Single(controller.Current.Descriptor!.Rows, r => r.Label == "Lampka nocna");

        var activated = await controller.ActivateRowByKeyAsync(row.SearchResultKey!);

        Assert.True(activated);
        var command = Assert.Single(localClient.CommandsSent);
        Assert.Equal("Bedroom_Lamp", command.ItemName);
        Assert.Equal("ON", command.Command);
        Assert.True(controller.Current.IsSearchActive);
        Assert.Equal("__search__", controller.Current.Descriptor!.PageId);
    }

    [Fact]
    public async Task StaleSearchResultDoesNotCommandWrongWidget()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageSearchActionJson("OFF", includeWidgetIds: false, sameLabels: true));
        localClient.EnqueueSitemapJson(HomepageSearchActionJson("OFF", includeWidgetIds: false, reversed: true, sameLabels: true));
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());
        await controller.LoadAsync();

        controller.ApplySearchQuery("Lampka");
        var row = controller.Current.Descriptor!.Rows.First(r => r.Label == "Lampka");
        await controller.RefreshAsync();

        var activated = await controller.ActivateRowByKeyAsync(row.SearchResultKey!);

        Assert.False(activated);
        Assert.Empty(localClient.CommandsSent);
    }

    [Fact]
    public async Task SearchHeaderRowDoesNotFallBackToCurrentPageIndex()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageSearchActionJson("OFF"));
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());
        await controller.LoadAsync();

        controller.ApplySearchQuery("Lampka");
        var header = Assert.Single(controller.Current.Descriptor!.Rows, r => r.IsSectionHeader && r.Label == "Search results");

        var activated = await controller.ActivateRowByKeyAsync(header.SearchResultKey!);

        Assert.False(activated);
        Assert.Empty(localClient.CommandsSent);
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

    private static SitemapWidgetEvent WidgetEvent(string widgetId, string itemName, string state, string pageId)
    {
        return new SitemapWidgetEvent(
            WidgetId: widgetId,
            Label: null,
            Icon: null,
            Visibility: true,
            ItemName: itemName,
            ItemState: state,
            SitemapName: "default",
            PageId: pageId,
            DescriptionChanged: false);
    }

    private static string HomepageWithOpaqueWidgetIdsJson()
    {
        return """
            {
              "homepage": {
                "id": "home",
                "title": "Home",
                "widgets": [
                  { "type": "Switch", "widgetId": "1:010", "label": "Prefix [OFF]", "item": { "name": "Prefix_Widget", "state": "OFF" } },
                  { "type": "Switch", "widgetId": "2:0010", "label": "Similar [OFF]", "item": { "name": "Similar_Widget", "state": "OFF" } },
                  { "type": "Switch", "widgetId": "2:001100", "label": "Wide [OFF]", "item": { "name": "Wide_Widget", "state": "OFF" } }
                ]
              }
            }
            """;
    }

    private static string HomepageWithNestedButtonsJson()
    {
        return """
            {
              "homepage": {
                "id": "home",
                "title": "Home",
                "widgets": [
                  {
                    "type": "Buttongrid",
                    "widgetId": "2:0011",
                    "label": "Mode [OFF]",
                    "item": { "name": "Mode_Grid", "state": "OFF" },
                    "widgets": [
                      { "type": "Button", "widgetId": "2:001100", "label": "First [OFF]", "item": { "name": "Button_Mode", "state": "OFF" } },
                      { "type": "Button", "widgetId": "2:001101", "label": "Second [OFF]", "item": { "name": "Button_Mode", "state": "OFF" } }
                    ]
                  }
                ]
              }
            }
            """;
    }

    private static string HomepageWithOpaqueChildPageJson()
    {
        return """
            {
              "homepage": {
                "id": "home",
                "title": "Home",
                "widgets": [
                  {
                    "type": "Group",
                    "widgetId": "home:group",
                    "label": "Child",
                    "linkedPage": {
                      "id": "floor/main",
                      "title": "Child",
                      "widgets": [
                        { "type": "Switch", "widgetId": "child:widget", "label": "Child switch [OFF]", "item": { "name": "Shared_Item", "state": "OFF" } }
                      ]
                    }
                  },
                  { "type": "Switch", "widgetId": "home:widget", "label": "Home switch [OFF]", "item": { "name": "Shared_Item", "state": "OFF" } }
                ]
              }
            }
            """;
    }

    private static string HomepageWithLegacyWidgetIdJson()
    {
        return """
            {
              "homepage": {
                "id": "home",
                "title": "Home",
                "widgets": [
                  { "type": "Switch", "widgetId": "000100", "label": "Legacy [OFF]", "item": { "name": "Legacy_Widget", "state": "OFF" } }
                ]
              }
            }
            """;
    }

    private static string HomepageWithMixedWidgetIdsJson()
    {
        return """
            {
              "homepage": {
                "id": "home",
                "title": "Home",
                "widgets": [
                  { "type": "Switch", "widgetId": "identified", "label": "Identified [OFF]", "item": { "name": "Shared_Widget", "state": "OFF" } },
                  { "type": "Switch", "label": "Legacy [OFF]", "item": { "name": "Shared_Widget", "state": "OFF" } }
                ]
              }
            }
            """;
    }

    private static string HomepageWithDuplicateWidgetIdsJson()
    {
        return """
            {
              "homepage": {
                "id": "home",
                "title": "Home",
                "widgets": [
                  { "type": "Switch", "widgetId": "2:001100", "label": "First [OFF]", "item": { "name": "First_Widget", "state": "OFF" } },
                  { "type": "Switch", "widgetId": "2:001100", "label": "Second [OFF]", "item": { "name": "Second_Widget", "state": "OFF" } }
                ]
              }
            }
            """;
    }

    private static string FormattedSwitchJson(string displayState, string rawItemState)
    {
        return $$"""
            {
              "homepage": {
                "id": "home",
                "title": "Home",
                "widgets": [
                  {
                    "type": "Switch",
                    "widgetId": "front-door-lock",
                    "label": "Front Door Lock [{{displayState}}]",
                    "icon": "lock",
                    "item": {
                      "name": "FrontDoor_Lock",
                      "state": "{{rawItemState}}"
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

    private static string HomepageSearchActionJson(
        string lampState,
        bool includeWidgetIds = true,
        bool reversed = false,
        bool sameLabels = false)
    {
        var firstLabel = sameLabels ? "Lampka" : reversed ? "Lampka mobilna" : "Lampka nocna";
        var firstItem = reversed ? "Mobile_Lamp" : "Bedroom_Lamp";
        var firstId = reversed ? "lamp-mobile" : "lamp-night";
        var secondLabel = sameLabels ? "Lampka" : reversed ? "Lampka nocna" : "Lampka mobilna";
        var secondItem = reversed ? "Bedroom_Lamp" : "Mobile_Lamp";
        var secondId = reversed ? "lamp-night" : "lamp-mobile";
        var firstWidgetId = includeWidgetIds
            ? $"                    \"widgetId\": \"{firstId}\",{Environment.NewLine}"
            : string.Empty;
        var secondWidgetId = includeWidgetIds
            ? $"                    \"widgetId\": \"{secondId}\",{Environment.NewLine}"
            : string.Empty;

        return $$"""
            {
              "homepage": {
                "id": "home",
                "title": "Home",
                "widgets": [
                  {
                    {{firstWidgetId}}
                    "type": "Switch",
                    "label": "{{firstLabel}} [{{lampState}}]",
                    "item": {
                      "name": "{{firstItem}}",
                      "state": "{{lampState}}"
                    },
                    "visibility": true
                  },
                  {
                    {{secondWidgetId}}
                    "type": "Switch",
                    "label": "{{secondLabel}} [OFF]",
                    "item": {
                      "name": "{{secondItem}}",
                      "state": "OFF"
                    },
                    "visibility": true
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
        var controller = CreateRuntimeController(settings, new FakeOpenHabClient(), new FakeOpenHabClient(), eventClient);

        eventClient.ConnectResults.Enqueue(staleConnect.Task);
        var staleStart = controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "kitchen");
        await eventClient.WaitUntilConnectStartedAsync();

        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "living");
        eventClient.FireConnectionState("connected");
        staleConnect.SetException(new InvalidOperationException("stale connect failed"));
        await staleStart;

        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "living");

        Assert.Equal(ConnectionState.Online, controller.Current.ConnectionState);
        Assert.Equal(2, eventClient.ConnectCalls);
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
    public async Task DisconnectedConnectionStatePublishesUpdatedSnapshot()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await eventClient.WaitUntilConnectStartedAsync();
        var snapshots = new List<SitemapRuntimeSnapshot>();
        controller.SnapshotChanged += (_, _) => snapshots.Add(controller.Current);

        eventClient.FireConnectionState("disconnected");

        var published = Assert.Single(snapshots);
        Assert.Same(controller.Current, published);
        Assert.Equal(ConnectionState.Degraded, published.ConnectionState);
        Assert.Equal("Live updates unavailable. Refresh manually.", published.StatusText);
    }

    [Fact]
    public async Task ReconnectingConnectionStatePublishesLocalizedStatus()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await eventClient.WaitUntilConnectStartedAsync();
        var snapshots = new List<SitemapRuntimeSnapshot>();
        controller.SnapshotChanged += (_, _) => snapshots.Add(controller.Current);

        eventClient.FireConnectionState("reconnecting");

        var published = Assert.Single(snapshots);
        Assert.Equal(ConnectionState.Online, published.ConnectionState);
        Assert.Equal("Reconnecting to live updates...", published.StatusText);
    }

    [Fact]
    public async Task ConnectedConnectionStateRestoresOnlineAndPublishesSnapshot()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await eventClient.WaitUntilConnectStartedAsync();
        eventClient.FireConnectionState("disconnected");
        var snapshots = new List<SitemapRuntimeSnapshot>();
        controller.SnapshotChanged += (_, _) => snapshots.Add(controller.Current);

        eventClient.FireConnectionState("connected");

        var published = Assert.Single(snapshots);
        Assert.Equal(ConnectionState.Online, published.ConnectionState);
        Assert.Equal("Connected via local.", published.StatusText);
    }

    [Fact]
    public async Task RepeatedIdenticalConnectionStateDoesNotPublishRedundantSnapshot()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await eventClient.WaitUntilConnectStartedAsync();
        var snapshotChanges = 0;
        controller.SnapshotChanged += (_, _) => snapshotChanges++;

        eventClient.FireConnectionState("disconnected");
        eventClient.FireConnectionState("disconnected");

        Assert.Equal(1, snapshotChanges);
    }

    [Fact]
    public async Task ReconnectSitemapEventStreamObservesConnectFailure()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, new FakeOpenHabClient(), new FakeOpenHabClient(), eventClient);

        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
        var expected = new InvalidOperationException("reconnect failed");
        eventClient.ConnectFailure = expected;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.ReconnectSitemapEventStreamAsync(
                new Uri("http://localhost:8080"),
                "default",
                "home"));

        Assert.Same(expected, thrown);
        Assert.Equal(2, eventClient.ConnectCalls);
    }

    [Theory]
    [InlineData("https://proxy.test/openhab")]
    [InlineData("https://proxy.test/openhab/")]
    public async Task StartAndReconnectSitemapEventStreamPreserveReverseProxyBasePath(string endpointText)
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default-sitemap");
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, new FakeOpenHabClient(), new FakeOpenHabClient(), eventClient);
        var endpoint = new Uri(endpointText);
        const string sitemapName = "default-sitemap";
        const string pageId = "page with spaces";

        await controller.StartSitemapEventStreamAsync(endpoint, sitemapName, pageId);
        await controller.ReconnectSitemapEventStreamAsync(endpoint, sitemapName, pageId);

        var expectedSseUri =
            "https://proxy.test/openhab/rest/sitemaps/events/fake-subscription-id?sitemap=default-sitemap&pageid=page%20with%20spaces";
        Assert.Equal(endpoint.AbsoluteUri, Assert.Single(eventClient.SubscribeUris).AbsoluteUri);
        Assert.Equal(expectedSseUri, eventClient.ConnectUris[0].AbsoluteUri);
        Assert.Equal(expectedSseUri, eventClient.ConnectUris[1].AbsoluteUri);

        var query = System.Web.HttpUtility.ParseQueryString(eventClient.ConnectUris[0].Query);
        Assert.Equal(sitemapName, query["sitemap"]);
        Assert.Equal(pageId, query["pageid"]);
    }

    [Fact]
    public async Task StartSitemapEventStreamDoesNotLogUriUserInfo()
    {
        var capturedLines = new ConcurrentQueue<string>();
        using var capture = DiagnosticLogger.BeginLogCapture(true, capturedLines.Enqueue);
        var userMarker = $"user{Guid.NewGuid():N}";
        var passwordMarker = $"password{Guid.NewGuid():N}";
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, new FakeOpenHabClient(), new FakeOpenHabClient(), eventClient);

        await controller.StartSitemapEventStreamAsync(
            new Uri($"https://{userMarker}:{passwordMarker}@proxy.test/openhab"),
            "default",
            "home");

        Assert.DoesNotContain(capturedLines, line => line.Contains(userMarker, StringComparison.Ordinal));
        Assert.DoesNotContain(capturedLines, line => line.Contains(passwordMarker, StringComparison.Ordinal));
        Assert.Contains(capturedLines, line =>
            line.Contains("https://proxy.test/openhab", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1:0")]
    [InlineData("2:0011")]
    [InlineData("floor/main")]
    [InlineData("page with spaces")]
    public async Task ReconnectPreservesAndEscapesOpaquePageId(string pageId)
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, new FakeOpenHabClient(), new FakeOpenHabClient(), eventClient);

        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
        await controller.ReconnectSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", pageId);

        var reconnectUri = eventClient.ConnectUris.Last();
        var query = System.Web.HttpUtility.ParseQueryString(reconnectUri.Query);
        Assert.Equal(pageId, query["pageid"]);
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
    public async Task StopSitemapEventStreamClearsStateAndAllowsRestart()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, new FakeOpenHabClient(), new FakeOpenHabClient(), eventClient);

        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
        Assert.Equal(1, eventClient.SubscribeCalls);
        Assert.Equal(1, eventClient.ConnectCalls);

        controller.StopSitemapEventStream();

        Assert.Equal(1, eventClient.DisposeCount);
        Assert.False(eventClient.IsConnected);

        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");

        Assert.Equal(2, eventClient.SubscribeCalls);
        Assert.Equal(2, eventClient.ConnectCalls);
        Assert.True(eventClient.IsConnected);
    }

    [Fact]
    public async Task StopSitemapEventStreamWhileConnectInFlightPreventsStaleReconnectAndAllowsRestart()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var eventClient = new FakeEventStreamClient
        {
            ConnectBlock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var controller = CreateRuntimeController(settings, new FakeOpenHabClient(), new FakeOpenHabClient(), eventClient);

        var staleStart = controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
        await eventClient.WaitUntilConnectStartedAsync();

        controller.StopSitemapEventStream();
        Assert.False(eventClient.IsConnected);
        Assert.Equal(1, eventClient.DisposeCount);

        eventClient.ConnectBlock.SetResult();
        await staleStart;

        Assert.Equal(1, eventClient.ConnectCalls);
        Assert.False(eventClient.IsConnected);
        Assert.Equal(1, eventClient.DisposeCount);

        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");

        Assert.Equal(2, eventClient.SubscribeCalls);
        Assert.Equal(2, eventClient.ConnectCalls);
        Assert.True(eventClient.IsConnected);
    }

    [Fact]
    public async Task StopSitemapEventStreamWhileConnectInFlightThenCanceledDoesNotFaultStaleStart()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var connectResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventClient = new FakeEventStreamClient();
        eventClient.ConnectResults.Enqueue(connectResult.Task);
        var controller = CreateRuntimeController(settings, new FakeOpenHabClient(), new FakeOpenHabClient(), eventClient);

        var staleStart = controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
        await eventClient.WaitUntilConnectStartedAsync();

        controller.StopSitemapEventStream();
        Assert.False(eventClient.IsConnected);
        Assert.Equal(1, eventClient.DisposeCount);

        connectResult.SetException(new OperationCanceledException("canceled"));
        await staleStart;

        Assert.Equal(1, eventClient.ConnectCalls);
        Assert.False(eventClient.IsConnected);
        Assert.Equal(1, eventClient.DisposeCount);
    }

    [Fact]
    public async Task OverlappingStartsStaleCompletionDoesNotDisposeActiveConnection()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var attemptAConnectBlock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventClient = new FakeEventStreamClient();
        eventClient.ConnectBlocks.Enqueue(attemptAConnectBlock);
        var controller = CreateRuntimeController(settings, new FakeOpenHabClient(), new FakeOpenHabClient(), eventClient);

        var attemptA = controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "kitchen");
        await eventClient.WaitUntilConnectStartedAsync(1);

        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "living");
        Assert.True(eventClient.IsConnected);
        Assert.Equal(2, eventClient.ConnectCalls);
        Assert.Equal(0, eventClient.DisposeCount);

        attemptAConnectBlock.SetResult();
        await attemptA;

        Assert.True(eventClient.IsConnected);
        Assert.Equal(2, eventClient.ConnectCalls);
        Assert.Equal(0, eventClient.DisposeCount);
    }

    [Fact]
    public async Task NavigateToChildDoesNotWaitForColdSitemapEventSubscriptionStartup()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageWithChildJson());
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        eventClient.BlockSubscribeAsynchronously = true;

        try
        {
            var navigationTask = controller.NavigateToChildAsync(0);

            Assert.True(navigationTask.IsCompleted, "Navigation should not wait for cold sitemap event subscription startup.");
            Assert.True(await navigationTask);
            Assert.Equal("kitchen", controller.Current.Descriptor!.PageId);
        }
        finally
        {
            eventClient.ReleaseSubscribeBlock();
        }
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
        Assert.Equal(FirstRowChanged, controller.Current.ChangedRowIndices);

        // Assert descriptor shows updated state
        Assert.Equal("ON", controller.Current.Descriptor!.Rows[0].State);

        // Hallway_Temperature should be unchanged
        Assert.Equal("21.4 C", controller.Current.Descriptor!.Rows[1].State);
    }

    [Fact]
    public async Task OpenHab52VariableWidthWidgetEventUpdatesMatchingRow()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageWithOpaqueWidgetIdsJson());
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
        var snapshots = new List<SitemapRuntimeSnapshot>();
        controller.SnapshotChanged += (_, _) => snapshots.Add(controller.Current);

        eventClient.FireWidgetEvent(WidgetEvent("2:001100", "Wide_Widget", "ON", "home"));

        var snapshot = Assert.Single(snapshots);
        Assert.Equal([2], snapshot.ChangedRowIndices);
        Assert.Equal("OFF", snapshot.Descriptor!.Rows[0].State);
        Assert.Equal("OFF", snapshot.Descriptor.Rows[1].State);
        Assert.Equal("ON", snapshot.Descriptor.Rows[2].State);
    }

    [Fact]
    public async Task OpenHab52WidgetEventDoesNotMatchSimilarSuffix()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageWithOpaqueWidgetIdsJson());
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
        var snapshots = new List<SitemapRuntimeSnapshot>();
        controller.SnapshotChanged += (_, _) => snapshots.Add(controller.Current);

        eventClient.FireWidgetEvent(WidgetEvent("2:0010", "Similar_Widget", "ON", "home"));

        var snapshot = Assert.Single(snapshots);
        Assert.Equal([1], snapshot.ChangedRowIndices);
        Assert.Equal("OFF", snapshot.Descriptor!.Rows[0].State);
        Assert.Equal("ON", snapshot.Descriptor.Rows[1].State);
        Assert.Equal("OFF", snapshot.Descriptor.Rows[2].State);
    }

    [Fact]
    public async Task OpenHab52NestedButtonEventUpdatesOnlyMatchingButton()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageWithNestedButtonsJson());
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
        var snapshots = new List<SitemapRuntimeSnapshot>();
        controller.SnapshotChanged += (_, _) => snapshots.Add(controller.Current);

        eventClient.FireWidgetEvent(WidgetEvent("2:001100", "Button_Mode", "ON", "home"));

        var snapshot = Assert.Single(snapshots);
        Assert.Equal([1], snapshot.ChangedRowIndices);
        Assert.Equal("OFF", snapshot.Descriptor!.Rows[0].State);
        Assert.Equal("ON", snapshot.Descriptor.Rows[1].State);
        Assert.Equal("OFF", snapshot.Descriptor.Rows[2].State);
    }

    [Fact]
    public async Task WidgetEventFromPreviousPageDoesNotMutateCurrentPage()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageWithOpaqueChildPageJson());
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
        Assert.True(await controller.NavigateToChildAsync(0));
        var snapshots = new List<SitemapRuntimeSnapshot>();
        controller.SnapshotChanged += (_, _) => snapshots.Add(controller.Current);

        eventClient.FireWidgetEvent(WidgetEvent("home:widget", "Shared_Item", "ON", "home"));

        Assert.Empty(snapshots);
        Assert.Equal("OFF", controller.Current.Descriptor!.Rows[0].State);
    }

    [Fact]
    public async Task WidgetEventWithKnownNonMatchingIdDoesNotFallbackToItemName()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageWithOpaqueWidgetIdsJson());
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
        var snapshots = new List<SitemapRuntimeSnapshot>();
        controller.SnapshotChanged += (_, _) => snapshots.Add(controller.Current);

        eventClient.FireWidgetEvent(WidgetEvent("not:an:existing:widget", "Wide_Widget", "ON", "home"));

        Assert.Empty(snapshots);
        Assert.Equal("OFF", controller.Current.Descriptor!.Rows[2].State);
    }

    [Fact]
    public async Task LegacyWidgetIdStillUpdatesMatchingRow()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageWithLegacyWidgetIdJson());
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");

        eventClient.FireWidgetEvent(WidgetEvent("000100", "Legacy_Widget", "ON", "home"));

        Assert.Equal(FirstRowChanged, controller.Current.ChangedRowIndices);
        Assert.Equal("ON", controller.Current.Descriptor!.Rows[0].State);
    }

    [Fact]
    public async Task ContextlessWidgetEventWithoutStreamContextDoesNotMutateCurrentPage()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");

        eventClient.FireWidgetEvent(WidgetEvent("w1", "LivingRoom_Light", "ON", pageId: string.Empty) with { SitemapName = string.Empty });

        Assert.Equal("OFF", controller.Current.Descriptor!.Rows[0].State);
    }

    [Fact]
    public async Task ContextlessWidgetEventFromCurrentStreamUpdatesCurrentPage()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
        await eventClient.WaitUntilConnectStartedAsync();

        eventClient.FireContextlessWidgetEventFromConnection(0, WidgetEvent("w1", "LivingRoom_Light", "ON", string.Empty) with { SitemapName = string.Empty });

        Assert.Equal("ON", controller.Current.Descriptor!.Rows[0].State);
    }

    [Fact]
    public async Task ContextlessWidgetEventFromPreviousStreamDoesNotMutateCurrentPage()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageWithOpaqueChildPageJson());
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
        await eventClient.WaitUntilConnectStartedAsync();
        Assert.True(await controller.NavigateToChildAsync(0));

        eventClient.FireContextlessWidgetEventFromConnection(0, WidgetEvent("home:widget", "Shared_Item", "ON", string.Empty) with { SitemapName = string.Empty });

        Assert.Equal("OFF", controller.Current.Descriptor!.Rows[0].State);
    }

    [Fact]
    public async Task BlankWidgetIdEventOnMixedIdPageUpdatesOnlyBlankIdRows()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageWithMixedWidgetIdsJson());
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");

        eventClient.FireWidgetEvent(WidgetEvent(string.Empty, "Shared_Widget", "ON", "home"));

        Assert.Equal([1], controller.Current.ChangedRowIndices);
        Assert.Equal("OFF", controller.Current.Descriptor!.Rows[0].State);
        Assert.Equal("ON", controller.Current.Descriptor.Rows[1].State);
    }

    [Fact]
    public async Task DuplicateWidgetIdEventDoesNotChooseAnArbitraryRow()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageWithDuplicateWidgetIdsJson());
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");

        eventClient.FireWidgetEvent(WidgetEvent("2:001100", "Second_Widget", "ON", "home"));

        Assert.Empty(controller.Current.ChangedRowIndices);
        Assert.All(controller.Current.Descriptor!.Rows, row => Assert.Equal("OFF", row.State));
    }

    [Fact]
    public async Task SitemapEventStreamStartDistinguishesOrdinalPageIds()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, new FakeOpenHabClient(), new FakeOpenHabClient(), eventClient);

        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "Page");
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "page");

        Assert.Equal(2, eventClient.ConnectCalls);
    }

    [Theory]
    [InlineData("UNLOCKED", "OFF", "ON", "LOCKED", true)]
    [InlineData("LOCKED", "ON", "OFF", "UNLOCKED", false)]
    public async Task WidgetEventForFormattedLockStateDoesNotPublishRawBinaryState(
        string initialDisplayState,
        string initialRawItemState,
        string eventRawItemState,
        string expectedDisplayState,
        bool expectedIsOn)
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(FormattedSwitchJson(initialDisplayState, initialRawItemState));
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");

        eventClient.FireWidgetEvent(new SitemapWidgetEvent(
            WidgetId: "front-door-lock",
            Label: null,
            Icon: null,
            Visibility: true,
            ItemName: "FrontDoor_Lock",
            ItemState: eventRawItemState,
            SitemapName: "default",
            PageId: "home",
            DescriptionChanged: false));

        var row = controller.Current.Descriptor!.Rows[0];
        var visualState = SitemapUiLogic.ResolveToggleVisualState(row);
        Assert.Equal(expectedDisplayState, row.State);
        Assert.Equal(expectedDisplayState, visualState.DisplayText);
        Assert.Equal(expectedIsOn, visualState.IsOn);
    }

    [Fact]
    public async Task WidgetEventWhileSearchActivePublishesSearchDescriptor()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageJson("OFF"));
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
        controller.ApplySearchQuery("Living Room Light");

        var snapshots = new List<SitemapRuntimeSnapshot>();
        controller.SnapshotChanged += (_, _) => snapshots.Add(controller.Current);

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

        var eventSnapshot = Assert.Single(snapshots);
        Assert.True(eventSnapshot.IsSearchActive);
        Assert.Equal("__search__", eventSnapshot.Descriptor!.PageId);
        Assert.Contains(eventSnapshot.Descriptor.Rows, row => row.Label == "Living Room Light" && row.State == "ON");
    }

    [Fact]
    public async Task SearchDescriptorRemovesResultWhenSitemapWidgetEventHidesIt()
    {
        var settings = CreateSettingsController();
        settings.SetSitemapName("default");

        var localClient = new FakeOpenHabClient();
        localClient.EnqueueSitemapJson(HomepageSearchActionJson("OFF"));
        var eventClient = new FakeEventStreamClient();
        var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

        await controller.LoadAsync();
        await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
        controller.ApplySearchQuery("Lampka nocna");

        eventClient.FireWidgetEvent(new SitemapWidgetEvent(
            WidgetId: "lamp-night",
            Label: null,
            Icon: null,
            Visibility: false,
            ItemName: "Bedroom_Lamp",
            ItemState: "OFF",
            SitemapName: "default",
            PageId: "home",
            DescriptionChanged: false));

        Assert.True(controller.Current.IsSearchActive);
        Assert.DoesNotContain(controller.Current.Descriptor!.Rows, row => row.Label == "Lampka nocna");
        Assert.Contains(controller.Current.Descriptor.Rows, row => row.Label == "No matching sitemap elements");
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

public sealed partial class FakeEventStreamClient : IOpenHabEventStreamClient
{
    public event EventHandler<OpenHabEvent>? EventReceived;
    public event EventHandler<SitemapWidgetEvent>? WidgetEventReceived;
    public event EventHandler<string>? ConnectionStateChanged;
    public Exception? SubscribeFailure { get; set; }
    public Exception? ConnectFailure { get; set; }
    public string? SubscriptionId { get; set; } = "fake-subscription-id";
    public TaskCompletionSource? ConnectBlock { get; set; }
    public int SubscribeCalls { get; private set; }
    public int ConnectCalls { get; private set; }
    public int DisposeCount { get; private set; }
    public List<Uri> ConnectUris { get; } = new();
    public List<Uri> SubscribeUris { get; } = new();
    public bool BlockSubscribeSynchronously { get; set; }
    public bool BlockSubscribeAsynchronously { get; set; }
    public Queue<TaskCompletionSource> ConnectBlocks { get; } = new();
    public Queue<Task> ConnectResults { get; } = new();
    public bool IsConnected { get; private set; }
    private int disposeGeneration;
    private readonly List<TaskCompletionSource> connectStartedSignals = [];
    private readonly ManualResetEventSlim subscribeBlock = new(initialState: false);
    private readonly TaskCompletionSource subscribeAsyncBlock = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void FireEvent(OpenHabEvent e)
    {
        EventReceived?.Invoke(this, e);
    }

    public void FireWidgetEvent(SitemapWidgetEvent e)
    {
        WidgetEventReceived?.Invoke(this, e);
    }

    public void FireContextlessWidgetEventFromConnection(int connectionIndex, SitemapWidgetEvent e)
    {
        var query = System.Web.HttpUtility.ParseQueryString(ConnectUris[connectionIndex].Query);
        FireWidgetEvent(e with
        {
            SitemapName = query["sitemap"] ?? string.Empty,
            PageId = query["pageid"] ?? string.Empty
        });
    }

    public void FireConnectionState(string state)
    {
        ConnectionStateChanged?.Invoke(this, state);
    }

    public async Task ConnectAsync(Uri baseUri, CancellationToken cancellationToken = default)
    {
        ConnectCalls++;
        ConnectUris.Add(baseUri);
        var connectStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connectStartedSignals.Add(connectStarted);
        connectStarted.TrySetResult();
        var generationAtStart = disposeGeneration;

        if (ConnectBlock is not null)
        {
            await ConnectBlock.Task.WaitAsync(cancellationToken);
        }

        if (ConnectBlocks.Count > 0)
        {
            await ConnectBlocks.Dequeue().Task.WaitAsync(cancellationToken);
        }

        if (ConnectResults.Count > 0)
        {
            await ConnectResults.Dequeue();
            if (generationAtStart != disposeGeneration)
            {
                return;
            }

            IsConnected = true;
            return;
        }

        if (ConnectFailure is not null)
        {
            throw ConnectFailure;
        }

        if (generationAtStart != disposeGeneration)
        {
            return;
        }

        IsConnected = true;
    }

    public async Task WaitUntilConnectStartedAsync(int callNumber = 1)
    {
        while (connectStartedSignals.Count < callNumber)
        {
            await Task.Delay(10);
        }

        await connectStartedSignals[callNumber - 1].Task;
    }

    public async Task<string?> SubscribeToSitemapEventsAsync(Uri baseUri, CancellationToken cancellationToken = default)
    {
        SubscribeCalls++;
        SubscribeUris.Add(baseUri);
        if (BlockSubscribeSynchronously)
        {
            subscribeBlock.Wait(cancellationToken);
        }

        if (BlockSubscribeAsynchronously)
        {
            await subscribeAsyncBlock.Task.WaitAsync(cancellationToken);
        }

        if (SubscribeFailure is not null)
        {
            throw SubscribeFailure;
        }

        return SubscriptionId;
    }

    public void ReleaseSubscribeBlock()
    {
        subscribeBlock.Set();
        subscribeAsyncBlock.TrySetResult();
    }

    public void Dispose()
    {
        DisposeCount++;
        disposeGeneration++;
        IsConnected = false;
    }
}
