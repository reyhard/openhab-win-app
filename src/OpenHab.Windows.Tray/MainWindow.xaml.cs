
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;
using OpenHab.App.Notifications;
using OpenHab.App.Runtime;
using OpenHab.App.Shell;
using OpenHab.App.Settings;
using OpenHab.App.Tray;
using OpenHab.App.MainUi;
using OpenHab.Core;
using OpenHab.Core.Api;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using OpenHab.Windows.Tray.MainUi;
using OpenHab.Windows.Tray.Rendering;
using OpenHab.Windows.Tray.Rendering.SitemapSurface;
namespace OpenHab.Windows.Tray;

public sealed partial class MainWindow : Window
{
    private const double ExpandedSidebarWidth = 220d;
    private const double CollapsedSidebarWidth = 56d;
    private const double ExpandedSitemapPaneWidth = 380d;
    private const int ShellChromeAnimationDurationMs = 180;
    private static readonly HttpClient FallbackOpenHabClient = new();
    private readonly AppSettingsController settingsController;
    private readonly SitemapRuntimeController runtimeController;
    private readonly OpenHab.App.Shell.MainWindowShellController shellController;
    private readonly NotificationStore? notificationStore;
    private readonly Action requestHideToTray;
    private readonly Func<bool> shouldAllowClose;
    private readonly Func<TransportKind, Uri, IOpenHabClient> openHabClientFactory;
    private readonly Func<TransportKind, MainUiAuthContext> mainUiAuthResolver;
    private readonly SitemapIconAuthResolver sitemapIconAuthResolver;
    private readonly SitemapSurfaceRenderer sitemapSurfaceRenderer;
    private readonly DispatcherRefreshGate snapshotRefreshGate;
    private readonly UISettings uiSettings = new();
    private IReadOnlyList<OpenHab.App.MainUi.MainUiPageLink> promotedMainUiPages = [];
    private bool isRefreshing;
    private bool isHandlingCloseRequest;
    private bool _activeSlotIsA = true;
    private bool _suppressNextSnapshotRefresh;
    private bool _isPageTransitionRunning;
    private bool _pendingSnapshotRefresh;
    private bool _lastRefreshUseWindows11Icons;
    private bool isUpdatingSearchBox;
    private bool isSearchChromeOpen;
    private bool isSitemapSearchBoxFocused;
    private readonly DispatcherTimer sitemapSearchDebounceTimer = new();
    private string pendingSitemapSearchQuery = string.Empty;
    private string? currentMainUiRoute;
    private TransportKind? currentMainUiTransport;
    private bool isMainUiNavigationInProgress;
    private bool isSidebarCollapsed;
    private TransportKind? activeMainUiNavigationTransport;
    private bool pendingMainUiTransportResync;
    private bool hasAppliedInitialShellState;
    private DispatcherTimer? sidebarWidthAnimationTimer;
    private DispatcherTimer? sitemapPaneWidthAnimationTimer;
    private string? pendingExplicitMainUiRoute;
    private MainUiWebViewHost? mainUiHost;

    private Notifications.NotificationsPageControl? notificationsPage;
    private Settings.SettingsPageControl? settingsPage;

    private StackPanel ActiveRows => _activeSlotIsA ? SitemapRows : SitemapRowsB;
    private StackPanel InactiveRows => _activeSlotIsA ? SitemapRowsB : SitemapRows;
    private Grid ActiveSlotContainer => _activeSlotIsA ? SitemapPageSlotA : SitemapPageSlotB;
    private Grid InactiveSlotContainer => _activeSlotIsA ? SitemapPageSlotB : SitemapPageSlotA;
    private MainUiWebViewHost MainUiHost => mainUiHost ??= CreateMainUiHost();


    public MainWindow(AppSettingsController settingsController, SitemapRuntimeController runtimeController)
        : this(
            settingsController,
            runtimeController,
            notificationStore: null,
            () => { },
            () => false,
            (transportKind, endpoint) => new OpenHabHttpClient(FallbackOpenHabClient, endpoint),
            _ => MainUiAuthContext.None)
    {
    }

    public MainWindow(
        AppSettingsController settingsController,
        SitemapRuntimeController runtimeController,
        Action requestHideToTray)
        : this(
            settingsController,
            runtimeController,
            notificationStore: null,
            requestHideToTray,
            () => false,
            (transportKind, endpoint) => new OpenHabHttpClient(FallbackOpenHabClient, endpoint),
            _ => MainUiAuthContext.None)
    {
    }

    public MainWindow(
        AppSettingsController settingsController,
        SitemapRuntimeController runtimeController,
        NotificationStore? notificationStore,
        Action requestHideToTray,
        Func<bool>? shouldAllowClose,
        Func<TransportKind, Uri, IOpenHabClient> openHabClientFactory,
        Func<TransportKind, MainUiAuthContext>? mainUiAuthResolver = null)
    {
        this.settingsController = settingsController;
        this.runtimeController = runtimeController;
        this.notificationStore = notificationStore;
        this.requestHideToTray = requestHideToTray;
        this.shouldAllowClose = shouldAllowClose ?? (() => false);
        this.openHabClientFactory = openHabClientFactory;
        this.mainUiAuthResolver = mainUiAuthResolver ?? (_ => MainUiAuthContext.None);
        sitemapIconAuthResolver = new SitemapIconAuthResolver(settingsController);
        sitemapSurfaceRenderer = new SitemapSurfaceRenderer(
            settingsController,
            sitemapIconAuthResolver,
            activateByRowKey: OnRowActivatedByKeyAsync,
            navigateByRowKey: OnRowNavigateByKeyAsync,
            sendCommandByRowKey: SendCommandForRowKeyAsync);
        snapshotRefreshGate = new DispatcherRefreshGate(action => DispatcherQueue.TryEnqueue(() => action()));

        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        settingsController.SettingsChanged += OnSettingsChanged;
        uiSettings.ColorValuesChanged += OnColorValuesChanged;
        ApplyWindowTheme();
        shellController = new OpenHab.App.Shell.MainWindowShellController(settingsController.Current.MainWindowSitemapPaneVisible);
        shellController.Changed += (_, _) => ApplyMainWindowShellState();
        sitemapSearchDebounceTimer.Interval = TimeSpan.FromMilliseconds(150);
        sitemapSearchDebounceTimer.Tick += SitemapSearchDebounceTimer_Tick;
        SitemapSearchBox.GotFocus += (_, _) => isSitemapSearchBoxFocused = true;
        SitemapSearchBox.LostFocus += (_, _) => isSitemapSearchBoxFocused = false;
        promotedMainUiPages = settingsController.Current.CachedMainUiPageLinks;
        ApplyMainWindowShellState();
        SyncSidebarStateFromSettings();
        RefreshPromotedMainUiPagesList();

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "openhab-icon.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);

        AppWindow.Closing += AppWindow_Closing;
        this.Content.KeyDown += MainContent_KeyDown;
        this.Content.PointerPressed += MainContent_PointerPressed;
        runtimeController.SnapshotChanged += (_, _) =>
        {
            if (_suppressNextSnapshotRefresh)
            {
                _suppressNextSnapshotRefresh = false;
                return;
            }
            if (_isPageTransitionRunning)
            {
                _pendingSnapshotRefresh = true;
                return;
            }
            snapshotRefreshGate.Request(() => RefreshRuntimeBindings(targetRows: null));
        };
        _lastRefreshUseWindows11Icons = settingsController.Current.UseWindows11Icons;
        RefreshChromeBindings(runtimeController.Current);
        // Initial load is deferred until sitemaps are resolved in CompleteStartupAsync.
    }

    /// <summary>
    /// Positions the main window in the center of its current display's work area
    /// so it appears consistently regardless of screen resolution or monitor configuration.
    /// </summary>
    public void CenterOnCurrentScreen()
    {
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var size = AppWindow.Size;

        if (size.Width <= 0 || size.Height <= 0)
        {
            return; // Window hasn't been sized yet — let the OS default placement handle it.
        }

        var x = workArea.X + ((workArea.Width - size.Width) / 2);
        var y = workArea.Y + ((workArea.Height - size.Height) / 2);

        // Clamp so the window is never partially off-screen.
        x = Math.Max(workArea.X, x);
        y = Math.Max(workArea.Y, y);

        AppWindow.Move(new PointInt32(x, y));
    }

    public async Task LoadRuntimeAsync()
    {
        await RunRuntimeOperationAsync(ct => runtimeController.LoadAsync(ct));
    }

    public async Task<bool> RefreshRuntimeAsync()
    {
        return await RunRuntimeOperationAsync(ct => runtimeController.RefreshAsync(ct));
    }

    internal async Task<TraySurfaceRefreshOutcome> RefreshRuntimeForShellAsync()
    {
        if (!AppWindow.IsVisible)
        {
            return TraySurfaceRefreshOutcome.SkippedNotVisible;
        }

        if (isRefreshing)
        {
            return TraySurfaceRefreshOutcome.SkippedBusy;
        }

        isRefreshing = true;
        try
        {
            await runtimeController.RefreshAsync(CancellationToken.None);
            return TryRefreshRuntimeBindings()
                ? TraySurfaceRefreshOutcome.Applied
                : TraySurfaceRefreshOutcome.NoVisibleSurface;
        }
        catch (Exception ex)
        {
            ShellConnectionText.Text = $"Error: {ex.Message}";
            return TraySurfaceRefreshOutcome.Failed;
        }
        finally
        {
            isRefreshing = false;
        }
    }

    public void ShowNotificationsTab()
    {
        shellController.SelectCenterPage(MainWindowCenterPage.Notifications);
    }

    public void ShowSettingsTab()
    {
        shellController.SelectCenterPage(MainWindowCenterPage.Settings);
    }

    public void ReleaseSitemapVisualRows()
    {
        if (_isPageTransitionRunning)
        {
            return;
        }

        SitemapRows.Children.Clear();
        SitemapRowsB.Children.Clear();
        sitemapSurfaceRenderer.ForceFullRebuild();
    }

    public void ReleaseBackgroundResources()
    {
        ReleaseSitemapVisualRows();
        ReleaseMainUiHost();
    }

    private void ReleaseMainUiHost()
    {
        if (mainUiHost is null)
        {
            return;
        }

        mainUiHost.CurrentRouteChanged -= MainUiHost_CurrentRouteChanged;
        CenterContentHost.Children.Remove(mainUiHost);
        mainUiHost.Close();
        mainUiHost = null;
    }

    private void ShowNotificationsPage()
    {
        notificationsPage ??= new Notifications.NotificationsPageControl(settingsController, notificationStore);
        CenterContentHost.Children.Clear();
        CenterContentHost.Children.Add(notificationsPage);
    }

    private void ShowSettingsPage()
    {
        settingsPage ??= new Settings.SettingsPageControl(settingsController, RefreshRuntimeAsync, SetShellStatusText);
        settingsPage.ShowRoot();
        CenterContentHost.Children.Clear();
        CenterContentHost.Children.Add(settingsPage);
    }

    private void ApplyMainWindowShellState()
    {
        var state = shellController.Current;
        SetSitemapPaneVisibility(state.IsSitemapVisible, animate: hasAppliedInitialShellState);
        ToggleSitemapIcon.Foreground = state.IsSitemapVisible
            ? GetThemeBrush("AccentTextFillColorPrimaryBrush")
            : GetThemeBrush("TextFillColorPrimaryBrush");
        ToolTipService.SetToolTip(ToggleSitemapButton, state.IsSitemapVisible ? "Hide sitemap" : "Show sitemap");
        AutomationProperties.SetName(ToggleSitemapButton, state.IsSitemapVisible ? "Hide sitemap" : "Show sitemap");
        if (settingsController.Current.MainWindowSitemapPaneVisible != state.IsSitemapVisible)
        {
            settingsController.SetMainWindowSitemapPaneVisible(state.IsSitemapVisible);
        }

        SyncSidebarStateFromSettings();
        hasAppliedInitialShellState = true;

        if (state.CenterPage == MainWindowCenterPage.MainUi)
        {
            ShowMainUi();
            var targetRoute = !string.IsNullOrWhiteSpace(state.PendingMainUiRoute)
                ? state.PendingMainUiRoute
                : !string.IsNullOrWhiteSpace(currentMainUiRoute)
                    ? currentMainUiRoute
                    : MainUiHost.CurrentRoute;
            if (!string.IsNullOrWhiteSpace(targetRoute))
            {
                var normalizedRoute = NormalizeMainUiRoute(targetRoute);
                var isExplicitRouteRequest = !string.IsNullOrWhiteSpace(state.PendingMainUiRoute);
                var activeTransport = GetPreferredMainUiTransport();
                if (!string.Equals(currentMainUiRoute, normalizedRoute, StringComparison.Ordinal)
                    || currentMainUiTransport != activeTransport)
                {
                    _ = NavigateMainUiAsync(normalizedRoute, isExplicitRouteRequest);
                }
            }
        }
        else if (state.CenterPage == MainWindowCenterPage.Notifications)
        {
            ShowNotificationsPage();
        }
        else if (state.CenterPage == MainWindowCenterPage.Settings)
        {
            ShowSettingsPage();
        }
    }

    private void ShowMainUi()
    {
        var host = MainUiHost;
        if (!CenterContentHost.Children.Contains(host))
        {
            CenterContentHost.Children.Clear();
            CenterContentHost.Children.Add(host);
        }
    }

    private MainUiWebViewHost CreateMainUiHost()
    {
        var host = new MainUiWebViewHost();
        host.CurrentRouteChanged += MainUiHost_CurrentRouteChanged;
        return host;
    }

    private void MainUiHost_CurrentRouteChanged(object? sender, string route)
    {
        var normalizedRoute = NormalizeMainUiRoute(route);
        currentMainUiRoute = normalizedRoute;
        if (!string.IsNullOrWhiteSpace(pendingExplicitMainUiRoute)
            && !string.Equals(pendingExplicitMainUiRoute, normalizedRoute, StringComparison.Ordinal))
        {
            return;
        }

        shellController.SyncCurrentMainUiRoute(normalizedRoute);
    }

    private async Task NavigateMainUiAsync(string route, bool isExplicitRouteRequest = false)
    {
        var normalizedRoute = NormalizeMainUiRoute(route);
        if (isMainUiNavigationInProgress)
        {
            if (!isExplicitRouteRequest && !string.IsNullOrWhiteSpace(pendingExplicitMainUiRoute))
            {
                return;
            }

            pendingExplicitMainUiRoute = normalizedRoute;
            return;
        }

        isMainUiNavigationInProgress = true;
        var settings = settingsController.Current;
        var selectedTransport = GetPreferredMainUiTransport();
        activeMainUiNavigationTransport = selectedTransport;
        var endpoint = selectedTransport == TransportKind.Cloud
            ? settings.CloudEndpoint
            : settings.LocalEndpoint;
        var authContext = mainUiAuthResolver(selectedTransport);
        string? followUpRoute = null;
        var runTransportResync = false;
        try
        {
            await MainUiHost.NavigateAsync(endpoint, normalizedRoute, authContext);
            currentMainUiRoute = MainUiHost.CurrentRoute;
            currentMainUiTransport = selectedTransport;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Main UI navigation failed: {ex.GetType().Name}");
            ShellConnectionText.Text = "Error: Main UI could not be loaded.";
        }
        finally
        {
            activeMainUiNavigationTransport = null;
            isMainUiNavigationInProgress = false;

            var queuedRoute = pendingExplicitMainUiRoute;
            if (!string.IsNullOrWhiteSpace(queuedRoute))
            {
                pendingExplicitMainUiRoute = null;
                var currentHostRoute = NormalizeMainUiRoute(MainUiHost.CurrentRoute);
                if (!string.Equals(currentHostRoute, queuedRoute, StringComparison.Ordinal))
                {
                    followUpRoute = queuedRoute;
                }
                else
                {
                    currentMainUiRoute = currentHostRoute;
                    shellController.SyncCurrentMainUiRoute(currentHostRoute);
                }
            }

            if (pendingMainUiTransportResync)
            {
                pendingMainUiTransportResync = false;
                runTransportResync = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(followUpRoute))
        {
            _ = NavigateMainUiAsync(followUpRoute, isExplicitRouteRequest: true);
            return;
        }

        if (runTransportResync)
        {
            EnsureMainUiEndpointMatchesActiveTransport(runtimeController.Current);
        }
    }

    private static string NormalizeMainUiRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return "/";
        }

        var normalized = route.Trim();
        return normalized.StartsWith("/", StringComparison.Ordinal) ? normalized : "/" + normalized;
    }

    private TransportKind GetPreferredMainUiTransport()
    {
        return GetPreferredMainUiTransport(runtimeController.Current);
    }

    private TransportKind GetPreferredMainUiTransport(SitemapRuntimeSnapshot snapshot)
    {
        return snapshot.ActiveTransport switch
        {
            TransportKind.Cloud => TransportKind.Cloud,
            TransportKind.Local => TransportKind.Local,
            _ => settingsController.Current.EndpointMode == EndpointMode.CloudOnly ? TransportKind.Cloud : TransportKind.Local
        };
    }

    public async Task RefreshPromotedMainUiPagesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = settingsController.Current;
            var selectedTransport = runtimeController.Current.ActiveTransport switch
            {
                TransportKind.Cloud => TransportKind.Cloud,
                TransportKind.Local => TransportKind.Local,
                _ => settings.EndpointMode == EndpointMode.CloudOnly ? TransportKind.Cloud : TransportKind.Local
            };
            var endpoint = selectedTransport == TransportKind.Cloud
                ? settings.CloudEndpoint
                : settings.LocalEndpoint;
            var client = openHabClientFactory(selectedTransport, endpoint);
            var discoveryService = new MainUiPageDiscoveryService(client);
            promotedMainUiPages = await discoveryService.DiscoverPromotedLinksAsync(cancellationToken);
            settingsController.SetCachedMainUiPageLinks(promotedMainUiPages);
            if (promotedMainUiPages.Count > 0 && !settingsController.Current.MainUiPagesExpanded)
            {
                settingsController.SetMainUiPagesExpanded(true);
            }

            RefreshPromotedMainUiPagesList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Main UI page discovery failed: {ex.GetType().Name}");
            promotedMainUiPages = settingsController.Current.CachedMainUiPageLinks;
            RefreshPromotedMainUiPagesList(discoveryError: true);
        }
    }

    private void RefreshPromotedMainUiPagesList(bool discoveryError = false)
    {
        var isExpanded = settingsController.Current.MainUiPagesExpanded;
        MainUiPagesList.Children.Clear();
        ApplyMainUiPagesVisibilityState();
        if (isSidebarCollapsed || !isExpanded)
        {
            return;
        }

        if (discoveryError)
        {
            MainUiPagesList.Children.Add(new TextBlock
            {
                Text = "Could not refresh pages. Showing cached links.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
        }

        var linksToRender = promotedMainUiPages.Count > 0
            ? promotedMainUiPages
            : settingsController.Current.CachedMainUiPageLinks;
        if (linksToRender.Count == 0)
        {
            MainUiPagesList.Children.Add(new TextBlock
            {
                Text = "No promoted pages"
            });
            return;
        }

        foreach (var page in linksToRender)
        {
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = page.Label,
                Tag = page.Route
            };
            button.Click += PromotedMainUiPageButton_Click;
            MainUiPagesList.Children.Add(button);
        }
    }

    private void PromotedMainUiPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string route } && !string.IsNullOrWhiteSpace(route))
        {
            shellController.SelectPromotedMainUiPage(route);
        }
    }

    private async Task<bool> RunRuntimeOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (isRefreshing)
        {
            return false;
        }

        isRefreshing = true;
        try
        {
            await operation(CancellationToken.None);
            return TryRefreshRuntimeBindings();
        }
        catch (Exception ex)
        {
            ShellConnectionText.Text = $"Error: {ex.Message}";
            return false;
        }
        finally
        {
            isRefreshing = false;
        }
    }

    internal void RefreshRuntimeBindings(StackPanel? targetRows = null, bool animateStructuralInsertions = true)
    {
        _ = TryRefreshRuntimeBindings(targetRows, animateStructuralInsertions);
    }

    internal bool TryRefreshRuntimeBindings(StackPanel? targetRows = null, bool animateStructuralInsertions = true)
    {
        if (targetRows is null && !AppWindow.IsVisible)
        {
            return false;
        }

        var rowsPanel = targetRows ?? ActiveRows;
        var snapshot = runtimeController.Current;
        RefreshChromeBindings(snapshot);
        EnsureMainUiEndpointMatchesActiveTransport(snapshot);
        if (ShouldSkipStaleSearchRender(snapshot))
        {
            snapshotRefreshGate.Drain(() => RefreshRuntimeBindings(targetRows: null));
            return true;
        }

        sitemapSurfaceRenderer.Refresh(rowsPanel, snapshot, animateStructuralInsertions);
        snapshotRefreshGate.Drain(() => RefreshRuntimeBindings(targetRows: null));
        return true;
    }

    private bool ShouldSkipStaleSearchRender(SitemapRuntimeSnapshot snapshot)
    {
        return snapshot.IsSearchActive &&
               (isSitemapSearchBoxFocused || sitemapSearchDebounceTimer.IsEnabled) &&
               !string.Equals(SitemapSearchBox.Text, snapshot.SearchQuery, StringComparison.Ordinal);
    }

    private void EnsureMainUiEndpointMatchesActiveTransport(SitemapRuntimeSnapshot snapshot)
    {
        if (shellController.Current.CenterPage != MainWindowCenterPage.MainUi)
        {
            return;
        }

        var desiredTransport = GetPreferredMainUiTransport(snapshot);
        if (isMainUiNavigationInProgress)
        {
            var inFlightTransport = activeMainUiNavigationTransport ?? currentMainUiTransport;
            if (inFlightTransport != desiredTransport)
            {
                pendingMainUiTransportResync = true;
            }
            return;
        }

        if (currentMainUiTransport == desiredTransport)
        {
            currentMainUiRoute = mainUiHost?.CurrentRoute ?? "/";
            return;
        }

        var route = mainUiHost?.CurrentRoute ?? "/";
        _ = NavigateMainUiAsync(route);
    }

    private async Task OnRowActivatedAsync(int rowIndex)
    {
        if (isRefreshing)
        {
            return;
        }

        await RunRuntimeOperationAsync(async ct =>
        {
            await runtimeController.ActivateRowAsync(rowIndex, ct);
        });
    }

    private async Task OnRowActivatedByKeyAsync(string rowKey)
    {
        if (isRefreshing)
        {
            return;
        }

        await RunRuntimeOperationAsync(ct => runtimeController.ActivateRowByKeyAsync(rowKey, ct));
    }

    private async Task OnRowNavigateAsync(int rowIndex)
    {
        if (isRefreshing) return;
        await RunNavigateTransitionAsync(ct => runtimeController.NavigateToChildAsync(rowIndex, ct));
    }

    private async Task RunNavigateTransitionAsync(Func<CancellationToken, Task<bool>> navigateAsync)
    {
        isRefreshing = true;
        _isPageTransitionRunning = true;
        try
        {
            _suppressNextSnapshotRefresh = true;
            if (!await navigateAsync(CancellationToken.None))
            {
                _suppressNextSnapshotRefresh = false;
                return;
            }
            isSearchChromeOpen = false;

            InactiveSlotContainer.Visibility = Visibility.Visible;
            RefreshRuntimeBindings(InactiveRows, animateStructuralInsertions: false);

            await AnimatePageTransitionOverlapAsync(NavigationDirection.Forward);

            ActiveRows.Children.Clear();
            ActiveSlotContainer.Visibility = Visibility.Collapsed;
            _activeSlotIsA = !_activeSlotIsA;
        }
        catch (Exception ex)
        {
            ShellConnectionText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _isPageTransitionRunning = false;
            if (_pendingSnapshotRefresh)
            {
                _pendingSnapshotRefresh = false;
                RefreshRuntimeBindings(targetRows: null);
            }
            isRefreshing = false;
        }
    }

    private async Task OnRowNavigateByKeyAsync(string rowKey)
    {
        if (isRefreshing)
        {
            return;
        }

        if (runtimeController.Current.IsSearchActive)
        {
            await RunRuntimeOperationAsync(ct => runtimeController.NavigateRowByKeyAsync(rowKey, ct));
            isSearchChromeOpen = runtimeController.Current.IsSearchActive;
            RefreshRuntimeBindings(ActiveRows);
            return;
        }

        await RunNavigateTransitionAsync(ct => runtimeController.NavigateRowByKeyAsync(rowKey, ct));
    }

    private Task SendCommandForRowKeyAsync(string rowKey, string command)
    {
        return runtimeController.SendCommandForRowKeyAsync(rowKey, command);
    }

    public void PopulateSitemaps(IReadOnlyList<SitemapInfo> sitemaps)
    {
        SitemapMenuFlyout.Items.Clear();
        foreach (var s in sitemaps)
        {
            var item = new MenuFlyoutItem { Text = s.Label, Tag = s.Name };
            item.Click += SitemapMenuItem_Click;
            SitemapMenuFlyout.Items.Add(item);
        }
    }

    private async void SitemapMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (isRefreshing)
        {
            return;
        }

        if (sender is MenuFlyoutItem item && item.Tag is string sitemapName)
        {
            if (HasVisibleSearchChrome)
            {
                CloseSearchChrome();
            }

            settingsController.SetSitemapName(sitemapName);
            await LoadRuntimeAsync();
        }
    }

    private void SitemapHeaderArea_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            ShowSitemapMenuAt(element);
        }
    }

    private void MainContent_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && HasVisibleSearchChrome)
        {
            CloseSearchChrome();
            e.Handled = true;
            return;
        }

        if (e.Key != VirtualKey.GoBack)
        {
            return;
        }

        if (HasVisibleSearchChrome && !isRefreshing)
        {
            CloseSearchChrome();
            e.Handled = true;
            return;
        }

        if (TryNavigateMainUiBack())
        {
            e.Handled = true;
            return;
        }

        if (runtimeController.CanGoBack && !isRefreshing)
        {
            e.Handled = true;
            _ = NavigateBackWithAnimationAsync();
        }
    }

    private void MainContent_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(sender as UIElement).Properties;
        if (!props.IsXButton1Pressed)
        {
            return;
        }

        if (HasVisibleSearchChrome && !isRefreshing)
        {
            CloseSearchChrome();
            e.Handled = true;
            return;
        }

        if (TryNavigateMainUiBack())
        {
            e.Handled = true;
            return;
        }

        if (runtimeController.CanGoBack && !isRefreshing)
        {
            e.Handled = true;
            _ = NavigateBackWithAnimationAsync();
        }
    }

    private bool TryNavigateMainUiBack()
    {
        if (shellController.Current.CenterPage != MainWindowCenterPage.MainUi)
        {
            return false;
        }

        if (mainUiHost?.CanGoBack != true)
        {
            return false;
        }

        mainUiHost.GoBack();
        return true;
    }

    private async Task NavigateBackWithAnimationAsync()
    {
        if (!runtimeController.CanGoBack || isRefreshing) return;
        isRefreshing = true;
        _isPageTransitionRunning = true;
        try
        {
            _suppressNextSnapshotRefresh = true;
            runtimeController.NavigateBack();
            isSearchChromeOpen = false;

            InactiveSlotContainer.Visibility = Visibility.Visible;
            RefreshRuntimeBindings(InactiveRows, animateStructuralInsertions: false);

            await AnimatePageTransitionOverlapAsync(NavigationDirection.Back);

            ActiveRows.Children.Clear();
            ActiveSlotContainer.Visibility = Visibility.Collapsed;
            _activeSlotIsA = !_activeSlotIsA;
        }
        finally
        {
            _isPageTransitionRunning = false;
            if (_pendingSnapshotRefresh)
            {
                _pendingSnapshotRefresh = false;
                RefreshRuntimeBindings(targetRows: null);
            }
            isRefreshing = false;
        }
    }

    private void ShowSitemapMenuAt(FrameworkElement target)
    {
        if (SitemapMenuFlyout.Items.Count == 0)
        {
            return;
        }

        SitemapMenuFlyout.ShowAt(target);
    }

    private bool HasVisibleSearchChrome => isSearchChromeOpen || runtimeController.Current.IsSearchActive;

    private void CloseSearchChrome()
    {
        sitemapSearchDebounceTimer.Stop();
        isSearchChromeOpen = false;
        runtimeController.ClearSearch();
        RefreshChromeBindings(runtimeController.Current);
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (HasVisibleSearchChrome)
        {
            CloseSearchChrome();
            return;
        }

        isSearchChromeOpen = true;
        sitemapSearchDebounceTimer.Stop();
        await ApplySitemapSearchQueryAsync(SitemapSearchBox.Text);
        RefreshChromeBindings(runtimeController.Current);
        SitemapSearchBox.Focus(FocusState.Programmatic);
    }

    private void SitemapSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (isUpdatingSearchBox || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        pendingSitemapSearchQuery = sender.Text;
        isSearchChromeOpen = true;
        sitemapSearchDebounceTimer.Stop();
        sitemapSearchDebounceTimer.Start();
    }

    private async void SitemapSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        sitemapSearchDebounceTimer.Stop();
        await ApplySitemapSearchQueryAsync(sender.Text);
        isSearchChromeOpen = true;
    }

    private async void SitemapSearchDebounceTimer_Tick(object? sender, object e)
    {
        sitemapSearchDebounceTimer.Stop();
        await ApplySitemapSearchQueryAsync(pendingSitemapSearchQuery);
    }

    private async Task ApplySitemapSearchQueryAsync(string query)
    {
        try
        {
            await runtimeController.ApplySearchQueryAsync(query);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Main window sitemap search failed: {ex.Message}");
        }
    }

    private async void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (isRefreshing)
        {
            return;
        }

        var previousDepth = runtimeController.Current.Breadcrumbs.Count;
        if (!runtimeController.NavigateToBreadcrumb(args.Index))
        {
            return;
        }

        isSearchChromeOpen = false;
        isRefreshing = true;
        _isPageTransitionRunning = true;
        try
        {
            var currentDepth = runtimeController.Current.Breadcrumbs.Count;
            if (currentDepth == previousDepth)
            {
                _isPageTransitionRunning = false;
                RefreshRuntimeBindings(ActiveRows);
                RefreshChromeBindings(runtimeController.Current);
                return;
            }

            _suppressNextSnapshotRefresh = true;
            InactiveSlotContainer.Visibility = Visibility.Visible;
            RefreshRuntimeBindings(InactiveRows, animateStructuralInsertions: false);
            RefreshChromeBindings(runtimeController.Current);

            await AnimatePageTransitionOverlapAsync(NavigationDirection.Back);

            ActiveRows.Children.Clear();
            ActiveSlotContainer.Visibility = Visibility.Collapsed;
            _activeSlotIsA = !_activeSlotIsA;
        }
        finally
        {
            _isPageTransitionRunning = false;
            if (_pendingSnapshotRefresh)
            {
                _pendingSnapshotRefresh = false;
                RefreshRuntimeBindings(targetRows: null);
            }
            isRefreshing = false;
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (shouldAllowClose())
        {
            return;
        }

        if (isHandlingCloseRequest)
        {
            return;
        }

        args.Cancel = true;
        isHandlingCloseRequest = true;
        try
        {
            requestHideToTray();
        }
        finally
        {
            isHandlingCloseRequest = false;
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        var useWindows11Icons = settingsController.Current.UseWindows11Icons;
        if (useWindows11Icons != _lastRefreshUseWindows11Icons)
        {
            sitemapSurfaceRenderer.ForceFullRebuild();
            _lastRefreshUseWindows11Icons = useWindows11Icons;
        }

        await RefreshRuntimeAsync();
    }

    private void HomeNavButton_Click(object sender, RoutedEventArgs e)
    {
        shellController.SelectPromotedMainUiPage("/");
    }

    private void HomePagesToggleButton_Click(object sender, RoutedEventArgs e)
    {
        settingsController.SetMainUiPagesExpanded(!settingsController.Current.MainUiPagesExpanded);
        RefreshPromotedMainUiPagesList();
    }

    private void NotificationsNavButton_Click(object sender, RoutedEventArgs e)
    {
        shellController.SelectCenterPage(MainWindowCenterPage.Notifications);
    }

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
    {
        shellController.SelectCenterPage(MainWindowCenterPage.Settings);
    }

    private void ToggleSitemapButton_Click(object sender, RoutedEventArgs e)
    {
        shellController.SetSitemapVisible(!shellController.Current.IsSitemapVisible);
    }

    private void SidebarCollapseButton_Click(object sender, RoutedEventArgs e)
    {
        isSidebarCollapsed = !isSidebarCollapsed;
        settingsController.SetMainWindowSidebarCollapsed(isSidebarCollapsed);
        ApplySidebarState(animate: true);
    }

    private void ApplySidebarState(bool animate = false)
    {
        var targetWidth = isSidebarCollapsed ? CollapsedSidebarWidth : ExpandedSidebarWidth;
        if (animate)
        {
            AnimateSidebarWidth(targetWidth);
        }
        else
        {
            sidebarWidthAnimationTimer?.Stop();
            sidebarWidthAnimationTimer = null;
            SidebarColumn.Width = new GridLength(targetWidth);
        }

        SidebarLayoutRoot.Padding = isSidebarCollapsed ? new Thickness(8, 18, 8, 12) : new Thickness(12, 18, 12, 12);
        SidebarBrandTextPanel.Visibility = isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        HomeNavText.Visibility = isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        NotificationsNavText.Visibility = isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SettingsNavText.Visibility = isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarConnectionPanel.Visibility = isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ApplyMainUiPagesVisibilityState();

        Grid.SetColumnSpan(HomeNavButton, isSidebarCollapsed ? 2 : 1);
        HomeNavButton.Padding = isSidebarCollapsed ? new Thickness(0) : new Thickness(10, 0, 10, 0);
        NotificationsNavButton.Padding = isSidebarCollapsed ? new Thickness(0) : new Thickness(10, 0, 10, 0);
        SettingsNavButton.Padding = isSidebarCollapsed ? new Thickness(0) : new Thickness(10, 0, 10, 0);
        HomeNavButton.HorizontalContentAlignment = isSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        NotificationsNavButton.HorizontalContentAlignment = isSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        SettingsNavButton.HorizontalContentAlignment = isSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;

        SidebarBrandPanel.Margin = isSidebarCollapsed ? new Thickness(0, 0, 0, 14) : new Thickness(4, 0, 0, 14);
        SidebarBrandPanel.HorizontalAlignment = isSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        SidebarCollapseIcon.Glyph = isSidebarCollapsed ? "\uE970" : "\uE96F";
        ToolTipService.SetToolTip(SidebarCollapseButton, isSidebarCollapsed ? "Expand navigation" : "Collapse navigation");
        AutomationProperties.SetName(SidebarCollapseButton, isSidebarCollapsed ? "Expand navigation" : "Collapse navigation");
    }

    private void ApplyMainUiPagesVisibilityState()
    {
        var isExpanded = settingsController.Current.MainUiPagesExpanded;
        MainUiPagesChevron.Glyph = isExpanded ? "\uE70E" : "\uE70D";
        HomePagesToggleButton.Visibility = isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        MainUiPagesList.Visibility = isExpanded && !isSidebarCollapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SyncSidebarStateFromSettings()
    {
        isSidebarCollapsed = settingsController.Current.MainWindowSidebarCollapsed;
        ApplySidebarState();
    }

    private void SetSitemapPaneVisibility(bool isVisible, bool animate)
    {
        var targetWidth = isVisible ? ExpandedSitemapPaneWidth : 0d;
        var currentWidth = GetCurrentColumnWidth(SitemapPaneColumn);
        if (!animate || Math.Abs(currentWidth - targetWidth) < 0.5d)
        {
            sitemapPaneWidthAnimationTimer?.Stop();
            sitemapPaneWidthAnimationTimer = null;
            SitemapPaneColumn.Width = new GridLength(targetWidth);
            SitemapContentRoot.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            SitemapContentRoot.Opacity = 1d;
            UpdateSitemapPaneClip();
            return;
        }

        AnimateSitemapPaneWidth(targetWidth, isVisible);
    }

    private void AnimateSidebarWidth(double targetWidth)
    {
        sidebarWidthAnimationTimer?.Stop();
        var startWidth = GetCurrentColumnWidth(SidebarColumn);
        if (Math.Abs(startWidth - targetWidth) < 0.5d)
        {
            SidebarColumn.Width = new GridLength(targetWidth);
            return;
        }

        var started = DateTimeOffset.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        sidebarWidthAnimationTimer = timer;
        timer.Tick += (_, _) =>
        {
            var progress = Math.Clamp((DateTimeOffset.UtcNow - started).TotalMilliseconds / ShellChromeAnimationDurationMs, 0d, 1d);
            var width = Lerp(startWidth, targetWidth, EaseOutCubic(progress));
            SidebarColumn.Width = new GridLength(width);
            if (progress < 1d)
            {
                return;
            }

            timer.Stop();
            if (ReferenceEquals(sidebarWidthAnimationTimer, timer))
            {
                sidebarWidthAnimationTimer = null;
            }

            SidebarColumn.Width = new GridLength(targetWidth);
        };
        timer.Start();
    }

    private void AnimateSitemapPaneWidth(double targetWidth, bool targetVisible)
    {
        sitemapPaneWidthAnimationTimer?.Stop();
        var startWidth = GetCurrentColumnWidth(SitemapPaneColumn);
        SitemapContentRoot.Visibility = Visibility.Visible;
        UpdateSitemapPaneClip();

        var started = DateTimeOffset.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        sitemapPaneWidthAnimationTimer = timer;
        timer.Tick += (_, _) =>
        {
            var progress = Math.Clamp((DateTimeOffset.UtcNow - started).TotalMilliseconds / ShellChromeAnimationDurationMs, 0d, 1d);
            var width = Lerp(startWidth, targetWidth, EaseOutCubic(progress));
            SitemapPaneColumn.Width = new GridLength(width);
            SitemapContentRoot.Opacity = targetVisible
                ? Math.Clamp(width / ExpandedSitemapPaneWidth, 0.25d, 1d)
                : Math.Clamp(width / Math.Max(startWidth, 1d), 0d, 1d);
            UpdateSitemapPaneClip();
            if (progress < 1d)
            {
                return;
            }

            timer.Stop();
            if (ReferenceEquals(sitemapPaneWidthAnimationTimer, timer))
            {
                sitemapPaneWidthAnimationTimer = null;
            }

            SitemapPaneColumn.Width = new GridLength(targetWidth);
            SitemapContentRoot.Opacity = 1d;
            SitemapContentRoot.Visibility = targetVisible ? Visibility.Visible : Visibility.Collapsed;
            UpdateSitemapPaneClip();
        };
        timer.Start();
    }

    private void SitemapContentRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSitemapPaneClip();
    }

    private void UpdateSitemapPaneClip()
    {
        var width = SitemapPaneColumn.Width.IsAbsolute
            ? SitemapPaneColumn.Width.Value
            : SitemapContentRoot.ActualWidth;
        var height = SitemapContentRoot.ActualHeight;
        SitemapContentRoot.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, Math.Max(0d, width), Math.Max(0d, height))
        };
    }

    private static double GetCurrentColumnWidth(ColumnDefinition column)
    {
        if (column.Width.IsAbsolute)
        {
            return column.Width.Value;
        }

        return Math.Max(0d, column.ActualWidth);
    }

    private static double Lerp(double start, double end, double progress)
    {
        return start + ((end - start) * progress);
    }

    private static double EaseOutCubic(double progress)
    {
        var inverse = 1d - progress;
        return 1d - (inverse * inverse * inverse);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            ApplyWindowTheme();
            sitemapSurfaceRenderer.ForceFullRebuild();
            RefreshRuntimeBindings();
        });
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        _ = DispatcherQueue.TryEnqueue(ApplyWindowTheme);
    }

    private void ApplyWindowTheme()
    {
        var theme = DwmWindowDecorations.ResolveFlyoutTheme(
            settingsController.Current.AppColorTheme,
            IsSystemBackgroundDark());

        if (Content is FrameworkElement contentRoot)
        {
            contentRoot.RequestedTheme = theme == FlyoutTheme.Dark
                ? ElementTheme.Dark
                : ElementTheme.Light;
        }

        ShellRoot.Background = new SolidColorBrush(theme == FlyoutTheme.Dark
            ? Color.FromArgb(255, 32, 32, 32)
            : Color.FromArgb(255, 250, 250, 250));

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        DwmWindowDecorations.TryApply(hwnd, theme);
        ApplyTitleBarStyle(theme);
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            DwmWindowDecorations.TryApply(hwnd, theme);
            ApplyTitleBarStyle(theme);
        });
    }

    private void ApplyTitleBarStyle(FlyoutTheme theme)
    {
        var titleBar = AppWindow.TitleBar;
        var isDark = theme == FlyoutTheme.Dark;
        var background = isDark
            ? Color.FromArgb(255, 28, 28, 28)
            : Color.FromArgb(255, 250, 250, 250);
        var foreground = isDark
            ? Color.FromArgb(255, 255, 255, 255)
            : Color.FromArgb(255, 32, 32, 32);
        var hover = isDark
            ? Color.FromArgb(255, 44, 44, 44)
            : Color.FromArgb(255, 232, 232, 232);
        var pressed = isDark
            ? Color.FromArgb(255, 58, 58, 58)
            : Color.FromArgb(255, 218, 218, 218);

        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = background;
        titleBar.InactiveForegroundColor = foreground;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonInactiveForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    private bool IsSystemBackgroundDark()
    {
        var background = uiSettings.GetColorValue(UIColorType.Background);
        return IsDark(background);
    }

    private static bool IsDark(Color color)
    {
        var brightness = ((color.R * 299) + (color.G * 587) + (color.B * 114)) / 1000;
        return brightness < 128;
    }

    public void SetShellStatusText(string text)
    {
        ShellStatusText.Text = text;
        ShellStatusText.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Brush GetThemeBrush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var resource) && resource is Brush brush)
        {
            return brush;
        }

        if (Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out resource) && resource is Brush fallback)
        {
            return fallback;
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Black);
    }

    /// <summary>Updates header chrome independently of sitemap rows.</summary>
    private void RefreshChromeBindings(SitemapRuntimeSnapshot snapshot)
    {
        var chrome = SitemapChromeStateBuilder.Build(
            snapshot,
            settingsController.Current.SitemapName,
            isSearchChromeOpen);

        SitemapTitleText.Text = chrome.Title;
        SitemapStatusText.Text = chrome.StatusText;
        BreadcrumbBar.ItemsSource = chrome.Breadcrumbs
            .Select((label, index) => index == 0
                ? BreadcrumbDisplayItem.CreateHomeIcon()
                : BreadcrumbDisplayItem.CreateText(label))
            .ToList();
        BreadcrumbBar.Visibility = chrome.ShowBreadcrumbs
            ? Visibility.Visible
            : Visibility.Collapsed;
        SitemapSearchBox.Visibility = chrome.ShowSearch
            ? Visibility.Visible
            : Visibility.Collapsed;
        SearchButtonIcon.Foreground = chrome.ShowSearch
            ? GetThemeBrush("AccentTextFillColorPrimaryBrush")
            : GetThemeBrush("TextFillColorPrimaryBrush");

        if (!isUpdatingSearchBox &&
            !sitemapSearchDebounceTimer.IsEnabled &&
            !isSitemapSearchBoxFocused &&
            SitemapSearchBox.Text != chrome.SearchText)
        {
            isUpdatingSearchBox = true;
            SitemapSearchBox.Text = chrome.SearchText;
            isUpdatingSearchBox = false;
        }

        ShellConnectionText.Text = snapshot.ActiveTransport switch
        {
            TransportKind.Cloud => $"Connected via cloud ({snapshot.ConnectionState})",
            TransportKind.Local => $"Connected via local ({snapshot.ConnectionState})",
            _ => $"Connection: {snapshot.ConnectionState}"
        };
    }

    public sealed record BreadcrumbDisplayItem(string Label, FontFamily FontFamily, double FontSize)
    {
        public static BreadcrumbDisplayItem CreateHomeIcon() =>
            new("\uEA8A", new FontFamily("Segoe MDL2 Assets"), 18);

        public static BreadcrumbDisplayItem CreateText(string label) =>
            new(label, new FontFamily("Segoe UI"), 14);
    }

    /// <summary>
    /// Slides the active slot out and the inactive slot in simultaneously,
    /// matching the Android openHAB horizontal-push transition.
    /// </summary>
    private async Task AnimatePageTransitionOverlapAsync(NavigationDirection direction)
    {
        var durationMs = SitemapPageTransitionAnimator.ResolveDurationMs(settingsController.GetFlyoutAnimationDurationMs());
        await SitemapPageTransitionAnimator.AnimateOverlapAsync(
            SitemapContentRoot,
            ActiveSlotContainer,
            InactiveSlotContainer,
            direction,
            durationMs);
    }
}
