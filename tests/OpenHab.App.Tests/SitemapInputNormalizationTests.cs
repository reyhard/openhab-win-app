using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;
using OpenHab.Windows.Tray.Rendering;

namespace OpenHab.App.Tests;

public class SitemapInputNormalizationTests
{
    [Theory]
    [InlineData("0012")]
    [InlineData("0000")]
    [InlineData("0123456789")]
    public void NormalizeInputByHint_NumberWithLeadingZeros_IsPreserved(string raw)
    {
        var normalized = SitemapControlFactory.NormalizeInputByHint(raw, SitemapInputHint.Number);

        Assert.Equal(raw, normalized);
    }

    [Fact]
    public void NormalizeInputByHint_NumberWithoutLeadingZeros_IsNormalizedNumerically()
    {
        var normalized = SitemapControlFactory.NormalizeInputByHint("12.50", SitemapInputHint.Number);

        Assert.Equal("12.5", normalized);
    }
}
