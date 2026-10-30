using System.Globalization;
using OpenHab.Rendering;
using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;

namespace OpenHab.Rendering.Tests;

public sealed class SitemapUiLogicTests
{
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
    public void NormalizeIconName_ReturnsExpectedValue(string? input, string expected)
    {
        var result = SitemapUiLogic.NormalizeIconName(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("rollershutter")]
    [InlineData("roller_shutter")]
    [InlineData("roller-shutter")]
    [InlineData("light")]
    [InlineData("lights")]
    [InlineData("temperature")]
    [InlineData("air_quality")]
    [InlineData("air-quality")]
    [InlineData("battery_level")]
    [InlineData("color_picker")]
    [InlineData("chart")]
    [InlineData("chart-1")]
    [InlineData("chart_2")]
    [InlineData("sun_clouds")]
    [InlineData("poweroutlet")]
    [InlineData("radiator")]
    [InlineData("fan_ceiling")]
    [InlineData("line")]
    [InlineData("pie")]
    public void CanResolveWin11Glyph_ReturnsTrueForKnownIcons(string iconName)
    {
        Assert.True(SitemapUiLogic.CanResolveWin11Glyph(iconName));
    }

    [Theory]
    [InlineData("custom_sensor")]
    [InlineData("my_gadget")]
    [InlineData("totally-unknown-42")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void CanResolveWin11Glyph_ReturnsFalseForUnknownIcons(string? iconName)
    {
        Assert.False(SitemapUiLogic.CanResolveWin11Glyph(iconName));
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
    public void ResolveWin11Glyph_ReturnsExpectedGlyph(string iconName, string expectedGlyph)
    {
        Assert.Equal(expectedGlyph, SitemapUiLogic.ResolveWin11Glyph(iconName));
    }

    [Fact]
    public void ResolveToggleVisualState_UsesFormattedLockState()
    {
        var row = new SitemapRowDescriptor(
            "Zamek",
            "LOCKED",
            RenderControlKind.Toggle,
            RenderActionKind.SendCommand,
            RenderDensity.Compact,
            [],
            RawState: "LOCKED",
            RawItemState: "ON",
            IconName: "lock");

        var state = SitemapUiLogic.ResolveToggleVisualState(row);

        Assert.Equal("LOCKED", state.DisplayText);
        Assert.True(state.IsOn);
    }

    [Fact]
    public void ResolveToggleVisualState_UsesFormattedUnlockedState()
    {
        var row = new SitemapRowDescriptor(
            "Zamek",
            "UNLOCKED",
            RenderControlKind.Toggle,
            RenderActionKind.SendCommand,
            RenderDensity.Compact,
            [],
            RawState: "UNLOCKED",
            RawItemState: "OFF",
            IconName: "lock");

        var state = SitemapUiLogic.ResolveToggleVisualState(row);

        Assert.Equal("UNLOCKED", state.DisplayText);
        Assert.False(state.IsOn);
    }

    [Fact]
    public void BuildChartUrl_UsesStableUrlByDefault()
    {
        var row = new SitemapRowDescriptor(
            "Power", "12", RenderControlKind.Chart, RenderActionKind.None, RenderDensity.Compact,
            [], ItemName: "Weather_Temperature", Period: "D");
        var baseUri = new Uri("http://localhost:8080/");

        var first = SitemapUiLogic.BuildChartUrl(row, baseUri, chartDpi: 192);
        var second = SitemapUiLogic.BuildChartUrl(row, baseUri, chartDpi: 192);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Contains("items=Weather_Temperature", first!.ToString());
        Assert.Contains("period=D", first.ToString());
        Assert.Contains("dpi=192", first.ToString());
        Assert.DoesNotContain("random=", first.ToString());
    }

    [Fact]
    public void BuildChartUrl_AppendsRandomWhenCacheBustEnabled()
    {
        var row = new SitemapRowDescriptor(
            "Power", "12", RenderControlKind.Chart, RenderActionKind.None, RenderDensity.Compact,
            [], ItemName: "Weather_Temperature", Period: "D");
        var baseUri = new Uri("http://localhost:8080/");

        var uri = SitemapUiLogic.BuildChartUrl(row, baseUri, chartDpi: 192, cacheBust: true);

        Assert.NotNull(uri);
        Assert.Contains("random=", uri!.ToString());
    }

    [Fact]
    public void BuildMapviewUrl_UsesLocationState()
    {
        var row = new SitemapRowDescriptor(
            "Tracker",
            "52.5200,13.4050,34",
            RenderControlKind.Mapview,
            RenderActionKind.None,
            RenderDensity.Compact,
            []);

        var uri = SitemapUiLogic.BuildMapviewUrl(row);

        Assert.NotNull(uri);
        Assert.Equal("https", uri!.Scheme);
        Assert.Contains("openstreetmap.org", uri.Host);
        Assert.Contains("/export/embed.html", uri.AbsolutePath);
        Assert.Contains("bbox=", uri.Query);
        Assert.Contains("marker=52.52%2C13.405", uri.Query);
        Assert.DoesNotContain("#map=", uri.ToString());
    }

    [Fact]
    public void BuildMapviewUrl_ReturnsNullForInvalidLocationState()
    {
        var row = new SitemapRowDescriptor(
            "Tracker",
            "UNDEF",
            RenderControlKind.Mapview,
            RenderActionKind.None,
            RenderDensity.Compact,
            []);

        Assert.Null(SitemapUiLogic.BuildMapviewUrl(row));
    }

    [Fact]
    public void ResolveEmbeddedUrl_UsesAbsoluteUrlBeforeItemState()
    {
        var row = new SitemapRowDescriptor(
            "Camera",
            "https://items.example.test/camera.m3u8",
            RenderControlKind.Video,
            RenderActionKind.None,
            RenderDensity.Compact,
            [],
            Url: "https://sitemap.example.test/camera.mjpeg");

        var uri = SitemapUiLogic.ResolveEmbeddedUrl(row, new Uri("http://openhab:8080/"));

        Assert.Equal("https://sitemap.example.test/camera.mjpeg", uri?.AbsoluteUri);
    }

    [Fact]
    public void ResolveEmbeddedUrl_ResolvesRelativeUrlAgainstEndpoint()
    {
        var row = new SitemapRowDescriptor(
            "Camera",
            null,
            RenderControlKind.Video,
            RenderActionKind.None,
            RenderDensity.Compact,
            [],
            Url: "/static/camera.mjpeg");

        var uri = SitemapUiLogic.ResolveEmbeddedUrl(row, new Uri("http://openhab:8080/rest/"));

        Assert.Equal("http://openhab:8080/static/camera.mjpeg", uri?.AbsoluteUri);
    }

    [Fact]
    public void BuildIconPayloadCacheKey_DoesNotIncludeVisualDimensions()
    {
        var uri = new Uri("https://demo.local/icon/light?format=svg&state=ON");
        var key = SitemapUiLogic.BuildIconPayloadCacheKey(uri, "#ff0000", "none");

        Assert.Equal("https://demo.local/icon/light?format=svg&state=ON|#ff0000|none", key);
        Assert.DoesNotContain("Width", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Height", key, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ON", "on", true)]
    [InlineData(" 3 ", "3.0", true)]
    [InlineData("3.00009", "3", true)]
    [InlineData("3.1", "3", false)]
    [InlineData("", "3", false)]
    [InlineData("3", null, false)]
    public void SelectionValueMatches_HandlesTextAndNumericStates(string? left, string? right, bool expected)
    {
        Assert.Equal(expected, SitemapUiLogic.SelectionValueMatches(left, right));
    }

    [Theory]
    [InlineData(null, 300)]
    [InlineData(0, 300)]
    [InlineData(-1, 300)]
    [InlineData(5, 200)]
    [InlineData(130, 5200)]
    public void ResolveWebviewHeight_ConvertsSitemapRowsToPixels(int? heightRows, double expectedHeight)
    {
        var row = new SitemapRowDescriptor(
            "Events",
            null,
            RenderControlKind.Webview,
            RenderActionKind.None,
            RenderDensity.Compact,
            [],
            HeightRows: heightRows);

        Assert.Equal(expectedHeight, SitemapUiLogic.ResolveWebviewHeight(row));
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

        Assert.Equal("widget:0101", SitemapUiLogic.BuildRowIdentityKey(row));
    }

    [Fact]
    public void BuildRowIdentityKey_PrefersSearchResultKey()
    {
        var row = new SitemapRowDescriptor(
            "Lampka nocna",
            "OFF",
            RenderControlKind.Toggle,
            RenderActionKind.SendCommand,
            RenderDensity.Comfortable,
            [],
            ItemName: "Bedroom_Lamp",
            WidgetId: "real-widget-id",
            SearchResultKey: "search:home/lights/bedroom-lamp");

        Assert.Equal("search:home/lights/bedroom-lamp", SitemapUiLogic.BuildRowIdentityKey(row));
    }

    [Fact]
    public void BuildRowVisualStateKey_IgnoresRowIndexAndStateChanges()
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
            SitemapUiLogic.BuildRowVisualStateKey(offRow, rowIndex: 1),
            SitemapUiLogic.BuildRowVisualStateKey(onRow, rowIndex: 3));
    }

    [Theory]
    [InlineData("0012")]
    [InlineData("0000")]
    [InlineData("0123456789")]
    public void NormalizeInputByHint_NumberWithLeadingZeros_IsPreserved(string raw)
    {
        var normalized = SitemapUiLogic.NormalizeInputByHint(raw, SitemapInputHint.Number);

        Assert.Equal(raw, normalized);
    }

    [Fact]
    public void NormalizeInputByHint_ColorHex_IsConvertedToOpenHabCommand()
    {
        var normalized = SitemapUiLogic.NormalizeInputByHint("#FF0000", SitemapInputHint.Color);

        Assert.Equal("0,100,100", normalized);
    }

    [Fact]
    public void TryParseOpenHabColorState_ParsesHsbTriplet()
    {
        var parsed = SitemapUiLogic.TryParseOpenHabColorState("240,50,75", out var hue, out var saturation, out var brightness);

        Assert.True(parsed);
        Assert.Equal(240d, hue);
        Assert.Equal(50d, saturation);
        Assert.Equal(75d, brightness);
    }

    [Theory]
    [InlineData("-10,150,-5", 0d, 100d, 0d)]
    [InlineData("400,20,110", 360d, 20d, 100d)]
    public void TryParseOpenHabColorState_ClampsComponentsToValidRange(
        string raw,
        double expectedHue,
        double expectedSaturation,
        double expectedBrightness)
    {
        var parsed = SitemapUiLogic.TryParseOpenHabColorState(
            raw,
            out var hue,
            out var saturation,
            out var brightness);

        Assert.True(parsed);
        Assert.Equal(expectedHue, hue);
        Assert.Equal(expectedSaturation, saturation);
        Assert.Equal(expectedBrightness, brightness);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("120")]
    [InlineData("120,50")]
    [InlineData("abc,50,50")]
    [InlineData("120,abc,50")]
    [InlineData("120,50,abc")]
    public void TryParseOpenHabColorState_RejectsMalformedInput(string? raw)
    {
        var parsed = SitemapUiLogic.TryParseOpenHabColorState(
            raw,
            out _,
            out _,
            out _);

        Assert.False(parsed);
    }

    [Theory]
    [InlineData("0,100,100", 255, 0, 0)]
    [InlineData("60,100,100", 255, 255, 0)]
    [InlineData("120,100,100", 0, 255, 0)]
    [InlineData("180,100,100", 0, 255, 255)]
    [InlineData("240,100,100", 0, 0, 255)]
    [InlineData("300,100,100", 255, 0, 255)]
    [InlineData("360,100,100", 255, 0, 0)]
    [InlineData("120,0,50", 128, 128, 128)]
    public void TryResolveOpenHabColor_ConvertsHsbTripletToOpaqueColor(
        string raw,
        int expectedR,
        int expectedG,
        int expectedB)
    {
        var resolved = SitemapUiLogic.TryResolveOpenHabColor(raw, out var color);

        Assert.True(resolved);
        Assert.Equal(new SitemapColor((byte)255, (byte)expectedR, (byte)expectedG, (byte)expectedB), color);
    }

    [Theory]
    [InlineData(255, 255, 0, 0, "0,100,100")]
    [InlineData(255, 255, 255, 0, "60,100,100")]
    [InlineData(255, 0, 255, 0, "120,100,100")]
    [InlineData(255, 0, 255, 255, "180,100,100")]
    [InlineData(255, 0, 0, 255, "240,100,100")]
    [InlineData(255, 255, 0, 255, "300,100,100")]
    [InlineData(255, 0, 0, 0, "0,0,0")]
    [InlineData(255, 255, 255, 255, "0,0,100")]
    public void BuildOpenHabColorCommand_ConvertsColorToHsbCommand(
        int a,
        int r,
        int g,
        int b,
        string expectedCommand)
    {
        var command = SitemapUiLogic.BuildOpenHabColorCommand(new SitemapColor((byte)a, (byte)r, (byte)g, (byte)b));

        Assert.Equal(expectedCommand, command);
    }

    [Theory]
    [InlineData(" #abc ", 255, 170, 187, 204)]
    [InlineData("#123456", 255, 18, 52, 86)]
    [InlineData("#80123456", 128, 18, 52, 86)]
    [InlineData("ReD", 255, 255, 0, 0)]
    public void TryResolveOpenHabColor_AcceptsHexAndNamedColors(
        string raw,
        int expectedA,
        int expectedR,
        int expectedG,
        int expectedB)
    {
        var resolved = SitemapUiLogic.TryResolveOpenHabColor(raw, out var color);

        Assert.True(resolved);
        Assert.Equal(new SitemapColor((byte)expectedA, (byte)expectedR, (byte)expectedG, (byte)expectedB), color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#12")]
    [InlineData("#gggggg")]
    [InlineData("chartreuse")]
    public void TryResolveOpenHabColor_RejectsUnsupportedColorText(string? raw)
    {
        var resolved = SitemapUiLogic.TryResolveOpenHabColor(raw, out _);

        Assert.False(resolved);
    }

    [Theory]
    [InlineData("#00FF00", "120,100,100")]
    [InlineData("  not-a-color  ", "not-a-color")]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    public void NormalizeInputByHint_ColorInput_ProducesCommandOrPreservesTrimmedText(string? raw, string? expected)
    {
        var normalized = SitemapUiLogic.NormalizeInputByHint(raw, SitemapInputHint.Color);

        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("  2024-02-29  ", "2024-02-29")]
    [InlineData("2024-02-29T12:34:56", "2024-02-29")]
    public void NormalizeInputByHint_DateInput_ProducesIsoDate(string raw, string expected)
    {
        var normalized = SitemapUiLogic.NormalizeInputByHint(raw, SitemapInputHint.Date);

        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void NormalizeInputByHint_TimeInput_AppendsSecondsToIsoTime()
    {
        var normalized = SitemapUiLogic.NormalizeInputByHint("  12:34  ", SitemapInputHint.Time);

        Assert.Equal("12:34:00", normalized);
    }

    [Theory]
    [InlineData("2024-02-29T12:34:56", "2024-02-29T12:34:56")]
    [InlineData("2024-02-29T12:34:56.789", "2024-02-29T12:34:56")]
    public void NormalizeInputByHint_DateTimeInput_ProducesIsoDateTimeWithoutFractionalSeconds(
        string raw,
        string expected)
    {
        var normalized = SitemapUiLogic.NormalizeInputByHint(raw, SitemapInputHint.DateTime);

        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void NormalizeInputByHint_DateInput_ParsesCurrentCultureFallback()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pl-PL");

            var normalized = SitemapUiLogic.NormalizeInputByHint("29.02.2024", SitemapInputHint.Date);

            Assert.Equal("2024-02-29", normalized);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData(SitemapInputHint.Date, "  not-a-date  ", "not-a-date")]
    [InlineData(SitemapInputHint.Time, "  not-a-time  ", "not-a-time")]
    [InlineData(SitemapInputHint.DateTime, "  not-a-datetime  ", "not-a-datetime")]
    public void NormalizeInputByHint_UnparsableTemporalInput_FallsBackToTrimmedText(
        SitemapInputHint hint,
        string raw,
        string expected)
    {
        var normalized = SitemapUiLogic.NormalizeInputByHint(raw, hint);

        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeInputByHint_TemporalHints_ReturnNullForBlankInput(string? raw)
    {
        Assert.Null(SitemapUiLogic.NormalizeInputByHint(raw, SitemapInputHint.Date));
        Assert.Null(SitemapUiLogic.NormalizeInputByHint(raw, SitemapInputHint.Time));
        Assert.Null(SitemapUiLogic.NormalizeInputByHint(raw, SitemapInputHint.DateTime));
    }

    [Fact]
    public void ResolveColorTemperaturePreview_ReturnsWarmerColorForLowerValues()
    {
        var warm = SitemapUiLogic.ResolveColorTemperaturePreview(0, 0, 100);
        var cool = SitemapUiLogic.ResolveColorTemperaturePreview(100, 0, 100);

        Assert.True(warm.R > cool.R);
        Assert.True(warm.B < cool.B);
    }
}
