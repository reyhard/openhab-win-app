using System.Diagnostics;
using System.Text.Json;

namespace OpenHab.Core.Events;

public static class SseMessageParser
{
    public static OpenHabEvent? ParseLine(string line)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            if (line.StartsWith(':'))
                return null;

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                return null;

            var json = line.Substring(6);

            using var outerDoc = JsonDocument.Parse(json);
            var root = outerDoc.RootElement;

            if (!root.TryGetProperty("topic", out var topicElement) ||
                !root.TryGetProperty("type", out var typeElement) ||
                !root.TryGetProperty("payload", out var payloadElement))
                return null;

            var topic = topicElement.GetString();
            var type = typeElement.GetString();
            var payloadString = payloadElement.GetString();

            if (topic is null || type is null || payloadString is null)
                return null;

            var itemName = ExtractItemName(topic);
            if (itemName is null)
                return null;

            using var payloadDoc = JsonDocument.Parse(payloadString);
            var payloadRoot = payloadDoc.RootElement;

            if (!payloadRoot.TryGetProperty("value", out var valueElement))
                return null;

            var value = GetValueAsString(valueElement);
            if (value is null)
                return null;

            return type switch
            {
                "ItemStateEvent" or "ItemStateChangedEvent" => new ItemStateChangedEvent(itemName, value, topic, type),
                "ItemCommandEvent" => new ItemCommandEvent(itemName, value, topic, type),
                _ => null
            };
        }
        catch (JsonException)
        {
            Debug.WriteLine("SseMessageParser: failed to parse JSON from SSE line");
            return null;
        }
    }

    private static string? ExtractItemName(string topic)
    {
        // Expected format: openhab/items/{itemName}/{action}
        var segments = topic.Split('/');
        if (segments.Length < 3)
            return null;
        if (segments[0] != "openhab" || segments[1] != "items")
            return null;
        return segments[2];
    }

    private static string? GetValueAsString(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.GetRawText(),
            _ => null
        };
}
