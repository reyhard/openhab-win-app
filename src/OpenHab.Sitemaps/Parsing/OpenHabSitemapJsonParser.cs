using System.Text.Json;
using OpenHab.Sitemaps.Models;

namespace OpenHab.Sitemaps.Parsing;

public static class OpenHabSitemapJsonParser
{
    public static SitemapPage ParseHomepage(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Sitemap JSON cannot be blank.", nameof(json));
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("homepage", out var homepageElement))
        {
            throw new FormatException("Sitemap JSON does not contain a homepage object.");
        }
        if (homepageElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Sitemap homepage must be a JSON object.");
        }

        return ParsePage(homepageElement);
    }

    private static SitemapPage ParsePage(JsonElement pageElement)
    {
        var id = GetStringOrDefault(pageElement, "id");
        var label = GetStringOrDefault(pageElement, "title");
        if (string.IsNullOrWhiteSpace(label))
        {
            label = GetStringOrDefault(pageElement, "label");
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            label = id;
        }

        var widgets = new List<SitemapWidget>();
        if (pageElement.TryGetProperty("widgets", out var widgetsElement) &&
            widgetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var widgetElement in widgetsElement.EnumerateArray())
            {
                if (widgetElement.ValueKind != JsonValueKind.Object)
                {
                    throw new FormatException(
                        $"Sitemap page '{id}' contains a non-object widget entry.");
                }

                var widget = ParseWidget(widgetElement);
                widgets.Add(widget);

                // Frames contain inline child widgets that need to be flattened into the page.
                if (widget.Type == SitemapWidgetType.Frame &&
                    widgetElement.TryGetProperty("widgets", out var frameWidgetsElement) &&
                    frameWidgetsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var childElement in frameWidgetsElement.EnumerateArray())
                    {
                        if (childElement.ValueKind == JsonValueKind.Object)
                        {
                            widgets.Add(ParseWidget(childElement));
                        }
                    }
                }
            }
        }

        return new SitemapPage(id, label, widgets);
    }

    private static SitemapWidget ParseWidget(JsonElement widgetElement)
    {
        var rawLabel = GetStringOrDefault(widgetElement, "label");
        var (label, stateFromLabel) = SplitLabelAndState(rawLabel);

        var typeRaw = GetStringOrDefault(widgetElement, "type");
        var type = Enum.TryParse<SitemapWidgetType>(typeRaw, ignoreCase: true, out var parsedType)
            ? parsedType
            : SitemapWidgetType.Default;

        string? itemName = null;
        string? itemState = null;
        if (widgetElement.TryGetProperty("item", out var itemElement) && itemElement.ValueKind == JsonValueKind.Object)
        {
            itemName = GetStringOrNull(itemElement, "name");
            itemState = GetStringOrNull(itemElement, "state");
        }

        var state = ResolveWidgetState(itemState, stateFromLabel);

        var mappings = new List<SitemapMapping>();
        if (widgetElement.TryGetProperty("mappings", out var mappingsElement) &&
            mappingsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var mappingElement in mappingsElement.EnumerateArray())
            {
                if (mappingElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var command = GetStringOrDefault(mappingElement, "command");
                var mappingLabel = GetStringOrDefault(mappingElement, "label");
                mappings.Add(new SitemapMapping(command, mappingLabel));
            }
        }

        var children = new List<SitemapPage>();
        if (widgetElement.TryGetProperty("linkedPage", out var linkedPageElement) &&
            linkedPageElement.ValueKind == JsonValueKind.Object)
        {
            children.Add(ParsePage(linkedPageElement));
        }

        var icon = GetStringOrNull(widgetElement, "icon");
        var minValue = GetDoubleOrNull(widgetElement, "minValue");
        var maxValue = GetDoubleOrNull(widgetElement, "maxValue");
        var step = GetDoubleOrNull(widgetElement, "step");

        var isVisible = true;
        if (widgetElement.TryGetProperty("visibility", out var visibilityElement) &&
            visibilityElement.ValueKind == JsonValueKind.False)
        {
            isVisible = false;
        }

        return new SitemapWidget(
            label,
            type,
            itemName,
            state,
            mappings,
            isVisible,
            children,
            icon,
            minValue,
            maxValue,
            step,
            itemState);
    }

    private static (string Label, string? State) SplitLabelAndState(string rawLabel)
    {
        if (string.IsNullOrWhiteSpace(rawLabel))
        {
            return (string.Empty, null);
        }

        var start = rawLabel.IndexOf(" [", StringComparison.Ordinal);
        if (start >= 0 && rawLabel.EndsWith(']'))
        {
            var stateStart = start + 2;
            var stateLength = rawLabel.Length - stateStart - 1;
            if (stateLength >= 0)
            {
                var parsedLabel = rawLabel[..start];
                var parsedState = rawLabel.Substring(stateStart, stateLength);
                return (parsedLabel, parsedState);
            }
        }

        return (rawLabel, null);
    }

    private static string GetStringOrDefault(JsonElement element, string propertyName)
    {
        return GetStringOrNull(element, propertyName) ?? string.Empty;
    }

    private static string? GetStringOrNull(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyElement))
        {
            return null;
        }

        return propertyElement.ValueKind == JsonValueKind.String
            ? propertyElement.GetString()
            : null;
    }

    private static double? GetDoubleOrNull(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyElement))
        {
            return null;
        }

        return propertyElement.ValueKind switch
        {
            JsonValueKind.Number when propertyElement.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(
                propertyElement.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null
        };
    }

    private static string? ResolveWidgetState(string? itemState, string? stateFromLabel)
    {
        if (string.IsNullOrWhiteSpace(itemState))
        {
            return stateFromLabel;
        }

        if (string.IsNullOrWhiteSpace(stateFromLabel))
        {
            return itemState;
        }

        if (string.Equals(itemState, stateFromLabel, StringComparison.OrdinalIgnoreCase))
        {
            return itemState;
        }

        // If both sides are opposite binary tokens, prefer item state to avoid stale
        // label snapshots (e.g. label "ON" but current item state "OFF").
        var itemIsBinary =
            string.Equals(itemState, "ON", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(itemState, "OFF", StringComparison.OrdinalIgnoreCase);
        var labelIsBinary =
            string.Equals(stateFromLabel, "ON", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(stateFromLabel, "OFF", StringComparison.OrdinalIgnoreCase);
        if (itemIsBinary && labelIsBinary &&
            !string.Equals(itemState, stateFromLabel, StringComparison.OrdinalIgnoreCase))
        {
            return itemState;
        }

        // Prefer user-facing formatted state from label (MAP transforms, DateTime format,
        // units, and other sitemap presentation rules).
        return stateFromLabel;
    }
}
