using OpenHab.App.MainUi;

namespace OpenHab.App.Tests.MainUi;

public sealed class MainUiUrlBuilderTests
{
    [Fact]
    public void Build_RewritesMyOpenHabRootToHomeHost()
    {
        var uri = MainUiUrlBuilder.Build(new Uri("https://myopenhab.org"), "/");

        Assert.Equal(new Uri("https://home.myopenhab.org/"), uri);
    }

    [Fact]
    public void Build_CombinesLocalEndpointAndRelativeRoute()
    {
        var uri = MainUiUrlBuilder.Build(new Uri("http://openhab:8080/base/"), "/page/energy");

        Assert.Equal(new Uri("http://openhab:8080/page/energy"), uri);
    }

    [Fact]
    public void IsSameHost_ReturnsTrueForSameSchemeHostAndPort()
    {
        Assert.True(MainUiUrlBuilder.IsSameHost(
            new Uri("http://openhab:8080/"),
            new Uri("http://openhab:8080/page/energy")));
    }

    [Fact]
    public void IsSameHost_ReturnsFalseForExternalHost()
    {
        Assert.False(MainUiUrlBuilder.IsSameHost(
            new Uri("http://openhab:8080/"),
            new Uri("https://example.com/")));
    }
}
