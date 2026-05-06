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
}
