using OpenHab.App.Settings;
using OpenHab.App.Tests.Settings;
using OpenHab.Core.Auth;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using System.IO;

namespace OpenHab.App.Tests;

public sealed class AppSettingsControllerTests
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenHab.WinApp",
        "settings.json");

    public AppSettingsControllerTests()
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
    public void DefaultsUseWindows11SkinAndAutomaticEndpointMode()
    {
        var controller = new AppSettingsController();

        Assert.Equal(SitemapSkinKind.Windows11, controller.Current.Skin);
        Assert.Equal(EndpointMode.Automatic, controller.Current.EndpointMode);
        Assert.Equal(new Uri("http://openhab:8080"), controller.Current.LocalEndpoint);
        Assert.Equal(new Uri("https://myopenhab.org"), controller.Current.CloudEndpoint);
        Assert.Equal("default", controller.Current.SitemapName);
        Assert.Equal(460, controller.Current.FlyoutWidth);
        Assert.Equal(FlyoutAnimationSpeed.Default, controller.Current.AnimationSpeed);
        Assert.Equal(ChartQuality.High, controller.Current.ChartQuality);
        Assert.False(controller.Current.HasLocalToken);
        Assert.False(controller.Current.HasCloudCredentials);
        Assert.Null(controller.Current.CloudUserName);
    }

    [Fact]
    public void CanChangeSkinAndEndpointMode()
    {
        var controller = new AppSettingsController();

        controller.SetSkin(SitemapSkinKind.Basic);
        controller.SetEndpointMode(EndpointMode.CloudOnly);

        Assert.Equal(SitemapSkinKind.Basic, controller.Current.Skin);
        Assert.Equal(EndpointMode.CloudOnly, controller.Current.EndpointMode);
    }

    [Fact]
    public void CanChangeSitemapName()
    {
        var controller = new AppSettingsController();

        controller.SetSitemapName("home");

        Assert.Equal("home", controller.Current.SitemapName);
    }

    [Fact]
    public void CanChangeFlyoutWidth()
    {
        var controller = new AppSettingsController();

        controller.SetFlyoutWidth(420);

        Assert.Equal(420, controller.Current.FlyoutWidth);
    }

    [Fact]
    public void SetFlyoutWidthRejectsOutOfRangeValues()
    {
        var controller = new AppSettingsController();

        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetFlyoutWidth(200));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetFlyoutWidth(1200));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void SetSitemapNameRejectsBlankInput(string sitemapName)
    {
        var controller = new AppSettingsController();

        var exception = Assert.Throws<ArgumentException>(() => controller.SetSitemapName(sitemapName));

        Assert.Equal("sitemapName", exception.ParamName);
    }

    [Fact]
    public void SetSitemapNameRejectsInvalidCharacters()
    {
        var controller = new AppSettingsController();

        var exception = Assert.Throws<ArgumentException>(() => controller.SetSitemapName("home/main"));

        Assert.Equal("sitemapName", exception.ParamName);
    }

    [Fact]
    public void RejectsRelativeEndpointUris()
    {
        var controller = new AppSettingsController();

        var exception = Assert.Throws<ArgumentException>(() =>
            controller.SetEndpoints(new Uri("/rest", UriKind.Relative), new Uri("https://myopenhab.org")));

        Assert.Contains("absolute", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetEndpointsThrowsWhenLocalEndpointIsNull()
    {
        var controller = new AppSettingsController();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            controller.SetEndpoints(null!, new Uri("https://myopenhab.org")));

        Assert.Equal("localEndpoint", exception.ParamName);
    }

    [Fact]
    public void SetEndpointsThrowsWhenCloudEndpointIsNull()
    {
        var controller = new AppSettingsController();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            controller.SetEndpoints(new Uri("http://openhab:8080"), null!));

        Assert.Equal("cloudEndpoint", exception.ParamName);
    }

    [Fact]
    public void SetEndpointsRejectsNonHttpAbsoluteUris()
    {
        var controller = new AppSettingsController();

        var exception = Assert.Throws<ArgumentException>(() =>
            controller.SetEndpoints(new Uri("ftp://openhab.local:21"), new Uri("https://myopenhab.org")));

        Assert.Contains("HTTP or HTTPS", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetEndpointsRejectsNonHttpAbsoluteCloudUri()
    {
        var controller = new AppSettingsController();

        var exception = Assert.Throws<ArgumentException>(() =>
            controller.SetEndpoints(new Uri("http://openhab:8080"), new Uri("ftp://myopenhab.org")));

        Assert.Contains("HTTP or HTTPS", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DefaultsHaveNoTokensOrCloudCredentials()
    {
        var controller = new AppSettingsController();

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
        var controller = new AppSettingsController(store);

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
        var controller = new AppSettingsController(store);

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
        var controller = new AppSettingsController(store);

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
        var controller = new AppSettingsController(store);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => controller.SetApiTokenAsync(TransportKind.Local, token));

        Assert.Equal("token", exception.ParamName);
    }

    [Fact]
    public async Task GetApiTokenAsyncReturnsTokenFromStore()
    {
        var store = new FakeCredentialStore();
        await store.StoreAsync("OpenHabAuth", "local-token", "pre-seeded-token", CancellationToken.None);
        var controller = new AppSettingsController(store);

        var result = await controller.GetApiTokenAsync(TransportKind.Local);

        Assert.Equal("pre-seeded-token", result);
    }

    [Fact]
    public async Task GetApiTokenAsyncReturnsNullWhenNotStored()
    {
        var store = new FakeCredentialStore();
        var controller = new AppSettingsController(store);

        var result = await controller.GetApiTokenAsync(TransportKind.Local);

        Assert.Null(result);
    }

    [Fact]
    public async Task SetApiTokenAsyncThrowsWhenNoStore()
    {
        var controller = new AppSettingsController();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.SetApiTokenAsync(TransportKind.Local, "token"));

        Assert.Contains("No credential store", exception.Message);
    }

    [Fact]
    public async Task ClearApiTokenAsyncThrowsWhenNoStore()
    {
        var controller = new AppSettingsController();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.ClearApiTokenAsync(TransportKind.Local));

        Assert.Contains("No credential store", exception.Message);
    }

    [Fact]
    public async Task GetApiTokenAsyncThrowsWhenNoStore()
    {
        var controller = new AppSettingsController();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.GetApiTokenAsync(TransportKind.Local));

        Assert.Contains("No credential store", exception.Message);
    }

    [Fact]
    public async Task CloudTransportRejectsApiTokenOperations()
    {
        var store = new FakeCredentialStore();
        var controller = new AppSettingsController(store);

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
        var controller = new AppSettingsController(store);

        var result = await controller.GetCloudCredentialsAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task CloudCredentialMethodsThrowWhenNoStore()
    {
        var controller = new AppSettingsController();

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
        var controller = new AppSettingsController();
        Assert.Equal(FlyoutAnimationSpeed.Default, controller.Current.AnimationSpeed);
    }

    [Fact]
    public void GetFlyoutAnimationDurationMs_ReturnsCorrectDurations()
    {
        var controller = new AppSettingsController();
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
        var controller = new AppSettingsController();

        Assert.Equal(ChartQuality.High, controller.Current.ChartQuality);

        controller.SetChartQuality(ChartQuality.Normal);

        Assert.Equal(ChartQuality.Normal, controller.Current.ChartQuality);
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
}
