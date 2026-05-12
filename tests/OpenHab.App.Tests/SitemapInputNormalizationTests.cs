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

    [Fact]
    public void NormalizeInputByHint_ColorHex_IsConvertedToOpenHabCommand()
    {
        var normalized = SitemapControlFactory.NormalizeInputByHint("#FF0000", SitemapInputHint.Color);

        Assert.Equal("0,100,100", normalized);
    }

    [Fact]
    public void TryParseOpenHabColorState_ParsesHsbTriplet()
    {
        var parsed = SitemapControlFactory.TryParseOpenHabColorState("240,50,75", out var hue, out var saturation, out var brightness);

        Assert.True(parsed);
        Assert.Equal(240d, hue);
        Assert.Equal(50d, saturation);
        Assert.Equal(75d, brightness);
    }

    [Fact]
    public void ResolveColorTemperaturePreview_ReturnsWarmerColorForLowerValues()
    {
        var warm = SitemapControlFactory.ResolveColorTemperaturePreview(0, 0, 100);
        var cool = SitemapControlFactory.ResolveColorTemperaturePreview(100, 0, 100);

        Assert.True(warm.R > cool.R);
        Assert.True(warm.B < cool.B);
    }
}
