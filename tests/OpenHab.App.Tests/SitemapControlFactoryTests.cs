using OpenHab.Windows.Tray.Rendering;
using Microsoft.UI.Xaml;
using OpenHab.Rendering.Descriptors;
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
    [InlineData("sun_clouds")]       // common classic weather icon
    [InlineData("poweroutlet")]      // common classic thing icon
    [InlineData("radiator")]         // common classic thing icon
    [InlineData("fan_ceiling")]      // custom/common alias
    [InlineData("line")]             // fallback chart-style alias
    [InlineData("pie")]              // fallback chart-style alias
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
    public void BuildOpenHabIconUri_IncludesDefaultFormat_AndState_WhenProvided()
    {
        var baseUri = new Uri("https://demo.local/");
        var uri = SitemapControlFactory.BuildOpenHabIconUri(baseUri, "rollershutter", "50");

        Assert.Equal("https://demo.local/icon/rollershutter?format=png&state=50", uri.ToString());
    }

    [Fact]
    public void BuildOpenHabIconUri_IncludesDefaultFormat_WithoutState_WhenNotProvided()
    {
        var baseUri = new Uri("https://demo.local/");
        var uri = SitemapControlFactory.BuildOpenHabIconUri(baseUri, "switch", null);

        Assert.Equal("https://demo.local/icon/switch?format=png", uri.ToString());
    }

    [Fact]
    public void BuildChartUrl_UsesItemNamePeriodAndDpi()
    {
        var row = new SitemapRowDescriptor(
            "Power", "12", RenderControlKind.Chart, RenderActionKind.None, RenderDensity.Compact,
            [], ItemName: "Weather_Temperature", Period: "D");
        var baseUri = new Uri("http://localhost:8080/");

        var uri = SitemapControlFactory.BuildChartUrl(row, baseUri, chartDpi: 192);

        Assert.NotNull(uri);
        Assert.Contains("items=Weather_Temperature", uri!.ToString());
        Assert.Contains("period=D", uri.ToString());
        Assert.Contains("dpi=192", uri.ToString());
        Assert.Contains("random=", uri.ToString());
    }

    [Fact]
    public void BuildChartUrl_ReturnsNull_WhenNoItemName()
    {
        var row = new SitemapRowDescriptor(
            "Power", null, RenderControlKind.Chart, RenderActionKind.None, RenderDensity.Compact, []);
        var baseUri = new Uri("http://localhost:8080/");

        var uri = SitemapControlFactory.BuildChartUrl(row, baseUri, chartDpi: 96);

        Assert.Null(uri);
    }

    [Fact]
    public void BuildChartUrl_ReturnsNull_WhenNoBaseUri()
    {
        var row = new SitemapRowDescriptor(
            "Power", "12", RenderControlKind.Chart, RenderActionKind.None, RenderDensity.Compact,
            [], ItemName: "Weather_Temperature", Period: "D");

        var uri = SitemapControlFactory.BuildChartUrl(row, null, chartDpi: 96);

        Assert.Null(uri);
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
        Assert.Equal(56, controlLaneWidth);
    }

    [Fact]
    public void UpdateState_ExposesPartialRowUpdateContract()
    {
        var method = typeof(SitemapControlFactory).GetMethod(
            nameof(SitemapControlFactory.UpdateState),
            BindingFlags.Public | BindingFlags.Static,
            [typeof(FrameworkElement), typeof(SitemapRowDescriptor)]);

        Assert.NotNull(method);
    }

    [Fact]
    public void BuildRowIdentityKey_PrefersWidgetId()
    {
        var row = new SitemapRowDescriptor(
            "Biurko - Opcje",
            null,
            RenderControlKind.Text,
            RenderActionKind.Navigate,
            RenderDensity.Compact,
            [],
            WidgetId: "0101");

        Assert.Equal("widget:0101", SitemapControlFactory.BuildRowIdentityKey(row));
    }

    [Fact]
    public void BuildRowIdentityKey_UsesStableFallbackForRowsWithoutItems()
    {
        var row = new SitemapRowDescriptor(
            "Biurko - Opcje",
            null,
            RenderControlKind.Text,
            RenderActionKind.Navigate,
            RenderDensity.Compact,
            [],
            IconName: "light");

        Assert.Equal("row:Text:Navigate:light:Biurko - Opcje", SitemapControlFactory.BuildRowIdentityKey(row));
    }

    [Fact]
    public void BuildRowVisualStateKey_IgnoresRowIndexChanges()
    {
        var row = new SitemapRowDescriptor(
            "Kuchnia",
            "OFF",
            RenderControlKind.Toggle,
            RenderActionKind.SendCommand,
            RenderDensity.Compact,
            [],
            RawState: "OFF",
            IconName: "switch",
            ItemName: "Kitchen_Light",
            WidgetId: "0102");

        Assert.Equal(
            SitemapControlFactory.BuildRowVisualStateKey(row, rowIndex: 2),
            SitemapControlFactory.BuildRowVisualStateKey(row, rowIndex: 3));
    }

    [Fact]
    public void BuildRowVisualStateKey_IgnoresIconStateChanges()
    {
        var offRow = new SitemapRowDescriptor(
            "Biurko",
            "OFF",
            RenderControlKind.Toggle,
            RenderActionKind.SendCommand,
            RenderDensity.Compact,
            [],
            RawState: "OFF",
            IconName: "switch",
            ItemName: "BulbDesk_01_Switch",
            WidgetId: "0100");
        var onRow = offRow with { State = "ON", RawState = "ON" };

        Assert.Equal(
            SitemapControlFactory.BuildRowVisualStateKey(offRow, rowIndex: 1),
            SitemapControlFactory.BuildRowVisualStateKey(onRow, rowIndex: 1));
    }

    [Fact]
    public void BuildRowVisualStateKey_ChangesWhenIconNameChanges()
    {
        var switchRow = new SitemapRowDescriptor(
            "Biurko",
            "OFF",
            RenderControlKind.Toggle,
            RenderActionKind.SendCommand,
            RenderDensity.Compact,
            [],
            RawState: "OFF",
            IconName: "switch",
            ItemName: "BulbDesk_01_Switch",
            WidgetId: "0100");
        var lightRow = switchRow with { IconName = "light" };

        Assert.NotEqual(
            SitemapControlFactory.BuildRowVisualStateKey(switchRow, rowIndex: 1),
            SitemapControlFactory.BuildRowVisualStateKey(lightRow, rowIndex: 1));
    }
}
