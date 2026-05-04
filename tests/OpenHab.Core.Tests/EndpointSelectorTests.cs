using OpenHab.Core.Profiles;

namespace OpenHab.Core.Tests;

public sealed class EndpointSelectorTests
{
    [Fact]
    public void LocalOnlyUsesLocalEndpoint()
    {
        var profile = new ServerProfile("home", new Uri("http://openhab:8080"), new Uri("https://myopenhab.org"), EndpointMode.LocalOnly);

        var result = EndpointSelector.Select(profile, localReachable: false);

        Assert.Equal(TransportKind.Local, result.Kind);
        Assert.Equal(new Uri("http://openhab:8080"), result.BaseUri);
    }

    [Fact]
    public void CloudOnlyUsesCloudEndpoint()
    {
        var profile = new ServerProfile("home", new Uri("http://openhab:8080"), new Uri("https://myopenhab.org"), EndpointMode.CloudOnly);

        var result = EndpointSelector.Select(profile, localReachable: true);

        Assert.Equal(TransportKind.Cloud, result.Kind);
        Assert.Equal(new Uri("https://myopenhab.org"), result.BaseUri);
    }

    [Fact]
    public void AutomaticPrefersLocalWhenReachable()
    {
        var profile = new ServerProfile("home", new Uri("http://openhab:8080"), new Uri("https://myopenhab.org"), EndpointMode.Automatic);

        var result = EndpointSelector.Select(profile, localReachable: true);

        Assert.Equal(TransportKind.Local, result.Kind);
    }

    [Fact]
    public void AutomaticFallsBackToCloudWhenLocalIsNotReachable()
    {
        var profile = new ServerProfile("home", new Uri("http://openhab:8080"), new Uri("https://myopenhab.org"), EndpointMode.Automatic);

        var result = EndpointSelector.Select(profile, localReachable: false);

        Assert.Equal(TransportKind.Cloud, result.Kind);
    }

    [Fact]
    public void CloudOnlyRequiresCloudEndpoint()
    {
        var profile = new ServerProfile("home", new Uri("http://openhab:8080"), cloudEndpoint: null, EndpointMode.CloudOnly);

        var error = Assert.Throws<InvalidOperationException>(() => EndpointSelector.Select(profile, localReachable: true));

        Assert.Equal("Profile 'home' is CloudOnly but has no cloud endpoint.", error.Message);
    }
}
