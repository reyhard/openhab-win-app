using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;

namespace OpenHab.Rendering.Skins;

internal static class SitemapRowMapper
{
    public static SitemapRowDescriptor ToRow(NormalizedSitemapWidget widget, RenderDensity density)
    {
        var control = ControlFor(widget);
        var action = ActionFor(widget, control);
        return new SitemapRowDescriptor(widget.Label, widget.State, control, action, density);
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

    private static RenderActionKind ActionFor(NormalizedSitemapWidget widget, RenderControlKind control)
    {
        if (widget.RequiresFallback)
        {
            return RenderActionKind.OpenFallback;
        }

        if (widget.CanNavigate)
        {
            return RenderActionKind.Navigate;
        }

        return control is RenderControlKind.Toggle or RenderControlKind.Slider or RenderControlKind.Selection
            ? RenderActionKind.SendCommand
            : RenderActionKind.None;
    }
}
