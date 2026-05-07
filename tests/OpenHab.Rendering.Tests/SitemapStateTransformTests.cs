using OpenHab.Rendering.Descriptors;
using OpenHab.Rendering.Skins;
using OpenHab.Sitemaps.Models;

namespace OpenHab.Rendering.Tests;

public sealed class SitemapStateTransformTests
{
    [Fact]
    public void ToRow_UsesMappingLabel_WhenStateMatchesMappingCommand()
    {
        var page = new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget(
                "Door",
                SitemapWidgetType.Switch,
                "DoorSensor",
                "OPEN",
                [new SitemapMapping("OPEN", "Unlocked"), new SitemapMapping("CLOSED", "Locked")],
                false,
                false,
                SitemapFallbackKind.None,
                [])
        ]);

        var descriptor = new Windows11SitemapSkin().Render(page);
        var row = Assert.Single(descriptor.Rows);

        Assert.Equal("Unlocked", row.State);
    }

    [Fact]
    public void ToRow_KeepsOriginalState_WhenNoMappingMatch()
    {
        var page = new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget(
                "Temperature",
                SitemapWidgetType.Text,
                "Temperature",
                "21.5",
                [],
                false,
                false,
                SitemapFallbackKind.None,
                [])
        ]);

        var descriptor = new Windows11SitemapSkin().Render(page);
        var row = Assert.Single(descriptor.Rows);

        Assert.Equal("21.5", row.State);
    }

    [Fact]
    public void ToRow_KeepsOriginalState_WhenStateDoesNotMatchAnyMappingCommand()
    {
        var page = new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget(
                "Switch",
                SitemapWidgetType.Switch,
                "SwitchItem",
                "UNKNOWN",
                [new SitemapMapping("ON", "On"), new SitemapMapping("OFF", "Off")],
                false,
                false,
                SitemapFallbackKind.None,
                [])
        ]);

        var descriptor = new Windows11SitemapSkin().Render(page);
        var row = Assert.Single(descriptor.Rows);

        Assert.Equal("UNKNOWN", row.State);
    }

    [Fact]
    public void ToRow_MappedSwitch_UsesButtonGridAndPreservesRawStateAndMappedDisplay()
    {
        var page = new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget(
                "Light",
                SitemapWidgetType.Switch,
                "LightItem",
                "ON",
                [new SitemapMapping("ON", "An"), new SitemapMapping("OFF", "Aus")],
                false,
                false,
                SitemapFallbackKind.None,
                [])
        ]);

        var descriptor = new Windows11SitemapSkin().Render(page);
        var row = Assert.Single(descriptor.Rows);

        Assert.Equal(RenderControlKind.ButtonGrid, row.Control);
        Assert.Equal(RenderActionKind.SendCommand, row.Action);
        Assert.Equal(2, row.SelectionOptions.Count);
        Assert.Contains(row.SelectionOptions, option => option.Command == "ON" && option.Label == "An" && option.IsActive);
        Assert.Contains(row.SelectionOptions, option => option.Command == "OFF" && option.Label == "Aus" && !option.IsActive);
        Assert.Equal("An", row.State);
        Assert.Equal("ON", row.RawState);
    }

    [Fact]
    public void ToRow_ToggleWithoutMappings_StateEqualsRawState()
    {
        var page = new NormalizedSitemapPage("root", "Home", [
            new NormalizedSitemapWidget(
                "Light",
                SitemapWidgetType.Switch,
                "LightItem",
                "OFF",
                [],
                false,
                false,
                SitemapFallbackKind.None,
                [])
        ]);

        var descriptor = new Windows11SitemapSkin().Render(page);
        var row = Assert.Single(descriptor.Rows);

        Assert.Equal(RenderControlKind.Toggle, row.Control);
        Assert.Equal("OFF", row.State);
        Assert.Equal("OFF", row.RawState);
    }
}
