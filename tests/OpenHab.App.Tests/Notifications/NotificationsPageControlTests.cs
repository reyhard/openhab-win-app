using Microsoft.UI.Xaml;
using OpenHab.App.Notifications;
using OpenHab.App.Localization;
using OpenHab.Windows.Tray.Localization;
using OpenHab.Windows.Tray.Notifications;

namespace OpenHab.App.Tests.Notifications;

public class NotificationsPageControlTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

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

    [Fact]
    public void NotificationListProjection_SortsByCreationDateDescendingAndPreservesRowData()
    {
        var oldest = CreateNotification("oldest", "Oldest message", "Oldest", FixedNow.AddHours(-3));
        var newest = CreateNotification(
            "newest",
            "Newest message",
            "Newest",
            FixedNow.AddHours(-1),
            icon: "icon://newest",
            severity: "warning");
        var middle = CreateNotification("middle", "Middle message", "Middle", FixedNow.AddHours(-2));

        var rows = Project([middle, oldest, newest], NotificationSortOrder.DateDescending);

        Assert.Equal(["newest", "middle", "oldest"], rows.Select(row => row.Id).ToArray());
        Assert.Same(newest, rows[0].Notification);
        Assert.Equal("Newest message", rows[0].Message);
        Assert.Equal("Newest", rows[0].Title);
        Assert.Equal("icon://newest", rows[0].Icon);
        Assert.Equal("warning", rows[0].Severity);
    }

    [Fact]
    public void NotificationListProjection_SortsByCreationDateAscending()
    {
        var oldest = CreateNotification("oldest", "Oldest message", "Oldest", FixedNow.AddHours(-3));
        var newest = CreateNotification("newest", "Newest message", "Newest", FixedNow.AddHours(-1));
        var middle = CreateNotification("middle", "Middle message", "Middle", FixedNow.AddHours(-2));

        var rows = Project([newest, middle, oldest], NotificationSortOrder.DateAscending);

        Assert.Equal(["oldest", "middle", "newest"], rows.Select(row => row.Id).ToArray());
    }

    [Fact]
    public void NotificationListProjection_BreaksEqualCreationTimesByTitleThenMessageThenId()
    {
        var created = FixedNow.AddMinutes(-5);
        var notifications = new[]
        {
            CreateNotification("d", "Same message", "alpha", created),
            CreateNotification("c", "Same message", "Alpha", created),
            CreateNotification("b", "Zulu message", "Alpha", created),
            CreateNotification("a", "alpha message", "Alpha", created)
        };

        var descending = Project(notifications, NotificationSortOrder.DateDescending);
        var ascending = Project(notifications, NotificationSortOrder.DateAscending);

        Assert.Equal(["a", "c", "d", "b"], descending.Select(row => row.Id).ToArray());
        Assert.Equal(["a", "c", "d", "b"], ascending.Select(row => row.Id).ToArray());
    }

    [Fact]
    public void NotificationListProjection_SortsByNameWithMessageAndIdTieBreakers()
    {
        var notifications = new[]
        {
            CreateNotification("z", "Second", "Beta", FixedNow),
            CreateNotification("y", "First", "beta", FixedNow),
            CreateNotification("x", "Third", "Alpha", FixedNow),
            CreateNotification("w", "Third", "alpha", FixedNow)
        };

        var rows = Project(notifications, NotificationSortOrder.Name);

        Assert.Equal(["w", "x", "y", "z"], rows.Select(row => row.Id).ToArray());
    }

    [Fact]
    public void NotificationListProjection_FormatsElapsedTimeAtBoundaries()
    {
        var notifications = new[]
        {
            CreateNotification("future", "Message", "Future", FixedNow.AddMinutes(5)),
            CreateNotification("seconds", "Message", "Seconds", FixedNow.AddSeconds(-59)),
            CreateNotification("minute", "Message", "Minute", FixedNow.AddMinutes(-1)),
            CreateNotification("minutes", "Message", "Minutes", FixedNow.AddMinutes(-59)),
            CreateNotification("hour", "Message", "Hour", FixedNow.AddHours(-1)),
            CreateNotification("hours", "Message", "Hours", FixedNow.AddHours(-23)),
            CreateNotification("hoursTruncated", "Message", "Hours truncated", FixedNow.AddMinutes(-90)),
            CreateNotification("day", "Message", "Day", FixedNow.AddDays(-1))
        };

        var rows = Project(notifications, NotificationSortOrder.DateAscending);
        var elapsedById = rows.ToDictionary(row => row.Id, row => row.ElapsedText);

        Assert.Equal(notifications.Length, rows.Count);
        Assert.Equal("Just now", elapsedById["future"]);
        Assert.Equal("Just now", elapsedById["seconds"]);
        Assert.Equal("1m ago", elapsedById["minute"]);
        Assert.Equal("59m ago", elapsedById["minutes"]);
        Assert.Equal("1h ago", elapsedById["hour"]);
        Assert.Equal("23h ago", elapsedById["hours"]);
        Assert.Equal("1h ago", elapsedById["hoursTruncated"]);
        Assert.Equal("1d ago", elapsedById["day"]);
    }

    [Theory]
    [InlineData(false, false, Visibility.Visible)]
    [InlineData(true, false, Visibility.Collapsed)]
    [InlineData(false, true, Visibility.Collapsed)]
    [InlineData(true, true, Visibility.Collapsed)]
    public void NotificationListProjection_ShowsUnreadIndicatorOnlyForUnreadVisibleRows(
        bool isRead,
        bool isDismissed,
        Visibility expectedVisibility)
    {
        var notification = CreateNotification(
            "n1",
            "Message",
            "Title",
            FixedNow.AddMinutes(-2),
            isRead: isRead,
            isDismissed: isDismissed);

        var row = Assert.Single(Project([notification], NotificationSortOrder.DateDescending));

        Assert.Equal(expectedVisibility == Visibility.Visible, row.IsUnread);
        Assert.Equal(expectedVisibility, row.UnreadVisibility);
    }

    [Theory]
    [InlineData(null, "", Visibility.Collapsed)]
    [InlineData("", "", Visibility.Collapsed)]
    [InlineData("   ", "   ", Visibility.Collapsed)]
    [InlineData("warning", "warning", Visibility.Visible)]
    public void NotificationListProjection_PresentsSeverityTextAndVisibility(
        string? severity,
        string expectedText,
        Visibility expectedVisibility)
    {
        var notification = CreateNotification("n1", "Message", "Title", FixedNow, severity: severity);

        var row = Assert.Single(Project([notification], NotificationSortOrder.DateDescending));

        Assert.Equal(expectedText, row.SeverityText);
        Assert.Equal(expectedVisibility, row.SeverityVisibility);
    }

    [Fact]
    public void NotificationListProjection_UsesLocalizedDefaultTitleAndPreservesRemainingFields()
    {
        var notification = CreateNotification(
            "n1",
            "Body",
            title: null,
            FixedNow,
            icon: "icon://logo",
            severity: "alert");

        var row = Assert.Single(Project([notification], NotificationSortOrder.DateDescending));

        Assert.Equal("openHAB", row.Title);
        Assert.Equal("Body", row.Message);
        Assert.Equal("icon://logo", row.Icon);
        Assert.Equal("alert", row.Severity);
        Assert.Equal("n1", row.Id);
    }

    [Fact]
    public void NotificationListProjection_RejectsNullNotifications()
    {
        Assert.Throws<ArgumentNullException>(() => NotificationListProjection.CreateRows(
            null!,
            NotificationSortOrder.Name,
            DefaultEnglishTextLocalizer.Instance,
            FixedNow));
    }

    [Fact]
    public void NotificationListProjection_RejectsNullLocalizer()
    {
        Assert.Throws<ArgumentNullException>(() => NotificationListProjection.CreateRows(
            Array.Empty<StoredNotification>(),
            NotificationSortOrder.Name,
            null!,
            FixedNow));
    }

    [Fact]
    public void NotificationListProjection_ReturnsEmptyListForNoNotifications()
    {
        var rows = Project([], NotificationSortOrder.DateAscending);

        Assert.Empty(rows);
    }

    private static IReadOnlyList<NotificationRowViewModel> Project(
        IEnumerable<StoredNotification> notifications,
        NotificationSortOrder sortOrder) =>
        NotificationListProjection.CreateRows(
            notifications,
            sortOrder,
            DefaultEnglishTextLocalizer.Instance,
            FixedNow);

    private static StoredNotification CreateNotification(
        string id,
        string message,
        string? title,
        DateTimeOffset created,
        string? icon = null,
        string? severity = null,
        bool isRead = false,
        bool isDismissed = false) =>
        new(
            id,
            message,
            title,
            Icon: icon,
            Severity: severity,
            created,
            ReceivedAt: created,
            IsRead: isRead,
            IsDismissed: isDismissed,
            ReferenceId: null,
            OnClickAction: null,
            MediaAttachmentUrl: null,
            ActionButton1: null,
            ActionButton2: null,
            ActionButton3: null);
}
