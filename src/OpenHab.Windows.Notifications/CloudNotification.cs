using System.Text.Json.Serialization;

namespace OpenHab.Windows.Notifications;

public sealed record CloudNotification(
    [property: JsonPropertyName("_id")] string Id,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("created")] DateTimeOffset Created,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("severity")] string? Severity);
