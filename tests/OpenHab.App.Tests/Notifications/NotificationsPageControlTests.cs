using OpenHab.App.Notifications;

namespace OpenHab.App.Tests.Notifications;

public class NotificationsPageControlTests
{
    [Fact]
    public void NotificationServerIcons_KeepCompactInterfaceSize()
    {
        Assert.Equal(20, NotificationUiMetrics.ServerIconSize);
    }
}
