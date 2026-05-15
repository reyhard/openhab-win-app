using System.Text.Json;

namespace OpenHab.Windows.Notifications;

public static class CloudNotificationNormalizer
{
    public static NormalizedCloudNotification Normalize(CloudNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var payload = notification.Payload;
        var message = FirstNonEmpty(
            GetPayloadString(payload, "message"),
            notification.Message) ?? string.Empty;
        var title = FirstNonEmpty(
            GetPayloadString(payload, "title"),
            notification.Title);
        var icon = FirstNonEmpty(
            GetPayloadString(payload, "icon"),
            notification.Icon);
        var tag = FirstNonEmpty(
            GetPayloadString(payload, "tag"),
            GetPayloadString(payload, "severity"),
            notification.Tag,
            notification.Severity);
        var referenceId = FirstNonEmpty(
            GetPayloadString(payload, "reference-id"),
            GetPayloadString(payload, "referenceId"),
            notification.ReferenceId);
        var onClickAction = FirstNonEmpty(
            GetPayloadString(payload, "on-click-action"),
            GetPayloadString(payload, "onClickAction"),
            notification.OnClickAction);
        var mediaAttachmentUrl = FirstNonEmpty(
            GetPayloadString(payload, "media-attachment-url"),
            GetPayloadString(payload, "mediaAttachmentUrl"),
            notification.MediaAttachmentUrl);
        var kind = ResolveKind(payload, notification.Type);
        var hideTargets = BuildHideTargets(kind, referenceId, tag);
        var buttons = kind == CloudNotificationKind.Hide
            ? []
            : BuildActionButtons(notification, payload);

        return new NormalizedCloudNotification(
            notification.Id,
            message.Trim(),
            notification.Created,
            TrimToNull(title),
            TrimToNull(icon),
            TrimToNull(tag),
            TrimToNull(referenceId),
            TrimToNull(onClickAction),
            TrimToNull(mediaAttachmentUrl),
            buttons,
            kind,
            hideTargets);
    }

    private static CloudNotificationKind ResolveKind(JsonElement? payload, string? fallbackType)
    {
        var type = FirstNonEmpty(GetPayloadString(payload, "type"), fallbackType);
        if (string.Equals(type, "hideNotification", StringComparison.OrdinalIgnoreCase))
        {
            return CloudNotificationKind.Hide;
        }

        if (string.Equals(type, "log", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "logOnly", StringComparison.OrdinalIgnoreCase))
        {
            return CloudNotificationKind.LogOnly;
        }

        return CloudNotificationKind.Push;
    }

    private static List<NotificationHideTarget> BuildHideTargets(
        CloudNotificationKind kind,
        string? referenceId,
        string? tag)
    {
        if (kind != CloudNotificationKind.Hide)
        {
            return [];
        }

        var targets = new List<NotificationHideTarget>();
        if (!string.IsNullOrWhiteSpace(referenceId))
        {
            targets.Add(new NotificationHideTarget(NotificationHideTargetKind.ReferenceId, referenceId.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            targets.Add(new NotificationHideTarget(NotificationHideTargetKind.Tag, tag.Trim()));
        }

        return targets;
    }

    private static IReadOnlyList<NotificationActionButton> BuildActionButtons(
        CloudNotification notification,
        JsonElement? payload)
    {
        var buttons = new List<NotificationActionButton>();
        AddButton(buttons, GetPayloadString(payload, "actionButton1") ?? notification.ActionButton1);
        AddButton(buttons, GetPayloadString(payload, "actionButton2") ?? notification.ActionButton2);
        AddButton(buttons, GetPayloadString(payload, "actionButton3") ?? notification.ActionButton3);
        AddPayloadActions(buttons, payload);
        return [.. buttons.Take(3)];
    }

    private static void AddPayloadActions(List<NotificationActionButton> buttons, JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } payloadElement
            || !payloadElement.TryGetProperty("actions", out var actions))
        {
            return;
        }

        if (actions.ValueKind == JsonValueKind.String)
        {
            AddButton(buttons, actions.GetString());
            return;
        }

        if (actions.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var action in actions.EnumerateArray())
        {
            if (action.ValueKind == JsonValueKind.String)
            {
                AddButton(buttons, action.GetString());
                continue;
            }

            if (action.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var title = GetString(action, "title") ?? GetString(action, "label");
            var rawAction = GetString(action, "action");
            if (!string.IsNullOrWhiteSpace(title)
                && !string.IsNullOrWhiteSpace(rawAction)
                && NotificationActionParser.TryParse(rawAction) is { } parsed)
            {
                buttons.Add(new NotificationActionButton(title.Trim(), parsed.Type, parsed.Payload));
            }
        }
    }

    private static void AddButton(List<NotificationActionButton> buttons, string? rawButton)
    {
        if (NotificationActionParser.TryParseButton(rawButton) is { } button)
        {
            buttons.Add(button);
        }
    }

    private static string? GetPayloadString(JsonElement? payload, string propertyName)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } element)
        {
            return null;
        }

        return GetString(element, propertyName);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return Array.Find(values, value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
