using OpenHab.Rendering.Descriptors;
using OpenHab.Rendering.SitemapSurface;

namespace OpenHab.Rendering.Tests.SitemapSurface;

public sealed class SitemapRowVisualPolicyTests
{
    [Fact]
    public void BuildRowIdentityKey_UsesWidgetIdWhenPresent()
    {
        var row = new SitemapRowDescriptor(
            "Biurko - Opcje",
            null,
            RenderControlKind.Text,
            RenderActionKind.Navigate,
            RenderDensity.Compact,
            [],
            WidgetId: "0101");

        Assert.Equal("widget:0101", SitemapRowVisualPolicy.BuildRowIdentityKey(row));
    }

    [Fact]
    public void BuildRowIdentityKey_UsesSearchPathWhenPresent()
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

        Assert.Equal("search:home/lights/bedroom-lamp", SitemapRowVisualPolicy.BuildRowIdentityKey(row));
    }

    [Fact]
    public void BuildRowVisualStateKey_RemainsStableWhenStateChanges()
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
            SitemapRowVisualPolicy.BuildRowVisualStateKey(offRow, rowIndex: 1),
            SitemapRowVisualPolicy.BuildRowVisualStateKey(onRow, rowIndex: 3));
    }


    [Fact]
    public void BuildRowVisualStateKey_ChangesWhenFrameHeadingChanges()
    {
        var original = new SitemapRowDescriptor(
            "1 🌱 [Grubosz]",
            "Grubosz",
            RenderControlKind.Text,
            RenderActionKind.None,
            RenderDensity.Compact,
            [],
            IsSectionHeader: true,
            ItemName: "Miflora_Ficus_Name",
            WidgetId: "frame-1");
        var renamed = original with { Label = "1 🌱 [Oregano Kuchnia]", State = "Oregano Kuchnia" };

        Assert.NotEqual(
            SitemapRowVisualPolicy.BuildRowVisualStateKey(original, rowIndex: 0),
            SitemapRowVisualPolicy.BuildRowVisualStateKey(renamed, rowIndex: 0));
    }

    [Fact]
    public void BuildRowIdentityKey_UsesItemFallbackWithEmptyOptionalFields()
    {
        var row = KeyRow(
            RenderControlKind.Slider,
            label: "Salon",
            action: RenderActionKind.SendCommand,
            itemName: "Salon_Dimmer");

        Assert.Equal(
            "item:Salon_Dimmer:Slider:SendCommand:Salon::::",
            SitemapRowVisualPolicy.BuildRowIdentityKey(row));
    }

    [Fact]
    public void BuildRowIdentityKey_ItemFallbackIncludesEveryOptionalField()
    {
        var row = KeyRow(
            RenderControlKind.Slider,
            label: "Salon",
            action: RenderActionKind.SendCommand,
            itemName: "Salon_Dimmer",
            iconName: "light",
            command: "ON",
            releaseCommand: "OFF",
            period: "PT1H");

        var identity = SitemapRowVisualPolicy.BuildRowIdentityKey(row);

        Assert.Equal("item:Salon_Dimmer:Slider:SendCommand:Salon:light:ON:OFF:PT1H", identity);
        Assert.NotEqual(identity, SitemapRowVisualPolicy.BuildRowIdentityKey(row with { ItemName = "Salon_Dimmer_2" }));
        Assert.NotEqual(identity, SitemapRowVisualPolicy.BuildRowIdentityKey(row with { Control = RenderControlKind.Setpoint }));
        Assert.NotEqual(identity, SitemapRowVisualPolicy.BuildRowIdentityKey(row with { Action = RenderActionKind.Navigate }));
        Assert.NotEqual(identity, SitemapRowVisualPolicy.BuildRowIdentityKey(row with { Label = "Salon 2" }));
        Assert.NotEqual(identity, SitemapRowVisualPolicy.BuildRowIdentityKey(row with { IconName = "switch" }));
        Assert.NotEqual(identity, SitemapRowVisualPolicy.BuildRowIdentityKey(row with { Command = "50" }));
        Assert.NotEqual(identity, SitemapRowVisualPolicy.BuildRowIdentityKey(row with { ReleaseCommand = "STOP" }));
        Assert.NotEqual(identity, SitemapRowVisualPolicy.BuildRowIdentityKey(row with { Period = "PT2H" }));
    }

    [Fact]
    public void BuildRowIdentityKey_UsesAnonymousFallbackWhenAllIdentitySourcesAreBlank()
    {
        var row = KeyRow(
            RenderControlKind.Text,
            label: "Ustawienia",
            action: RenderActionKind.Navigate,
            itemName: "   ",
            widgetId: " ",
            searchResultKey: "\t");

        Assert.Equal(
            "row:Text:Navigate::Ustawienia",
            SitemapRowVisualPolicy.BuildRowIdentityKey(row));
    }

    [Fact]
    public void BuildRowIdentityKey_AnonymousFallbackReflectsIconControlActionAndLabel()
    {
        var row = KeyRow(RenderControlKind.Text, label: "Ustawienia", action: RenderActionKind.Navigate);

        var identity = SitemapRowVisualPolicy.BuildRowIdentityKey(row);

        Assert.Equal("row:Text:Navigate::Ustawienia", identity);
        Assert.NotEqual(identity, SitemapRowVisualPolicy.BuildRowIdentityKey(row with { IconName = "gear" }));
        Assert.NotEqual(identity, SitemapRowVisualPolicy.BuildRowIdentityKey(row with { Control = RenderControlKind.Fallback }));
        Assert.NotEqual(identity, SitemapRowVisualPolicy.BuildRowIdentityKey(row with { Action = RenderActionKind.None }));
        Assert.NotEqual(identity, SitemapRowVisualPolicy.BuildRowIdentityKey(row with { Label = "Settings" }));
    }

    [Theory]
    [InlineData(RenderControlKind.Webview)]
    [InlineData(RenderControlKind.Mapview)]
    [InlineData(RenderControlKind.Video)]
    public void BuildRowVisualStateKey_MediaRowsRefreshForEachMediaInput(RenderControlKind control)
    {
        var row = KeyRow(control, label: "Media", widgetId: "media-1");

        var key = SitemapRowVisualPolicy.BuildRowVisualStateKey(row, rowIndex: 0);
        var expected = $"key:widget:media-1|control:{control}|action:None|label:Media|icon:|command:|release:|url:|raw:|encoding:|height:";

        Assert.Equal(expected, key);
        Assert.NotEqual(key, SitemapRowVisualPolicy.BuildRowVisualStateKey(row with { Url = "http://openhab.local/cam" }, rowIndex: 0));
        Assert.NotEqual(key, SitemapRowVisualPolicy.BuildRowVisualStateKey(row with { Encoding = "video/mp4" }, rowIndex: 0));
        Assert.NotEqual(key, SitemapRowVisualPolicy.BuildRowVisualStateKey(row with { HeightRows = 5 }, rowIndex: 0));
    }

    [Fact]
    public void BuildRowVisualStateKey_MediaRawStatePrefersMostSpecificValue()
    {
        var row = KeyRow(
            RenderControlKind.Webview,
            label: "Media",
            widgetId: "media-raw",
            state: "40",
            rawState: "41",
            rawItemState: "42");

        var key = SitemapRowVisualPolicy.BuildRowVisualStateKey(row, rowIndex: 0);

        Assert.Equal(
            "key:widget:media-raw|control:Webview|action:None|label:Media|icon:|command:|release:|url:|raw:42|encoding:|height:",
            key);
        Assert.Equal(key, SitemapRowVisualPolicy.BuildRowVisualStateKey(row with { RawState = "39", State = "38" }, rowIndex: 0));
    }

    [Fact]
    public void BuildRowVisualStateKey_MediaRawStateFallsThroughToStateAndThenEmpty()
    {
        var withRawState = KeyRow(
            RenderControlKind.Mapview,
            label: "Media",
            widgetId: "media-fallback",
            state: "40",
            rawState: "41");
        var withStateOnly = withRawState with { RawState = null };

        var rawStateKey = SitemapRowVisualPolicy.BuildRowVisualStateKey(withRawState, rowIndex: 0);
        Assert.EndsWith("|raw:41|encoding:|height:", rawStateKey, StringComparison.Ordinal);
        Assert.Equal(
            rawStateKey,
            SitemapRowVisualPolicy.BuildRowVisualStateKey(withRawState with { State = "39" }, rowIndex: 0));

        var stateKey = SitemapRowVisualPolicy.BuildRowVisualStateKey(withStateOnly, rowIndex: 0);
        Assert.EndsWith("|raw:40|encoding:|height:", stateKey, StringComparison.Ordinal);
        Assert.NotEqual(
            stateKey,
            SitemapRowVisualPolicy.BuildRowVisualStateKey(withStateOnly with { State = "39" }, rowIndex: 0));
        Assert.EndsWith(
            "|raw:|encoding:|height:",
            SitemapRowVisualPolicy.BuildRowVisualStateKey(withStateOnly with { State = null }, rowIndex: 0),
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRowVisualStateKey_NonMediaRowsIgnoreMediaFieldsAndRowIndex()
    {
        var row = KeyRow(
            RenderControlKind.Toggle,
            label: "Salon",
            widgetId: "toggle-1",
            state: "OFF",
            rawState: "OFF");

        var key = SitemapRowVisualPolicy.BuildRowVisualStateKey(row, rowIndex: 0);

        Assert.Equal(
            "key:widget:toggle-1|control:Toggle|action:None|label:Salon|icon:|command:|release:",
            key);
        Assert.Equal(
            key,
            SitemapRowVisualPolicy.BuildRowVisualStateKey(
                row with { Url = "http://openhab.local", Encoding = "video/mp4", HeightRows = 5, RawItemState = "ON" },
                rowIndex: 7));
    }

    [Fact]
    public void BuildRowVisualStateKey_NonMediaRowsReflectCommandAndReleaseCommand()
    {
        var row = KeyRow(RenderControlKind.Button, label: "Salon", widgetId: "button-1", command: "ON");

        var key = SitemapRowVisualPolicy.BuildRowVisualStateKey(row, rowIndex: 0);

        Assert.NotEqual(key, SitemapRowVisualPolicy.BuildRowVisualStateKey(row with { Command = "50" }, rowIndex: 0));
        Assert.NotEqual(key, SitemapRowVisualPolicy.BuildRowVisualStateKey(row with { ReleaseCommand = "OFF" }, rowIndex: 0));
    }

    [Theory]
    [InlineData("Brightness [42 %%]", 12, "Brightness [12 %%]")]
    [InlineData("42 %%", 0, "0 %%")]
    [InlineData(null, 16, "16")]
    [InlineData("", 99, "99")]
    public void FormatSliderStateText_ReplacesFirstNumericTokenAndFallsBack(string? template, double value, string expected)
    {
        Assert.Equal(expected, SitemapRowVisualPolicy.FormatSliderStateText(template, value));
    }

    [Theory]
    [InlineData("%.1f °C", 21.5, "21.5 °C")]
    [InlineData("%.1f °C", 22d, "22.0 °C")]
    [InlineData("%.0f ml", 4870.4, "4870 ml")]
    [InlineData("%.2f A", 1.234, "1.23 A")]
    public void FormatSliderStateText_UsesStatePatternForDisplayPrecision(string statePattern, double value, string expected)
    {
        Assert.Equal(
            expected,
            SitemapRowVisualPolicy.FormatSliderStateText(template: null, value, step: null, statePattern));
    }

    [Theory]
    [InlineData("22.0 °C", 21.5, 0.5, "21.5 °C")]
    [InlineData("22.0 °C", 22d, null, "22.0 °C")]
    [InlineData(null, 21.5, 0.5, "21.5")]
    [InlineData("42 %%", 12d, 1d, "12 %%")]
    [InlineData("12", 21.5, 0d, "21.5")]
    [InlineData("old 1,000 °C", 2.5, null, "old 2.500 °C")]
    [InlineData("no numeric value", 2.5, null, "2.5")]
    [InlineData("0.00000 V", 1.2, null, "1.2000 V")]
    [InlineData(null, 1.23456, null, "1.2346")]
    public void FormatSliderStateText_FallsBackToStepAndTemplatePrecision(
        string? template,
        double value,
        double? step,
        string expected)
    {
        Assert.Equal(expected, SitemapRowVisualPolicy.FormatSliderStateText(template, value, step));
    }

    [Theory]
    [InlineData("%d", "22.0 °C", 21.5, 0.5, "21.5 °C")]
    [InlineData(null, "22.0 °C", 21.5, 0.5, "21.5 °C")]
    public void FormatSliderStateText_FallsBackWhenPatternRejectsFractionalValue(
        string? statePattern,
        string? template,
        double value,
        double? step,
        string expected)
    {
        Assert.Equal(expected, SitemapRowVisualPolicy.FormatSliderStateText(template, value, step, statePattern));
    }

    [Theory]
    [InlineData("%.1f °C", "old 1,000 °C", 2.5, 0.001, "2.5 °C")]
    [InlineData("%q", "value 1.00 V", 2.5, null, "value 2.50 V")]
    [InlineData("   ", null, 2.5, -1d, "2.5")]
    public void FormatSliderStateText_PrefersValidPatternAndFallsBackForUnsupportedPattern(
        string? statePattern,
        string? template,
        double value,
        double? step,
        string expected)
    {
        Assert.Equal(expected, SitemapRowVisualPolicy.FormatSliderStateText(template, value, step, statePattern));
    }

    [Theory]
    [InlineData(21.5, 0.5, "21.5")]
    [InlineData(22d, 0.5, "22")]
    [InlineData(21.5, null, "21.5")]
    [InlineData(42d, 1d, "42")]
    [InlineData(42.0000000001, 1d, "42")]
    [InlineData(0.30000000000000004, 0.1, "0.3")]
    [InlineData(1.23456, null, "1.2346")]
    [InlineData(-1.25, 0.01, "-1.25")]
    [InlineData(2d, 0.01, "2")]
    public void FormatSliderCommand_PreservesFractionalStepValues(double value, double? step, string expected)
    {
        Assert.Equal(expected, SitemapRowVisualPolicy.FormatSliderCommand(value, step));
    }

    [Theory]
    [InlineData("21.5 °C", 22.25, "22.25 °C")]
    [InlineData(null, 4.5, "4.5")]
    public void FormatSetpointStateText_PreservesFractionalValues(string? template, double value, string expected)
    {
        Assert.Equal(expected, SitemapRowVisualPolicy.FormatSetpointStateText(template, value));
    }

    [Fact]
    public void ResolveWebviewHeight_UsesPositiveHeightRows()
    {
        var row = new SitemapRowDescriptor(
            "Events",
            null,
            RenderControlKind.Webview,
            RenderActionKind.None,
            RenderDensity.Compact,
            [],
            HeightRows: 9);

        Assert.Equal(360, SitemapRowVisualPolicy.ResolveWebviewHeight(row));
    }

    [Fact]
    public void ResolveWebviewHeight_FallsBackWhenHeightMissing()
    {
        var row = new SitemapRowDescriptor(
            "Events",
            null,
            RenderControlKind.Webview,
            RenderActionKind.None,
            RenderDensity.Compact,
            [],
            HeightRows: null);

        Assert.Equal(300, SitemapRowVisualPolicy.ResolveWebviewHeight(row));
    }

    [Fact]
    public void ShouldShowMappedSwitchState_ReturnsTrueOnlyForMappedSwitchWithState()
    {
        var mapped = Row(RenderControlKind.MappedSwitch, "4870 ml", [new SitemapMapOption("5000", "Zbiornik pełny")]);
        var mappedWithoutState = Row(RenderControlKind.MappedSwitch, null, [new SitemapMapOption("5000", "Zbiornik pełny")]);
        var toggle = Row(RenderControlKind.Toggle, "ON", []);

        Assert.True(SitemapRowVisualPolicy.ShouldShowMappedSwitchState(mapped));
        Assert.False(SitemapRowVisualPolicy.ShouldShowMappedSwitchState(mappedWithoutState));
        Assert.False(SitemapRowVisualPolicy.ShouldShowMappedSwitchState(toggle));
    }

    [Fact]
    public void ResolveMappedSwitchButtonPlacement_PlacesSingleShortMappingInlineWithState()
    {
        var row = Row(RenderControlKind.MappedSwitch, "4870 ml", [new SitemapMapOption("5000", "Zbiornik pełny")]);

        Assert.Equal(
            SitemapMappedSwitchButtonPlacement.InlineControl,
            SitemapRowVisualPolicy.ResolveMappedSwitchButtonPlacement(row));
    }

    [Fact]
    public void ResolveMappedSwitchButtonPlacement_UsesSeparateRowForMultipleMappings()
    {
        var row = Row(
            RenderControlKind.MappedSwitch,
            "ON",
            [new SitemapMapOption("ON", "An"), new SitemapMapOption("OFF", "Aus")]);

        Assert.Equal(
            SitemapMappedSwitchButtonPlacement.SeparateRow,
            SitemapRowVisualPolicy.ResolveMappedSwitchButtonPlacement(row));
    }

    [Fact]
    public void ResolveMappedSwitchButtonPlacement_UsesSeparateRowForLongButtonLabel()
    {
        var longLabel = new string('a', SitemapRowVisualPolicy.MappedSwitchInlineButtonMaxLabelLength + 1);
        var row = Row(RenderControlKind.MappedSwitch, "ON", [new SitemapMapOption("ON", longLabel)]);

        Assert.Equal(
            SitemapMappedSwitchButtonPlacement.SeparateRow,
            SitemapRowVisualPolicy.ResolveMappedSwitchButtonPlacement(row));
    }

    [Fact]
    public void ResolveMappedSwitchButtonPlacement_UsesSeparateRowForButtongrid()
    {
        var row = Row(RenderControlKind.ButtonGrid, null, [new SitemapMapOption("A", "A")]);

        Assert.Equal(
            SitemapMappedSwitchButtonPlacement.SeparateRow,
            SitemapRowVisualPolicy.ResolveMappedSwitchButtonPlacement(row));
    }

    [Fact]
    public void TryResolveRowIndex_UsesPolicyIdentityKey()
    {
        var rows = new[]
        {
            new SitemapRowDescriptor(
                "Kitchen",
                "OFF",
                RenderControlKind.Toggle,
                RenderActionKind.SendCommand,
                RenderDensity.Compact,
                [],
                WidgetId: "widget-1",
                SearchResultKey: "search:home/kitchen"),
            new SitemapRowDescriptor(
                "Living Room",
                "ON",
                RenderControlKind.Toggle,
                RenderActionKind.SendCommand,
                RenderDensity.Compact,
                [],
                WidgetId: "widget-2")
        };

        var found = SitemapRowPlanner.TryResolveRowIndex(rows, "search:home/kitchen", out var rowIndex);

        Assert.True(found);
        Assert.Equal(0, rowIndex);
    }

    private static SitemapRowDescriptor Row(
        RenderControlKind control,
        string? state,
        IReadOnlyList<SitemapMapOption> options) =>
        new("Row", state, control, RenderActionKind.SendCommand, RenderDensity.Comfortable, options);

    private static SitemapRowDescriptor KeyRow(
        RenderControlKind control,
        string label,
        RenderActionKind action = RenderActionKind.None,
        string? widgetId = null,
        string? itemName = null,
        string? searchResultKey = null,
        string? iconName = null,
        string? command = null,
        string? releaseCommand = null,
        string? period = null,
        string? state = null,
        string? rawState = null,
        string? rawItemState = null,
        string? url = null,
        string? encoding = null,
        int? heightRows = null) =>
        new(
            label,
            state,
            control,
            action,
            RenderDensity.Compact,
            [],
            RawState: rawState,
            IconName: iconName,
            RawItemState: rawItemState,
            Command: command,
            ReleaseCommand: releaseCommand,
            Url: url,
            Period: period,
            ItemName: itemName,
            WidgetId: widgetId,
            HeightRows: heightRows,
            SearchResultKey: searchResultKey,
            Encoding: encoding);
}
