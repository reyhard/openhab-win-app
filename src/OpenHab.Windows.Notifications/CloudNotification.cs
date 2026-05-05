namespace OpenHab.Windows.Notifications;

public sealed record CloudNotification(
    string Id,
    string Message,
    DateTimeOffset Created,
    string? Icon,
    string? Severity);
