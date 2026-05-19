using OpenHab.App.Notifications;
using OpenHab.App.Localization;
using OpenHab.Windows.Tray.Localization;
using OpenHab.Windows.Tray.Notifications;

namespace OpenHab.App.Tests.Notifications;

public class NotificationsPageControlTests
{
    [Fact]
    public void NotificationServerIcons_KeepCompactInterfaceSize()
    {
        Assert.Equal(20, NotificationUiMetrics.ServerIconSize);
    }

    [Fact]
    public void NotificationListProjection_SortsByNameWithFallbackTitle()
    {
        var created = new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);
        var notifications = new[]
        {
            CreateNotification("b", "Second", title: "Beta", created),
            CreateNotification("a", "First", title: null, created),
            CreateNotification("c", "Third", title: "Alpha", created)
        };

        var rows = NotificationListProjection.CreateRows(
            notifications,
            NotificationSortOrder.Name,
            DefaultEnglishTextLocalizer.Instance,
            now: created);

        Assert.Equal(["c", "b", "a"], rows.Select(row => row.Id).ToArray());
        Assert.Equal("openHAB", rows[2].Title);
    }

    [Fact]
    public void NotificationListProjection_FormatsElapsedTimeOnceForVisibleRows()
    {
        var now = new DateTimeOffset(2026, 5, 19, 12, 30, 0, TimeSpan.Zero);
        var notifications = new[]
        {
            CreateNotification("n1", "Message", title: "Title", now.AddMinutes(-12))
        };

        var rows = NotificationListProjection.CreateRows(
            notifications,
            NotificationSortOrder.DateDescending,
            DefaultEnglishTextLocalizer.Instance,
            now);

        Assert.Single(rows);
        Assert.Equal("12m ago", rows[0].ElapsedText);
    }

    private static StoredNotification CreateNotification(
        string id,
        string message,
        string? title,
        DateTimeOffset created) =>
        new(
            id,
            message,
            title,
            Icon: null,
            Severity: null,
            created,
            ReceivedAt: created,
            IsRead: false,
            IsDismissed: false,
            ReferenceId: null,
            OnClickAction: null,
            MediaAttachmentUrl: null,
            ActionButton1: null,
            ActionButton2: null,
            ActionButton3: null);
}
