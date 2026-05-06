using System.Runtime.InteropServices;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using OpenHab.Core;

namespace OpenHab.Windows.Notifications;

public static class ToastService
{
    private static bool isRegistered;
    private static bool isAvailable;

    /// <summary>
    /// True after a successful <c>AppNotificationManager.Register</c> call.
    /// When false, <see cref="Show"/> is a no-op (toasts are unavailable
    /// on this system — e.g. unpackaged app without notification COM registration).
    /// </summary>
    public static bool IsAvailable => isAvailable;

    public static void EnsureRegistered()
    {
        if (isRegistered || !isAvailable) return;

        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            isRegistered = true;
            isAvailable = true;
            DiagnosticLogger.Info("Toast notification system registered");
        }
        catch (COMException ex)
        {
            isAvailable = false;
            DiagnosticLogger.Warn($"Toast notifications unavailable — {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static void Show(string title, string body)
    {
        if (!isAvailable) return;

        EnsureRegistered();
        if (!isRegistered) return;

        DiagnosticLogger.Info($"Showing toast: \"{title}\"");

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
        DiagnosticLogger.Info("User activated a toast notification");
        NotificationActivated?.Invoke(null, EventArgs.Empty);
    }
}
