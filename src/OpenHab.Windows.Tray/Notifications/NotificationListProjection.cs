using OpenHab.App.Localization;
using OpenHab.App.Notifications;
using OpenHab.Windows.Tray.Localization;
using Microsoft.UI.Xaml;

namespace OpenHab.Windows.Tray.Notifications;

internal enum NotificationSortOrder
{
    DateDescending,
    DateAscending,
    Name
}

internal sealed record NotificationRowViewModel(
    StoredNotification Notification,
    string Id,
    string Title,
    string Message,
    string? Icon,
    string? Severity,
    string ElapsedText,
    bool IsUnread)
{
    public string SeverityText => Severity ?? string.Empty;

    public Visibility SeverityVisibility => string.IsNullOrWhiteSpace(Severity)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility UnreadVisibility => IsUnread ? Visibility.Visible : Visibility.Collapsed;
}

internal static class NotificationListProjection
{
    public static IReadOnlyList<NotificationRowViewModel> CreateRows(
        IEnumerable<StoredNotification> notifications,
        NotificationSortOrder sortOrder,
        ITextLocalizer text,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(text);

        var defaultTitle = text.Get(AppResourceKeys.NotificationsDefaultTitle);
        var sorted = sortOrder switch
        {
            NotificationSortOrder.DateAscending => notifications
                .OrderBy(n => n.Created)
                .ThenBy(n => n.Title ?? defaultTitle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(n => n.Message ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(n => n.Id, StringComparer.Ordinal),
            NotificationSortOrder.Name => notifications
                .OrderBy(n => n.Title ?? defaultTitle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(n => n.Message ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(n => n.Id, StringComparer.Ordinal),
            _ => notifications
                .OrderByDescending(n => n.Created)
                .ThenBy(n => n.Title ?? defaultTitle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(n => n.Message ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(n => n.Id, StringComparer.Ordinal)
        };

        return sorted
            .Select(n => new NotificationRowViewModel(
                Notification: n,
                Id: n.Id,
                Title: n.Title ?? defaultTitle,
                Message: n.Message,
                Icon: n.Icon,
                Severity: n.Severity,
                ElapsedText: FormatElapsedTime(now - n.Created, text),
                IsUnread: !n.IsRead && !n.IsDismissed))
            .ToList();
    }

    private static string FormatElapsedTime(TimeSpan elapsed, ITextLocalizer text)
    {
        if (elapsed.TotalMinutes < 1)
        {
            return text.Get(AppResourceKeys.NotificationsElapsedJustNow);
        }

        if (elapsed.TotalHours < 1)
        {
            return text.Format(AppResourceKeys.NotificationsElapsedMinutesAgo, (int)elapsed.TotalMinutes);
        }

        return elapsed.TotalDays < 1
            ? text.Format(AppResourceKeys.NotificationsElapsedHoursAgo, (int)elapsed.TotalHours)
            : text.Format(AppResourceKeys.NotificationsElapsedDaysAgo, (int)elapsed.TotalDays);
    }
}
