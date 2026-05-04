using OpenHab.Sitemaps.Models;

namespace OpenHab.Sitemaps.Runtime;

public static class SitemapNormalizer
{
    private static readonly HashSet<SitemapWidgetType> NativeTypes =
    [
        SitemapWidgetType.Default,
        SitemapWidgetType.Text,
        SitemapWidgetType.Group,
        SitemapWidgetType.Switch,
        SitemapWidgetType.Selection,
        SitemapWidgetType.Setpoint,
        SitemapWidgetType.Slider,
        SitemapWidgetType.Colorpicker,
        SitemapWidgetType.Colortemperaturepicker,
        SitemapWidgetType.Input,
        SitemapWidgetType.Buttongrid,
        SitemapWidgetType.Button,
        SitemapWidgetType.Image
    ];

    public static NormalizedSitemapPage Normalize(SitemapPage page)
    {
        var widgets = page.Widgets
            .Where(widget => widget.IsVisible)
            .Select(NormalizeWidget)
            .ToArray();

        return new NormalizedSitemapPage(page.Id, page.Label, widgets);
    }

    private static NormalizedSitemapWidget NormalizeWidget(SitemapWidget widget)
    {
        var supported = NativeTypes.Contains(widget.Type);
        return new NormalizedSitemapWidget(
            widget.Label,
            widget.Type,
            widget.ItemName,
            widget.State,
            widget.Mappings,
            widget.Children.Count > 0,
            !supported,
            supported ? SitemapFallbackKind.None : SitemapFallbackKind.MainUiOrBrowser,
            widget.Children);
    }
}
