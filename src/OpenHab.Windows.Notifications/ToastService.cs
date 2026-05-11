using CommunityToolkit.WinUI.Notifications;
using OpenHab.Core;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.ApplicationModel;
using Windows.UI.Notifications;

namespace OpenHab.Windows.Notifications;

public static class ToastService
{
    private static bool isAvailable;
    private static bool isPackaged;
    private static bool isInitialized;
    private static int _toastSequence;
    private static int _toastHeaderSequence;

    public static bool IsAvailable => isAvailable;

    private static bool IsRunningPackaged()
    {
        try
        {
            // Package.Current throws if the app is not running in a package context.
            // In unpackaged mode, this property access fails.
            _ = Package.Current;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void EnsureRegistered()
    {
        if (isInitialized) return;
        isInitialized = true;

        isPackaged = IsRunningPackaged();
        DiagnosticLogger.Info($"ToastService.EnsureRegistered — packaged={isPackaged} threadId={Environment.CurrentManagedThreadId}");

        if (isPackaged)
        {
            // In a packaged (MSIX) app, use the native ToastNotificationManager.
            // The package manifest provides identity and COM activation via
            // desktop:toastNotificationActivation extension — no compat layer needed.
            try
            {
                _ = ToastNotificationManager.CreateToastNotifier();
                isAvailable = true;
                DiagnosticLogger.Info("Toast notification system registered via native API (packaged)");
                return;
            }
            catch (Exception ex)
            {
                isAvailable = false;
                DiagnosticLogger.Error(
                    $"ToastService.EnsureRegistered (packaged) FAILED — {ex.GetType().FullName}: {ex.Message}", ex);
                return;
            }
        }

        // Unpackaged: use CommunityToolkit compat layer
        try
        {
            var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
            _ = notifier.Setting;
            ToastNotificationManagerCompat.OnActivated += HandleToastActivated;
            isAvailable = true;
            DiagnosticLogger.Info("Toast notification system registered via CommunityToolkit (unpackaged)");
        }
        catch (Exception ex)
        {
            isAvailable = false;
            DiagnosticLogger.Error(
                $"ToastService.EnsureRegistered (unpackaged) FAILED — {ex.GetType().FullName}: {ex.Message}", ex);
        }
    }

    public static void Show(string title, string body)
    {
        if (!isAvailable || !isInitialized) return;
        ShowInternal(title, body, null, important: false, header: null, tag: null, appLogoOverrideUri: null);
    }

    public static void Show(string title, string body, IReadOnlyList<NotificationActionButton>? actions)
    {
        if (!isAvailable || !isInitialized) return;
        ShowInternal(title, body, actions, important: false, header: null, tag: null, appLogoOverrideUri: null);
    }

    public static void Show(
        string title,
        string body,
        IReadOnlyList<NotificationActionButton>? actions,
        bool important,
        string? header,
        string? tag,
        Uri? appLogoOverrideUri)
    {
        if (!isAvailable || !isInitialized) return;
        ShowInternal(title, body, actions, important, header, tag, appLogoOverrideUri);
    }

    private static void ShowInternal(
        string title,
        string body,
        IReadOnlyList<NotificationActionButton>? actions,
        bool important,
        string? header,
        string? tag,
        Uri? appLogoOverrideUri)
    {
        var seq = Interlocked.Increment(ref _toastSequence);
        var actionCount = actions?.Count ?? 0;
        DiagnosticLogger.Info(
            $"Toast.Show#{seq} begin title=\"{title}\" actions={actionCount} " +
            $"packaged={isPackaged} important={important} tag={tag ?? "<none>"} threadId={Environment.CurrentManagedThreadId}");

        try
        {
            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(body);

            if (!string.IsNullOrWhiteSpace(header))
            {
                var headerId = Interlocked.Increment(ref _toastHeaderSequence).ToString();
                builder.AddHeader(headerId, header.Trim(), "openhab:open");
            }

            if (appLogoOverrideUri is not null)
            {
                builder.AddAppLogoOverride(appLogoOverrideUri);
            }

            if (actions is not null)
            {
                foreach (var action in actions)
                {
                    builder.AddButton(action.Title, ToastActivationType.Foreground,
                        $"{action.Type}:{action.Payload}");
                }
            }

            var toast = new ToastNotification(builder.GetXml());
            if (!string.IsNullOrWhiteSpace(tag))
            {
                toast.Tag = tag.Trim();
            }

            if (important)
            {
                toast.Priority = ToastNotificationPriority.High;
            }

            if (isPackaged)
            {
                ToastNotificationManager.CreateToastNotifier().Show(toast);
            }
            else
            {
                ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
            }

            DiagnosticLogger.Info($"Toast.Show#{seq} done");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Error(
                $"Toast.Show#{seq} FAILED — {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}",
                ex);
            // Mark as unavailable on persistent failures so we don't keep crashing
            if (ex is InvalidOperationException || ex is COMException)
            {
                isAvailable = false;
                DiagnosticLogger.Warn("ToastService disabled due to persistent failure");
            }
        }
    }

    public static event EventHandler<ToastNotificationActivatedEventArgsCompat>? NotificationActivated;

    /// <summary>
    /// Raised when a toast notification is activated in packaged (MSIX) mode.
    /// The string argument is the activation payload from the COM server.
    /// </summary>
    public static event Action<string>? PackagedActivated;

    /// <summary>
    /// Called by the COM notification activator (registered via Package.appxmanifest)
    /// when the user interacts with a toast notification in packaged mode.
    /// </summary>
    public static void HandlePackagedActivation(string arguments)
    {
        DiagnosticLogger.Info(
            $"Packaged toast activated — arguments=\"{arguments}\" threadId={Environment.CurrentManagedThreadId}");
        PackagedActivated?.Invoke(arguments);
    }

    /// <summary>
    /// Called by CommunityToolkit for unpackaged toast activation.
    /// </summary>
    private static void HandleToastActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        DiagnosticLogger.Info(
            $"Toast activated (unpackaged) — threadId={Environment.CurrentManagedThreadId}");
        NotificationActivated?.Invoke(null, args);
    }
}
