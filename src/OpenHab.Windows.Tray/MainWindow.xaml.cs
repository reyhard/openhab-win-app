
using System;
using System.IO;
using System.Threading;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.System;
using OpenHab.App.Notifications;
using OpenHab.App.Runtime;
using OpenHab.App.Shell;
using OpenHab.App.Settings;
using OpenHab.Core.Api;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using OpenHab.Windows.Tray.Rendering;
using OpenHab.Windows.Tray.Rendering.SitemapSurface;
namespace OpenHab.Windows.Tray;

public sealed partial class MainWindow : Window
{
    private readonly AppSettingsController settingsController;
    private readonly SitemapRuntimeController runtimeController;
    private readonly OpenHab.App.Shell.MainWindowShellController shellController;
    private readonly NotificationStore? notificationStore;
    private readonly Action requestHideToTray;
    private readonly SitemapIconAuthResolver sitemapIconAuthResolver;
    private readonly SitemapSurfaceRenderer sitemapSurfaceRenderer;
    private readonly DispatcherRefreshGate snapshotRefreshGate;
    private IReadOnlyList<OpenHab.App.MainUi.MainUiPageLink> promotedMainUiPages = [];
    private bool isRefreshing;
    private bool isHandlingCloseRequest;
    private bool _activeSlotIsA = true;
    private bool _suppressNextSnapshotRefresh;
    private bool _isPageTransitionRunning;
    private bool _pendingSnapshotRefresh;
    private bool _lastRefreshUseWindows11Icons;
    private string? currentMainUiRoute;
    private TransportKind? currentMainUiTransport;
    private bool isMainUiNavigationInProgress;
    private TransportKind? activeMainUiNavigationTransport;
    private bool pendingMainUiTransportResync;
    private string? pendingExplicitMainUiRoute;

    private Notifications.NotificationsPageControl? notificationsPage;
    private Settings.SettingsPageControl? settingsPage;

    private StackPanel ActiveRows => _activeSlotIsA ? SitemapRows : SitemapRowsB;
    private StackPanel InactiveRows => _activeSlotIsA ? SitemapRowsB : SitemapRows;
    private Grid ActiveSlotContainer => _activeSlotIsA ? SitemapPageSlotA : SitemapPageSlotB;
    private Grid InactiveSlotContainer => _activeSlotIsA ? SitemapPageSlotB : SitemapPageSlotA;


    public MainWindow(AppSettingsController settingsController, SitemapRuntimeController runtimeController)
        : this(settingsController, runtimeController, notificationStore: null, () => { })
    {
    }

    public MainWindow(
        AppSettingsController settingsController,
        SitemapRuntimeController runtimeController,
        Action requestHideToTray)
        : this(settingsController, runtimeController, notificationStore: null, requestHideToTray)
    {
    }

    public MainWindow(
        AppSettingsController settingsController,
        SitemapRuntimeController runtimeController,
        NotificationStore? notificationStore,
        Action requestHideToTray)
    {
        this.settingsController = settingsController;
        this.runtimeController = runtimeController;
        this.notificationStore = notificationStore;
        this.requestHideToTray = requestHideToTray;
        sitemapIconAuthResolver = new SitemapIconAuthResolver(settingsController);
        sitemapSurfaceRenderer = new SitemapSurfaceRenderer(
            settingsController,
            sitemapIconAuthResolver,
            activateByRowKey: OnRowActivatedByKeyAsync,
            navigateByRowKey: OnRowNavigateByKeyAsync,
            sendCommandByRowKey: SendCommandForRowKeyAsync);
        snapshotRefreshGate = new DispatcherRefreshGate(action => DispatcherQueue.TryEnqueue(() => action()));

        InitializeComponent();
        MainUiHost.CurrentRouteChanged += MainUiHost_CurrentRouteChanged;
        shellController = new OpenHab.App.Shell.MainWindowShellController(settingsController.Current.MainWindowSitemapPaneVisible);
        shellController.Changed += (_, _) => ApplyMainWindowShellState();
        promotedMainUiPages = settingsController.Current.CachedMainUiPageLinks;
        ApplyMainWindowShellState();
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

    public async Task RefreshRuntimeAsync()
    {
        await RunRuntimeOperationAsync(ct => runtimeController.RefreshAsync(ct));
    }

    public void ShowNotificationsTab()
    {
        shellController.SelectCenterPage(MainWindowCenterPage.Notifications);
    }

    public void ShowSettingsTab()
    {
        shellController.SelectCenterPage(MainWindowCenterPage.Settings);
    }

    private void ShowNotificationsPage()
    {
        notificationsPage ??= new Notifications.NotificationsPageControl(settingsController, notificationStore);
        CenterContentHost.Children.Clear();
        CenterContentHost.Children.Add(notificationsPage);
    }

    private void ShowSettingsPage()
    {
        settingsPage ??= new Settings.SettingsPageControl(settingsController, RefreshRuntimeAsync, text => StatusText.Text = text);
        settingsPage.ShowRoot();
        CenterContentHost.Children.Clear();
        CenterContentHost.Children.Add(settingsPage);
    }

    private void ApplyMainWindowShellState()
    {
        var state = shellController.Current;
        SitemapPaneColumn.Width = state.IsSitemapVisible ? new GridLength(380) : new GridLength(0);
        SitemapContentRoot.Visibility = state.IsSitemapVisible ? Visibility.Visible : Visibility.Collapsed;
        if (settingsController.Current.MainWindowSitemapPaneVisible != state.IsSitemapVisible)
        {
            settingsController.SetMainWindowSitemapPaneVisible(state.IsSitemapVisible);
        }

        if (state.CenterPage == MainWindowCenterPage.MainUi)
        {
            ShowMainUi();
            var targetRoute = !string.IsNullOrWhiteSpace(state.PendingMainUiRoute)
                ? state.PendingMainUiRoute
                : MainUiHost.CurrentRoute;
            if (!string.IsNullOrWhiteSpace(targetRoute))
            {
                var normalizedRoute = NormalizeMainUiRoute(targetRoute);
                var isExplicitRouteRequest = !string.IsNullOrWhiteSpace(state.PendingMainUiRoute);
                var activeTransport = runtimeController.Current.ActiveTransport == TransportKind.Cloud
                    ? TransportKind.Cloud
                    : TransportKind.Local;
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
        if (!CenterContentHost.Children.Contains(MainUiHost))
        {
            CenterContentHost.Children.Clear();
            CenterContentHost.Children.Add(MainUiHost);
        }
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
        var selectedTransport = runtimeController.Current.ActiveTransport == TransportKind.Cloud
            ? TransportKind.Cloud
            : TransportKind.Local;
        activeMainUiNavigationTransport = selectedTransport;
        var endpoint = selectedTransport == TransportKind.Cloud
            ? settings.CloudEndpoint
            : settings.LocalEndpoint;
        string? followUpRoute = null;
        var runTransportResync = false;
        try
        {
            await MainUiHost.NavigateAsync(endpoint, normalizedRoute);
            currentMainUiRoute = MainUiHost.CurrentRoute;
            currentMainUiTransport = selectedTransport;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
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

    private void RefreshPromotedMainUiPagesList()
    {
        promotedMainUiPages = settingsController.Current.CachedMainUiPageLinks;
        var isExpanded = settingsController.Current.MainUiPagesExpanded;
        MainUiPagesChevron.Glyph = isExpanded ? "\uE70E" : "\uE70D";
        MainUiPagesList.Children.Clear();
        MainUiPagesList.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (!isExpanded)
        {
            return;
        }

        foreach (var page in promotedMainUiPages)
        {
            var route = page.Route;
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new TextBlock { Text = page.Label }
            };
            button.Click += (_, _) => shellController.SelectPromotedMainUiPage(route);
            MainUiPagesList.Children.Add(button);
        }
    }

    private async Task RunRuntimeOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (isRefreshing)
        {
            return;
        }

        isRefreshing = true;
        try
        {
            await operation(CancellationToken.None);
            RefreshRuntimeBindings();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            isRefreshing = false;
        }
    }

    internal void RefreshRuntimeBindings(StackPanel? targetRows = null)
    {
        var rowsPanel = targetRows ?? ActiveRows;
        var snapshot = runtimeController.Current;
        RefreshChromeBindings(snapshot);
        EnsureMainUiEndpointMatchesActiveTransport(snapshot);
        sitemapSurfaceRenderer.Refresh(rowsPanel, snapshot);
        snapshotRefreshGate.Drain(() => RefreshRuntimeBindings(targetRows: null));
    }

    private void EnsureMainUiEndpointMatchesActiveTransport(SitemapRuntimeSnapshot snapshot)
    {
        if (shellController.Current.CenterPage != MainWindowCenterPage.MainUi)
        {
            return;
        }

        var desiredTransport = snapshot.ActiveTransport == TransportKind.Cloud
            ? TransportKind.Cloud
            : TransportKind.Local;
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
            currentMainUiRoute = MainUiHost.CurrentRoute;
            return;
        }

        var route = MainUiHost.CurrentRoute;
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

            InactiveSlotContainer.Visibility = Visibility.Visible;
            RefreshRuntimeBindings(InactiveRows);

            await AnimatePageTransitionOverlapAsync(NavigationDirection.Forward);

            ActiveRows.Children.Clear();
            ActiveSlotContainer.Visibility = Visibility.Collapsed;
            _activeSlotIsA = !_activeSlotIsA;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
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
            settingsController.SetSitemapName(sitemapName);
            await LoadRuntimeAsync();
        }
    }

    private void SitemapHeaderArea_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        ShowSitemapMenuAt(TitleText);
    }

    private void NavigateBack_Click(object sender, RoutedEventArgs e)
    {
        if (TryNavigateMainUiBack())
        {
            return;
        }

        _ = NavigateBackWithAnimationAsync();
    }

    private void MainContent_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.GoBack)
        {
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

        if (!MainUiHost.CanGoBack)
        {
            return false;
        }

        MainUiHost.GoBack();
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

            InactiveSlotContainer.Visibility = Visibility.Visible;
            RefreshRuntimeBindings(InactiveRows);

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

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
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

    private void NotificationsNavButton_Click(object sender, RoutedEventArgs e)
    {
        shellController.SelectCenterPage(MainWindowCenterPage.Notifications);
    }

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
    {
        shellController.SelectCenterPage(MainWindowCenterPage.Settings);
    }

    private void MainUiPagesToggleButton_Click(object sender, RoutedEventArgs e)
    {
        settingsController.SetMainUiPagesExpanded(!settingsController.Current.MainUiPagesExpanded);
        RefreshPromotedMainUiPagesList();
    }

    private void ToggleSitemapButton_Click(object sender, RoutedEventArgs e)
    {
        shellController.SetSitemapVisible(!shellController.Current.IsSitemapVisible);
    }

    /// <summary>Updates header chrome independently of sitemap rows.</summary>
    private void RefreshChromeBindings(SitemapRuntimeSnapshot snapshot)
    {
        TitleText.Text = snapshot.Descriptor?.Title ?? "openHAB";
        StatusText.Text = snapshot.StatusText;
        ShellConnectionText.Text = snapshot.ActiveTransport switch
        {
            TransportKind.Cloud => $"Connected via cloud ({snapshot.ConnectionState})",
            TransportKind.Local => $"Connected via local ({snapshot.ConnectionState})",
            _ => $"Connection: {snapshot.ConnectionState}"
        };
        BackButton.Visibility = runtimeController.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
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
