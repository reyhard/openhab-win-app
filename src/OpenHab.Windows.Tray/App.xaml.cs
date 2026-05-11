using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
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
using OpenHab.App.DeviceInfo;
using OpenHab.Windows.Notifications;
using OpenHab.Windows.Tray.Tray;
using OpenHab.Windows.Tray.Startup;
using OpenHab.Windows.Tray.DeviceInfo;
using Microsoft.UI.Dispatching;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Windows.Networking.Connectivity;

namespace OpenHab.Windows.Tray;

public partial class App : Application
{
    private static Mutex? instanceMutex;

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
    private DeviceInfoSyncService? deviceInfoSyncService;
    private WindowsSessionInfoReader? windowsSessionInfoReader;
    private bool deviceInfoEventsRegistered;
    private readonly SemaphoreSlim shellApplySemaphore = new(1, 1);
    private int isShuttingDown;

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        instanceMutex = new Mutex(initiallyOwned: true, name: "OpenHab.Windows.Tray.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            instanceMutex.Dispose();
            instanceMutex = null;
            Environment.Exit(0);
            return;
        }

        RegisterCrashHandlers();

        DiagnosticLogger.Info($"openHAB Windows App v{typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown"} starting");

        SetCurrentProcessExplicitAppUserModelID("OpenHab.OpenHabWinApp");
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();

        var activatedEventArgs = AppInstance.GetCurrent().GetActivatedEventArgs();

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
            sitemapEventStreamClient: CreateEventStreamClient(settingsController, httpClient));
        windowsSessionInfoReader = new WindowsSessionInfoReader();
        var deviceStateSource = new WindowsDeviceStateSnapshotSource(
            runtimeController,
            new WindowsBatteryInfoReader(),
            new WindowsNetworkInfoReader(),
            new WindowsFocusInfoReader(),
            windowsSessionInfoReader);
        deviceInfoSyncService = new DeviceInfoSyncService(
            getSettings: () => this.settingsController?.Current.DeviceInfoSync ?? DeviceInfoSyncSettings.Default,
            getClient: CreateDeviceInfoSyncClient,
            snapshotSource: deviceStateSource);

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
            notificationStore,
            requestOpenMainWindow: () =>
            {
                shellController.HandleOpenMainWindow();
                _ = ApplyShellStateAsync();
            },
            requestOpenNotifications: () =>
            {
                mainWindow?.ShowNotificationsTab();
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
            // If the window is already hidden (by exit animation), just cancel
            if (!flyoutWindow.AppWindow.IsVisible)
            {
                args.Cancel = true;
                return;
            }
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

        _ = CompleteStartupAsync(settingsController, activatedEventArgs);
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
                    var tag = ResolveNotificationTag(n);
                    var body = BuildNotificationBody(n);
                    notificationStore?.AddOrUpdate(
                        n.Id, body, n.Created, n.Title, n.Icon, tag,
                        n.ReferenceId, n.OnClickAction, n.MediaAttachmentUrl,
                        n.ActionButton1, n.ActionButton2, n.ActionButton3);
                });

            DiagnosticLogger.Info($"Notification poller created — polling {settings.CloudEndpoint.Host} every 60s");

            notificationPoller.NotificationReceived += (_, notification) =>
            {
                DiagnosticLogger.Info($"Notification received — severity: {notification.Severity ?? "none"}");
                var title = BuildNotificationHeader(notification);
                var body = BuildNotificationBody(notification);

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

    private IOpenHabClient? CreateDeviceInfoSyncClient()
    {
        var settings = settingsController?.Current;
        var sharedClient = httpClient;
        if (settings is null || sharedClient is null)
        {
            return null;
        }

        var preferredTransport = runtimeController?.Current.ActiveTransport ??
            (settings.EndpointMode == EndpointMode.CloudOnly ? TransportKind.Cloud : TransportKind.Local);
        var endpoint = preferredTransport == TransportKind.Cloud
            ? settings.CloudEndpoint
            : settings.LocalEndpoint;
        var auth = ResolveRuntimeAuthSync(settingsController!, preferredTransport);

        return new OpenHabHttpClient(
            sharedClient,
            endpoint,
            apiToken: auth.ApiToken,
            basicUserName: auth.BasicUserName,
            basicPassword: auth.BasicPassword);
    }

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
                    if (flyout.AppWindow.IsVisible)
                    {
                        await flyout.AnimateFlyoutExitAndHideAsync();
                    }
                    main.CenterOnCurrentScreen();
                    main.Activate();
                    break;
                case TrayShellSurface.Flyout:
                    main.AppWindow.Hide();
                    TrayFlyoutPositioner.PlaceNearTrayArea(
                        flyout,
                        settingsController?.Current.FlyoutWidth ?? AppSettings.Default.FlyoutWidth);
                    flyout.PrepareForShowAnimation();
                    FlyoutWindow.GrantForegroundPermission(
                        WinRT.Interop.WindowNative.GetWindowHandle(flyout));
                    flyout.Activate();
                    flyout.StartEntranceAnimationIfPending();
                    break;
                default:
                    main.AppWindow.Hide();
                    if (flyout.AppWindow.IsVisible)
                    {
                        await flyout.AnimateFlyoutExitAndHideAsync();
                    }
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

    private async Task CompleteStartupAsync(AppSettingsController settingsController, AppActivationArguments? activatedEventArgs)
    {
        await InitializeAsync(settingsController);

        RegisterDeviceInfoSyncEvents();
        deviceInfoSyncService?.Start();
        _ = TriggerDeviceInfoSyncAsync();

        await ApplyShellStateAsync();

        // Sync autostart state with saved preference.
        _ = StartupManager.SetEnabledAsync(settingsController.Current.LaunchAtStartup);

        try
        {
            var sitemaps = await runtimeController!.LoadSitemapListAsync();
            _ = uiDispatcherQueue?.TryEnqueue(() =>
            {
                flyoutWindow?.PopulateSitemaps(sitemaps);
                mainWindow?.PopulateSitemaps(sitemaps);
            });

            var settings = settingsController.Current;

            // Preserve user's previously selected sitemap if it still exists.
            if (!string.IsNullOrWhiteSpace(settings.SitemapName) &&
                sitemaps.Any(s => string.Equals(s.Name, settings.SitemapName, StringComparison.OrdinalIgnoreCase)))
            {
                _ = uiDispatcherQueue?.TryEnqueue(() =>
                {
                    flyoutWindow?.LoadRuntimeAsync();
                    mainWindow?.LoadRuntimeAsync();
                });
            }
            else if (sitemaps.Count == 0)
            {
                runtimeController!.SetBannerStatus("No sitemaps were detected");
                settingsController.SetSitemapName(string.Empty);
            }
            else if (sitemaps.Count == 1)
            {
                settingsController.SetSitemapName(sitemaps[0].Name);
                _ = uiDispatcherQueue?.TryEnqueue(() =>
                {
                    flyoutWindow?.LoadRuntimeAsync();
                    mainWindow?.LoadRuntimeAsync();
                });
            }
            else // Multiple sitemaps
            {
                var defaultSitemap = sitemaps.FirstOrDefault(
                    s => string.Equals(s.Name, "default", StringComparison.OrdinalIgnoreCase));
                if (defaultSitemap is not null)
                {
                    settingsController.SetSitemapName(defaultSitemap.Name);
                    _ = uiDispatcherQueue?.TryEnqueue(() =>
                    {
                        flyoutWindow?.LoadRuntimeAsync();
                        mainWindow?.LoadRuntimeAsync();
                    });
                }
                else
                {
                    runtimeController!.SetBannerStatus("Multiple sitemaps detected — choose one");
                    settingsController.SetSitemapName(string.Empty);
                }
            }
        }
        catch
        {
            // Best-effort; dropdown will be empty if server unreachable.
        }

        DiagnosticLogger.Info("Completing startup — starting notification polling");
        StartNotificationPolling(settingsController);
        HandleStartupActivation(activatedEventArgs);
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
        UnregisterDeviceInfoSyncEvents();
        deviceInfoSyncService?.Dispose();
        deviceInfoSyncService = null;
        windowsSessionInfoReader = null;
        DiagnosticLogger.Info("Shutting down notification poller");
        notificationPoller?.Dispose();
        notificationPoller = null;
        trayIcon?.Dispose();
        trayIcon = null;
        httpClient?.Dispose();
        httpClient = null;
    }

    private void RegisterDeviceInfoSyncEvents()
    {
        if (deviceInfoEventsRegistered)
        {
            return;
        }
        var sessionSwitchRegistered = false;
        var powerModeRegistered = false;
        try
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
            sessionSwitchRegistered = true;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            powerModeRegistered = true;
            NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
            deviceInfoEventsRegistered = true;
        }
        catch (Exception ex)
        {
            if (powerModeRegistered)
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            }

            if (sessionSwitchRegistered)
            {
                SystemEvents.SessionSwitch -= OnSessionSwitch;
            }

            deviceInfoEventsRegistered = false;
            DiagnosticLogger.Warn($"Device Info Sync event registration failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void UnregisterDeviceInfoSyncEvents()
    {
        if (!deviceInfoEventsRegistered)
        {
            return;
        }

        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
        deviceInfoEventsRegistered = false;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        try
        {
            var sessionReader = windowsSessionInfoReader;
            if (sessionReader is not null)
            {
                if (e.Reason == SessionSwitchReason.SessionLock)
                {
                    sessionReader.MarkLocked();
                }
                else if (e.Reason == SessionSwitchReason.SessionUnlock)
                {
                    sessionReader.MarkActive();
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Device Info Sync session event update failed: {ex.GetType().Name}: {ex.Message}");
        }

        _ = TriggerDeviceInfoSyncAsync();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        try
        {
            var sessionReader = windowsSessionInfoReader;
            if (sessionReader is not null)
            {
                if (e.Mode == PowerModes.Suspend)
                {
                    sessionReader.MarkSleep();
                }
                else if (e.Mode == PowerModes.Resume)
                {
                    sessionReader.MarkResume();
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Device Info Sync power event update failed: {ex.GetType().Name}: {ex.Message}");
        }

        _ = TriggerDeviceInfoSyncAsync();
    }

    private void OnNetworkStatusChanged(object sender)
    {
        _ = TriggerDeviceInfoSyncAsync();
    }

    private async Task TriggerDeviceInfoSyncAsync()
    {
        try
        {
            var service = deviceInfoSyncService;
            if (service is null)
            {
                return;
            }

            await service.TriggerSyncAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Device Info Sync trigger failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TryAddButton(string? rawButton, List<NotificationActionButton> buttons)
    {
        var parsed = NotificationActionParser.TryParseButton(rawButton);
        if (parsed is not null)
            buttons.Add(parsed);
    }

    private static string BuildNotificationHeader(CloudNotification notification)
    {
        if (!string.IsNullOrWhiteSpace(notification.Title))
        {
            return notification.Title;
        }

        var tag = ResolveNotificationTag(notification);
        return !string.IsNullOrWhiteSpace(tag)
            ? $"[{tag}] openHAB"
            : "openHAB";
    }

    private static string BuildNotificationBody(CloudNotification notification)
    {
        var body = notification.Message ?? string.Empty;
        var tag = ResolveNotificationTag(notification);
        if (!string.IsNullOrWhiteSpace(tag))
        {
            body = StripLeadingBracketToken(body, tag);
        }

        body = body.Trim();
        if (body.Length > 200)
        {
            return body[..197] + "...";
        }

        return body;
    }

    private static string StripLeadingBracketToken(string text, string token)
    {
        var escapedToken = Regex.Escape(token.Trim());
        return Regex.Replace(
            text,
            $"^\\s*\\[{escapedToken}\\]\\s*",
            string.Empty,
            RegexOptions.IgnoreCase);
    }

    // openHAB Cloud docs: "tag" supersedes/semsame as legacy "severity".
    // Keep it in-memory (store severity slot) for future important-notification policy.
    private static string? ResolveNotificationTag(CloudNotification notification)
    {
        return string.IsNullOrWhiteSpace(notification.Tag)
            ? notification.Severity
            : notification.Tag;
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

    private void HandleStartupActivation(AppActivationArguments? activatedEventArgs)
    {
        if (activatedEventArgs?.Kind != ExtendedActivationKind.ToastNotification)
        {
            return;
        }

        _ = uiDispatcherQueue?.TryEnqueue(() =>
        {
            if (shellController is null)
            {
                return;
            }

            DiagnosticLogger.Info("Processing startup toast activation");
            shellController.HandleNotificationActivated();
            _ = ApplyShellStateAsync();
        });
    }

    // ── Crash diagnostics ──────────────────────────────────────────

    private static readonly string CrashLogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenHab.WinApp");

    private static void RegisterCrashHandlers()
    {
        Directory.CreateDirectory(CrashLogDir);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var crashPath = Path.Combine(CrashLogDir, "crash.log");
            try
            {
                var text = args.ExceptionObject?.ToString() ?? "Unknown unhandled exception (null ExceptionObject)";
                var entry = $"[{DateTime.UtcNow:O}] FATAL — AppDomain.UnhandledException{Environment.NewLine}{text}{Environment.NewLine}---{Environment.NewLine}";
                File.AppendAllText(crashPath, entry);
            }
            catch
            {
                // Best-effort; we're crashing anyway.
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            // Observe the exception so it doesn't crash the finalizer thread.
            args.SetObserved();
            var crashPath = Path.Combine(CrashLogDir, "task-crash.log");
            try
            {
                var text = args.Exception?.ToString() ?? "Unknown unobserved task exception";
                var entry = $"[{DateTime.UtcNow:O}] UnobservedTaskException{Environment.NewLine}{text}{Environment.NewLine}---{Environment.NewLine}";
                File.AppendAllText(crashPath, entry);
            }
            catch
            {
                // Best-effort.
            }
        };
    }
}
