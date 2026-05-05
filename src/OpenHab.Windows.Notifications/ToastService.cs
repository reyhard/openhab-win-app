using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace OpenHab.Windows.Notifications;

public static class ToastService
{
    private static bool isRegistered;

    public static void EnsureRegistered()
    {
        if (isRegistered) return;

        AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
        AppNotificationManager.Default.Register();
        isRegistered = true;
    }

    public static void Show(string title, string body)
    {
        EnsureRegistered();

        var appNotification = new AppNotificationBuilder()
            .AddText(title)
            .AddText(body)
            .BuildNotification();

        AppNotificationManager.Default.Show(appNotification);
    }

    public static event EventHandler? NotificationActivated;

    private static void OnNotificationInvoked(
        AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        NotificationActivated?.Invoke(null, EventArgs.Empty);
    }
}
