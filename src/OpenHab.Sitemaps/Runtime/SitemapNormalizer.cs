using OpenHab.Sitemaps.Models;

namespace OpenHab.Sitemaps.Runtime;

public static class SitemapNormalizer
{
    private static readonly HashSet<SitemapWidgetType> NativeTypes =
    [
        SitemapWidgetType.Frame,
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
        ArgumentNullException.ThrowIfNull(page);

        if (page.Widgets is null)
        {
            throw new ArgumentException($"Sitemap page '{page.Label}' has null widgets.", nameof(page));
        }

        var widgets = page.Widgets
            .Where(widget => widget.IsVisible)
            .Select(NormalizeWidget)
            .ToArray();

        return new NormalizedSitemapPage(page.Id, page.Label, widgets);
    }

    private static NormalizedSitemapWidget NormalizeWidget(SitemapWidget widget)
    {
        if (widget.Mappings is null)
        {
            throw new ArgumentException($"Visible widget '{widget.Label}' has null mappings.", nameof(widget));
        }

        if (widget.Children is null)
        {
            throw new ArgumentException($"Visible widget '{widget.Label}' has null children.", nameof(widget));
        }

        var supported = NativeTypes.Contains(widget.Type);
        var mappings = widget.Mappings.ToArray();
        var children = widget.Children.ToArray();
        return new NormalizedSitemapWidget(
            widget.Label,
            widget.Type,
            widget.ItemName,
            widget.State,
            mappings,
            children.Length > 0,
            !supported,
            supported ? SitemapFallbackKind.None : SitemapFallbackKind.MainUiOrBrowser,
            children,
            widget.Icon,
            widget.MinValue,
            widget.MaxValue,
            widget.Step,
            widget.RawItemState);
    }
}
