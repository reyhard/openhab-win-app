using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenHab.Windows.Notifications;

public sealed record CloudNotification(
    [property: JsonPropertyName("_id")] string Id,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("created")] DateTimeOffset Created,
    [property: JsonPropertyName("tag")] string? Tag,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("referenceId")] string? ReferenceId,
    [property: JsonPropertyName("onClickAction")] string? OnClickAction,
    [property: JsonPropertyName("mediaAttachmentUrl")] string? MediaAttachmentUrl,
    [property: JsonPropertyName("actionButton1")] string? ActionButton1,
    [property: JsonPropertyName("actionButton2")] string? ActionButton2,
    [property: JsonPropertyName("actionButton3")] string? ActionButton3,
    [property: JsonPropertyName("payload")] JsonElement? Payload);
