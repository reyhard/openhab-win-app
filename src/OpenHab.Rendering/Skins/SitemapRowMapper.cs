using System.Linq;
using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;

namespace OpenHab.Rendering.Skins;

internal static class SitemapRowMapper
{
    public static SitemapRowDescriptor ToRow(NormalizedSitemapWidget widget, RenderDensity density)
    {
        var control = ControlFor(widget);
        var action = ActionFor(widget, control);
        var rawState = widget.State;
        var state = TransformState(widget.State, widget.Mappings);
        var options = widget.Mappings.Select(m => new SitemapMapOption(m.Command, m.Label)).ToArray();
        var isSectionHeader = widget.Type == SitemapWidgetType.Frame;
        return new SitemapRowDescriptor(
            widget.Label,
            state,
            control,
            action,
            density,
            options,
            rawState,
            widget.Icon,
            isSectionHeader);
    }

    private static string? TransformState(string? state, IReadOnlyList<SitemapMapping> mappings)
    {
        if (string.IsNullOrEmpty(state) || mappings.Count == 0) return state;
        var match = mappings.FirstOrDefault(m =>
            string.Equals(m.Command, state, StringComparison.OrdinalIgnoreCase));
        return match is not null && !string.IsNullOrWhiteSpace(match.Label) ? match.Label : state;
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
