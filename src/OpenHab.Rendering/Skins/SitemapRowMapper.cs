using System.Globalization;
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
        var commandSource = widget.RawItemState ?? widget.State;
        var options = widget.Mappings
            .Select(m => new SitemapMapOption(
                m.Command,
                m.Label,
                IsActive: SelectionValueMatches(m.Command, commandSource)))
            .ToArray();
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
            widget.LabelColor,
            widget.ValueColor,
            widget.IconColor,
            isSectionHeader,
            widget.MinValue,
            widget.MaxValue,
            widget.Step,
            widget.RawItemState,
            widget.IsVisible,
            widget.Row,
            widget.Column,
            widget.Command,
            widget.ReleaseCommand,
            widget.Stateless,
            SliderUpdateOnMove: widget.Type != SitemapWidgetType.Setpoint,
            Url: widget.Url,
            Period: widget.Period,
            ItemName: widget.ItemName,
            InputHint: widget.InputHint,
            WidgetId: widget.WidgetId);
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
            SitemapWidgetType.Switch when widget.Mappings.Count > 0 => RenderControlKind.ButtonGrid,
            SitemapWidgetType.Switch => RenderControlKind.Toggle,
            SitemapWidgetType.Slider or SitemapWidgetType.Setpoint => RenderControlKind.Slider,
            SitemapWidgetType.Selection => RenderControlKind.Selection,
            SitemapWidgetType.Button => RenderControlKind.Button,
            SitemapWidgetType.Buttongrid => RenderControlKind.ButtonGrid,
            SitemapWidgetType.Image => RenderControlKind.Image,
            SitemapWidgetType.Webview => RenderControlKind.Webview,
            SitemapWidgetType.Chart => RenderControlKind.Chart,
            SitemapWidgetType.Input => RenderControlKind.Input,
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

        if (control is RenderControlKind.Toggle or RenderControlKind.Slider or RenderControlKind.Selection or RenderControlKind.ButtonGrid or RenderControlKind.Input)
        {
            return RenderActionKind.SendCommand;
        }

        if (widget.Type == SitemapWidgetType.Button && !string.IsNullOrWhiteSpace(widget.ItemName))
        {
            return RenderActionKind.SendCommand;
        }

        return RenderActionKind.None;
    }

    private static bool SelectionValueMatches(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var l = left.Trim();
        var r = right.Trim();
        if (string.Equals(l, r, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var leftIsNumber = double.TryParse(l, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber);
        var rightIsNumber = double.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber);
        return leftIsNumber && rightIsNumber && Math.Abs(leftNumber - rightNumber) < 0.0001;
    }
}
