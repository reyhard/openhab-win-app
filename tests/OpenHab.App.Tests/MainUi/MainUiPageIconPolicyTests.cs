using OpenHab.App.MainUi;

namespace OpenHab.App.Tests.MainUi;

public sealed class MainUiPageIconPolicyTests
{
    [Fact]
    public void BuildIconUri_UsesIconifyForFramework7Icons()
    {
        var uri = MainUiPageIconPolicy.BuildIconUri(
            new Uri("http://openhab:8080/"),
            "f7:graph_circle");

        Assert.Equal("https://api.iconify.design/f7/graph-circle.svg", uri!.ToString());
    }

    [Fact]
    public void BuildIconUri_UsesOpenHabIconEndpointForClassicIcons()
    {
        var uri = MainUiPageIconPolicy.BuildIconUri(
            new Uri("http://openhab:8080/"),
            "energy");

        Assert.Equal("http://openhab:8080/icon/energy?format=svg", uri!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildIconUri_ReturnsNullForBlankIcons(string? icon)
    {
        var uri = MainUiPageIconPolicy.BuildIconUri(
            new Uri("http://openhab:8080/"),
            icon);

        Assert.Null(uri);
    }

    [Fact]
    public void ShouldAttachOpenHabAuth_ReturnsFalseForExternalIconifyIcons()
    {
        var openHabBaseUri = new Uri("http://openhab:8080/");
        var iconUri = MainUiPageIconPolicy.BuildIconUri(openHabBaseUri, "f7:graph_circle");

        Assert.False(MainUiPageIconPolicy.ShouldAttachOpenHabAuth(iconUri!, openHabBaseUri));
    }

    [Fact]
    public void ShouldAttachOpenHabAuth_ReturnsTrueForOpenHabIconEndpoint()
    {
        var openHabBaseUri = new Uri("http://openhab:8080/");
        var iconUri = MainUiPageIconPolicy.BuildIconUri(openHabBaseUri, "energy");

        Assert.True(MainUiPageIconPolicy.ShouldAttachOpenHabAuth(iconUri!, openHabBaseUri));
    }
}
