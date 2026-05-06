using Microsoft.UI.Xaml;
using OpenHab.App.Tray;
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Core.Api;
using OpenHab.Core.Auth;
using OpenHab.Core.Profiles;
using OpenHab.Windows.Notifications;
using OpenHab.Windows.Tray.Tray;
using System.Linq;
using System.Threading;
using Microsoft.UI.Dispatching;
using System.Net.Http;
using System.Threading.Tasks;

namespace OpenHab.Windows.Tray;

public partial class App : Application
{
    private MainWindow? mainWindow;
    private FlyoutWindow? flyoutWindow;
    private TrayIconService? trayIcon;
    private TrayShellController? shellController;
    private DispatcherQueue? uiDispatcherQueue;
    private HttpClient? httpClient;
    private NotificationPoller? notificationPoller;
    private SitemapRuntimeController? runtimeController;
    private readonly SemaphoreSlim shellApplySemaphore = new(1, 1);
    private int isShuttingDown;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();

        ICredentialStore? credentialStore;
        try
        {
            credentialStore = new WindowsCredentialStore();
        }
        catch
        {
            // PasswordVault may not be available in all Windows configurations.
            // Token methods will throw InvalidOperationException when no store is configured.
            credentialStore = null;
        }

        var settingsController = new AppSettingsController(credentialStore);
        var renderController = new SitemapRenderController(settingsController);
        httpClient = new HttpClient();
        runtimeController = new SitemapRuntimeController(
            settingsController,
            renderController,
            (transportKind, endpoint) =>
            {
                var auth = ResolveRuntimeAuthSync(settingsController, transportKind);
                return new OpenHabHttpClient(
                    httpClient,
                    endpoint,
                    apiToken: auth.ApiToken,
                    basicUserName: auth.BasicUserName,
                    basicPassword: auth.BasicPassword);
            });

        shellController = new TrayShellController();
        shellController.HandleLaunch();

        mainWindow = new MainWindow(
            settingsController,
            runtimeController,
            requestHideToTray: () =>
            {
                shellController.HandleWindowCloseRequested(TrayShellSurface.MainWindow);
                _ = ApplyShellStateAsync();
            });
        flyoutWindow = new FlyoutWindow(
            settingsController,
            runtimeController,
            requestOpenMainWindow: () =>
            {
                shellController.HandleOpenMainWindow();
                _ = ApplyShellStateAsync();
            });

        flyoutWindow.AppWindow.Closing += (sender, args) =>
        {
            args.Cancel = true;
            shellController.HandleWindowCloseRequested(TrayShellSurface.Flyout);
            _ = ApplyShellStateAsync();
        };

        trayIcon = new TrayIconService(
            toggleFlyout: () =>
            {
                shellController.HandlePrimaryTrayClick();
                _ = ApplyShellStateAsync();
            },
            openMainWindow: () =>
            {
                shellController.HandleOpenMainWindow();
                _ = ApplyShellStateAsync();
            },
            exitApplication: () =>
            {
                shellController.HandleExitRequested();
                _ = ApplyShellStateAsync();
            });

        _ = CompleteStartupAsync(settingsController);
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
        try
        {
            var settings = settingsController.Current;
            if (settings.EndpointMode == EndpointMode.LocalOnly) return;

            ToastService.EnsureRegistered();
            ToastService.NotificationActivated += (_, _) =>
            {
                _ = uiDispatcherQueue?.TryEnqueue(() =>
                {
                    if (shellController is null)
                    {
                        return;
                    }

                    shellController.HandleNotificationActivated();
                    _ = ApplyShellStateAsync();
                });
            };

            var cloudCredentials = GetCloudCredentialsSync(settingsController);
            notificationPoller = new NotificationPoller(
                httpClient!,
                settings.CloudEndpoint,
                basicUserName: cloudCredentials?.UserName,
                basicPassword: cloudCredentials?.Password,
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
        catch
        {
            // Notification infrastructure may not be available in all configurations
            // (e.g., unpackaged apps without shortcut identity, or restricted user accounts).
        }
    }

    // Sync helper — safe because the underlying store returns Task.FromResult.
    private static string? GetApiTokenSync(AppSettingsController controller, TransportKind kind)
    {
        try { return controller.GetApiTokenAsync(kind, CancellationToken.None).GetAwaiter().GetResult(); }
        catch { return null; }
    }

    private static CloudCredentials? GetCloudCredentialsSync(AppSettingsController controller)
    {
        try { return controller.GetCloudCredentialsAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch { return null; }
    }

    private static RuntimeAuth ResolveRuntimeAuthSync(AppSettingsController controller, TransportKind kind)
    {
        if (kind == TransportKind.Local)
        {
            return new RuntimeAuth(GetApiTokenSync(controller, TransportKind.Local), null, null);
        }

        var cloudCredentials = GetCloudCredentialsSync(controller);
        return new RuntimeAuth(null, cloudCredentials?.UserName, cloudCredentials?.Password);
    }

    private readonly record struct RuntimeAuth(string? ApiToken, string? BasicUserName, string? BasicPassword);

    private void OnProcessExit(object? sender, EventArgs args)
    {
        ShutdownTrayResources();
    }

    private async Task ApplyShellStateAsync()
    {
        await shellApplySemaphore.WaitAsync();
        try
        {
            if (shellController is null)
            {
                return;
            }

            var state = shellController.Current;

            if (state.ShouldExitProcess)
            {
                ShutdownTrayResources();
                Exit();
                return;
            }

            var main = mainWindow;
            var flyout = flyoutWindow;

            if (main is null || flyout is null)
            {
                return;
            }

            switch (state.VisibleSurface)
            {
                case TrayShellSurface.MainWindow:
                    flyout.AppWindow.Hide();
                    main.Activate();
                    break;
                case TrayShellSurface.Flyout:
                    main.AppWindow.Hide();
                    TrayFlyoutPositioner.PlaceNearTrayArea(flyout);
                    flyout.Activate();
                    break;
                default:
                    main.AppWindow.Hide();
                    flyout.AppWindow.Hide();
                    break;
            }

            if (state.PendingRefresh)
            {
                if (state.VisibleSurface == TrayShellSurface.MainWindow)
                {
                    await main.RefreshRuntimeAsync();
                }
                else if (state.VisibleSurface == TrayShellSurface.Flyout)
                {
                    await flyout.RefreshRuntimeAsync();
                }

                shellController.HandleRefreshCompleted();
            }
        }
        finally
        {
            shellApplySemaphore.Release();
        }
    }

    private async Task CompleteStartupAsync(AppSettingsController settingsController)
    {
        await InitializeAsync(settingsController);
        await ApplyShellStateAsync();

        try
        {
            var sitemaps = await runtimeController!.LoadSitemapListAsync();
            _ = uiDispatcherQueue?.TryEnqueue(() =>
            {
                flyoutWindow?.PopulateSitemaps(sitemaps);
                mainWindow?.PopulateSitemaps(sitemaps);
            });

            // Auto-select first available sitemap if current doesn't exist
            var settings = settingsController.Current;
            if (sitemaps.Count > 0 && !sitemaps.Any(s => string.Equals(s.Name, settings.SitemapName, StringComparison.OrdinalIgnoreCase)))
            {
                settingsController.SetSitemapName(sitemaps[0].Name);
            }
        }
        catch
        {
            // Best-effort; dropdown will be empty if server unreachable.
        }

        StartNotificationPolling(settingsController);
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
