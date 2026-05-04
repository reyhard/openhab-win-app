using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;

namespace OpenHab.Rendering.Skins;

public sealed class Windows11SitemapSkin : ISitemapSkin
{
    public SitemapRenderDescriptor Render(NormalizedSitemapPage page)
    {
        var basicRows = page.Widgets.Select(ToRow).ToArray();
        return new SitemapRenderDescriptor(SitemapSkinKind.Windows11, page.Id, page.Label, basicRows);
    }

    private static SitemapRowDescriptor ToRow(NormalizedSitemapWidget widget)
    {
        if (widget.RequiresFallback)
        {
            return new SitemapRowDescriptor(widget.Label, widget.State, RenderControlKind.Fallback, RenderActionKind.OpenFallback, RenderDensity.Comfortable);
        }

        var control = widget.Type switch
        {
            SitemapWidgetType.Switch => RenderControlKind.Toggle,
            SitemapWidgetType.Slider or SitemapWidgetType.Setpoint => RenderControlKind.Slider,
            SitemapWidgetType.Selection => RenderControlKind.Selection,
            _ => RenderControlKind.Text
        };

        var action = widget.CanNavigate
            ? RenderActionKind.Navigate
            : control is RenderControlKind.Toggle or RenderControlKind.Slider or RenderControlKind.Selection
                ? RenderActionKind.SendCommand
                : RenderActionKind.None;

        return new SitemapRowDescriptor(widget.Label, widget.State, control, action, RenderDensity.Comfortable);
    }
}
