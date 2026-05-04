using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;

namespace OpenHab.Rendering.Skins;

public sealed class BasicSitemapSkin : ISitemapSkin
{
    public SitemapRenderDescriptor Render(NormalizedSitemapPage page)
    {
        return new SitemapRenderDescriptor(SitemapSkinKind.Basic, page.Id, page.Label, page.Widgets.Select(ToRow).ToArray());
    }

    private static SitemapRowDescriptor ToRow(NormalizedSitemapWidget widget)
    {
        return new SitemapRowDescriptor(widget.Label, widget.State, ControlFor(widget), ActionFor(widget), RenderDensity.Compact);
    }

    private static RenderControlKind ControlFor(NormalizedSitemapWidget widget)
    {
        if (widget.RequiresFallback)
        {
            return RenderControlKind.Fallback;
        }

        return widget.Type switch
        {
            SitemapWidgetType.Switch => RenderControlKind.Toggle,
            SitemapWidgetType.Slider or SitemapWidgetType.Setpoint => RenderControlKind.Slider,
            SitemapWidgetType.Selection => RenderControlKind.Selection,
            _ => RenderControlKind.Text
        };
    }

    private static RenderActionKind ActionFor(NormalizedSitemapWidget widget)
    {
        if (widget.RequiresFallback)
        {
            return RenderActionKind.OpenFallback;
        }

        if (widget.CanNavigate)
        {
            return RenderActionKind.Navigate;
        }

        return widget.Type is SitemapWidgetType.Switch or SitemapWidgetType.Slider or SitemapWidgetType.Selection
            ? RenderActionKind.SendCommand
            : RenderActionKind.None;
    }
}
