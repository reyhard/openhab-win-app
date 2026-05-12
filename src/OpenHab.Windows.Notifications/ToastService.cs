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

    public static bool IsAvailable => isAvailable;

    private static bool IsRunningPackaged()
    {
        try
        {
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
        Show(new ToastNotificationRequest(title, body));
    }

    public static void Show(string title, string body, IReadOnlyList<NotificationActionButton>? actions)
    {
        if (!isAvailable || !isInitialized) return;
        Show(new ToastNotificationRequest(title, body, actions));
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
        Show(new ToastNotificationRequest(
            title,
            body,
            actions,
            LaunchAction: null,
            Important: important,
            Header: header,
            Tag: tag,
            ReferenceId: null,
            AppLogoOverrideUri: appLogoOverrideUri,
            HeroImageUri: null));
    }

    public static void Show(ToastNotificationRequest request)
    {
        if (!isAvailable || !isInitialized) return;
        ArgumentNullException.ThrowIfNull(request);

        var seq = Interlocked.Increment(ref _toastSequence);
        var actionCount = request.Actions?.Count ?? 0;
        DiagnosticLogger.Info(
            $"Toast.Show#{seq} begin title=\"{request.Title}\" actions={actionCount} " +
            $"packaged={isPackaged} important={request.Important} tag={request.Tag ?? "<none>"} threadId={Environment.CurrentManagedThreadId}");

        try
        {
            var xml = ToastNotificationXmlBuilder.Build(request);
            var toast = new ToastNotification(xml);
            var tagGroup = ToastNotificationXmlBuilder.BuildTagAndGroup(request);

            if (!string.IsNullOrWhiteSpace(tagGroup.Tag))
            {
                toast.Tag = tagGroup.Tag;
            }

            if (!string.IsNullOrWhiteSpace(tagGroup.Group))
            {
                toast.Group = tagGroup.Group;
            }

            if (request.Important)
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
            if (ex is InvalidOperationException || ex is COMException)
            {
                isAvailable = false;
                DiagnosticLogger.Warn("ToastService disabled due to persistent failure");
            }
        }
    }

    public static event EventHandler<ToastNotificationActivatedEventArgsCompat>? NotificationActivated;

    public static event Action<string>? PackagedActivated;

    public static void HandlePackagedActivation(string arguments)
    {
        DiagnosticLogger.Info(
            $"Packaged toast activated — arguments=\"{arguments}\" threadId={Environment.CurrentManagedThreadId}");
        PackagedActivated?.Invoke(arguments);
    }

    private static void HandleToastActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        DiagnosticLogger.Info(
            $"Toast activated (unpackaged) — threadId={Environment.CurrentManagedThreadId}");
        NotificationActivated?.Invoke(null, args);
    }
}
