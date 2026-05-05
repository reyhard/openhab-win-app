using OpenHab.App.Settings;
using OpenHab.App.Tests.Settings;
using OpenHab.Core.Auth;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.App.Tests;

public sealed class AppSettingsControllerTests
{
    [Fact]
    public void DefaultsUseWindows11SkinAndAutomaticEndpointMode()
    {
        var controller = new AppSettingsController();

        Assert.Equal(SitemapSkinKind.Windows11, controller.Current.Skin);
        Assert.Equal(EndpointMode.Automatic, controller.Current.EndpointMode);
        Assert.Equal(new Uri("http://openhab.local:8080"), controller.Current.LocalEndpoint);
        Assert.Equal(new Uri("https://myopenhab.org"), controller.Current.CloudEndpoint);
        Assert.Equal("default", controller.Current.SitemapName);
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
            controller.SetEndpoints(new Uri("http://openhab.local:8080"), null!));

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
            controller.SetEndpoints(new Uri("http://openhab.local:8080"), new Uri("ftp://myopenhab.org")));

        Assert.Contains("HTTP or HTTPS", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DefaultsHaveNoTokens()
    {
        var controller = new AppSettingsController();

        Assert.False(controller.Current.HasLocalToken);
        Assert.False(controller.Current.HasCloudToken);
    }

    [Fact]
    public async Task InitializeAsyncHydratesTokenFlags()
    {
        var store = new FakeCredentialStore();
        await store.StoreAsync("OpenHabAuth", "local-token", "local", CancellationToken.None);
        await store.StoreAsync("OpenHabAuth", "cloud-token", "cloud", CancellationToken.None);
        var controller = new AppSettingsController(store);

        Assert.False(controller.Current.HasLocalToken);
        Assert.False(controller.Current.HasCloudToken);

        await controller.InitializeAsync();

        Assert.True(controller.Current.HasLocalToken);
        Assert.True(controller.Current.HasCloudToken);
    }

    [Fact]
    public async Task SetAndClearLocalApiToken()
    {
        var store = new FakeCredentialStore();
        var controller = new AppSettingsController(store);

        await controller.SetApiTokenAsync(TransportKind.Local, "my-local-token");

        Assert.True(controller.Current.HasLocalToken);
        Assert.False(controller.Current.HasCloudToken);
        Assert.Equal("my-local-token", await store.RetrieveAsync("OpenHabAuth", "local-token", CancellationToken.None));

        await controller.ClearApiTokenAsync(TransportKind.Local);

        Assert.False(controller.Current.HasLocalToken);
        Assert.False(controller.Current.HasCloudToken);
        Assert.Null(await store.RetrieveAsync("OpenHabAuth", "local-token", CancellationToken.None));
    }

    [Fact]
    public async Task SetAndClearCloudApiToken()
    {
        var store = new FakeCredentialStore();
        var controller = new AppSettingsController(store);

        await controller.SetApiTokenAsync(TransportKind.Cloud, "my-cloud-token");

        Assert.True(controller.Current.HasCloudToken);
        Assert.False(controller.Current.HasLocalToken);
        Assert.Equal("my-cloud-token", await store.RetrieveAsync("OpenHabAuth", "cloud-token", CancellationToken.None));

        await controller.ClearApiTokenAsync(TransportKind.Cloud);

        Assert.False(controller.Current.HasCloudToken);
        Assert.False(controller.Current.HasLocalToken);
        Assert.Null(await store.RetrieveAsync("OpenHabAuth", "cloud-token", CancellationToken.None));
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
}
