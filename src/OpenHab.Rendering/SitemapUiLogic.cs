using System.Globalization;
using System.Text;
using OpenHab.Rendering.Descriptors;
using OpenHab.Rendering.SitemapSurface;
using OpenHab.Sitemaps.Models;

namespace OpenHab.Rendering;

public readonly record struct SitemapColor(byte A, byte R, byte G, byte B);

public static class SitemapUiLogic
{
    private const string OpenStreetMapCoordinateFormat = "0.#####";

    private static readonly Dictionary<string, string> Win11IconMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["light"] = "\uE706", ["lights"] = "\uE706",
        ["lighton"] = "\uE706", ["lightoff"] = "\uE706",
        ["lightson"] = "\uE706", ["lightsoff"] = "\uE706",
        ["dimmer"] = "\uE706",
        ["colorpicker"] = "\uE790", ["color"] = "\uE790",
        ["switch"] = "\uE7E8",
        ["switchon"] = "\uE7E8", ["switchoff"] = "\uE7E8",
        ["poweron"] = "\uE7E8", ["poweroff"] = "\uE7E8",
        ["energy"] = "\uE946", ["power"] = "\uE946",
        ["outlet"] = "\uE994", ["plug"] = "\uE994",
        ["poweroutlet"] = "\uE994", ["power_outlet"] = "\uE994",
        ["battery"] = "\uEBA0", ["batterylevel"] = "\uEBA0",
        ["rollershutter"] = "\uE728", ["blinds"] = "\uE728",
        ["window"] = "\uE7C4",
        ["door"] = "\uE8E1", ["garagedoor"] = "\uE8E1",
        ["contact"] = "\uE8E1",
        ["lock"] = "\uE72E",
        ["heating"] = "\uE9CA", ["temp"] = "\uE9CA",
        ["temperature"] = "\uE9CA", ["climate"] = "\uE9CA",
        ["radiator"] = "\uE9CA",
        ["humidity"] = "\uEB42", ["moisture"] = "\uEB42",
        ["water"] = "\uEB42",
        ["gas"] = "\uE825",
        ["fan"] = "\uE785", ["fan_ceiling"] = "\uE785",
        ["pump"] = "\uE785",
        ["motion"] = "\uE916",
        ["presence"] = "\uE716", ["occupancy"] = "\uE716",
        ["alarm"] = "\uEA8F", ["siren"] = "\uEA8F",
        ["smoke"] = "\uE7BA",
        ["camera"] = "\uE722",
        ["speaker"] = "\uE7F5", ["audio"] = "\uE7F5", ["receiver"] = "\uE7F5",
        ["tv"] = "\uE7F4", ["screen"] = "\uE7F4",
        ["player"] = "\uE768", ["music"] = "\uE768",
        ["image"] = "\uE722", ["video"] = "\uE714",
        ["sun"] = "\uE706", ["sunrise"] = "\uE706", ["sunset"] = "\uE706",
        ["moon"] = "\uE708",
        ["cloud"] = "\uE753", ["weather"] = "\uE753",
        ["sunclouds"] = "\uE753", ["sun_clouds"] = "\uE753",
        ["rain"] = "\uEB42",
        ["wind"] = "\uE743",
        ["snow"] = "\uE9C8",
        ["pressure"] = "\uE976",
        ["groundfloor"] = "\uE831", ["ground_floor"] = "\uE831", ["firstfloor"] = "\uE831", ["first_floor"] = "\uE831",
        ["floorplan"] = "\uE831",
        ["kitchen"] = "\uE7A7", ["bath"] = "\uE7A8", ["bathroom"] = "\uE7A8",
        ["bedroom"] = "\uE7A9", ["living"] = "\uE7F4",
        ["office"] = "\uE7AB",
        ["garage"] = "\uE83D", ["garden"] = "\uE7A5", ["terrace"] = "\uE7A5",
        ["attic"] = "\uE831", ["cellar"] = "\uE831", ["basement"] = "\uE831",
        ["location"] = "\uE707",
        ["network"] = "\uE701", ["wifi"] = "\uE701",
        ["quality"] = "\uE769", ["co2"] = "\uE769", ["airquality"] = "\uE769",
        ["chart"] = "\uE9D2", ["number"] = "\uE9D2",
        ["pie"] = "\uE9D2", ["line"] = "\uE9D2",
        ["text"] = "\uE8A5", ["string"] = "\uE8A5", ["group"] = "\uE902",
        ["settings"] = "\uE713", ["setup"] = "\uE713",
        ["time"] = "\uE787", ["datetime"] = "\uE787", ["date"] = "\uE787",
        ["none"] = "\uE776"
    };

    private static readonly Dictionary<string, string> NormalizedWin11IconMap = Win11IconMap
        .GroupBy(kvp => NormalizeIconName(kvp.Key))
        .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

    private static readonly Dictionary<string, SitemapColor> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = new(255, 0, 0, 0),
        ["blue"] = new(255, 0, 0, 255),
        ["gray"] = new(255, 128, 128, 128),
        ["green"] = new(255, 0, 128, 0),
        ["red"] = new(255, 255, 0, 0),
        ["transparent"] = new(0, 255, 255, 255),
        ["white"] = new(255, 255, 255, 255),
        ["yellow"] = new(255, 255, 255, 0)
    };

    public static string NormalizeIconName(string? iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName)) return string.Empty;
        ReadOnlySpan<char> span = iconName.Trim();

        var sb = new StringBuilder(span.Length);
        foreach (var ch in span)
        {
            if (ch is '_' or '-') continue;
            if (char.IsDigit(ch)) continue;
            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }

    public static string? ResolveWin11Glyph(string? iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName)) return null;

        if (Win11IconMap.TryGetValue(iconName, out var glyph))
        {
            return glyph;
        }

        var normalized = NormalizeIconName(iconName);
        return NormalizedWin11IconMap.TryGetValue(normalized, out glyph) ? glyph : null;
    }

    public static bool CanResolveWin11Glyph(string? iconName) => ResolveWin11Glyph(iconName) is not null;

    public static double ResolveWebviewHeight(SitemapRowDescriptor row) =>
        SitemapRowVisualPolicy.ResolveWebviewHeight(row);

    public static string BuildRowIdentityKey(SitemapRowDescriptor row) =>
        SitemapRowVisualPolicy.BuildRowIdentityKey(row);

    public static string BuildRowVisualStateKey(SitemapRowDescriptor row, int rowIndex) =>
        SitemapRowVisualPolicy.BuildRowVisualStateKey(row, rowIndex);

    public static Uri? BuildChartUrl(SitemapRowDescriptor row, Uri? baseUri, int chartDpi = 96, bool cacheBust = false)
    {
        ArgumentNullException.ThrowIfNull(row);

        var itemName = row.ItemName ?? row.RawItemState ?? row.RawState ?? row.State;
        if (string.IsNullOrWhiteSpace(itemName) && string.IsNullOrWhiteSpace(row.Command))
        {
            return null;
        }

        if (baseUri is null)
        {
            return null;
        }

        var items = Uri.EscapeDataString(itemName ?? row.Command ?? string.Empty);
        var period = row.Period ?? "D";
        var query = $"items={items}&period={Uri.EscapeDataString(period)}&dpi={chartDpi}";
        if (cacheBust)
        {
            query += $"&random={Random.Shared.Next()}";
        }

        return new Uri(baseUri, $"chart?{query}");
    }

    public static Uri? BuildMapviewUrl(SitemapRowDescriptor row, int zoom = 16)
    {
        ArgumentNullException.ThrowIfNull(row);

        var location = row.RawItemState ?? row.RawState ?? row.State;
        if (!TryParseOpenHabLocationState(location, out var latitude, out var longitude))
        {
            return null;
        }

        zoom = Math.Clamp(zoom, 1, 19);
        var lat = latitude.ToString(OpenStreetMapCoordinateFormat, CultureInfo.InvariantCulture);
        var lon = longitude.ToString(OpenStreetMapCoordinateFormat, CultureInfo.InvariantCulture);
        var margin = Math.Clamp(400d / Math.Pow(2d, zoom), 0.001d, 20d);
        var minLatitude = Math.Clamp(latitude - margin, -90d, 90d).ToString(OpenStreetMapCoordinateFormat, CultureInfo.InvariantCulture);
        var maxLatitude = Math.Clamp(latitude + margin, -90d, 90d).ToString(OpenStreetMapCoordinateFormat, CultureInfo.InvariantCulture);
        var minLongitude = Math.Clamp(longitude - margin, -180d, 180d).ToString(OpenStreetMapCoordinateFormat, CultureInfo.InvariantCulture);
        var maxLongitude = Math.Clamp(longitude + margin, -180d, 180d).ToString(OpenStreetMapCoordinateFormat, CultureInfo.InvariantCulture);
        var bbox = Uri.EscapeDataString($"{minLongitude},{minLatitude},{maxLongitude},{maxLatitude}");
        var marker = Uri.EscapeDataString($"{lat},{lon}");
        return new Uri($"https://www.openstreetmap.org/export/embed.html?bbox={bbox}&layer=mapnik&marker={marker}");
    }

    public static Uri? ResolveEmbeddedUrl(SitemapRowDescriptor row, Uri? baseUri)
    {
        ArgumentNullException.ThrowIfNull(row);

        var url = row.Url ?? row.RawItemState ?? row.RawState ?? row.State;
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmed = url.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri;
        }

        return baseUri is not null && Uri.TryCreate(baseUri, trimmed, out var relativeUri)
            ? relativeUri
            : null;
    }

    public static string BuildIconPayloadCacheKey(Uri iconUri, string? iconColor, string authMode)
    {
        ArgumentNullException.ThrowIfNull(iconUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(authMode);

        return $"{iconUri.AbsoluteUri}|{iconColor ?? string.Empty}|{authMode}";
    }

    public static bool SelectionValueMatches(string? left, string? right)
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

    public static string? NormalizeInputByHint(string? raw, SitemapInputHint hint)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();
        return hint switch
        {
            SitemapInputHint.Date when TryParseDateOnly(value, out var date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            SitemapInputHint.Time when TryParseTimeOnly(value, out var time) => time.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            SitemapInputHint.DateTime when TryParseDateTime(value, out var dateTime) => dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            SitemapInputHint.Color when TryResolveOpenHabColor(value, out var color) => BuildOpenHabColorCommand(color),
            SitemapInputHint.Number when HasIntegerLeadingZeros(value) => value,
            SitemapInputHint.Number when TryParseDoubleInvariant(value, out var number) => number.ToString(CultureInfo.InvariantCulture),
            _ => value
        };
    }

    public static bool TryParseOpenHabColorState(string? raw, out double hue, out double saturation, out double brightness)
    {
        hue = 0;
        saturation = 0;
        brightness = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var parts = raw.Trim().Split(',');
        if (parts.Length < 3)
        {
            return false;
        }

        if (!TryParseDoubleInvariant(parts[0], out hue)
            || !TryParseDoubleInvariant(parts[1], out saturation)
            || !TryParseDoubleInvariant(parts[2], out brightness))
        {
            return false;
        }

        hue = Math.Clamp(hue, 0d, 360d);
        saturation = Math.Clamp(saturation, 0d, 100d);
        brightness = Math.Clamp(brightness, 0d, 100d);
        return true;
    }

    public static bool TryParseOpenHabLocationState(string? raw, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var parts = raw.Trim().Split(',');
        if (parts.Length < 2)
        {
            return false;
        }

        if (!TryParseDoubleInvariant(parts[0], out latitude)
            || !TryParseDoubleInvariant(parts[1], out longitude))
        {
            return false;
        }

        return latitude is >= -90d and <= 90d
            && longitude is >= -180d and <= 180d;
    }

    public static bool TryResolveOpenHabColor(string? raw, out SitemapColor color)
    {
        color = default;
        if (TryParseOpenHabColorState(raw, out var hue, out var saturation, out var brightness))
        {
            color = ColorFromHsv(hue, saturation, brightness);
            return true;
        }

        return TryParseColor(raw, out color);
    }

    public static string BuildOpenHabColorCommand(SitemapColor color)
    {
        ColorToHsv(color, out var hue, out var saturation, out var brightness);
        var roundedHue = (int)Math.Round(hue, MidpointRounding.AwayFromZero);
        var roundedSaturation = (int)Math.Round(saturation, MidpointRounding.AwayFromZero);
        var roundedBrightness = (int)Math.Round(brightness, MidpointRounding.AwayFromZero);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{roundedHue},{roundedSaturation},{roundedBrightness}");
    }

    public static SitemapColor ResolveColorTemperaturePreview(double value, double min, double max)
    {
        var range = max - min;
        var ratio = range <= double.Epsilon ? 0d : (value - min) / range;
        ratio = Math.Clamp(ratio, 0d, 1d);

        var warm = new SitemapColor(255, 255, 180, 96);
        var cool = new SitemapColor(255, 201, 226, 255);

        var r = (byte)Math.Round(warm.R + ((cool.R - warm.R) * ratio), MidpointRounding.AwayFromZero);
        var g = (byte)Math.Round(warm.G + ((cool.G - warm.G) * ratio), MidpointRounding.AwayFromZero);
        var b = (byte)Math.Round(warm.B + ((cool.B - warm.B) * ratio), MidpointRounding.AwayFromZero);
        return new SitemapColor(255, r, g, b);
    }

    public static void ColorToHsv(SitemapColor color, out double hue, out double saturation, out double brightness)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        if (delta <= double.Epsilon)
        {
            hue = 0d;
        }
        else if (Math.Abs(max - r) < double.Epsilon)
        {
            hue = 60d * (((g - b) / delta) % 6d);
        }
        else if (Math.Abs(max - g) < double.Epsilon)
        {
            hue = 60d * (((b - r) / delta) + 2d);
        }
        else
        {
            hue = 60d * (((r - g) / delta) + 4d);
        }

        if (hue < 0d)
        {
            hue += 360d;
        }

        saturation = max <= double.Epsilon ? 0d : (delta / max) * 100d;
        brightness = max * 100d;
    }

    public static string BuildOpenHabColorHex(SitemapColor color)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"#{color.R:X2}{color.G:X2}{color.B:X2}");
    }

    private static bool TryParseColor(string? raw, out SitemapColor parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var input = raw.Trim();
        if (input.StartsWith('#'))
        {
            var hex = input[1..];
            if (hex.Length == 3)
            {
                hex = string.Concat(hex.Select(c => $"{c}{c}"));
            }

            if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                parsed = new SitemapColor(
                    255,
                    (byte)((rgb >> 16) & 0xFF),
                    (byte)((rgb >> 8) & 0xFF),
                    (byte)(rgb & 0xFF));
                return true;
            }

            if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            {
                parsed = new SitemapColor(
                    (byte)((argb >> 24) & 0xFF),
                    (byte)((argb >> 16) & 0xFF),
                    (byte)((argb >> 8) & 0xFF),
                    (byte)(argb & 0xFF));
                return true;
            }
        }

        return NamedColors.TryGetValue(input, out parsed);
    }

    private static bool TryParseDateOnly(string? raw, out DateTimeOffset date)
    {
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date))
        {
            return true;
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out date);
    }

    private static bool TryParseTimeOnly(string? raw, out TimeSpan time)
    {
        if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out time))
        {
            return true;
        }

        return TimeSpan.TryParse(raw, CultureInfo.CurrentCulture, out time);
    }

    private static bool TryParseDateTime(string? raw, out DateTimeOffset dateTime)
    {
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dateTime))
        {
            return true;
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dateTime);
    }

    private static bool HasIntegerLeadingZeros(string value)
    {
        if (value.Length < 2 || value[0] != '0')
        {
            return false;
        }

        return value.All(char.IsAsciiDigit);
    }

    private static bool TryParseDoubleInvariant(string input, out double value)
    {
        var candidate = input.Trim();
        return double.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || double.TryParse(candidate, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static SitemapColor ColorFromHsv(double hue, double saturation, double brightness)
    {
        var normalizedHue = ((hue % 360d) + 360d) % 360d;
        var s = Math.Clamp(saturation, 0d, 100d) / 100d;
        var v = Math.Clamp(brightness, 0d, 100d) / 100d;
        var chroma = v * s;
        var x = chroma * (1d - Math.Abs((normalizedHue / 60d) % 2d - 1d));
        var m = v - chroma;

        var (r1, g1, b1) = normalizedHue switch
        {
            < 60d => (chroma, x, 0d),
            < 120d => (x, chroma, 0d),
            < 180d => (0d, chroma, x),
            < 240d => (0d, x, chroma),
            < 300d => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        var r = (byte)Math.Round((r1 + m) * 255d, MidpointRounding.AwayFromZero);
        var g = (byte)Math.Round((g1 + m) * 255d, MidpointRounding.AwayFromZero);
        var b = (byte)Math.Round((b1 + m) * 255d, MidpointRounding.AwayFromZero);
        return new SitemapColor(255, r, g, b);
    }
}
