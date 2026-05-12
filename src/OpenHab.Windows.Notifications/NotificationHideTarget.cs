namespace OpenHab.Windows.Notifications;

public enum NotificationHideTargetKind
{
    ReferenceId,
    Tag
}

public sealed record NotificationHideTarget(NotificationHideTargetKind Kind, string Value);
