using System.Globalization;
using System.Text;

namespace OpenHab.Sitemaps.Runtime;

/// <summary>
/// Minimal formatter for the numeric/string subset of openHAB state patterns that appear in
/// sitemap widget metadata (for example <c>%.0f ml</c>, <c>%.1f</c>, <c>%d</c>, <c>%s</c>).
/// Returns <see langword="null"/> for unsupported patterns or non-numeric raw values so callers
/// can fall back to the raw value instead of displaying fabricated formatting.
/// </summary>
public static class OpenHabStatePatternFormatter
{
    public static string? TryFormat(string? pattern, string? rawState)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(rawState))
        {
            return null;
        }

        if (!TryFindSingleConversion(pattern, out var start, out var length, out var spec))
        {
            return null;
        }

        var prefix = UnescapeLiterals(pattern[..start]);
        var suffix = UnescapeLiterals(pattern[(start + length)..]);
        if (prefix is null || suffix is null)
        {
            return null;
        }

        var replacement = FormatConversion(spec, rawState);
        return replacement is null ? null : string.Concat(prefix, replacement, suffix);
    }

    private readonly record struct ConversionSpec(
        char Conversion,
        int? Precision,
        int? Width,
        bool LeftAlign,
        bool ZeroPad,
        bool PlusSign,
        bool SpaceSign);

    private static bool TryFindSingleConversion(string pattern, out int start, out int length, out ConversionSpec spec)
    {
        start = -1;
        length = 0;
        spec = default;
        var found = false;
        var i = 0;
        while (i < pattern.Length)
        {
            if (pattern[i] != '%')
            {
                i++;
                continue;
            }

            if (i + 1 < pattern.Length && pattern[i + 1] == '%')
            {
                i += 2;
                continue;
            }

            if (found || !TryParseConversion(pattern, i, out spec, out var consumed))
            {
                return false;
            }

            found = true;
            start = i;
            length = consumed;
            i += consumed;
        }

        return found;
    }

    private static bool TryParseConversion(string pattern, int index, out ConversionSpec spec, out int consumed)
    {
        spec = default;
        consumed = 0;
        var i = index + 1;
        var leftAlign = false;
        var zeroPad = false;
        var plusSign = false;
        var spaceSign = false;
        while (i < pattern.Length)
        {
            switch (pattern[i])
            {
                case '-':
                    leftAlign = true;
                    i++;
                    continue;
                case '0':
                    zeroPad = true;
                    i++;
                    continue;
                case '+':
                    plusSign = true;
                    i++;
                    continue;
                case ' ':
                    spaceSign = true;
                    i++;
                    continue;
                default:
                    break;
            }

            break;
        }

        int? width = null;
        if (i < pattern.Length && char.IsAsciiDigit(pattern[i]))
        {
            var widthStart = i;
            while (i < pattern.Length && char.IsAsciiDigit(pattern[i]))
            {
                i++;
            }

            width = int.Parse(pattern.AsSpan(widthStart, i - widthStart), CultureInfo.InvariantCulture);
        }

        int? precision = null;
        if (i < pattern.Length && pattern[i] == '.')
        {
            i++;
            var precisionStart = i;
            while (i < pattern.Length && char.IsAsciiDigit(pattern[i]))
            {
                i++;
            }

            if (i == precisionStart)
            {
                return false;
            }

            precision = int.Parse(pattern.AsSpan(precisionStart, i - precisionStart), CultureInfo.InvariantCulture);
        }

        if (i >= pattern.Length)
        {
            return false;
        }

        var conversion = pattern[i];
        if (conversion is not ('d' or 'f' or 's'))
        {
            return false;
        }

        spec = new ConversionSpec(conversion, precision, width, leftAlign, zeroPad, plusSign, spaceSign);
        consumed = i + 1 - index;
        return true;
    }

    private static string? UnescapeLiterals(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '%')
            {
                if (i + 1 < text.Length && text[i + 1] == '%')
                {
                    builder.Append('%');
                    i++;
                    continue;
                }

                // A second unescaped conversion is not supported.
                return null;
            }

            builder.Append(text[i]);
        }

        return builder.ToString();
    }

    private static string? FormatConversion(ConversionSpec spec, string rawState)
    {
        string text;
        switch (spec.Conversion)
        {
            case 's':
                text = rawState;
                break;
            case 'd':
                if (!TryParseNumber(rawState, out var integerValue) ||
                    Math.Abs(integerValue - Math.Round(integerValue, MidpointRounding.AwayFromZero)) > 1e-9)
                {
                    return null;
                }

                text = Math.Round(integerValue, MidpointRounding.AwayFromZero)
                    .ToString("F0", CultureInfo.InvariantCulture);
                break;
            case 'f':
                if (!TryParseNumber(rawState, out var floatValue))
                {
                    return null;
                }

                var precision = spec.Precision ?? 6;
                text = Math.Round(floatValue, precision, MidpointRounding.AwayFromZero)
                    .ToString("F" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                break;
            default:
                return null;
        }

        if (spec.Conversion != 's' && text.Length > 0 && text[0] != '-')
        {
            if (spec.PlusSign)
            {
                text = "+" + text;
            }
            else if (spec.SpaceSign)
            {
                text = " " + text;
            }
        }

        if (spec.Width is int width && text.Length < width)
        {
            if (spec.LeftAlign)
            {
                text = text.PadRight(width);
            }
            else if (spec.ZeroPad && text.Length > 0 && (text[0] == '-' || text[0] == '+' || char.IsAsciiDigit(text[0])))
            {
                var sign = text[0] is '-' or '+' ? text[..1] : string.Empty;
                text = sign + text[sign.Length..].PadLeft(width - sign.Length, '0');
            }
            else
            {
                text = text.PadLeft(width);
            }
        }

        return text;
    }

    private static bool TryParseNumber(string raw, out double value)
    {
        var candidate = raw.Trim();
        return double.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || double.TryParse(candidate, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }
}
