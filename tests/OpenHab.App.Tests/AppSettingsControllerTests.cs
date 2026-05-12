using OpenHab.App.Settings;
using OpenHab.App.Tests.Settings;
using OpenHab.Core.Auth;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using System.IO;

namespace OpenHab.App.Tests;

public sealed class AppSettingsControllerTests
{
    private readonly string settingsFilePath = Path.Combine(
        Path.GetTempPath(),
        "OpenHab.App.Tests",
        Guid.NewGuid().ToString("N"),
        "settings.json");

    private AppSettingsController CreateController(ICredentialStore? credentialStore = null)
    {
        return new AppSettingsController(credentialStore, settingsFilePath);
    }

    [Fact]
    public void DefaultsUseWindows11SkinAndAutomaticEndpointMode()
    {
        var controller = CreateController();

        Assert.Equal(SitemapSkinKind.Windows11, controller.Current.Skin);
        Assert.Equal(EndpointMode.Automatic, controller.Current.EndpointMode);
        Assert.Equal(new Uri("http://openhab:8080"), controller.Current.LocalEndpoint);
        Assert.Equal(new Uri("https://myopenhab.org"), controller.Current.CloudEndpoint);
        Assert.Equal(string.Empty, controller.Current.SitemapName);
        Assert.Equal(460, controller.Current.FlyoutWidth);
        Assert.Equal(FlyoutAnimationSpeed.Default, controller.Current.AnimationSpeed);
        Assert.Equal(ChartQuality.High, controller.Current.ChartQuality);
        Assert.Empty(controller.Current.ImportantNotificationTags);
        Assert.False(controller.Current.HasLocalToken);
        Assert.False(controller.Current.HasCloudCredentials);
        Assert.Null(controller.Current.CloudUserName);
    }

    [Fact]
    public void CanChangeSkinAndEndpointMode()
    {
        var controller = CreateController();

        controller.SetSkin(SitemapSkinKind.Basic);
        controller.SetEndpointMode(EndpointMode.CloudOnly);

        Assert.Equal(SitemapSkinKind.Basic, controller.Current.Skin);
        Assert.Equal(EndpointMode.CloudOnly, controller.Current.EndpointMode);
    }

    [Fact]
    public void CanChangeSitemapName()
    {
        var controller = CreateController();

        controller.SetSitemapName("home");

        Assert.Equal("home", controller.Current.SitemapName);
    }

    [Fact]
    public async Task FlushAsyncPersistsLatestQueuedSetting()
    {
        var controller = CreateController();

        controller.SetSitemapName("first");
        controller.SetSitemapName("second");
        await controller.FlushAsync();

        var reloaded = CreateController();
        Assert.Equal("second", reloaded.Current.SitemapName);
    }

    [Fact]
    public async Task FlushAsyncWaitsForSavesQueuedByPriorControllerInstances()
    {
        var first = CreateController();

        first.SetSitemapName("from-prior-controller");
        var second = CreateController();
        await second.FlushAsync();

        var reloaded = CreateController();
        Assert.Equal("from-prior-controller", reloaded.Current.SitemapName);
    }

    [Fact]
    public async Task FlushAsyncCompletesWhenNoSaveIsQueued()
    {
        var controller = CreateController();

        await controller.FlushAsync();

        Assert.NotNull(controller.Current);
    }

    [Fact]
    public void CanChangeFlyoutWidth()
    {
        var controller = CreateController();

        controller.SetFlyoutWidth(420);

        Assert.Equal(420, controller.Current.FlyoutWidth);
    }

    [Fact]
    public void SetFlyoutWidthRejectsOutOfRangeValues()
    {
        var controller = CreateController();

        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetFlyoutWidth(200));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetFlyoutWidth(1200));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void SetSitemapNameClearsSelectionForBlankInput(string sitemapName)
    {
        var controller = CreateController();
        controller.SetSitemapName("home");

        controller.SetSitemapName(sitemapName);

        Assert.Equal(string.Empty, controller.Current.SitemapName);
    }

    [Fact]
    public void SetSitemapNameRejectsInvalidCharacters()
    {
        var controller = CreateController();

        var exception = Assert.Throws<ArgumentException>(() => controller.SetSitemapName("home/main"));

        Assert.Equal("sitemapName", exception.ParamName);
    }

    [Fact]
    public void RejectsRelativeEndpointUris()
    {
        var controller = CreateController();

        var exception = Assert.Throws<ArgumentException>(() =>
            controller.SetEndpoints(new Uri("/rest", UriKind.Relative), new Uri("https://myopenhab.org")));

        Assert.Contains("absolute", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetEndpointsThrowsWhenLocalEndpointIsNull()
    {
        var controller = CreateController();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            controller.SetEndpoints(null!, new Uri("https://myopenhab.org")));

        Assert.Equal("localEndpoint", exception.ParamName);
    }

    [Fact]
    public void SetEndpointsThrowsWhenCloudEndpointIsNull()
    {
        var controller = CreateController();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            controller.SetEndpoints(new Uri("http://openhab:8080"), null!));

        Assert.Equal("cloudEndpoint", exception.ParamName);
    }

    [Fact]
    public void SetEndpointsRejectsNonHttpAbsoluteUris()
    {
        var controller = CreateController();

        var exception = Assert.Throws<ArgumentException>(() =>
            controller.SetEndpoints(new Uri("ftp://openhab.local:21"), new Uri("https://myopenhab.org")));

        Assert.Contains("HTTP or HTTPS", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetEndpointsRejectsNonHttpAbsoluteCloudUri()
    {
        var controller = CreateController();

        var exception = Assert.Throws<ArgumentException>(() =>
            controller.SetEndpoints(new Uri("http://openhab:8080"), new Uri("ftp://myopenhab.org")));

        Assert.Contains("HTTP or HTTPS", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetEndpointsRejectsLocalUriUserInfo()
    {
        var controller = CreateController();

        var exception = Assert.Throws<ArgumentException>(() =>
            controller.SetEndpoints(new Uri("http://user:pass@openhab:8080"), new Uri("https://myopenhab.org")));

        Assert.Contains("user information", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetEndpointsRejectsCloudUriUserInfo()
    {
        var controller = CreateController();

        var exception = Assert.Throws<ArgumentException>(() =>
            controller.SetEndpoints(new Uri("http://openhab:8080"), new Uri("https://user:pass@myopenhab.org")));

        Assert.Contains("user information", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DefaultsHaveNoTokensOrCloudCredentials()
    {
        var controller = CreateController();

        Assert.False(controller.Current.HasLocalToken);
        Assert.False(controller.Current.HasCloudCredentials);
        Assert.Null(controller.Current.CloudUserName);
    }

    [Fact]
    public async Task InitializeAsyncHydratesTokenAndCloudCredentialFlags()
    {
        var store = new FakeCredentialStore();
        await store.StoreAsync("OpenHabAuth", "local-token", "local", CancellationToken.None);
        await store.StoreAsync("OpenHabAuth", "cloud-username", "cloud-user", CancellationToken.None);
        await store.StoreAsync("OpenHabAuth", "cloud-password", "cloud-password", CancellationToken.None);
        var controller = CreateController(store);

        Assert.False(controller.Current.HasLocalToken);
        Assert.False(controller.Current.HasCloudCredentials);
        Assert.Null(controller.Current.CloudUserName);

        await controller.InitializeAsync();

        Assert.True(controller.Current.HasLocalToken);
        Assert.True(controller.Current.HasCloudCredentials);
        Assert.Equal("cloud-user", controller.Current.CloudUserName);
    }

    [Fact]
    public async Task SetAndClearLocalApiToken()
    {
        var store = new FakeCredentialStore();
        var controller = CreateController(store);

        await controller.SetApiTokenAsync(TransportKind.Local, "my-local-token");

        Assert.True(controller.Current.HasLocalToken);
        Assert.False(controller.Current.HasCloudCredentials);
        Assert.Null(controller.Current.CloudUserName);
        Assert.Equal("my-local-token", await store.RetrieveAsync("OpenHabAuth", "local-token", CancellationToken.None));

        await controller.ClearApiTokenAsync(TransportKind.Local);

        Assert.False(controller.Current.HasLocalToken);
        Assert.False(controller.Current.HasCloudCredentials);
        Assert.Null(controller.Current.CloudUserName);
        Assert.Null(await store.RetrieveAsync("OpenHabAuth", "local-token", CancellationToken.None));
    }

    [Fact]
    public async Task SetGetAndClearCloudCredentials()
    {
        var store = new FakeCredentialStore();
        var controller = CreateController(store);

        await controller.SetCloudCredentialsAsync("my-cloud-user", "my-cloud-password");

        Assert.True(controller.Current.HasCloudCredentials);
        Assert.False(controller.Current.HasLocalToken);
        Assert.Equal("my-cloud-user", controller.Current.CloudUserName);
        Assert.Equal("my-cloud-user", await store.RetrieveAsync("OpenHabAuth", "cloud-username", CancellationToken.None));
        Assert.Equal("my-cloud-password", await store.RetrieveAsync("OpenHabAuth", "cloud-password", CancellationToken.None));

        var credentials = await controller.GetCloudCredentialsAsync();

        Assert.NotNull(credentials);
        Assert.Equal("my-cloud-user", credentials!.UserName);
        Assert.Equal("my-cloud-password", credentials.Password);

        await controller.ClearCloudCredentialsAsync();

        Assert.False(controller.Current.HasCloudCredentials);
        Assert.False(controller.Current.HasLocalToken);
        Assert.Null(controller.Current.CloudUserName);
        Assert.Null(await store.RetrieveAsync("OpenHabAuth", "cloud-username", CancellationToken.None));
        Assert.Null(await store.RetrieveAsync("OpenHabAuth", "cloud-password", CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task SetApiTokenRejectsBlankToken(string token)
    {
        var store = new FakeCredentialStore();
        var controller = CreateController(store);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => controller.SetApiTokenAsync(TransportKind.Local, token));

        Assert.Equal("token", exception.ParamName);
    }

    [Fact]
    public async Task GetApiTokenAsyncReturnsTokenFromStore()
    {
        var store = new FakeCredentialStore();
        await store.StoreAsync("OpenHabAuth", "local-token", "pre-seeded-token", CancellationToken.None);
        var controller = CreateController(store);

        var result = await controller.GetApiTokenAsync(TransportKind.Local);

        Assert.Equal("pre-seeded-token", result);
    }

    [Fact]
    public async Task GetApiTokenAsyncReturnsNullWhenNotStored()
    {
        var store = new FakeCredentialStore();
        var controller = CreateController(store);

        var result = await controller.GetApiTokenAsync(TransportKind.Local);

        Assert.Null(result);
    }

    [Fact]
    public async Task SetApiTokenAsyncThrowsWhenNoStore()
    {
        var controller = CreateController();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.SetApiTokenAsync(TransportKind.Local, "token"));

        Assert.Contains("No credential store", exception.Message);
    }

    [Fact]
    public async Task ClearApiTokenAsyncThrowsWhenNoStore()
    {
        var controller = CreateController();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.ClearApiTokenAsync(TransportKind.Local));

        Assert.Contains("No credential store", exception.Message);
    }

    [Fact]
    public async Task GetApiTokenAsyncThrowsWhenNoStore()
    {
        var controller = CreateController();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.GetApiTokenAsync(TransportKind.Local));

        Assert.Contains("No credential store", exception.Message);
    }

    [Fact]
    public async Task CloudTransportRejectsApiTokenOperations()
    {
        var store = new FakeCredentialStore();
        var controller = CreateController(store);

        var setException = await Assert.ThrowsAsync<ArgumentException>(
            () => controller.SetApiTokenAsync(TransportKind.Cloud, "token"));
        var clearException = await Assert.ThrowsAsync<ArgumentException>(
            () => controller.ClearApiTokenAsync(TransportKind.Cloud));
        var getException = await Assert.ThrowsAsync<ArgumentException>(
            () => controller.GetApiTokenAsync(TransportKind.Cloud));

        Assert.Equal("transportKind", setException.ParamName);
        Assert.Equal("transportKind", clearException.ParamName);
        Assert.Equal("transportKind", getException.ParamName);
    }

    [Fact]
    public async Task GetCloudCredentialsAsyncReturnsNullWhenNotStored()
    {
        var store = new FakeCredentialStore();
        var controller = CreateController(store);

        var result = await controller.GetCloudCredentialsAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task CloudCredentialMethodsThrowWhenNoStore()
    {
        var controller = CreateController();

        var setException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.SetCloudCredentialsAsync("user", "password"));
        var clearException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.ClearCloudCredentialsAsync());
        var getException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.GetCloudCredentialsAsync());

        Assert.Contains("No credential store", setException.Message);
        Assert.Contains("No credential store", clearException.Message);
        Assert.Contains("No credential store", getException.Message);
    }

    [Fact]
    public void AnimationSpeed_DefaultsToDefault()
    {
        var controller = CreateController();
        Assert.Equal(FlyoutAnimationSpeed.Default, controller.Current.AnimationSpeed);
    }

    [Fact]
    public void GetFlyoutAnimationDurationMs_ReturnsCorrectDurations()
    {
        var controller = CreateController();
        Assert.Equal(300, controller.GetFlyoutAnimationDurationMs()); // Default = 300ms

        controller.SetAnimationSpeed(FlyoutAnimationSpeed.Off);
        Assert.Equal(0, controller.GetFlyoutAnimationDurationMs());

        controller.SetAnimationSpeed(FlyoutAnimationSpeed.Fast);
        Assert.Equal(150, controller.GetFlyoutAnimationDurationMs());

        controller.SetAnimationSpeed(FlyoutAnimationSpeed.Slow);
        Assert.Equal(450, controller.GetFlyoutAnimationDurationMs());

        controller.SetAnimationSpeed(FlyoutAnimationSpeed.Default);
        Assert.Equal(300, controller.GetFlyoutAnimationDurationMs());
    }

    [Fact]
    public void CanSetChartQuality()
    {
        var controller = CreateController();

        Assert.Equal(ChartQuality.High, controller.Current.ChartQuality);

        controller.SetChartQuality(ChartQuality.Normal);

        Assert.Equal(ChartQuality.Normal, controller.Current.ChartQuality);
    }

    [Fact]
    public void CanSetImportantNotificationTags()
    {
        var controller = CreateController();

        controller.SetImportantNotificationTags(["critical", "warning"]);

        Assert.Equal(["critical", "warning"], controller.Current.ImportantNotificationTags.ToArray());
    }

    [Fact]
    public void SetImportantNotificationTags_NormalizesWhitespaceAndDuplicates()
    {
        var controller = CreateController();

        controller.SetImportantNotificationTags(["  critical ", "warning", "CRITICAL", "  "]);

        Assert.Equal(["critical", "warning"], controller.Current.ImportantNotificationTags.ToArray());
    }

    [Fact]
    public void ChartQuality_RoundTripsThroughJson()
    {
        var original = AppSettings.Default with { ChartQuality = ChartQuality.Normal };
        var json = System.Text.Json.JsonSerializer.Serialize(original);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
        Assert.Equal(ChartQuality.Normal, deserialized!.ChartQuality);
    }

    [Fact]
    public void AnimationSpeed_RoundTripsThroughJson()
    {
        var original = AppSettings.Default with { AnimationSpeed = FlyoutAnimationSpeed.Slow };
        var json = System.Text.Json.JsonSerializer.Serialize(original);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
        Assert.Equal(FlyoutAnimationSpeed.Slow, deserialized!.AnimationSpeed);
    }

    [Fact]
    public void DefaultsDisableDeviceInfoSync()
    {
        var controller = CreateController();
        var deviceInfoSync = Assert.IsType<DeviceInfoSyncSettings>(controller.Current.DeviceInfoSync);

        Assert.False(deviceInfoSync.IsEnabled);
        Assert.False(string.IsNullOrWhiteSpace(deviceInfoSync.DeviceIdentifier));
        Assert.Equal(15, deviceInfoSync.SyncIntervalMinutes);
        Assert.True(deviceInfoSync.HasAnyMapping);
    }

    [Fact]
    public void DeviceInfoSyncDefaultItemNamesUseSanitizedIdentifier()
    {
        var settings = DeviceInfoSyncSettings.CreateDefault("Desk PC!");

        Assert.Equal("DeskPC", settings.DeviceIdentifier);
        Assert.Equal("DeskPCBatteryLevel", settings.BatteryLevelItem);
        Assert.Equal("DeskPCChargingState", settings.ChargingStateItem);
        Assert.Equal("DeskPCLockedState", settings.LockedStateItem);
        Assert.Equal("DeskPCSessionState", settings.SessionStateItem);
        Assert.Equal("DeskPCWifiConnected", settings.WifiConnectedItem);
        Assert.Equal("DeskPCWifiName", settings.WifiNameItem);
        Assert.Equal("DeskPCOpenHabConnection", settings.OpenHabConnectionItem);
        Assert.Equal("DeskPCFocusState", settings.FocusStateItem);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(240)]
    public void SetDeviceInfoSyncSettingsAcceptsIntervalBounds(int interval)
    {
        var controller = CreateController();
        var current = Assert.IsType<DeviceInfoSyncSettings>(controller.Current.DeviceInfoSync);
        var settings = current with { IsEnabled = true, SyncIntervalMinutes = interval };

        controller.SetDeviceInfoSyncSettings(settings);

        var updated = Assert.IsType<DeviceInfoSyncSettings>(controller.Current.DeviceInfoSync);
        Assert.Equal(interval, updated.SyncIntervalMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(241)]
    public void SetDeviceInfoSyncSettingsRejectsOutOfRangeInterval(int interval)
    {
        var controller = CreateController();
        var current = Assert.IsType<DeviceInfoSyncSettings>(controller.Current.DeviceInfoSync);
        var settings = current with { SyncIntervalMinutes = interval };

        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetDeviceInfoSyncSettings(settings));
    }

    [Fact]
    public async Task DeviceInfoSyncSettingsRoundTripThroughJson()
    {
        var controller = CreateController();
        var settings = DeviceInfoSyncSettings.CreateDefault("Desk") with
        {
            IsEnabled = true,
            SyncIntervalMinutes = 30,
            WifiNameItem = null
        };

        controller.SetDeviceInfoSyncSettings(settings);
        await controller.FlushAsync();

        var reloaded = CreateController();
        var reloadedDeviceInfoSync = Assert.IsType<DeviceInfoSyncSettings>(reloaded.Current.DeviceInfoSync);
        Assert.True(reloadedDeviceInfoSync.IsEnabled);
        Assert.Equal("Desk", reloadedDeviceInfoSync.DeviceIdentifier);
        Assert.Equal(30, reloadedDeviceInfoSync.SyncIntervalMinutes);
        Assert.Null(reloadedDeviceInfoSync.WifiNameItem);
    }

    [Fact]
    public void LegacySettingsJsonWithoutDeviceInfoSyncLoadsDefaultDeviceInfoSync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath)!);
        var legacyJson = """
        {
          "Skin": 1,
          "EndpointMode": 0,
          "LocalEndpoint": "http://openhab:8080/",
          "CloudEndpoint": "https://myopenhab.org/",
          "SitemapName": "home",
          "FollowSystemTheme": true,
          "UseWindows11Icons": false,
          "FlyoutWidth": 460,
          "AnimationSpeed": 2,
          "NotificationPollIntervalSeconds": 30,
          "LaunchAtStartup": true,
          "ChartQuality": 192
        }
        """;
        File.WriteAllText(settingsFilePath, legacyJson);

        var controller = CreateController();
        var deviceInfoSync = Assert.IsType<DeviceInfoSyncSettings>(controller.Current.DeviceInfoSync);

        Assert.False(deviceInfoSync.IsEnabled);
        Assert.Equal(DeviceInfoSyncSettings.DefaultSyncIntervalMinutes, deviceInfoSync.SyncIntervalMinutes);
        Assert.True(deviceInfoSync.HasAnyMapping);
        Assert.Empty(controller.Current.ImportantNotificationTags);
    }

    [Fact]
    public void LoadedSettingsStripEndpointUserInfo()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath)!);
        var json = System.Text.Json.JsonSerializer.Serialize(
            AppSettings.Default with
            {
                LocalEndpoint = new Uri("http://user:pass@openhab:8080/base"),
                CloudEndpoint = new Uri("https://cloud-user:cloud-pass@myopenhab.org/")
            });
        File.WriteAllText(settingsFilePath, json);

        var controller = CreateController();

        Assert.Equal(new Uri("http://openhab:8080/base"), controller.Current.LocalEndpoint);
        Assert.Equal(new Uri("https://myopenhab.org/"), controller.Current.CloudEndpoint);
        Assert.Equal(string.Empty, controller.Current.LocalEndpoint.UserInfo);
        Assert.Equal(string.Empty, controller.Current.CloudEndpoint.UserInfo);
    }

    [Fact]
    public async Task ImportantNotificationTagsRoundTripThroughJson()
    {
        var controller = CreateController();
        controller.SetImportantNotificationTags(["critical", "warning"]);
        await controller.FlushAsync();

        var reloaded = CreateController();
        Assert.Equal(["critical", "warning"], reloaded.Current.ImportantNotificationTags.ToArray());
    }

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

    [Fact]
    public void CachedMainUiPageLinksLoadedFromJsonAreNormalized()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath)!);
        var json = System.Text.Json.JsonSerializer.Serialize(
            AppSettings.Default with
            {
                CachedMainUiPageLinks =
                [
                    new OpenHab.App.MainUi.MainUiPageLink(" ", "Ignored", "/page/ignored", null, null, null),
                    new OpenHab.App.MainUi.MainUiPageLink(" energy ", " ", "page/energy", null, null, null)
                ]
            });
        File.WriteAllText(settingsFilePath, json);

        var controller = CreateController();

        var link = Assert.Single(controller.Current.CachedMainUiPageLinks);
        Assert.Equal("energy", link.Uid);
        Assert.Equal("energy", link.Label);
        Assert.Equal("/page/energy", link.Route);
    }
}
