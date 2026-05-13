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
    private NotificationMediaResolver? notificationMediaResolver;
    private NotificationActionExecutor? notificationActionExecutor;
    private NotificationStore? notificationStore;
    private NotificationPoller? notificationPoller;
    private SitemapRuntimeController? runtimeController;
    private ShortcutActionExecutor? shortcutActionExecutor;
    private GlobalHotkeyService? globalHotkeyService;
    private RadialCommandMenuWindow? radialCommandMenuWindow;
    private DeviceInfoSyncService? deviceInfoSyncService;
    private WindowsSessionInfoReader? windowsSessionInfoReader;
    private CancellationTokenSource? promotedMainUiDiscoveryCts;
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
            },
            openHabClientFactory: (transportKind, endpoint) =>
            {
                var auth = ResolveRuntimeAuthSync(settingsController, transportKind);
                return new OpenHabHttpClient(
                    httpClient,
                    endpoint,
                    apiToken: auth.ApiToken,
                    basicUserName: auth.BasicUserName,
                    basicPassword: auth.BasicPassword);
            },
            mainUiAuthResolver: transportKind =>
            {
                var auth = ResolveRuntimeAuthSync(settingsController, transportKind);
                return new MainUi.MainUiAuthContext(auth.ApiToken, auth.BasicUserName, auth.BasicPassword);
            });
        shortcutActionExecutor = new ShortcutActionExecutor(
            CreateActiveShortcutClient,
            () => runtimeController?.Current.ConnectionState ?? ConnectionState.Offline);
        radialCommandMenuWindow = new RadialCommandMenuWindow();
        flyoutWindow = new FlyoutWindow(
            settingsController,
            runtimeController,
            notificationStore,
            requestOpenMainWindow: () =>
            {
                shellController.HandleOpenMainWindow();
                _ = ApplyShellStateAsync();
            },
            requestOpenSettings: () =>
            {
                mainWindow?.ShowSettingsTab();
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
        globalHotkeyService = new GlobalHotkeyService(mainWindow, uiDispatcherQueue ?? DispatcherQueue.GetForCurrentThread());
        globalHotkeyService.CommandMenuRequested += (_, _) =>
        {
            _ = OpenShortcutCommandMenuAsync();
        };
        globalHotkeyService.ActionRequested += (_, action) =>
        {
            _ = ExecuteShortcutActionAsync(action);
        };
        RefreshShortcutHotkeys();
        settingsController.SettingsChanged += (_, _) => RefreshShortcutHotkeys();

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
                _ = HandleNotificationActivationAsync(args.Argument);
            };
            ToastService.PackagedActivated += arguments =>
            {
                _ = HandleNotificationActivationAsync(arguments);
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

            DiagnosticLogger.Info($"Notification poller created — polling {settings.CloudEndpoint.Host} every 60s");

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

        DiagnosticLogger.Info("Completing startup — starting notification polling");
        StartNotificationPolling(settingsController);
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

        var settings = settingsController?.Current.Shortcuts;
        var menuWindow = radialCommandMenuWindow;
        if (settings is null || menuWindow is null)
        {
            return Task.CompletedTask;
        }

        var actions = settings
            .Normalized()
            .Actions
            .Where(static action => action.ShowInCommandMenu && ShortcutValidation.ValidateAction(action).IsValid)
            .ToList();
        menuWindow.ShowActions(actions, ExecuteShortcutActionAsync);
        return Task.CompletedTask;
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
        globalHotkeyService?.Dispose();
        globalHotkeyService = null;
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
