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
}
