using OpenHab.Rendering.Icons;

namespace OpenHab.Rendering.Tests;

public sealed class OpenHabIconUriBuilderTests
{
    [Fact]
    public void BuildIncludesDefaultFormatAndStateWhenProvided()
    {
        var baseUri = new Uri("https://demo.local/");

        var uri = OpenHabIconUriBuilder.Build(baseUri, "rollershutter", "50");

        Assert.Equal("https://demo.local/icon/rollershutter?format=png&state=50", uri.ToString());
    }

    [Fact]
    public void BuildIncludesDefaultFormatWithoutStateWhenNotProvided()
    {
        var baseUri = new Uri("https://demo.local/");

        var uri = OpenHabIconUriBuilder.Build(baseUri, "switch", null);

        Assert.Equal("https://demo.local/icon/switch?format=png", uri.ToString());
    }

    [Fact]
    public void BuildOmitsImageDataUriState()
    {
        var baseUri = new Uri("https://demo.local/");

        var uri = OpenHabIconUriBuilder.Build(baseUri, "image", "data:image/png;base64,AAAA");

        Assert.Equal("https://demo.local/icon/image?format=png", uri.ToString());
    }

    [Fact]
    public void BuildOmitsOversizedState()
    {
        var baseUri = new Uri("https://demo.local/");
        var oversizedState = new string('A', 257);

        var uri = OpenHabIconUriBuilder.Build(baseUri, "image", oversizedState);

        Assert.Equal("https://demo.local/icon/image?format=png", uri.ToString());
    }
}
