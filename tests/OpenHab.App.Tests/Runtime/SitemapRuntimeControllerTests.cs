using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Core.Profiles;
using System.IO;

namespace OpenHab.App.Tests.Runtime;

public sealed class SitemapRuntimeControllerTests
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenHab.WinApp",
        "settings.json");

    public SitemapRuntimeControllerTests()
    {
        // Retry deletion — fire-and-forget SaveAsync from a previous test may still be writing.
        for (int i = 0; i < 5; i++)
        {
            try { File.Delete(SettingsFilePath); } catch { }
            if (!File.Exists(SettingsFilePath)) break;
            Thread.Sleep(10);
        }
    }

    [Fact]
    public async Task LoadUsesLocalEndpointInAutomaticModeWhenLocalSucceeds()
    {
        var settings = new AppSettingsController();
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
        var settings = new AppSettingsController();
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
        var settings = new AppSettingsController();
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
        var settings = new AppSettingsController();
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
        var settings = new AppSettingsController();
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
        var settings = new AppSettingsController();
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
        var settings = new AppSettingsController();
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
        var settings = new AppSettingsController();
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
}
