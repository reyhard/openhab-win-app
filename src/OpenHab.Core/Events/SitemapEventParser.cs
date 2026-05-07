using System.Text.Json;

namespace OpenHab.Core.Events;

public static class SitemapEventParser
{
    public static object? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        if (line.StartsWith(':')) return null;
        if (!line.StartsWith("data: ", StringComparison.Ordinal)) return null;

        var json = line.Substring(6);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Handle ALIVE pings
            if (root.TryGetProperty("TYPE", out var typeEl) && typeEl.GetString() == "ALIVE")
                return null;

            // Parse widget update
            var widgetId = GetStringOrDefault(root, "widgetId");
            var sitemapName = GetStringOrDefault(root, "sitemapName");
            var pageId = GetStringOrDefault(root, "pageId");

            if (string.IsNullOrEmpty(widgetId))
                return null;

            var label = GetStringOrNull(root, "label");
            var icon = GetStringOrNull(root, "icon");
            var visibility = !root.TryGetProperty("visibility", out var vis) || vis.ValueKind != JsonValueKind.False;
            var descChanged = root.TryGetProperty("descriptionChanged", out var dc) && dc.ValueKind == JsonValueKind.True;

            string? itemName = null;
            string? itemState = null;
            if (root.TryGetProperty("item", out var itemEl) && itemEl.ValueKind == JsonValueKind.Object)
            {
                itemName = GetStringOrNull(itemEl, "name");
                itemState = GetStringOrNull(itemEl, "state");
            }

            return new SitemapWidgetEvent(widgetId, label, icon, visibility, itemName, itemState, sitemapName, pageId, descChanged);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? ParseSubscriptionId(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            if (root.TryGetProperty("context", out var ctx) &&
                ctx.TryGetProperty("headers", out var headers) &&
                headers.TryGetProperty("Location", out var location) &&
                location.ValueKind == JsonValueKind.Array &&
                location.GetArrayLength() > 0)
            {
                var url = location[0].GetString();
                if (url is not null)
                {
                    var lastSlash = url.LastIndexOf('/');
                    return lastSlash >= 0 ? url.Substring(lastSlash + 1) : url;
                }
            }
        }
        catch (JsonException)
        {
            // Ignore parse errors
        }

        return null;
    }

    private static string GetStringOrDefault(JsonElement element, string propertyName)
    {
        return GetStringOrNull(element, propertyName) ?? string.Empty;
    }

    private static string? GetStringOrNull(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyElement))
            return null;

        return propertyElement.ValueKind == JsonValueKind.String
            ? propertyElement.GetString()
            : null;
    }
}
