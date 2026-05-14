using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using OpenHab.App.Shortcuts;
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
using OpenHab.Windows.Tray.Rendering;
using OpenHab.Windows.Tray.Tray;
using OpenHab.Windows.Tray.Startup;
using OpenHab.Windows.Tray.DeviceInfo;
using OpenHab.Windows.Tray.Shortcuts;
using Microsoft.UI.Dispatching;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Windows.Networking.Connectivity;
using VirtualKey = Windows.System.VirtualKey;

namespace OpenHab.Windows.Tray;

internal enum TraySurfaceRefreshOutcome
{
    Applied,
    SkippedNotVisible,
    SkippedBusy,
    NoVisibleSurface,
    Failed
}

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
    private NotificationMediaResolver? notificationMediaResolver;
    private NotificationActionExecutor? notificationActionExecutor;
    private NotificationStore? notificationStore;
    private NotificationPoller? notificationPoller;
    private NotificationPollingConfig? activeNotificationPollingConfig;
    private SitemapRuntimeController? runtimeController;
    private ShortcutActionExecutor? shortcutActionExecutor;
    private HotkeyMessageWindow? hotkeyMessageWindow;
    private GlobalHotkeyService? globalHotkeyService;
    private RadialCommandMenuWindow? radialCommandMenuWindow;
    private DispatcherTimer? commandMenuHoldTimer;
    private ShortcutBinding? commandMenuHoldBinding;
    private DeviceInfoSyncService? deviceInfoSyncService;
    private WindowsSessionInfoReader? windowsSessionInfoReader;
    private CancellationTokenSource? promotedMainUiDiscoveryCts;
    private IReadOnlyList<SitemapInfo>? discoveredSitemaps;
    private bool deviceInfoEventsRegistered;
    private readonly SemaphoreSlim shellApplySemaphore = new(1, 1);
    private int isPendingRefreshRetryScheduled;
    private int isShuttingDown;
    private bool shortcutHotkeysSuspended;
    private bool notificationActivationHandlersRegistered;
    private readonly SemaphoreSlim notificationPollingSettingsChangeSemaphore = new(1, 1);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    internal sealed record NotificationPollingConfig(
        EndpointMode EndpointMode,
        Uri CloudEndpoint,
        int PollIntervalSeconds,
        string? CloudCredentialsFingerprint);

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
        promotedMainUiDiscoveryCts = new CancellationTokenSource();

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
        notificationMediaResolver = new NotificationMediaResolver(
            httpClient,
            getSettings: () => this.settingsController?.Current ?? AppSettings.Default,
            getApiToken: kind => this.settingsController is null ? null : GetApiTokenSync(this.settingsController, kind),
            getCloudCredentials: kind => this.settingsController is null ? null : GetCloudCredentialsSync(this.settingsController));
        notificationActionExecutor = new NotificationActionExecutor(
            httpClient,
            getSettings: () => this.settingsController?.Current ?? AppSettings.Default,
            getApiToken: kind => this.settingsController is null ? null : GetApiTokenSync(this.settingsController, kind),
            getCloudCredentials: kind => this.settingsController is null ? null : GetCloudCredentialsSync(this.settingsController),
            openExternal: OpenExternalAsync);
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
        runtimeController.SnapshotChanged += OnRuntimeSnapshotChanged;
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

        shortcutActionExecutor = new ShortcutActionExecutor(
            CreateActiveShortcutClient,
            () => runtimeController?.Current.ConnectionState ?? ConnectionState.Offline);
        radialCommandMenuWindow = new RadialCommandMenuWindow();

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
                RequestApplicationExit();
            });
        hotkeyMessageWindow = new HotkeyMessageWindow();
        globalHotkeyService = new GlobalHotkeyService(
            hotkeyMessageWindow.Handle,
            uiDispatcherQueue ?? DispatcherQueue.GetForCurrentThread());
        globalHotkeyService.CommandMenuRequested += (_, _) =>
        {
            _ = OpenShortcutCommandMenuAsync();
        };
        globalHotkeyService.ActionRequested += (_, action) =>
        {
            _ = ExecuteShortcutActionAsync(action);
        };
        ShortcutRecorderControl.AnyRecordingChanged += OnShortcutRecorderRecordingChanged;
        RefreshShortcutHotkeys();
        settingsController.SettingsChanged += (_, _) =>
        {
            RefreshShortcutHotkeys();
            _ = HandleNotificationPollingSettingsChangedAsync();
        };

        _ = CompleteStartupAsync(settingsController, activatedEventArgs);
    }

    private MainWindow EnsureMainWindow()
    {
        if (mainWindow is not null)
        {
            return mainWindow;
        }

        mainWindow = CreateMainWindow();
        return mainWindow;
    }

    private FlyoutWindow EnsureFlyoutWindow()
    {
        if (flyoutWindow is not null)
        {
            return flyoutWindow;
        }

        flyoutWindow = CreateFlyoutWindow();
        return flyoutWindow;
    }

    private MainWindow CreateMainWindow()
    {
        var settings = settingsController ?? throw new InvalidOperationException("Settings controller is not initialized.");
        var runtime = runtimeController ?? throw new InvalidOperationException("Runtime controller is not initialized.");
        var notifications = notificationStore ?? throw new InvalidOperationException("Notification store is not initialized.");
        var sharedHttpClient = httpClient ?? throw new InvalidOperationException("HTTP client is not initialized.");

        var window = new MainWindow(
            settings,
            runtime,
            notifications,
            requestHideToTray: () =>
            {
                shellController?.HandleWindowCloseRequested(TrayShellSurface.MainWindow);
                _ = ApplyShellStateAsync();
            },
            shouldAllowClose: IsShutdownInProgress,
            openHabClientFactory: (transportKind, endpoint) =>
            {
                var auth = ResolveRuntimeAuthSync(settings, transportKind);
                return new OpenHabHttpClient(
                    sharedHttpClient,
                    endpoint,
                    apiToken: auth.ApiToken,
                    basicUserName: auth.BasicUserName,
                    basicPassword: auth.BasicPassword);
            },
            mainUiAuthResolver: transportKind =>
            {
                var auth = ResolveRuntimeAuthSync(settings, transportKind);
                return new MainUi.MainUiAuthContext(auth.ApiToken, auth.BasicUserName, auth.BasicPassword);
            });

        PopulateWindowSitemaps(window);
        return window;
    }

    private FlyoutWindow CreateFlyoutWindow()
    {
        var settings = settingsController ?? throw new InvalidOperationException("Settings controller is not initialized.");
        var runtime = runtimeController ?? throw new InvalidOperationException("Runtime controller is not initialized.");
        var notifications = notificationStore ?? throw new InvalidOperationException("Notification store is not initialized.");

        var window = new FlyoutWindow(
            settings,
            runtime,
            notifications,
            requestOpenMainWindow: () =>
            {
                shellController?.HandleOpenMainWindow();
                _ = ApplyShellStateAsync();
            },
            requestOpenSettings: () =>
            {
                EnsureMainWindow().ShowSettingsTab();
                shellController?.HandleOpenMainWindow();
                _ = ApplyShellStateAsync();
            },
            requestOpenNotifications: () =>
            {
                EnsureMainWindow().ShowNotificationsTab();
                shellController?.HandleOpenMainWindow();
                _ = ApplyShellStateAsync();
            },
            requestHideFlyout: () =>
            {
                shellController?.HandleWindowCloseRequested(TrayShellSurface.Flyout);
                _ = ApplyShellStateAsync();
            });

        window.AppWindow.Closing += (sender, args) =>
        {
            if (IsShutdownInProgress())
            {
                return;
            }

            // If the window is already hidden (by exit animation), just cancel
            if (!window.AppWindow.IsVisible)
            {
                args.Cancel = true;
                return;
            }

            args.Cancel = true;
            shellController?.HandleWindowCloseRequested(TrayShellSurface.Flyout);
            _ = ApplyShellStateAsync();
        };

        PopulateWindowSitemaps(window);
        return window;
    }

    private void PopulateWindowSitemaps(MainWindow window)
    {
        if (discoveredSitemaps is { } sitemaps)
        {
            window.PopulateSitemaps(sitemaps);
        }
    }

    private void PopulateWindowSitemaps(FlyoutWindow window)
    {
        if (discoveredSitemaps is { } sitemaps)
        {
            window.PopulateSitemaps(sitemaps);
        }
    }

    private void PopulateCreatedWindowsWithDiscoveredSitemaps()
    {
        if (discoveredSitemaps is not { } sitemaps)
        {
            return;
        }

        flyoutWindow?.PopulateSitemaps(sitemaps);
        mainWindow?.PopulateSitemaps(sitemaps);
    }

    private async Task LoadCurrentVisibleRuntimeSurfaceAsync()
    {
        if (shellController is null)
        {
            return;
        }

        if (shellController.Current.VisibleSurface == TrayShellSurface.MainWindow)
        {
            await EnsureMainWindow().LoadRuntimeAsync();
        }
        else if (shellController.Current.VisibleSurface == TrayShellSurface.Flyout)
        {
            await EnsureFlyoutWindow().LoadRuntimeAsync();
        }
    }

    private async Task<TraySurfaceRefreshOutcome> RefreshCurrentVisibleRuntimeSurfaceAsync(TrayShellSurface visibleSurface)
    {
        if (visibleSurface == TrayShellSurface.MainWindow)
        {
            var window = EnsureMainWindow();
            return await window.RefreshRuntimeForShellAsync();
        }

        if (visibleSurface == TrayShellSurface.Flyout)
        {
            var window = EnsureFlyoutWindow();
            return await window.RefreshRuntimeForShellAsync();
        }

        return TraySurfaceRefreshOutcome.NoVisibleSurface;
    }

    private void SchedulePendingRefreshRetry()
    {
        if (IsShutdownInProgress() || shellController?.Current.PendingRefresh != true)
        {
            return;
        }

        if (Interlocked.Exchange(ref isPendingRefreshRetryScheduled, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(75);

                if (IsShutdownInProgress() || shellController?.Current.PendingRefresh != true)
                {
                    return;
                }

                Interlocked.Exchange(ref isPendingRefreshRetryScheduled, 0);

                if (uiDispatcherQueue?.TryEnqueue(() => _ = ApplyShellStateAsync()) != true
                    && !IsShutdownInProgress()
                    && shellController?.Current.PendingRefresh == true)
                {
                    SchedulePendingRefreshRetry();
                }
            }
            catch
            {
                // Best-effort retry scheduling.
            }
            finally
            {
                Interlocked.Exchange(ref isPendingRefreshRetryScheduled, 0);
            }
        });
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
            if (notificationPoller is not null)
            {
                DiagnosticLogger.Info("Notification poller start skipped — poller already active");
                return;
            }

            DiagnosticLogger.Info("Starting notification polling");
            var settings = settingsController.Current;
            var cloudCredentials = GetCloudCredentialsSync(settingsController);
            var pollingConfig = BuildNotificationPollingConfig(settings, cloudCredentials);
            activeNotificationPollingConfig = pollingConfig;
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
            if (!notificationActivationHandlersRegistered)
            {
                ToastService.NotificationActivated += (_, args) =>
                {
                    _ = HandleNotificationActivationAsync(args.Argument);
                };
                ToastService.PackagedActivated += arguments =>
                {
                    _ = HandleNotificationActivationAsync(arguments);
                };
                notificationActivationHandlersRegistered = true;
            }

            DiagnosticLogger.Info($"Cloud credentials resolved: {(cloudCredentials is not null ? "yes" : "no")}");
            notificationPoller = new NotificationPoller(
                httpClient!,
                settings.CloudEndpoint,
                basicUserName: cloudCredentials?.UserName,
                basicPassword: cloudCredentials?.Password,
                pollInterval: TimeSpan.FromSeconds(settings.NotificationPollIntervalSeconds),
                dispatcher: uiDispatcherQueue,
                preSeenIds: notificationStore?.GetSeenUndismissedIds(),
                isDismissedFunc: id => notificationStore?.IsDismissed(id) ?? false,
                onNewNotification: n =>
                {
                    notificationStore?.AddOrUpdate(
                        n.Id, n.Message, n.Created, n.Title, n.Icon, n.Tag,
                        n.ReferenceId, n.OnClickAction, n.MediaAttachmentUrl,
                        n.ActionButtons.ElementAtOrDefault(0)?.ToRawButton(),
                        n.ActionButtons.ElementAtOrDefault(1)?.ToRawButton(),
                        n.ActionButtons.ElementAtOrDefault(2)?.ToRawButton());
                },
                onHideNotification: target =>
                {
                    DiagnosticLogger.Info($"Applying hide notification target: {target.Kind}={target.Value}");
                    if (target.Kind == NotificationHideTargetKind.ReferenceId)
                    {
                        notificationStore?.HideByReferenceId(target.Value);
                        ToastService.HideByReferenceId(target.Value);
                    }
                    else
                    {
                        var matchingReferenceIds = notificationStore?.GetAll()
                            .Where(n =>
                                !string.IsNullOrWhiteSpace(n.Severity)
                                && string.Equals(n.Severity, target.Value, StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrWhiteSpace(n.ReferenceId))
                            .Select(n => n.ReferenceId!)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList() ?? [];
                        notificationStore?.HideByTag(target.Value);
                        ToastService.HideByTag(target.Value);
                        foreach (var referenceId in matchingReferenceIds)
                        {
                            ToastService.HideByReferenceId(referenceId);
                        }
                    }
                });

            DiagnosticLogger.Info(
                $"Notification poller created — polling {settings.CloudEndpoint.Host} every {settings.NotificationPollIntervalSeconds}s");

            notificationPoller.NotificationReceived += (_, notification) =>
            {
                DiagnosticLogger.Info($"Notification received - tag: {notification.Tag ?? "none"}");
                var importantTags = settingsController.Current.ImportantNotificationTags;
                var isImportant = IsImportantNotification(notification.Tag, importantTags);
                var toastHeader = ResolveNotificationHeader(notification.Tag);

                _ = ShowNotificationToastAsync(notification, isImportant, toastHeader);
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

    private async Task HandleNotificationPollingSettingsChangedAsync()
    {
        await notificationPollingSettingsChangeSemaphore.WaitAsync();
        try
        {
            var controller = settingsController;
            if (controller is null)
            {
                return;
            }

            var settings = controller.Current;
            var cloudCredentials = GetCloudCredentialsSync(controller);
            var nextConfig = BuildNotificationPollingConfig(settings, cloudCredentials);
            if (!ShouldReconfigureNotificationPolling(activeNotificationPollingConfig, nextConfig))
            {
                return;
            }

            var existingPoller = notificationPoller;
            notificationPoller = null;
            if (existingPoller is not null)
            {
                await existingPoller.StopAsync();
                existingPoller.Dispose();
            }

            activeNotificationPollingConfig = nextConfig;
            if (settings.EndpointMode == EndpointMode.LocalOnly)
            {
                return;
            }

            StartNotificationPolling(controller);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Error("Failed to apply notification polling settings change", ex);
        }
        finally
        {
            notificationPollingSettingsChangeSemaphore.Release();
        }
    }

    internal static NotificationPollingConfig BuildNotificationPollingConfig(
        AppSettings settings,
        CloudCredentials? cloudCredentials)
    {
        return new NotificationPollingConfig(
            settings.EndpointMode,
            settings.CloudEndpoint,
            settings.NotificationPollIntervalSeconds,
            BuildCloudCredentialsFingerprint(cloudCredentials));
    }

    internal static bool ShouldReconfigureNotificationPolling(
        NotificationPollingConfig? activeConfig,
        NotificationPollingConfig nextConfig)
    {
        return !Equals(activeConfig, nextConfig);
    }

    private static string? BuildCloudCredentialsFingerprint(CloudCredentials? cloudCredentials)
    {
        if (cloudCredentials is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(cloudCredentials.UserName)
            || string.IsNullOrWhiteSpace(cloudCredentials.Password))
        {
            return null;
        }

        var fingerprintMaterial = $"{cloudCredentials.UserName.Trim()}\0{cloudCredentials.Password}";
        var fingerprintBytes = System.Text.Encoding.UTF8.GetBytes(fingerprintMaterial);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fingerprintBytes));
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

    private IOpenHabClient? CreateActiveShortcutClient()
    {
        var settings = settingsController?.Current;
        var runtime = runtimeController;
        var sharedClient = httpClient;
        var activeTransport = runtime?.Current.ActiveTransport;
        if (settings is null || runtime is null || sharedClient is null || activeTransport is null)
        {
            return null;
        }

        var endpoint = activeTransport == TransportKind.Cloud
            ? settings.CloudEndpoint
            : settings.LocalEndpoint;
        var auth = ResolveRuntimeAuthSync(settingsController!, activeTransport.Value);

        return new OpenHabHttpClient(
            sharedClient,
            endpoint,
            apiToken: auth.ApiToken,
            basicUserName: auth.BasicUserName,
            basicPassword: auth.BasicPassword);
    }

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

    private void RequestApplicationExit()
    {
        DiagnosticLogger.Info("Tray exit requested");
        shellController?.HandleExitRequested();
        ShutdownTrayResources();
        DiagnosticLogger.Info("Application exit invoked");
        Exit();
        Environment.Exit(0);
    }

    private bool IsShutdownInProgress()
    {
        return Volatile.Read(ref isShuttingDown) != 0
            || shellController?.Current.ShouldExitProcess == true;
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
            var refreshRequestVersion = state.RefreshRequestVersion;
            var visibleSurface = state.VisibleSurface;

            if (state.ShouldExitProcess)
            {
                RequestApplicationExit();
                return;
            }

            switch (visibleSurface)
            {
                case TrayShellSurface.MainWindow:
                    var main = EnsureMainWindow();
                    if (flyoutWindow is not null && flyoutWindow.AppWindow.IsVisible)
                    {
                        await flyoutWindow.AnimateFlyoutExitAndHideAsync();
                    }
                    main.CenterOnCurrentScreen();
                    main.Activate();
                    break;
                case TrayShellSurface.Flyout:
                    var flyout = EnsureFlyoutWindow();
                    if (mainWindow is not null)
                    {
                        mainWindow.AppWindow.Hide();
                    }
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
                    if (mainWindow is not null)
                    {
                        mainWindow.AppWindow.Hide();
                    }

                    if (flyoutWindow is not null && flyoutWindow.AppWindow.IsVisible)
                    {
                        await flyoutWindow.AnimateFlyoutExitAndHideAsync();
                    }
                    break;
            }

            if (state.PendingRefresh)
            {
                var refreshOutcome = await RefreshCurrentVisibleRuntimeSurfaceAsync(visibleSurface);
                switch (refreshOutcome)
                {
                    case TraySurfaceRefreshOutcome.Applied:
                    case TraySurfaceRefreshOutcome.NoVisibleSurface:
                    case TraySurfaceRefreshOutcome.Failed:
                        shellController.HandleRefreshCompleted(refreshRequestVersion, visibleSurface);
                        break;
                    case TraySurfaceRefreshOutcome.SkippedNotVisible:
                    case TraySurfaceRefreshOutcome.SkippedBusy:
                        SchedulePendingRefreshRetry();
                        break;
                }
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
            discoveredSitemaps = sitemaps.ToArray();
            _ = uiDispatcherQueue?.TryEnqueue(() =>
            {
                PopulateCreatedWindowsWithDiscoveredSitemaps();
            });

            var settings = settingsController.Current;

            // Preserve user's previously selected sitemap if it still exists.
            if (!string.IsNullOrWhiteSpace(settings.SitemapName) &&
                sitemaps.Any(s => string.Equals(s.Name, settings.SitemapName, StringComparison.OrdinalIgnoreCase)))
            {
                _ = uiDispatcherQueue?.TryEnqueue(() =>
                {
                    _ = LoadCurrentVisibleRuntimeSurfaceAsync();
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
                    _ = LoadCurrentVisibleRuntimeSurfaceAsync();
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
                        _ = LoadCurrentVisibleRuntimeSurfaceAsync();
                    });
                }
                else
                {
                    runtimeController!.SetBannerStatus("Multiple sitemaps detected — choose one");
                    settingsController.SetSitemapName(string.Empty);
                }
            }

            var discoveryCancellationToken = promotedMainUiDiscoveryCts?.Token ?? CancellationToken.None;
            _ = uiDispatcherQueue?.TryEnqueue(() =>
            {
                _ = RefreshPromotedMainUiPagesOnStartupAsync(discoveryCancellationToken);
            });
        }
        catch
        {
            // Best-effort; dropdown will be empty if server unreachable.
        }

        DiagnosticLogger.Info("Completing startup — applying notification polling configuration");
        await HandleNotificationPollingSettingsChangedAsync();
        HandleStartupActivation(activatedEventArgs);
    }

    private async Task RefreshPromotedMainUiPagesOnStartupAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (mainWindow is not null)
            {
                await mainWindow.RefreshPromotedMainUiPagesAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException ex)
            when (cancellationToken.IsCancellationRequested && ex.CancellationToken == cancellationToken)
        {
            // Expected when the app exits while startup discovery is still in flight.
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Startup Main UI page discovery failed: {ex.GetType().Name}");
        }
    }

    private void RefreshShortcutHotkeys()
    {
        var service = globalHotkeyService;
        if (service is null)
        {
            return;
        }

        var shortcutSettings = (settingsController?.Current.Shortcuts ?? ShortcutSettings.Default).Normalized();
        if (shortcutHotkeysSuspended)
        {
            service.Suspend();
            return;
        }

        var result = service.Refresh(shortcutSettings);
        foreach (var failure in result.Failures)
        {
            var key = ResolveShortcutFailureKey(shortcutSettings, failure.Owner);
            DiagnosticLogger.Warn(
                $"Shortcut hotkey registration failed: owner='{failure.Owner}', key='{key}', message='{failure.Message}'");
        }
    }

    private static string ResolveShortcutFailureKey(ShortcutSettings settings, string owner)
    {
        if (string.Equals(owner, "openHAB Command Menu", StringComparison.Ordinal))
        {
            return ShortcutBindingFormatter.Format(settings.CommandMenu.Binding);
        }

        const string actionPrefix = "Action: ";
        if (owner.StartsWith(actionPrefix, StringComparison.Ordinal))
        {
            var actionName = owner[actionPrefix.Length..];
            var action = settings.Actions.FirstOrDefault(action =>
                string.Equals(action.Name, actionName, StringComparison.Ordinal));
            if (action is not null)
            {
                return ShortcutBindingFormatter.Format(action.GlobalShortcut);
            }
        }

        return "(unknown)";
    }

    private void OnShortcutRecorderRecordingChanged(object? sender, bool isRecording)
    {
        var dispatcher = uiDispatcherQueue;
        if (dispatcher is not null && !dispatcher.HasThreadAccess)
        {
            _ = dispatcher.TryEnqueue(() => OnShortcutRecorderRecordingChanged(sender, isRecording));
            return;
        }

        shortcutHotkeysSuspended = isRecording;
        var service = globalHotkeyService;
        if (service is null)
        {
            return;
        }

        if (isRecording)
        {
            service.Suspend();
            return;
        }

        RegisterCurrentShortcutHotkeys(service);
    }

    private void RegisterCurrentShortcutHotkeys(GlobalHotkeyService service)
    {
        var shortcutSettings = (settingsController?.Current.Shortcuts ?? ShortcutSettings.Default).Normalized();
        var result = service.Resume(shortcutSettings);
        foreach (var failure in result.Failures)
        {
            var key = ResolveShortcutFailureKey(shortcutSettings, failure.Owner);
            DiagnosticLogger.Warn(
                $"Shortcut hotkey registration failed: owner='{failure.Owner}', key='{key}', message='{failure.Message}'");
        }
    }

    private Task OpenShortcutCommandMenuAsync()
    {
        var dispatcher = uiDispatcherQueue;
        if (dispatcher is not null && !dispatcher.HasThreadAccess)
        {
            _ = dispatcher.TryEnqueue(() => _ = OpenShortcutCommandMenuAsync());
            return Task.CompletedTask;
        }

        if (runtimeController?.Current.ConnectionState != ConnectionState.Online)
        {
            DiagnosticLogger.Info("Shortcut command menu skipped because openHAB is not online.");
            return Task.CompletedTask;
        }

        var settings = settingsController?.Current.Shortcuts?.Normalized();
        var menuWindow = radialCommandMenuWindow;
        if (settings is null || menuWindow is null)
        {
            return Task.CompletedTask;
        }

        StopCommandMenuHoldTimer();
        var canPollHoldBinding = ShortcutWindowsMapper.TryMapVirtualKey(settings.CommandMenu.Binding, out _);

        if (settings.CommandMenu.RadialActivationMode == RadialActivationMode.Toggle && menuWindow.IsMenuVisible)
        {
            menuWindow.CloseMenu();
            return Task.CompletedTask;
        }
        if (settings.CommandMenu.RadialActivationMode == RadialActivationMode.Hold
            && !canPollHoldBinding
            && menuWindow.IsMenuVisible)
        {
            menuWindow.CloseMenu();
            return Task.CompletedTask;
        }

        var actions = settings
            .Actions
            .Where(static action => action.ShowInCommandMenu && ShortcutValidation.ValidateAction(action).IsValid)
            .ToList();
        menuWindow.ShowActions(actions, ExecuteShortcutActionAsync);
        if (settings.CommandMenu.RadialActivationMode == RadialActivationMode.Hold && canPollHoldBinding)
        {
            StartCommandMenuHoldTimer(settings.CommandMenu.Binding);
        }

        return Task.CompletedTask;
    }

    private void StartCommandMenuHoldTimer(ShortcutBinding? binding)
    {
        if (!ShortcutWindowsMapper.TryMapVirtualKey(binding, out _))
        {
            return;
        }

        commandMenuHoldBinding = binding;
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(35)
        };
        timer.Tick += CommandMenuHoldTimer_Tick;
        commandMenuHoldTimer = timer;
        timer.Start();
    }

    private void StopCommandMenuHoldTimer()
    {
        if (commandMenuHoldTimer is not null)
        {
            commandMenuHoldTimer.Stop();
            commandMenuHoldTimer.Tick -= CommandMenuHoldTimer_Tick;
            commandMenuHoldTimer = null;
        }

        commandMenuHoldBinding = null;
    }

    private void CommandMenuHoldTimer_Tick(object? sender, object e)
    {
        if (radialCommandMenuWindow?.IsMenuVisible != true)
        {
            StopCommandMenuHoldTimer();
            return;
        }

        if (!IsShortcutBindingDown(commandMenuHoldBinding))
        {
            radialCommandMenuWindow.CloseMenu();
            StopCommandMenuHoldTimer();
        }
    }

    private static bool IsShortcutBindingDown(ShortcutBinding? binding)
    {
        if (!ShortcutBindingFormatter.TryNormalize(binding, out var normalized)
            || !ShortcutWindowsMapper.TryMapVirtualKey(normalized, out var virtualKey))
        {
            return false;
        }

        foreach (var modifier in normalized.Modifiers)
        {
            if (!IsModifierDown(modifier))
            {
                return false;
            }
        }

        return IsVirtualKeyDown(virtualKey);
    }

    private static bool IsModifierDown(ShortcutModifier modifier)
    {
        return modifier switch
        {
            ShortcutModifier.Win => IsVirtualKeyDown(VirtualKey.LeftWindows) || IsVirtualKeyDown(VirtualKey.RightWindows),
            ShortcutModifier.Ctrl => IsVirtualKeyDown(VirtualKey.Control) || IsVirtualKeyDown(VirtualKey.LeftControl) || IsVirtualKeyDown(VirtualKey.RightControl),
            ShortcutModifier.Alt => IsVirtualKeyDown(VirtualKey.Menu) || IsVirtualKeyDown(VirtualKey.LeftMenu) || IsVirtualKeyDown(VirtualKey.RightMenu),
            ShortcutModifier.Shift => IsVirtualKeyDown(VirtualKey.Shift) || IsVirtualKeyDown(VirtualKey.LeftShift) || IsVirtualKeyDown(VirtualKey.RightShift),
            _ => false
        };
    }

    private static bool IsVirtualKeyDown(VirtualKey virtualKey)
    {
        return (GetAsyncKeyState((int)virtualKey) & 0x8000) != 0;
    }

    private async Task ExecuteShortcutActionAsync(ShortcutAction action)
    {
        try
        {
            var executor = shortcutActionExecutor;
            if (executor is null)
            {
                DiagnosticLogger.Warn("Shortcut action execution skipped: executor is unavailable.");
                return;
            }

            var result = await executor.ExecuteAsync(action, CancellationToken.None);
            if (!result.Succeeded)
            {
                DiagnosticLogger.Warn(
                    $"Shortcut action execution failed: action='{action.Name}', failure='{result.Failure}', message='{result.Message}'");
                SetShellStatusText(result.Message);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn(
                $"Shortcut action execution failed unexpectedly: action='{action.Name}', error='{ex.GetType().Name}: {ex.Message}'");
        }
    }

    private void OnRuntimeSnapshotChanged(object? sender, EventArgs e)
    {
        var dispatcher = uiDispatcherQueue;
        if (dispatcher is not null && !dispatcher.HasThreadAccess)
        {
            _ = dispatcher.TryEnqueue(CloseShortcutCommandMenuWhenOffline);
            return;
        }

        CloseShortcutCommandMenuWhenOffline();
    }

    private void CloseShortcutCommandMenuWhenOffline()
    {
        if (runtimeController?.Current.ConnectionState == ConnectionState.Online)
        {
            return;
        }

        radialCommandMenuWindow?.CloseMenu();
    }

    private void SetShellStatusText(string text)
    {
        var dispatcher = uiDispatcherQueue;
        if (dispatcher is not null && !dispatcher.HasThreadAccess)
        {
            _ = dispatcher.TryEnqueue(() => mainWindow?.SetShellStatusText(text));
            return;
        }

        mainWindow?.SetShellStatusText(text);
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
        promotedMainUiDiscoveryCts?.Cancel();
        promotedMainUiDiscoveryCts?.Dispose();
        promotedMainUiDiscoveryCts = null;
        UnregisterDeviceInfoSyncEvents();
        deviceInfoSyncService?.Dispose();
        deviceInfoSyncService = null;
        windowsSessionInfoReader = null;
        DiagnosticLogger.Info("Shutting down notification poller");
        notificationPoller?.Dispose();
        notificationPoller = null;
        ShortcutRecorderControl.AnyRecordingChanged -= OnShortcutRecorderRecordingChanged;
        globalHotkeyService?.Dispose();
        globalHotkeyService = null;
        hotkeyMessageWindow?.Dispose();
        hotkeyMessageWindow = null;
        StopCommandMenuHoldTimer();
        radialCommandMenuWindow?.CloseMenu();
        radialCommandMenuWindow = null;
        shortcutActionExecutor = null;
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

    private static string BuildNotificationHeader(NormalizedCloudNotification notification)
    {
        var tag = notification.Tag;
        if (!string.IsNullOrWhiteSpace(notification.Title))
        {
            var title = notification.Title;
            if (!string.IsNullOrWhiteSpace(tag))
            {
                title = StripLeadingBracketToken(title, tag);
            }

            return CapitalizeFirstLetter(title.Trim());
        }

        return "openHAB";
    }

    private static string BuildNotificationBody(NormalizedCloudNotification notification)
    {
        var body = notification.Message ?? string.Empty;
        var tag = notification.Tag;
        if (!string.IsNullOrWhiteSpace(tag))
        {
            body = StripLeadingBracketToken(body, tag);
        }

        body = CapitalizeFirstLetter(body.Trim());
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

    private static string CapitalizeFirstLetter(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string? ResolveNotificationHeader(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        return CapitalizeFirstLetter(tag.Trim());
    }

    private static bool IsImportantNotification(string? tag, IReadOnlyList<string> configuredImportantTags)
    {
        if (string.IsNullOrWhiteSpace(tag) || configuredImportantTags.Count == 0)
        {
            return false;
        }

        return configuredImportantTags.Any(configured =>
            string.Equals(configured, tag.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task ShowNotificationToastAsync(
        NormalizedCloudNotification notification,
        bool isImportant,
        string? toastHeader)
    {
        Uri? appLogoOverrideUri = null;
        Uri? heroImageUri = null;
        try
        {
            using var iconCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            appLogoOverrideUri = await ResolveNotificationAppLogoOverrideUriAsync(notification.Icon, iconCts.Token);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Notification icon cache failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var resolver = notificationMediaResolver;
            if (resolver is not null)
            {
                heroImageUri = await resolver.ResolveAsync(notification.MediaAttachmentUrl, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Notification media resolution failed: {ex.GetType().Name}: {ex.Message}");
        }

        ToastService.Show(new ToastNotificationRequest(
            Title: string.IsNullOrWhiteSpace(notification.Title) ? "openHAB" : notification.Title,
            Body: notification.Message,
            Actions: notification.ActionButtons,
            LaunchAction: notification.OnClickAction,
            Important: isImportant,
            Header: toastHeader,
            Tag: notification.Tag,
            ReferenceId: notification.ReferenceId,
            AppLogoOverrideUri: appLogoOverrideUri,
            HeroImageUri: heroImageUri));
    }

    private async Task<Uri?> ResolveNotificationAppLogoOverrideUriAsync(string? icon, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(icon)
            && Uri.TryCreate(icon.Trim(), UriKind.Absolute, out var iconUri))
        {
            return iconUri;
        }

        if (string.IsNullOrWhiteSpace(icon))
        {
            return null;
        }

        var settings = settingsController?.Current;
        var client = httpClient;
        if (settings is null || client is null)
        {
            return null;
        }

        var primaryTransport = settings.EndpointMode == EndpointMode.CloudOnly
            ? TransportKind.Cloud
            : TransportKind.Local;

        var cached = await TryCacheNotificationIconAsync(icon, settings, primaryTransport, client, cancellationToken);
        if (cached is not null || settings.EndpointMode != EndpointMode.Automatic)
        {
            return cached;
        }

        return await TryCacheNotificationIconAsync(icon, settings, TransportKind.Cloud, client, cancellationToken);
    }

    private static async Task<Uri?> TryCacheNotificationIconAsync(
        string icon,
        AppSettings settings,
        TransportKind transport,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var baseUri = transport == TransportKind.Cloud ? settings.CloudEndpoint : settings.LocalEndpoint;
        foreach (var format in new[] { "svg", "png" })
        {
            var iconUri = SitemapControlFactory.BuildOpenHabIconUri(baseUri, icon.Trim(), null, format);
            var cachePath = BuildNotificationIconCachePath(iconUri, format);
            if (File.Exists(cachePath))
            {
                return new Uri(cachePath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            using var request = new HttpRequestMessage(HttpMethod.Get, iconUri);
            var authMode = ApplyNotificationIconAuth(request, transport);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                DiagnosticLogger.Warn(
                    $"Notification icon request failed: icon='{icon}', format='{format}', status={(int)response.StatusCode}, auth='{authMode}', uri={iconUri}");
                continue;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0 || bytes.Length > 3 * 1024 * 1024)
            {
                DiagnosticLogger.Warn(
                    $"Notification icon skipped due to invalid size: icon='{icon}', format='{format}', bytes={bytes.Length}");
                continue;
            }

            await File.WriteAllBytesAsync(cachePath, bytes, cancellationToken);
            DiagnosticLogger.Info(
                $"Notification icon cached: icon='{icon}', format='{format}', bytes={bytes.Length}, auth='{authMode}'");
            return new Uri(cachePath);
        }

        return null;
    }

    private static string ApplyNotificationIconAuth(HttpRequestMessage request, TransportKind transport)
    {
        var controller = ((App)Current).settingsController;
        if (controller is null)
        {
            return "none";
        }

        if (transport == TransportKind.Local)
        {
            var token = GetApiTokenSync(controller, TransportKind.Local);
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                return "bearer";
            }

            return "none";
        }

        var cloudCredentials = GetCloudCredentialsSync(controller);
        if (!string.IsNullOrWhiteSpace(cloudCredentials?.UserName))
        {
            var raw = $"{cloudCredentials.UserName}:{cloudCredentials.Password ?? string.Empty}";
            var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", base64);
            return "basic";
        }

        return "none";
    }

    private static string BuildNotificationIconCachePath(Uri iconUri, string format)
    {
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(iconUri.ToString())));
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenHab.WinApp",
            "NotificationIcons",
            $"{hash}.{format}");
    }

    private async Task HandleNotificationActivationAsync(string? arguments)
    {
        if (TryExtractToastActionArgument(arguments, out var actionArgument))
        {
            var parsed = NotificationActionParser.TryParse(actionArgument);
            if (parsed is null)
            {
                DiagnosticLogger.Warn("Unparseable toast action argument.");
            }
            else
            {
                var executor = notificationActionExecutor;
                if (executor is not null)
                {
                    if (await TryExecuteNotificationActionAsync(executor, parsed))
                    {
                        return;
                    }
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(arguments))
        {
            var parsed = NotificationActionParser.TryParse(arguments);
            if (parsed is not null && IsInlineToastAction(parsed))
            {
                var executor = notificationActionExecutor;
                if (executor is not null)
                {
                    if (await TryExecuteNotificationActionAsync(executor, parsed))
                    {
                        return;
                    }
                }
            }
        }

        _ = uiDispatcherQueue?.TryEnqueue(() =>
        {
            if (shellController is null) return;
            shellController.HandleNotificationActivated();
            _ = ApplyShellStateAsync();
        });
    }

    private static async Task<bool> TryExecuteNotificationActionAsync(
        NotificationActionExecutor executor,
        NotificationAction action)
    {
        try
        {
            await executor.ExecuteAsync(action, CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn(
                $"Notification activation action failed: type='{action.Type}', error={ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static bool TryExtractToastActionArgument(string? arguments, out string actionArgument)
    {
        actionArgument = string.Empty;
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return false;
        }

        const string prefix = "action=";
        foreach (var part in arguments.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                actionArgument = Uri.UnescapeDataString(part[prefix.Length..]);
                return true;
            }
        }

        return false;
    }

    private static bool IsInlineToastAction(NotificationAction action)
    {
        return action.Type.Equals("command", StringComparison.OrdinalIgnoreCase)
            || action.Type.Equals("ui", StringComparison.OrdinalIgnoreCase)
            || action.Type.Equals("http", StringComparison.OrdinalIgnoreCase)
            || action.Type.Equals("https", StringComparison.OrdinalIgnoreCase);
    }

    private static Task OpenExternalAsync(Uri uri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uri.ToString(),
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }

    private void HandleStartupActivation(AppActivationArguments? activatedEventArgs)
    {
        if (activatedEventArgs?.Kind != ExtendedActivationKind.ToastNotification)
        {
            return;
        }

        var startupArguments = ExtractStartupToastArguments(activatedEventArgs);
        DiagnosticLogger.Info("Processing startup toast activation");
        _ = HandleNotificationActivationAsync(startupArguments);
    }

    private static string? ExtractStartupToastArguments(AppActivationArguments activatedEventArgs)
    {
        if (activatedEventArgs.Data is global::Windows.ApplicationModel.Activation.ToastNotificationActivatedEventArgs toastArgs)
        {
            return toastArgs.Argument;
        }

        return activatedEventArgs.Data as string;
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
