using Microsoft.UI.Xaml;
using OpenHab.App.Tray;
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.App.Notifications;
using OpenHab.App.Sitemaps;
using OpenHab.Core.Api;
using OpenHab.Core.Auth;
using OpenHab.Core.Profiles;
using OpenHab.Core;
using OpenHab.Core.Events;
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
    private AppSettingsController? settingsController;
    private DispatcherQueue? uiDispatcherQueue;
    private HttpClient? httpClient;
    private NotificationStore? notificationStore;
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

        settingsController = new AppSettingsController(credentialStore);
        notificationStore = new NotificationStore();
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
            },
            eventStreamClient: CreateEventStreamClient(settingsController, httpClient),
            sitemapEventStreamClient: CreateEventStreamClient(settingsController, httpClient));

        shellController = new TrayShellController();
        shellController.HandleLaunch();

        mainWindow = new MainWindow(
            settingsController,
            runtimeController,
            notificationStore,
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
            },
            requestHideFlyout: () =>
            {
                shellController.HandleWindowCloseRequested(TrayShellSurface.Flyout);
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
            DiagnosticLogger.Info("Starting notification polling");
            var settings = settingsController.Current;
            if (settings.EndpointMode == EndpointMode.LocalOnly)
            {
                DiagnosticLogger.Info("Notification polling skipped — EndpointMode is LocalOnly");
                return;
            }

            try
            {
                ShortcutRegistrar.EnsureRegistered(Environment.ProcessPath!);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Error("Shortcut registration failed", ex);
                // Continue — ToastService might still work if shortcut was created by a previous run.
            }

            ToastService.EnsureRegistered();
            if (!ToastService.IsAvailable)
            {
                DiagnosticLogger.Warn("Toast notifications unavailable — notifications will be logged to diagnostics file only");
            }
            ToastService.NotificationActivated += (_, args) =>
            {
                if (!string.IsNullOrEmpty(args.Argument))
                {
                    _ = HandleNotificationActionAsync(args.Argument);
                    return;
                }
                // Simple tap (no action buttons) — open main window
                _ = uiDispatcherQueue?.TryEnqueue(() =>
                {
                    if (shellController is null) return;
                    shellController.HandleNotificationActivated();
                    _ = ApplyShellStateAsync();
                });
            };

            var cloudCredentials = GetCloudCredentialsSync(settingsController);
            DiagnosticLogger.Info($"Cloud credentials resolved: {(cloudCredentials is not null ? "yes" : "no")}");
            notificationPoller = new NotificationPoller(
                httpClient!,
                settings.CloudEndpoint,
                basicUserName: cloudCredentials?.UserName,
                basicPassword: cloudCredentials?.Password,
                dispatcher: uiDispatcherQueue,
                preSeenIds: notificationStore?.GetSeenUndismissedIds(),
                isDismissedFunc: id => notificationStore?.IsDismissed(id) ?? false,
                onNewNotification: n =>
                {
                    notificationStore?.AddOrUpdate(
                        n.Id, n.Message, n.Created, n.Title, n.Icon, n.Severity,
                        n.ReferenceId, n.OnClickAction, n.MediaAttachmentUrl,
                        n.ActionButton1, n.ActionButton2, n.ActionButton3);
                });

            DiagnosticLogger.Info($"Notification poller created — polling {settings.CloudEndpoint.Host} every 60s");

            notificationPoller.NotificationReceived += (_, notification) =>
            {
                DiagnosticLogger.Info($"Notification received — severity: {notification.Severity ?? "none"}");
                var title = notification.Title ?? (notification.Severity is not null
                    ? $"[{notification.Severity}] openHAB"
                    : "openHAB");
                var body = notification.Message.Length > 200
                    ? notification.Message[..197] + "..."
                    : notification.Message;

                // Parse action buttons from the notification data
                var actionButtons = new List<NotificationActionButton>();
                TryAddButton(notification.ActionButton1, actionButtons);
                TryAddButton(notification.ActionButton2, actionButtons);
                TryAddButton(notification.ActionButton3, actionButtons);

                ToastService.Show(title, body, actionButtons.Count > 0 ? actionButtons : null);
            };

            notificationPoller.Start();
        }
        catch (Exception ex)
        {
            // Notification infrastructure may not be available in all configurations
            // (e.g., unpackaged apps without shortcut identity, or restricted user accounts).
            DiagnosticLogger.Error("Failed to start notification polling", ex);
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

    private static OpenHabEventStreamClient CreateEventStreamClient(AppSettingsController settingsController, HttpClient httpClient)
    {
        var settings = settingsController.Current;
        var preferredTransport = settings.EndpointMode == EndpointMode.CloudOnly
            ? TransportKind.Cloud
            : TransportKind.Local;
        var auth = ResolveRuntimeAuthSync(settingsController, preferredTransport);
        return new OpenHabEventStreamClient(
            httpClient,
            apiToken: auth.ApiToken,
            basicUserName: auth.BasicUserName,
            basicPassword: auth.BasicPassword);
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
                    TrayFlyoutPositioner.PlaceNearTrayArea(
                        flyout,
                        settingsController?.Current.FlyoutWidth ?? AppSettings.Default.FlyoutWidth);
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

        DiagnosticLogger.Info("Completing startup — starting notification polling");
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

            // Late process shutdown can prevent marshaled cleanup; force UI-thread disposal.
            return;
        }

        ShutdownTrayResourcesCore();
    }

    private void ShutdownTrayResourcesCore()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        DiagnosticLogger.Info("Shutting down notification poller");
        notificationPoller?.Dispose();
        notificationPoller = null;
        trayIcon?.Dispose();
        trayIcon = null;
        httpClient?.Dispose();
        httpClient = null;
    }

    private static void TryAddButton(string? rawButton, List<NotificationActionButton> buttons)
    {
        var parsed = NotificationActionParser.TryParseButton(rawButton);
        if (parsed is not null)
            buttons.Add(parsed);
    }

    private async Task HandleNotificationActionAsync(string actionArg)
    {
        var action = NotificationActionParser.TryParse(actionArg);
        if (action is null)
        {
            DiagnosticLogger.Warn($"Unparseable toast action: '{actionArg}'");
            return;
        }

        DiagnosticLogger.Info($"Handling toast action: Type={action.Type}, Payload={action.Payload}");

        switch (action.Type)
        {
            case "command":
                await ExecuteCommandActionAsync(action.Payload);
                break;
            case "ui":
                OpenUiAction(action.Payload);
                break;
            case "http":
            case "https":
                OpenUrlAction(action.Payload);
                break;
            case "rule":
                DiagnosticLogger.Warn($"Rule action not implemented from client: {action.Payload}");
                break;
            case "app":
                DiagnosticLogger.Warn($"App launch action not implemented from client: {action.Payload}");
                break;
            default:
                DiagnosticLogger.Warn($"Unknown action type: {action.Type}");
                break;
        }
    }

    private async Task ExecuteCommandActionAsync(string payload)
    {
        // payload format: "ItemName:CommandValue"
        var colonIndex = payload.IndexOf(':');
        if (colonIndex < 0)
        {
            DiagnosticLogger.Warn($"Invalid command action payload: '{payload}'");
            return;
        }

        var itemName = payload[..colonIndex];
        var commandValue = payload[(colonIndex + 1)..];

        try
        {
            var settings = settingsController?.Current;
            if (settings is null) return;

            // Use runtime HTTP client to send command
            using var client = new HttpClient();
            var endpoint = settings.EndpointMode == OpenHab.Core.Profiles.EndpointMode.CloudOnly
                ? settings.CloudEndpoint
                : settings.LocalEndpoint;

            var commandUri = new Uri(endpoint, $"rest/items/{Uri.EscapeDataString(itemName)}");
            var content = new System.Net.Http.StringContent(commandValue,
                System.Text.Encoding.UTF8, "text/plain");

            // Try with API token auth first
            try
            {
                var token = GetApiTokenSync(settingsController!,
                    settings.EndpointMode == OpenHab.Core.Profiles.EndpointMode.CloudOnly
                        ? OpenHab.Core.Profiles.TransportKind.Cloud
                        : OpenHab.Core.Profiles.TransportKind.Local);
                if (token is not null)
                {
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }
            }
            catch { /* best-effort auth */ }

            var response = await client.PostAsync(commandUri, content);
            DiagnosticLogger.Info($"Command result: {(int)response.StatusCode} for {itemName}={commandValue}");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Error($"Command action failed: {itemName}={commandValue}", ex);
        }
    }

    private void OpenUiAction(string payload)
    {
        // Try to open as URL first (sitemap paths are full URLs or paths)
        var settings = settingsController?.Current;
        if (settings is null) return;

        try
        {
            var baseUri = settings.EndpointMode == OpenHab.Core.Profiles.EndpointMode.CloudOnly
                ? settings.CloudEndpoint
                : settings.LocalEndpoint;

            var url = payload.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                      payload.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? payload
                : new Uri(baseUri, payload.TrimStart('/')).ToString();

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Error($"Failed to open UI action: {payload}", ex);
        }
    }

    private static void OpenUrlAction(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Error($"Failed to open URL: {url}", ex);
        }
    }
}
