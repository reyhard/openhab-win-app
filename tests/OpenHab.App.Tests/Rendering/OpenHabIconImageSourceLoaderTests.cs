using OpenHab.Windows.Tray.Rendering;

namespace OpenHab.App.Tests.Rendering;

public class OpenHabIconImageSourceLoaderTests
{
    [Fact]
    public void BuildPayloadCacheKey_DoesNotIncludeVisualDimensions()
    {
        var uri = new Uri("https://demo.local/icon/light?format=svg&state=ON");
        var key = OpenHabIconImageSourceLoader.BuildPayloadCacheKey(uri, "#ff0000", null);

        Assert.Equal("https://demo.local/icon/light?format=svg&state=ON|#ff0000|none", key);
        Assert.DoesNotContain("Width", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Height", key, StringComparison.OrdinalIgnoreCase);
    }
}
