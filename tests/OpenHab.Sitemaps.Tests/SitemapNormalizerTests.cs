using OpenHab.Sitemaps.Models;
using OpenHab.Sitemaps.Runtime;

namespace OpenHab.Sitemaps.Tests;

public sealed class SitemapNormalizerTests
{
    [Fact]
    public void KeepsInvisibleWidgetsWithIsVisibleFalse()
    {
        var page = new SitemapPage("root", "Home", [
            new SitemapWidget("Light", SitemapWidgetType.Switch, "Light", "ON", [], true, []),
            new SitemapWidget("Hidden", SitemapWidgetType.Text, "Hidden", "OFF", [], false, [])
        ]);

        var normalized = SitemapNormalizer.Normalize(page);

        Assert.Equal(2, normalized.Widgets.Count);
        Assert.Equal("Light", normalized.Widgets[0].Label);
        Assert.True(normalized.Widgets[0].IsVisible);
        Assert.Equal("Hidden", normalized.Widgets[1].Label);
        Assert.False(normalized.Widgets[1].IsVisible);
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
    public void MarksSupportedWidgetsWithoutFallback()
    {
        var page = new SitemapPage("root", "Home", [
            new SitemapWidget("Light", SitemapWidgetType.Switch, "Light", "ON", [], true, [])
        ]);

        var normalized = SitemapNormalizer.Normalize(page);

        Assert.False(normalized.Widgets[0].RequiresFallback);
        Assert.Equal(SitemapFallbackKind.None, normalized.Widgets[0].FallbackKind);
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

    [Fact]
    public void PreservesHeightRows()
    {
        var page = new SitemapPage("root", "Home", [
            new SitemapWidget(
                "Events",
                SitemapWidgetType.Webview,
                null,
                null,
                [],
                true,
                [],
                Url: "http://openhab:9001",
                HeightRows: 130)
        ]);

        var normalized = SitemapNormalizer.Normalize(page);

        Assert.Equal(130, normalized.Widgets[0].HeightRows);
    }

    [Fact]
    public void NormalizedWidgetSnapshotsMappingsAndChildren()
    {
        var mappings = new List<SitemapMapping>
        {
            new("ON", "On")
        };
        var children = new List<SitemapPage>
        {
            new("child-1", "Child 1", [])
        };
        var page = new SitemapPage("root", "Home", [
            new SitemapWidget("Light", SitemapWidgetType.Switch, "Light", "ON", mappings, true, children)
        ]);

        var normalized = SitemapNormalizer.Normalize(page);

        mappings.Add(new SitemapMapping("OFF", "Off"));
        children.Add(new SitemapPage("child-2", "Child 2", []));

        Assert.Equal(["ON"], normalized.Widgets[0].Mappings.Select(mapping => mapping.Command).ToArray());
        Assert.Equal(["child-1"], normalized.Widgets[0].Children.Select(child => child.Id).ToArray());
    }

    [Fact]
    public void NormalizeThrowsForNullPage()
    {
        Assert.Throws<ArgumentNullException>(() => SitemapNormalizer.Normalize(null!));
    }

    [Fact]
    public void NormalizeThrowsForNullWidgets()
    {
        var page = new SitemapPage("root", "Home", null!);

        var ex = Assert.Throws<ArgumentException>(() => SitemapNormalizer.Normalize(page));

        Assert.Contains("widgets", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Home", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeThrowsForVisibleWidgetWithNullMappings()
    {
        var page = new SitemapPage("root", "Home", [
            new SitemapWidget("Light", SitemapWidgetType.Switch, "Light", "ON", null!, true, [])
        ]);

        var ex = Assert.Throws<ArgumentException>(() => SitemapNormalizer.Normalize(page));

        Assert.Contains("mappings", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Light", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeThrowsForVisibleWidgetWithNullChildren()
    {
        var page = new SitemapPage("root", "Home", [
            new SitemapWidget("Light", SitemapWidgetType.Switch, "Light", "ON", [], true, null!)
        ]);

        var ex = Assert.Throws<ArgumentException>(() => SitemapNormalizer.Normalize(page));

        Assert.Contains("children", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Light", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
