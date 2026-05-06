using OpenHab.Windows.Tray.Rendering;
using System.Reflection;

namespace OpenHab.App.Tests;

public class SitemapControlFactoryTests
{
    // ── Pure helper: NormalizeIconName ──────────────────────────────

    [Theory]
    [InlineData("light", "light")]
    [InlineData("LIGHT", "light")]
    [InlineData("roller_shutter", "rollershutter")]
    [InlineData("roller-shutter", "rollershutter")]
    [InlineData("ground_floor", "groundfloor")]
    [InlineData("ground-floor", "groundfloor")]
    [InlineData("groundFloor", "groundfloor")]
    [InlineData("battery_level", "batterylevel")]
    [InlineData("color_picker", "colorpicker")]
    [InlineData("color-picker", "colorpicker")]
    [InlineData("air_quality", "airquality")]
    [InlineData("air-quality", "airquality")]
    [InlineData("first_floor", "firstfloor")]
    [InlineData("chart-1", "chart")]
    [InlineData("chart_2", "chart")]
    [InlineData("   chart-3   ", "chart")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    public void NormalizeIconName_Returns_Expected(string? input, string expected)
    {
        var result = SitemapControlFactory.NormalizeIconName(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeIconName_CollisionPairs_YieldSameKey()
    {
        var n1 = SitemapControlFactory.NormalizeIconName("groundfloor");
        var n2 = SitemapControlFactory.NormalizeIconName("ground_floor");
        Assert.Equal(n1, n2);

        var n3 = SitemapControlFactory.NormalizeIconName("firstfloor");
        var n4 = SitemapControlFactory.NormalizeIconName("first_floor");
        Assert.Equal(n3, n4);
    }

    // ── Normalized-map construction is safe (no duplicate-key crash) ─

    [Fact]
    public void NormalizedMap_Constructed_WithoutCrash()
    {
        // Accessing CanResolveNormalizedIcon forces static init of
        // NormalizedWin11IconMap.  If the GroupBy→ToDictionary had
        // duplicate-key collisions that weren't handled, this would
        // throw at type-init time.

        // Ground: known collision pair – must resolve.
        Assert.True(SitemapControlFactory.CanResolveNormalizedIcon("ground_floor"));
        Assert.True(SitemapControlFactory.CanResolveNormalizedIcon("groundfloor"));

        // First: known collision pair – must resolve.
        Assert.True(SitemapControlFactory.CanResolveNormalizedIcon("first_floor"));
        Assert.True(SitemapControlFactory.CanResolveNormalizedIcon("firstfloor"));
    }

    // ── Normalization aliases resolve correctly ─────────────────────

    [Theory]
    [InlineData("rollershutter")]    // exact key
    [InlineData("roller_shutter")]   // underscore variant
    [InlineData("roller-shutter")]   // hyphen variant
    [InlineData("light")]            // exact key
    [InlineData("lights")]           // plural alias
    [InlineData("temperature")]      // exact key
    [InlineData("air_quality")]      // underscore variant of "airquality"
    [InlineData("air-quality")]      // hyphen variant
    [InlineData("battery_level")]    // underscore variant of "batterylevel"
    [InlineData("color_picker")]     // underscore variant of "colorpicker"
    [InlineData("chart")]            // exact key
    [InlineData("chart-1")]          // numbered variant
    [InlineData("chart_2")]          // numbered variant
    public void CanResolveNormalizedIcon_Resolves_KnownIcon(string iconName)
    {
        Assert.True(SitemapControlFactory.CanResolveNormalizedIcon(iconName));
    }

    // ── Unknown icons do NOT resolve ────────────────────────────────

    [Theory]
    [InlineData("custom_sensor")]
    [InlineData("my_gadget")]
    [InlineData("totally-unknown-42")]
    [InlineData("")]
    [InlineData("  ")]
    public void CanResolveNormalizedIcon_ReturnsFalse_ForUnknown(string? iconName)
    {
        Assert.False(SitemapControlFactory.CanResolveNormalizedIcon(iconName));
    }

    [Fact]
    public void CanResolveNormalizedIcon_ReturnsFalse_ForNull()
    {
        Assert.False(SitemapControlFactory.CanResolveNormalizedIcon(null));
    }

    [Theory]
    [InlineData("light", "\uE706")]
    [InlineData("light-on", "\uE706")]
    [InlineData("light-off", "\uE706")]
    [InlineData("lights-on", "\uE706")]
    [InlineData("switch", "\uE7E8")]
    [InlineData("switch-on", "\uE7E8")]
    [InlineData("switch-off", "\uE7E8")]
    [InlineData("power-on", "\uE7E8")]
    [InlineData("power-off", "\uE7E8")]
    public void ResolveGlyphForIcon_ReturnsExpectedGlyph_ForCommonSwitchAndLightStates(string iconName, string expectedGlyph)
    {
        Assert.Equal(expectedGlyph, SitemapControlFactory.ResolveGlyphForIcon(iconName));
    }

    [Fact]
    public void ToggleRows_DoNotUseCombinedFixedClusterWidth()
    {
        var clusterWidthField = typeof(SitemapControlFactory).GetField(
            "ToggleClusterWidth",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.Null(clusterWidthField);
    }

    [Fact]
    public void ToggleRows_UseCompactControlLaneNextToStateText()
    {
        var controlLaneWidthField = typeof(SitemapControlFactory).GetField(
            "ControlLaneWidth",
            BindingFlags.NonPublic | BindingFlags.Static);

        var controlLaneWidth = Assert.IsType<double>(controlLaneWidthField?.GetRawConstantValue());
        Assert.Equal(48, controlLaneWidth);
    }
}
