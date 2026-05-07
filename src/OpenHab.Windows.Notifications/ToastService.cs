using CommunityToolkit.WinUI.Notifications;
using OpenHab.Core;
using Windows.UI.Notifications;

namespace OpenHab.Windows.Notifications;

public static class ToastService
{
    private static bool isAvailable;

    /// <summary>
    /// True when toast notifications are available.
    /// In unpackaged mode, depends on the Start menu shortcut
    /// with AppUserModelId being present (via ShortcutRegistrar).
    /// </summary>
    public static bool IsAvailable => isAvailable;

    public static void EnsureRegistered()
    {
        if (isAvailable) return;

        try
        {
            // ToastNotificationManagerCompat auto-registers on first use.
            // This call forces early registration so we can detect failures.
            var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
            _ = notifier.Setting; // validates the notifier is functional
            ToastNotificationManagerCompat.OnActivated += HandleToastActivated;
            isAvailable = true;
            DiagnosticLogger.Info("Toast notification system registered via CommunityToolkit");
        }
        catch (Exception ex)
        {
            isAvailable = false;
            DiagnosticLogger.Warn($"Toast notifications unavailable — {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static void Show(string title, string body)
    {
        if (!isAvailable) return;

        EnsureRegistered();
        if (!isAvailable) return;

        DiagnosticLogger.Info($"Showing toast: \"{title}\"");

        var builder = new ToastContentBuilder()
            .AddText(title)
            .AddText(body);

        var toast = new ToastNotification(builder.GetXml());
        ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
    }

    public static void Show(string title, string body, IReadOnlyList<NotificationActionButton>? actions)
    {
        if (!isAvailable) return;

        EnsureRegistered();
        if (!isAvailable) return;

        DiagnosticLogger.Info($"Showing toast with actions: \"{title}\" ({actions?.Count ?? 0} buttons)");

        var builder = new ToastContentBuilder()
            .AddText(title)
            .AddText(body);

        if (actions is not null)
        {
            foreach (var action in actions)
            {
                builder.AddButton(action.Title, ToastActivationType.Foreground,
                    $"{action.Type}:{action.Payload}");
            }
        }

        var toast = new ToastNotification(builder.GetXml());
        ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
    }

    public static event EventHandler<ToastNotificationActivatedEventArgsCompat>? NotificationActivated;

    private static void HandleToastActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        DiagnosticLogger.Info("User activated a toast notification");
        NotificationActivated?.Invoke(null, args);
    }
}
