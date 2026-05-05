using OpenHab.App.Settings;
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
}
