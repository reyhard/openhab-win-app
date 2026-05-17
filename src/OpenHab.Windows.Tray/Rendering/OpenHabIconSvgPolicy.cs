using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenHab.Windows.Tray.Rendering;

internal static partial class OpenHabIconSvgPolicy
{
    internal static bool LooksLikeSvg(string? mediaType, byte[] bytes)
    {
        if (!string.IsNullOrWhiteSpace(mediaType) &&
            mediaType.Contains("svg", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var sampleLength = Math.Min(bytes.Length, 256);
        if (sampleLength == 0)
        {
            return false;
        }

        var sample = Encoding.UTF8.GetString(bytes, 0, sampleLength).TrimStart('\uFEFF', '\t', '\r', '\n', ' ');
        return sample.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
               sample.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);
    }

    internal static byte[]? TryApplySvgColorTint(byte[] svgBytes, string? iconColor)
    {
        if (string.IsNullOrWhiteSpace(iconColor))
        {
            return null;
        }

        if (!TryNormalizeColorToHex(iconColor, out var hexColor))
        {
            return null;
        }

        try
        {
            var svgText = Encoding.UTF8.GetString(svgBytes);
            if (string.IsNullOrWhiteSpace(svgText) || svgText.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            var match = SvgOpenTagRegex().Match(svgText);
            if (!match.Success)
            {
                return null;
            }

            var replacement = $"<svg style=\"color:{hexColor};\"";
            var tinted = svgText[..match.Index] + replacement + svgText[(match.Index + match.Length)..];
            return Encoding.UTF8.GetBytes(tinted);
        }
        catch
        {
            return null;
        }
    }

    internal static bool TryNormalizeColorToHex(string? color, out string hex)
    {
        hex = string.Empty;
        if (!TryParseColor(color, out var parsed))
        {
            return false;
        }

        hex = $"#{parsed.R:X2}{parsed.G:X2}{parsed.B:X2}";
        return true;
    }

    private static bool TryParseColor(string? color, out global::Windows.UI.Color parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(color))
        {
            return false;
        }

        var input = color.Trim();
        if (input.StartsWith('#'))
        {
            var hex = input[1..];
            if (hex.Length == 3)
            {
                hex = string.Concat(hex.Select(c => $"{c}{c}"));
            }

            if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            {
                parsed = CreateColor(
                    255,
                    (byte)((rgb >> 16) & 0xFF),
                    (byte)((rgb >> 8) & 0xFF),
                    (byte)(rgb & 0xFF));
                return true;
            }

            if (hex.Length == 8 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var argb))
            {
                parsed = CreateColor(
                    (byte)((argb >> 24) & 0xFF),
                    (byte)((argb >> 16) & 0xFF),
                    (byte)((argb >> 8) & 0xFF),
                    (byte)(argb & 0xFF));
                return true;
            }
        }

        var property = typeof(Microsoft.UI.Colors).GetProperty(input, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
        if (property?.PropertyType == typeof(global::Windows.UI.Color))
        {
            try
            {
                parsed = (global::Windows.UI.Color)property.GetValue(null)!;
            }
            catch (Exception ex) when (ex is COMException || ex.GetBaseException() is COMException)
            {
                parsed = CreateColorFromKnownName(input);
            }

            return true;
        }

        return false;
    }

    private static global::Windows.UI.Color CreateColorFromKnownName(string input)
    {
        var color = System.Drawing.Color.FromName(input);
        return CreateColor(color.A, color.R, color.G, color.B);
    }

    private static global::Windows.UI.Color CreateColor(byte a, byte r, byte g, byte b)
    {
        return global::Windows.UI.Color.FromArgb(a, r, g, b);
    }

    [GeneratedRegex("<svg\\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex SvgOpenTagRegex();
}
