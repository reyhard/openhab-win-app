using OpenHab.Sitemaps.Models;
using OpenHab.Sitemaps.Runtime;

namespace OpenHab.Sitemaps.Tests;

public sealed class SitemapNormalizerTests
{
    [Fact]
    public void RemovesInvisibleWidgets()
    {
        var page = new SitemapPage("root", "Home", [
            new SitemapWidget("Light", SitemapWidgetType.Switch, "Light", "ON", [], true, []),
            new SitemapWidget("Hidden", SitemapWidgetType.Text, "Hidden", "OFF", [], false, [])
        ]);

        var normalized = SitemapNormalizer.Normalize(page);

        Assert.Single(normalized.Widgets);
        Assert.Equal("Light", normalized.Widgets[0].Label);
    }

    [Fact]
    public void MarksUnsupportedWidgetsWithFallback()
    {
        var page = new SitemapPage("root", "Home", [
            new SitemapWidget("Camera", SitemapWidgetType.Video, "FrontCamera", "", [], true, [])
        ]);

        var normalized = SitemapNormalizer.Normalize(page);

        Assert.True(normalized.Widgets[0].RequiresFallback);
        Assert.Equal(SitemapFallbackKind.MainUiOrBrowser, normalized.Widgets[0].FallbackKind);
    }

    [Fact]
    public void PreservesMappingsForSwitchAndSelectionWidgets()
    {
        var page = new SitemapPage("root", "Home", [
            new SitemapWidget("Mode", SitemapWidgetType.Selection, "Mode", "AUTO", [
                new SitemapMapping("AUTO", "Auto"),
                new SitemapMapping("MANUAL", "Manual")
            ], true, [])
        ]);

        var normalized = SitemapNormalizer.Normalize(page);

        Assert.Equal(["AUTO", "MANUAL"], normalized.Widgets[0].Mappings.Select(mapping => mapping.Command).ToArray());
    }
}
