using Microsoft.UI.Xaml;
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Core.Api;
using OpenHab.Core.Auth;
using OpenHab.Core.Profiles;
using OpenHab.Windows.Notifications;
using OpenHab.Windows.Tray.Tray;
using System.Threading;
using Microsoft.UI.Dispatching;
using System.Net.Http;

namespace OpenHab.Windows.Tray;

public partial class App : Application
{
    private MainWindow? window;
    private TrayIconService? trayIcon;
    private DispatcherQueue? uiDispatcherQueue;
    private HttpClient? httpClient;
    private NotificationPoller? notificationPoller;
    private int isShuttingDown;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();

        ICredentialStore credentialStore = new WindowsCredentialStore();
        var settingsController = new AppSettingsController(credentialStore);
        var renderController = new SitemapRenderController(settingsController);
        httpClient = new HttpClient();
        var runtimeController = new SitemapRuntimeController(
            settingsController,
            renderController,
            (transportKind, endpoint) =>
            {
                string? token = null;
                // NOTE: GetAwaiter().GetResult() is safe here because WindowsCredentialStore.RetrieveAsync
                // returns Task.FromResult and never suspends. If the store becomes genuinely async,
                // this must be refactored to avoid deadlocking on the UI thread.
                try { token = settingsController.GetApiTokenAsync(transportKind, CancellationToken.None).GetAwaiter().GetResult(); }
                catch { }
                return new OpenHabHttpClient(httpClient, endpoint, apiToken: token);
            });

        window = new MainWindow(settingsController, runtimeController);
        trayIcon = new TrayIconService(
            showWindow: () =>
            {
                window.Activate();
                _ = window.RefreshRuntimeAsync();
            },
            exitApplication: () =>
            {
                ShutdownTrayResources();
                Exit();
            });

        _ = InitializeAsync(settingsController);
        window.Activate();
        StartNotificationPolling(settingsController);
    }

    private static async Task InitializeAsync(AppSettingsController settingsController)
    {
        try
        {
            await settingsController.InitializeAsync();
        }
        catch
        {
            // Credential store hydration is best-effort at startup.
        }
    }

    private void StartNotificationPolling(AppSettingsController settingsController)
    {
        var settings = settingsController.Current;
        if (settings.EndpointMode == EndpointMode.LocalOnly) return;

        ToastService.EnsureRegistered();
        ToastService.NotificationActivated += (_, _) =>
        {
            _ = uiDispatcherQueue?.TryEnqueue(() => window?.Activate());
        };

        var cloudToken = GetApiTokenSync(settingsController, TransportKind.Cloud);
        notificationPoller = new NotificationPoller(
            httpClient!,
            settings.CloudEndpoint,
            apiToken: cloudToken,
            dispatcher: uiDispatcherQueue);

        notificationPoller.NotificationReceived += (_, notification) =>
        {
            var title = notification.Severity is not null
                ? $"[{notification.Severity}] openHAB"
                : "openHAB";
            var body = notification.Message.Length > 200
                ? notification.Message[..197] + "..."
                : notification.Message;
            ToastService.Show(title, body);
        };

        notificationPoller.Start();
    }

    // Sync helper — safe because the underlying store returns Task.FromResult.
    private static string? GetApiTokenSync(AppSettingsController controller, TransportKind kind)
    {
        try { return controller.GetApiTokenAsync(kind, CancellationToken.None).GetAwaiter().GetResult(); }
        catch { return null; }
    }

    private void OnProcessExit(object? sender, EventArgs args)
    {
        ShutdownTrayResources();
    }

    private void ShutdownTrayResources()
    {
        // Shared shutdown path for both tray-initiated exit and process-exit cleanup.
        if (Interlocked.Exchange(ref isShuttingDown, 1) != 0)
        {
            return;
        }

        var dispatcher = uiDispatcherQueue;
        if (dispatcher is not null && !dispatcher.HasThreadAccess)
        {
            if (dispatcher.TryEnqueue(ShutdownTrayResourcesCore))
            {
                return;
            }

            // Late process shutdown can prevent marshaled cleanup; avoid direct WinForms disposal off the UI thread.
            return;
        }

        ShutdownTrayResourcesCore();
    }

    private void ShutdownTrayResourcesCore()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        notificationPoller?.Dispose();
        notificationPoller = null;
        trayIcon?.Dispose();
        trayIcon = null;
        httpClient?.Dispose();
        httpClient = null;
    }
}
