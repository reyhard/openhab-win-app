using System.Globalization;
using System.Text.RegularExpressions;
using OpenHab.Rendering;

namespace OpenHab.App.Shortcuts;

public sealed record ShortcutSliderState(double Minimum, double Maximum, double InitialValue);

public sealed record ShortcutColorState(SitemapColor Color, string Hex, string Command);

public static partial class ShortcutInteractiveCommandLogic
{
    public static ShortcutSliderState CreateSliderState(string? rawState)
    {
        const double minimum = 0;
        const double maximum = 100;

        var value = TryParseNumericState(NormalizeStateValue(rawState), out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : minimum;

        return new ShortcutSliderState(minimum, maximum, Math.Round(value, 0));
    }

    public static string FormatSliderCommand(double value)
    {
        return Math.Clamp(value, 0, 100).ToString("F0", CultureInfo.InvariantCulture);
    }

    public static ShortcutColorState CreateColorState(string? rawState)
    {
        var normalized = NormalizeStateValue(rawState);
        var color = SitemapUiLogic.TryResolveOpenHabColor(normalized, out var parsed)
            ? parsed
            : new SitemapColor(255, 255, 255, 255);

        return new ShortcutColorState(
            color,
            SitemapUiLogic.BuildOpenHabColorHex(color),
            SitemapUiLogic.BuildOpenHabColorCommand(color));
    }

    public static string FormatColorCommand(SitemapColor color)
    {
        return SitemapUiLogic.BuildOpenHabColorCommand(color);
    }

    private static string NormalizeStateValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var value = raw.Trim();
        return value.Equals("NULL", StringComparison.OrdinalIgnoreCase)
            || value.Equals("UNDEF", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : value;
    }

    private static bool TryParseNumericState(string? raw, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        var match = FirstNumberRegex().Match(raw);
        if (!match.Success)
        {
            return false;
        }

        var normalized = match.Value.Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    [GeneratedRegex(@"[-+]?\d+(?:[\.,]\d+)?", RegexOptions.Compiled)]
    private static partial Regex FirstNumberRegex();
}
