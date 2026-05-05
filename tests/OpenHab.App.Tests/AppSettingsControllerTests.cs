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
    public void RejectsRelativeEndpointUris()
    {
        var controller = new AppSettingsController();

        var exception = Assert.Throws<ArgumentException>(() =>
            controller.SetEndpoints(new Uri("/rest", UriKind.Relative), new Uri("https://myopenhab.org")));

        Assert.Contains("absolute", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
