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
    public void Build_TreatsAbsoluteRouteAsInternalPath()
    {
        var uri = MainUiUrlBuilder.Build(new Uri("http://openhab:8080/"), "http://evil.example");

        Assert.Equal(new Uri("http://openhab:8080/http://evil.example"), uri);
    }

    [Fact]
    public void Build_TreatsSchemeRelativeRouteAsInternalPath()
    {
        var uri = MainUiUrlBuilder.Build(new Uri("http://openhab:8080/"), "//evil.example");

        Assert.Equal(new Uri("http://openhab:8080/evil.example"), uri);
    }

    [Fact]
    public void IsSameHost_ReturnsTrueForSameSchemeHostAndPort()
    {
        Assert.True(MainUiUrlBuilder.IsSameHost(
            new Uri("http://openhab:8080/"),
            new Uri("http://openhab:8080/page/energy")));
    }

    [Fact]
    public void IsSameHost_TreatsMyOpenHabRewriteAsSameHost()
    {
        Assert.True(MainUiUrlBuilder.IsSameHost(
            new Uri("https://myopenhab.org/"),
            new Uri("https://home.myopenhab.org/page/energy")));
    }

    [Fact]
    public void IsSameHost_ReturnsFalseForExternalHost()
    {
        Assert.False(MainUiUrlBuilder.IsSameHost(
            new Uri("http://openhab:8080/"),
            new Uri("https://example.com/")));
    }
}
