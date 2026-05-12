namespace OpenHab.Windows.Notifications;

public sealed record NormalizedCloudNotification(
    string Id,
    string Message,
    DateTimeOffset Created,
    string? Title,
    string? Icon,
    string? Tag,
    string? ReferenceId,
    string? OnClickAction,
    string? MediaAttachmentUrl,
    IReadOnlyList<NotificationActionButton> ActionButtons,
    CloudNotificationKind Kind,
    IReadOnlyList<NotificationHideTarget> HideTargets);
