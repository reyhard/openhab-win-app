namespace OpenHab.Rendering.Icons;

public static class OpenHabIconUriBuilder
{
    private const int MaxStateQueryValueLength = 256;

    public static Uri Build(Uri baseUri, string iconName, string? iconState, string format = "png")
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconName);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        iconState = NormalizeStateForRequest(iconState);

        var sourceSplit = iconName.Split(':', 2, StringSplitOptions.TrimEntries);
        var hasExplicitSource = sourceSplit.Length == 2;
        var iconSource = hasExplicitSource ? sourceSplit[0] : "oh";
        if (hasExplicitSource &&
            (iconSource.Equals("f7", StringComparison.OrdinalIgnoreCase) ||
             iconSource.Equals("material", StringComparison.OrdinalIgnoreCase) ||
             iconSource.Equals("if", StringComparison.OrdinalIgnoreCase) ||
             iconSource.Equals("iconify", StringComparison.OrdinalIgnoreCase)))
        {
            var source = iconSource.ToLowerInvariant();
            var rawName = sourceSplit[1];
            if (source == "f7")
            {
                return new Uri($"https://api.iconify.design/f7/{Uri.EscapeDataString(rawName.Replace('_', '-'))}.svg");
            }

            if (source == "material")
            {
                var materialName = $"baseline-{rawName.Replace('_', '-')}";
                return new Uri($"https://api.iconify.design/ic/{Uri.EscapeDataString(materialName)}.svg");
            }

            var iconifyName = rawName.Replace(':', '/');
            return new Uri($"https://api.iconify.design/{Uri.EscapeDataString(iconifyName)}.svg");
        }

        var escapedIcon = hasExplicitSource
            ? $"{Uri.EscapeDataString(iconSource)}:{Uri.EscapeDataString(sourceSplit[1])}"
            : Uri.EscapeDataString(iconName);
        var query = string.IsNullOrWhiteSpace(iconState)
            ? $"format={Uri.EscapeDataString(format)}"
            : $"format={Uri.EscapeDataString(format)}&state={Uri.EscapeDataString(iconState)}";

        return new Uri(baseUri, $"icon/{escapedIcon}?{query}");
    }

    public static string? NormalizeStateForRequest(string? iconState)
    {
        if (string.IsNullOrWhiteSpace(iconState))
        {
            return null;
        }

        var trimmed = iconState.Trim();
        if (trimmed.Length > MaxStateQueryValueLength)
        {
            return null;
        }

        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }
}
