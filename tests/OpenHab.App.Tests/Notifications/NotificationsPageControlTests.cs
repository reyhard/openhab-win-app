using System.Reflection;
using Microsoft.UI.Xaml.Media;
using OpenHab.Windows.Tray.Notifications;

namespace OpenHab.App.Tests.Notifications;

public class NotificationsPageControlTests
{
    [Fact]
    public void NotificationServerIcons_KeepCompactInterfaceSize()
    {
        var iconSizeField = typeof(NotificationsPageControl).GetField(
            "NotificationServerIconSize",
            BindingFlags.NonPublic | BindingFlags.Static);

        var iconSize = Assert.IsType<double>(iconSizeField?.GetRawConstantValue());

        Assert.Equal(20, iconSize);
    }

    [Fact]
    public void NotificationIcons_UseSharedImageSourceLoader()
    {
        var localDecodeMethod = typeof(NotificationsPageControl).GetMethod(
            "CreateImageSourceFromBytesAsync",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.Null(localDecodeMethod);
    }
}
