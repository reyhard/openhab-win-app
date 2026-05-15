using OpenHab.Rendering;

namespace OpenHab.App.Tests.Rendering;

public class OpenHabIconImageSourceLoaderTests
{
    [Fact]
    public void BuildPayloadCacheKey_DoesNotIncludeVisualDimensions()
    {
        var uri = new Uri("https://demo.local/icon/light?format=svg&state=ON");
        var key = SitemapUiLogic.BuildIconPayloadCacheKey(uri, "#ff0000", "none");

        Assert.Equal("https://demo.local/icon/light?format=svg&state=ON|#ff0000|none", key);
        Assert.DoesNotContain("Width", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Height", key, StringComparison.OrdinalIgnoreCase);
    }
}
