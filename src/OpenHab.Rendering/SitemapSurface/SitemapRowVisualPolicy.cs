using System.Globalization;
using System.Text.RegularExpressions;
using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Runtime;

namespace OpenHab.Rendering.SitemapSurface;

public enum SitemapMappedSwitchButtonPlacement
{
    InlineControl,
    SeparateRow
}

public static partial class SitemapRowVisualPolicy
{
    private const double WebviewDefaultHeight = 300d;
    private const double SitemapRowHeight = 40d;
    private const int MaxSliderDecimals = 4;

    /// <summary>
    /// Longest mapping-button label that is still placed next to the state value. Longer labels
    /// (or multiple mappings) fall back to a separate button row to avoid truncation on narrow
    /// surfaces.
    /// </summary>
    public const int MappedSwitchInlineButtonMaxLabelLength = 18;

    [GeneratedRegex(@"[-+]?\d+([.,]\d+)?", RegexOptions.Compiled)]
    private static partial Regex FirstNumberRegexFactory();

    private static readonly Regex FirstNumberRegex = FirstNumberRegexFactory();

    public static double ResolveWebviewHeight(SitemapRowDescriptor row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.HeightRows is > 0
            ? row.HeightRows.Value * SitemapRowHeight
            : WebviewDefaultHeight;
    }

    public static string BuildRowIdentityKey(SitemapRowDescriptor row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!string.IsNullOrWhiteSpace(row.SearchResultKey))
        {
            return row.SearchResultKey;
        }

        if (!string.IsNullOrWhiteSpace(row.WidgetId))
        {
            return $"widget:{row.WidgetId}";
        }

        if (!string.IsNullOrWhiteSpace(row.ItemName))
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"item:{row.ItemName}:{row.Control}:{row.Action}:{row.Label}:{row.IconName ?? string.Empty}:{row.Command ?? string.Empty}:{row.ReleaseCommand ?? string.Empty}:{row.Period ?? string.Empty}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"row:{row.Control}:{row.Action}:{row.IconName ?? string.Empty}:{row.Label}");
    }

    public static string BuildRowVisualStateKey(SitemapRowDescriptor row, int rowIndex)
    {
        ArgumentNullException.ThrowIfNull(row);
        var mediaSource = row.Control is RenderControlKind.Webview or RenderControlKind.Mapview or RenderControlKind.Video
            ? $"|url:{row.Url ?? string.Empty}|raw:{row.RawItemState ?? row.RawState ?? row.State ?? string.Empty}|encoding:{row.Encoding ?? string.Empty}|height:{row.HeightRows?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}"
            : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"key:{BuildRowIdentityKey(row)}|control:{row.Control}|action:{row.Action}|label:{row.Label}|icon:{row.IconName ?? string.Empty}|command:{row.Command ?? string.Empty}|release:{row.ReleaseCommand ?? string.Empty}{mediaSource}");
    }

    public static bool ShouldShowMappedSwitchState(SitemapRowDescriptor row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.Control == RenderControlKind.MappedSwitch && !string.IsNullOrEmpty(row.State);
    }

    public static SitemapMappedSwitchButtonPlacement ResolveMappedSwitchButtonPlacement(SitemapRowDescriptor row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.Control != RenderControlKind.MappedSwitch || row.SelectionOptions.Count != 1)
        {
            return SitemapMappedSwitchButtonPlacement.SeparateRow;
        }

        var label = row.SelectionOptions[0].Label;
        if (string.IsNullOrWhiteSpace(label) || label.Trim().Length > MappedSwitchInlineButtonMaxLabelLength)
        {
            return SitemapMappedSwitchButtonPlacement.SeparateRow;
        }

        return SitemapMappedSwitchButtonPlacement.InlineControl;
    }

    public static string FormatSliderStateText(
        string? template,
        double value,
        double? step = null,
        string? statePattern = null)
    {
        if (TryFormatSliderStateWithPattern(statePattern, value) is { } patternText)
        {
            return patternText;
        }

        var decimals = ResolveSliderStateDecimals(template, step, value);
        return FormatNumericStateText(template, FormatSliderNumber(value, decimals));
    }

    /// <summary>
    /// Formats the numeric command sent for a slider value. Display metadata (state pattern,
    /// template decimals) is intentionally ignored here; the step decides how much precision
    /// the item expects, so a 0.5-step temperature sends "21.5" while a Dimmer sends "22".
    /// </summary>
    public static string FormatSliderCommand(double value, double? step = null)
    {
        var decimals = Math.Max(ResolveStepDecimals(step), ResolveValueDecimals(value));
        return TrimTrailingZeros(FormatSliderNumber(value, decimals));
    }

    public static string FormatSetpointStateText(string? template, double value)
    {
        var numeric = value.ToString("0.########", CultureInfo.InvariantCulture);
        return FormatNumericStateText(template, numeric);
    }

    private static string? TryFormatSliderStateWithPattern(string? statePattern, double value)
    {
        if (string.IsNullOrWhiteSpace(statePattern))
        {
            return null;
        }

        var rawValue = value.ToString("0.############", CultureInfo.InvariantCulture);
        return OpenHabStatePatternFormatter.TryFormat(statePattern, rawValue);
    }

    private static int ResolveSliderStateDecimals(string? template, double? step, double value)
    {
        var decimals = Math.Max(ResolveStepDecimals(step), ResolveValueDecimals(value));
        decimals = Math.Max(decimals, ResolveTemplateDecimals(template));
        return Math.Min(decimals, MaxSliderDecimals);
    }

    private static int ResolveStepDecimals(double? step)
    {
        return step is > 0d ? ResolveValueDecimals(step.Value) : 0;
    }

    private static int ResolveTemplateDecimals(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return 0;
        }

        var match = FirstNumberRegex.Match(template);
        if (!match.Success)
        {
            return 0;
        }

        var token = match.Value;
        var separatorIndex = token.IndexOfAny(['.', ',']);
        return separatorIndex < 0 ? 0 : token.Length - separatorIndex - 1;
    }

    private static int ResolveValueDecimals(double value)
    {
        var magnitude = Math.Abs(value);
        if (IsCloseToInteger(magnitude, tolerance: 1e-9))
        {
            return 0;
        }

        for (var decimals = 1; decimals < MaxSliderDecimals; decimals++)
        {
            if (IsCloseToInteger(magnitude * Math.Pow(10, decimals), tolerance: 1e-6))
            {
                return decimals;
            }
        }

        return MaxSliderDecimals;
    }

    private static bool IsCloseToInteger(double value, double tolerance) =>
        Math.Abs(value - Math.Round(value)) <= tolerance;

    private static string FormatSliderNumber(double value, int decimals)
    {
        var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        return rounded.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    private static string TrimTrailingZeros(string numeric)
    {
        if (!numeric.Contains('.'))
        {
            return numeric;
        }

        return numeric.TrimEnd('0').TrimEnd('.');
    }

    private static string FormatNumericStateText(string? template, string numeric)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return numeric;
        }

        var match = FirstNumberRegex.Match(template);
        if (!match.Success)
        {
            return numeric;
        }

        return string.Concat(template.AsSpan(0, match.Index), numeric, template.AsSpan(match.Index + match.Length));
    }
}
