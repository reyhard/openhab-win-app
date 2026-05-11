using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using OpenHab.App.Notifications;
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.Core;
using OpenHab.Core.Api;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using OpenHab.Windows.Tray.Rendering;
using System.Numerics;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.Graphics;
using Microsoft.UI.Dispatching;

namespace OpenHab.Windows.Tray;

public sealed partial class FlyoutWindow : Window
{
    private sealed record RenderedRowTag(int RowIndex, string RowKey, string VisualStateKey);
    private sealed record ExistingRenderedRow(FrameworkElement Element, int ChildIndex);
    private sealed record PendingRowUpdate(FrameworkElement Element, int RowIndex, SitemapRowDescriptor Row);
    private readonly AppSettingsController settingsController;
    private readonly SitemapRuntimeController runtimeController;
    private readonly NotificationStore? notificationStore;
    private readonly Action requestOpenMainWindow;
    private readonly Action requestOpenNotifications;
    private readonly Action requestHideFlyout;
    private readonly UISettings uiSettings = new();
    private bool isRefreshing;
    private bool isEntranceAnimationRunning;
    private bool isExitAnimationRunning;
    private bool shouldRunEntranceAnimation;
    private InputLightDismissAction? _lightDismissAction;
    private bool _lightDismissInitialized;
    private bool _activeSlotIsA = true;
    private bool _suppressNextSnapshotRefresh;

    private StackPanel ActiveRows => _activeSlotIsA ? SitemapRows : SitemapRowsB;
    private StackPanel InactiveRows => _activeSlotIsA ? SitemapRowsB : SitemapRows;
    private Grid ActiveSlotContainer => _activeSlotIsA ? SitemapPageSlotA : SitemapPageSlotB;
    private Grid InactiveSlotContainer => _activeSlotIsA ? SitemapPageSlotB : SitemapPageSlotA;

    public FlyoutWindow(
        AppSettingsController settingsController,
        SitemapRuntimeController runtimeController,
        NotificationStore? notificationStore,
        Action requestOpenMainWindow,
        Action requestOpenNotifications,
        Action requestHideFlyout)
    {
        this.settingsController = settingsController;
        this.runtimeController = runtimeController;
        this.notificationStore = notificationStore;
        this.requestOpenMainWindow = requestOpenMainWindow;
        this.requestOpenNotifications = requestOpenNotifications;
        this.requestHideFlyout = requestHideFlyout;

        InitializeComponent();
        ApplyFlyoutTheme();
        ConfigureFlyoutWindow();
        FlyoutChrome.PointerPressed += OnFlyoutChromePointerPressed;
        EnsureLightDismissInitialized();
        uiSettings.ColorValuesChanged += OnColorValuesChanged;
        runtimeController.SnapshotChanged += (_, _) =>
        {
            if (_suppressNextSnapshotRefresh)
            {
                _suppressNextSnapshotRefresh = false;
                return;
            }
            if (!DispatcherQueue.TryEnqueue(() => RefreshRuntimeBindings(targetRows: null)))
            {
                DiagnosticLogger.Warn("FlyoutWindow SnapshotChanged: DispatcherQueue.TryEnqueue returned false — UI update lost");
            }
        };
        if (notificationStore is not null)
        {
            notificationStore.Changed += (_, _) =>
            {
                if (!DispatcherQueue.TryEnqueue(RefreshNotificationBadge))
                {
                    DiagnosticLogger.Warn("FlyoutWindow NotificationStore.Changed: DispatcherQueue.TryEnqueue returned false — badge update lost");
                }
            };
        }
        RefreshSettingsBindings();
        RefreshNotificationBadge();
        // Initial load is deferred until sitemaps are resolved in CompleteStartupAsync.
    }

    public async Task LoadRuntimeAsync()
    {
        await RunRuntimeOperationAsync(ct => runtimeController.LoadAsync(ct));
    }

    public async Task RefreshRuntimeAsync()
    {
        await RunRuntimeOperationAsync(ct => runtimeController.RefreshAsync(ct));
    }

    public void PrepareForShowAnimation()
    {
        shouldRunEntranceAnimation = true;
        EnsureLightDismissInitialized();
        // Content is set to visible; entrance animation runs via StartEntranceAnimationIfPending.
        var visual = GetFlyoutChromeVisual();
        visual.Opacity = 1f;
        visual.Offset = Vector3.Zero;
        visual.Scale = new Vector3(1f, 1f, 1f);
        ApplyFlyoutTheme();
        ScheduleNativeDecorationApply();
    }

    public void StartEntranceAnimationIfPending()
    {
        if (!shouldRunEntranceAnimation)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            if (!shouldRunEntranceAnimation)
            {
                return;
            }

            shouldRunEntranceAnimation = false;
            try
            {
                await AnimateFlyoutEntranceAsync();
            }
            catch (ArgumentException)
            {
                // Animation is non-critical.
            }
            catch (InvalidOperationException)
            {
                // Composition can be transient during activation.
            }
        });
    }

    public void PopulateSitemaps(IReadOnlyList<SitemapInfo> sitemaps)
    {
        SitemapMenuFlyout.Items.Clear();
        var current = settingsController.Current.SitemapName;
        foreach (var s in sitemaps)
        {
            var item = new MenuFlyoutItem { Text = s.Label, Tag = s.Name };
            item.Click += SitemapMenuItem_Click;
            SitemapMenuFlyout.Items.Add(item);
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
            RefreshSettingsBindings();
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

    private void RefreshSettingsBindings()
    {
        // Sitemap selection is now reflected via the title; no ComboBox to update.
    }

    private void RefreshChromeBindings(SitemapRuntimeSnapshot snapshot)
    {
        // Keep title pinned to the root/main sitemap name instead of the current subpage.
        TitleText.Text = snapshot.Breadcrumbs.Count > 0
            ? snapshot.Breadcrumbs[0]
            : string.IsNullOrWhiteSpace(settingsController.Current.SitemapName)
                ? "openHAB"
                : settingsController.Current.SitemapName;
        StatusText.Text = snapshot.StatusText;
        var rawBreadcrumbs = snapshot.Breadcrumbs.Count > 0
            ? snapshot.Breadcrumbs
            : [TitleText.Text];
        var breadcrumbItems = rawBreadcrumbs
            .Select((label, index) => index == 0
                ? BreadcrumbDisplayItem.CreateHomeIcon()
                : BreadcrumbDisplayItem.CreateText(label))
            .ToList();

        BreadcrumbBar.ItemsSource = breadcrumbItems;
        BreadcrumbBar.Visibility = rawBreadcrumbs.Count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    internal void RefreshRuntimeBindings(StackPanel? targetRows = null)
    {
        var rowsPanel = targetRows ?? ActiveRows;
        var snapshot = runtimeController.Current;
        RefreshChromeBindings(snapshot);

        var rows = snapshot.Descriptor?.Rows;
        var changedIndices = snapshot.ChangedRowIndices;

        if (changedIndices is { Count: > 0 } && rows is not null)
        {
            var indicesToRefresh = ExpandChangedIndicesForMergedRows(changedIndices, rows);
            foreach (var index in indicesToRefresh)
            {
                if (index < 0 || index >= rows.Count) continue;
                if (!TryFindRenderedRow(rowsPanel, index, out var existing, out var existingChildIndex))
                {
                    continue;
                }
                var row = rows[index];
                if (row.Control == RenderControlKind.ButtonGrid)
                {
                    // ButtonGrid options are rendered as nested Button elements; rebuild this row
                    // so visibility/active-state swaps from paired rows are reflected immediately.
                    rowsPanel.Children.RemoveAt(existingChildIndex);
                    var replacement = CreateRowElementForIndex(index, rows, snapshot);
                    SitemapControlFactory.SetVisibility(replacement, row.IsVisible);
                    rowsPanel.Children.Insert(existingChildIndex, replacement);
                    continue;
                }

                if (ShouldRebuildRow(existing, row, index))
                {
                    rowsPanel.Children.RemoveAt(existingChildIndex);
                    var replacement = CreateRowElementForIndex(index, rows, snapshot);
                    SitemapControlFactory.SetVisibility(replacement, row.IsVisible);
                    rowsPanel.Children.Insert(existingChildIndex, replacement);
                    continue;
                }

                SitemapControlFactory.UpdateState(existing, row);
                SetRenderedRowTag(existing, index, row);
            }

            return;
        }

        if (rows is not null && rowsPanel.Children.Count == CountVisualRows(rows))
        {
            // Runtime did not report row-level deltas and row count is unchanged.
            // Avoid full rebuild to prevent control reset flicker on no-op refresh.
            return;
        }

        if (rows is not null && rowsPanel.Children.Count > 0)
        {
            ReconcileStructuralRows(rowsPanel, rows, snapshot);
            return;
        }

        rowsPanel.Children.Clear();
        if (rows is null)
        {
            return;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];

            if (row.Control == RenderControlKind.Button)
            {
                continue;
            }

            var rowElement = CreateRowElementForIndex(index, rows, snapshot);
            AddRenderedRow(rowsPanel, index, rowElement);
            SitemapControlFactory.SetVisibility(rowElement, row.IsVisible);

            if (row.Control == RenderControlKind.ButtonGrid)
            {
                while (index + 1 < rows.Count && rows[index + 1].Control == RenderControlKind.Button)
                {
                    index++;
                }
            }
        }
    }

    private SitemapControlFactory.IconAuthContext ResolveIconAuth(TransportKind transportKind)
    {
        if (transportKind == TransportKind.Local)
        {
            return new SitemapControlFactory.IconAuthContext(
                ApiToken: GetApiTokenSync(TransportKind.Local),
                BasicUserName: null,
                BasicPassword: null,
                TransportKind: transportKind);
        }

        var cloudCredentials = GetCloudCredentialsSync();
        return new SitemapControlFactory.IconAuthContext(
            ApiToken: null,
            BasicUserName: cloudCredentials?.UserName,
            BasicPassword: cloudCredentials?.Password,
            TransportKind: transportKind);
    }

    private string? GetApiTokenSync(TransportKind kind)
    {
        try { return settingsController.GetApiTokenAsync(kind, CancellationToken.None).GetAwaiter().GetResult(); }
        catch { return null; }
    }

    private CloudCredentials? GetCloudCredentialsSync()
    {
        try { return settingsController.GetCloudCredentialsAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch { return null; }
    }

    private async Task OnRowActivatedAsync(int rowIndex)
    {
        if (isRefreshing)
        {
            return;
        }

        await RunRuntimeOperationAsync(async ct => await runtimeController.ActivateRowAsync(rowIndex, ct));
    }

    private Task OnRowActivatedByKeyAsync(string rowKey)
    {
        return TryResolveCurrentRowIndex(rowKey, out var rowIndex)
            ? OnRowActivatedAsync(rowIndex)
            : Task.CompletedTask;
    }

    private async Task OnRowNavigateAsync(int rowIndex)
    {
        if (isRefreshing) return;
        isRefreshing = true;
        try
        {
            _suppressNextSnapshotRefresh = true;
            await runtimeController.NavigateToChildAsync(rowIndex, CancellationToken.None);

            InactiveSlotContainer.Visibility = Visibility.Visible;
            InactiveSlotContainer.Opacity = 1d;
            RefreshRuntimeBindings(InactiveRows);
            RefreshChromeBindings(runtimeController.Current);

            await AnimatePageTransitionOverlapAsync(NavigationDirection.Forward);

            ActiveRows.Children.Clear();
            ActiveSlotContainer.Visibility = Visibility.Collapsed;
            ActiveSlotContainer.Opacity = 1d;
            _activeSlotIsA = !_activeSlotIsA;
            Canvas.SetZIndex(ActiveSlotContainer, 0);
            Canvas.SetZIndex(InactiveSlotContainer, 0);
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

    private Task OnRowNavigateByKeyAsync(string rowKey)
    {
        return TryResolveCurrentRowIndex(rowKey, out var rowIndex)
            ? OnRowNavigateAsync(rowIndex)
            : Task.CompletedTask;
    }

    private Task SendCommandForRowKeyAsync(string rowKey, string command)
    {
        return TryResolveCurrentRowIndex(rowKey, out var rowIndex)
            ? runtimeController.SendCommandForRowAsync(rowIndex, command)
            : Task.CompletedTask;
    }

    private bool TryResolveCurrentRowIndex(string rowKey, out int rowIndex)
    {
        var rows = runtimeController.Current.Descriptor?.Rows;
        if (rows is null)
        {
            rowIndex = -1;
            return false;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            if (string.Equals(SitemapControlFactory.BuildRowIdentityKey(rows[index]), rowKey, StringComparison.Ordinal))
            {
                rowIndex = index;
                return true;
            }
        }

        rowIndex = -1;
        return false;
    }

    private async void SitemapMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string name)
        {
            if (isRefreshing) return;
            settingsController.SetSitemapName(name);
            await LoadRuntimeAsync();
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshRuntimeAsync();
    }

    private void OpenAppButton_Click(object sender, RoutedEventArgs e)
    {
        requestOpenMainWindow();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        requestOpenMainWindow();
    }

    private void OpenNotificationsButton_Click(object sender, RoutedEventArgs e)
    {
        requestOpenNotifications();
    }

    private void NavigateBack_Click(object sender, RoutedEventArgs e)
    {
        if (!runtimeController.CanGoBack || isRefreshing) return;
        _ = NavigateBackWithAnimationAsync();
    }

    private void MinimizeFlyoutButton_Click(object sender, RoutedEventArgs e)
    {
        _ = CloseFlyoutWithAnimationAsync();
    }

    private void SitemapHeaderArea_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            ShowSitemapMenuAt(element);
        }
    }

    private async void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (isRefreshing) return;
        var previousDepth = runtimeController.Current.Breadcrumbs.Count;

        if (runtimeController.NavigateToBreadcrumb(args.Index))
        {
            isRefreshing = true;
            try
            {
                _suppressNextSnapshotRefresh = true;
                var currentDepth = runtimeController.Current.Breadcrumbs.Count;
                if (currentDepth == previousDepth)
                {
                    RefreshRuntimeBindings(ActiveRows);
                    RefreshChromeBindings(runtimeController.Current);
                    return;
                }

                InactiveSlotContainer.Visibility = Visibility.Visible;
                InactiveSlotContainer.Opacity = 1d;
                RefreshRuntimeBindings(InactiveRows);
                RefreshChromeBindings(runtimeController.Current);

                await AnimatePageTransitionOverlapAsync(NavigationDirection.Back);

                ActiveRows.Children.Clear();
                ActiveSlotContainer.Visibility = Visibility.Collapsed;
                ActiveSlotContainer.Opacity = 1d;
                _activeSlotIsA = !_activeSlotIsA;
            }
            finally
            {
                isRefreshing = false;
            }
        }
    }

    private void EnsureLightDismissInitialized()
    {
        if (_lightDismissInitialized) return;

        try
        {
            _lightDismissAction = InputLightDismissAction.GetForWindowId(AppWindow.Id);
            _lightDismissAction.Dismissed += (_, _) =>
            {
                _ = CloseFlyoutWithAnimationAsync();
            };
            _lightDismissInitialized = true;
            DiagnosticLogger.Info("Flyout InputLightDismissAction initialized successfully");
        }
        catch (Exception ex)
        {
            _lightDismissInitialized = false;
            DiagnosticLogger.Warn(
                $"Failed to initialize InputLightDismissAction — light-dismiss unavailable. " +
                $"Exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ConfigureFlyoutWindow()
    {
        var appWindow = AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        appWindow.IsShownInSwitchers = false;
        var theme = DwmWindowDecorations.ResolveFlyoutTheme(
            settingsController.Current.FollowSystemTheme,
            IsSystemBackgroundDark());
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        StripNonClientFrame(hwnd);
        ApplySurfaceStyle(theme);
    }

    private void ShowSitemapMenuAt(FrameworkElement target)
    {
        if (SitemapMenuFlyout.Items.Count == 0)
        {
            return;
        }

        SitemapMenuFlyout.ShowAt(target);
    }

    private async Task AnimateFlyoutEntranceAsync()
    {
        if (isEntranceAnimationRunning) return;
        if (FlyoutChrome is not UIElement) return;

        // Cancel any concurrent exit animation so it won't
        // call AppWindow.Hide() or blank the visual after we show.
        isExitAnimationRunning = false;

        isEntranceAnimationRunning = true;
        var visual = GetFlyoutChromeVisual();
        var compositor = visual.Compositor;
        var duration = CompositionAnimationHelper.ResolveDuration(
            settingsController.GetFlyoutAnimationDurationMs());

        try
        {
            SetFlyoutAlwaysOnTop(false);
            UpdateFlyoutChromeCenterPoint(visual);
            var targetPos = AppWindow.Position;
            var startPos = new PointInt32(targetPos.X, ResolveOffscreenStartY());
            AppWindow.Move(startPos);

            // Pre-position: hidden and slightly scaled down.
            visual.Opacity = 0f;
            visual.Offset = Vector3.Zero;
            visual.Scale = new Vector3(0.97f, 0.97f, 1f);

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

            // Opacity: 0 → 1 with EaseOut
            var opacityAnim = CompositionAnimationHelper.BuildScalarEntrance(
                compositor, 0f, 1f, duration);
            visual.StartAnimation(nameof(visual.Opacity), opacityAnim);

            // Scale: 0.97 → 1.0 with EaseOut (the subtle "Fluent pop")
            var scaleAnim = CompositionAnimationHelper.BuildScalarEntrance(
                compositor, 0.97f, 1f, duration);
            visual.StartAnimation("Scale.X", scaleAnim);
            visual.StartAnimation("Scale.Y", scaleAnim);

            var moveTask = AnimateWindowYAsync(
                startPos.Y,
                targetPos.Y,
                duration,
                () => isEntranceAnimationRunning);

            batch.End();

            // Wait for batch completion instead of Task.Delay
            var tcs = new TaskCompletionSource<bool>();
            batch.Completed += (_, _) => tcs.TrySetResult(true);
            await Task.WhenAll(tcs.Task, moveTask);
        }
        finally
        {
            // Snap to final values to prevent sub-pixel drift
            visual.Opacity = 1f;
            visual.Offset = Vector3.Zero;
            visual.Scale = new Vector3(1f, 1f, 1f);
            SetFlyoutAlwaysOnTop(true);
            isEntranceAnimationRunning = false;
        }
    }

    /// <summary>
    /// Cancels any running animations and resets state.
    /// Must be called before programmatic AppWindow.Hide() to prevent
    /// stale animation flags from blocking subsequent show cycles.
    /// </summary>
    public void CancelRunningAnimations()
    {
        isEntranceAnimationRunning = false;
        isExitAnimationRunning = false;
    }

    /// <summary>
    /// Hides the content visual before positioning to prevent flicker.
    /// </summary>
    public void PrepareForHideVisual()
    {
        var visual = GetFlyoutChromeVisual();
        visual.Opacity = 0f;
        visual.Offset = Vector3.Zero;
        visual.Scale = new Vector3(1f, 1f, 1f);
    }

    public async Task AnimateFlyoutExitAndHideAsync()
    {
        if (isExitAnimationRunning) return;
        if (FlyoutChrome is not UIElement) return;

        isExitAnimationRunning = true;
        var visual = GetFlyoutChromeVisual();
        var compositor = visual.Compositor;
        var duration = CompositionAnimationHelper.ResolveDuration(
            settingsController.GetFlyoutAnimationDurationMs());

        // If animation disabled, hide instantly
        if (duration.TotalMilliseconds <= 1)
        {
            AppWindow.Hide();
            isExitAnimationRunning = false;
            return;
        }

        try
        {
            SetFlyoutAlwaysOnTop(false);
            UpdateFlyoutChromeCenterPoint(visual);
            var startPos = AppWindow.Position;
            var endPos = new PointInt32(startPos.X, ResolveOffscreenEndY());

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

            // Opacity: current → 0 with EaseIn
            var opacityAnim = CompositionAnimationHelper.BuildScalarExit(
                compositor, 1f, 0f, duration);
            visual.StartAnimation(nameof(visual.Opacity), opacityAnim);

            // Scale: slight shrink with EaseIn
            var scaleAnim = CompositionAnimationHelper.BuildScalarExit(
                compositor, 1f, 0.97f, duration);
            visual.StartAnimation("Scale.X", scaleAnim);
            visual.StartAnimation("Scale.Y", scaleAnim);

            var moveTask = AnimateWindowYAsync(
                startPos.Y,
                endPos.Y,
                duration,
                () => isExitAnimationRunning);

            batch.End();

            var tcs = new TaskCompletionSource<bool>();
            batch.Completed += (_, _) => tcs.TrySetResult(true);
            await Task.WhenAll(tcs.Task, moveTask);

            // Only hide if not cancelled by a concurrent entrance animation
            if (isExitAnimationRunning)
            {
                AppWindow.Hide();
            }
        }
        finally
        {
            // Reset visual state for next show, but only if not cancelled
            if (isExitAnimationRunning)
            {
                visual.Opacity = 0f;
                visual.Offset = Vector3.Zero;
                visual.Scale = new Vector3(1f, 1f, 1f);
            }
            isExitAnimationRunning = false;
        }
    }

    public sealed record BreadcrumbDisplayItem(string Label, FontFamily FontFamily, double FontSize)
    {
        public static BreadcrumbDisplayItem CreateHomeIcon() =>
            new("\uEA8A", new FontFamily("Segoe MDL2 Assets"), 18);

        public static BreadcrumbDisplayItem CreateText(string label) =>
            new(label, new FontFamily("Segoe UI"), 14);
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            ApplyFlyoutTheme();
            ScheduleNativeDecorationApply();
        });
    }

    private void ApplyFlyoutTheme()
    {
        var theme = DwmWindowDecorations.ResolveFlyoutTheme(
            settingsController.Current.FollowSystemTheme,
            IsSystemBackgroundDark());

        if (Content is FrameworkElement contentRoot)
        {
            contentRoot.RequestedTheme = theme == FlyoutTheme.Dark
                ? ElementTheme.Dark
                : ElementTheme.Light;
        }

        ApplySurfaceStyle(theme);
    }

    private void ApplySurfaceStyle(FlyoutTheme theme)
    {
        // Keep the root surface theme-aware while avoiding bright halo borders in dark mode.
        if (Application.Current.Resources["LayerFillColorAltBrush"] is Brush backgroundBrush)
        {
            FlyoutChrome.Background = backgroundBrush;
        }
        FlyoutChrome.BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        FlyoutChrome.BorderThickness = new Thickness(0);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AcrylicBlurHelper.Apply(hwnd, theme);
    }

    private Visual GetFlyoutChromeVisual() => ElementCompositionPreview.GetElementVisual(FlyoutChrome);

    private void UpdateFlyoutChromeCenterPoint(Visual visual)
    {
        var width = (float)Math.Max(0d, FlyoutChrome.ActualWidth);
        var height = (float)Math.Max(0d, FlyoutChrome.ActualHeight);
        visual.CenterPoint = new Vector3(width / 2f, height / 2f, 0f);
    }

    private async Task AnimateWindowYAsync(int startY, int endY, TimeSpan duration, Func<bool> shouldContinue)
    {
        if (duration.TotalMilliseconds <= 1 || startY == endY)
        {
            if (shouldContinue())
            {
                var pos = AppWindow.Position;
                AppWindow.Move(new PointInt32(pos.X, endY));
            }
            return;
        }

        var sw = Stopwatch.StartNew();
        var posX = AppWindow.Position.X;
        var totalMs = duration.TotalMilliseconds;
        var lastY = startY;

        // Poll at ~120 Hz for smoother animation on high-refresh-rate displays.
        var frameBudgetMs = 8;

        while (sw.Elapsed.TotalMilliseconds < totalMs)
        {
            if (!shouldContinue())
            {
                return;
            }

            var t = Math.Clamp(sw.Elapsed.TotalMilliseconds / totalMs, 0d, 1d);
            var eased = 1d - Math.Pow(1d - t, 3d); // cubic ease-out
            var y = (int)Math.Round(startY + ((endY - startY) * eased));

            // Skip redundant Move calls when the pixel position hasn't changed.
            if (y != lastY)
            {
                AppWindow.Move(new PointInt32(posX, y));
                lastY = y;
            }

            await Task.Delay(frameBudgetMs);
        }

        if (shouldContinue())
        {
            AppWindow.Move(new PointInt32(posX, endY));
        }
    }

    private async Task CloseFlyoutWithAnimationAsync()
    {
        if (isExitAnimationRunning)
        {
            return;
        }

        await AnimateFlyoutExitAndHideAsync();
        requestHideFlyout();
    }

    private void OnFlyoutChromePointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(sender as UIElement).Properties;
        if (props.IsXButton1Pressed && runtimeController.CanGoBack && !isRefreshing)
        {
            e.Handled = true;
            _ = NavigateBackWithAnimationAsync();
        }
    }

    private async Task NavigateBackWithAnimationAsync()
    {
        if (isRefreshing) return;
        isRefreshing = true;
        try
        {
            _suppressNextSnapshotRefresh = true;
            runtimeController.NavigateBack();

            InactiveSlotContainer.Visibility = Visibility.Visible;
            InactiveSlotContainer.Opacity = 1d;
            RefreshRuntimeBindings(InactiveRows);
            RefreshChromeBindings(runtimeController.Current);

            await AnimatePageTransitionOverlapAsync(NavigationDirection.Back);

            ActiveRows.Children.Clear();
            ActiveSlotContainer.Visibility = Visibility.Collapsed;
            ActiveSlotContainer.Opacity = 1d;
            _activeSlotIsA = !_activeSlotIsA;
            Canvas.SetZIndex(ActiveSlotContainer, 0);
            Canvas.SetZIndex(InactiveSlotContainer, 0);
        }
        finally { isRefreshing = false; }
    }

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

    private static IReadOnlyList<int> ExpandChangedIndicesForMergedRows(IReadOnlyList<int> changedIndices, IReadOnlyList<SitemapRowDescriptor> rows)
    {
        var set = new SortedSet<int>();
        foreach (var index in changedIndices)
        {
            if (index < 0 || index >= rows.Count)
            {
                continue;
            }

            var effective = index;
            if (rows[index].Control == RenderControlKind.Button)
            {
                for (var scan = index - 1; scan >= 0; scan--)
                {
                    if (rows[scan].Control == RenderControlKind.ButtonGrid)
                    {
                        effective = scan;
                        break;
                    }

                    if (rows[scan].Control != RenderControlKind.Button)
                    {
                        break;
                    }
                }
            }

            if (rows[effective].Control == RenderControlKind.Button)
            {
                // These are merged into an upstream ButtonGrid control and should
                // never be refreshed as stand-alone visual rows.
                continue;
            }

            set.Add(effective);
        }

        return set.ToArray();
    }

    private void ReconcileStructuralRows(StackPanel rowsPanel, IReadOnlyList<SitemapRowDescriptor> rows, SitemapRuntimeSnapshot snapshot)
    {
        var existingByKey = new Dictionary<string, Queue<ExistingRenderedRow>>(StringComparer.Ordinal);
        for (var childIndex = 0; childIndex < rowsPanel.Children.Count; childIndex++)
        {
            if (rowsPanel.Children[childIndex] is not FrameworkElement child ||
                child.Tag is not RenderedRowTag tag)
            {
                continue;
            }

            if (!existingByKey.TryGetValue(tag.RowKey, out var bucket))
            {
                bucket = new Queue<ExistingRenderedRow>();
                existingByKey[tag.RowKey] = bucket;
            }

            bucket.Enqueue(new ExistingRenderedRow(child, childIndex));
        }

        var oldVisualRows = rowsPanel.Children.Count;
        var reused = 0;
        var inserted = 0;
        var rebuilt = 0;
        var removed = 0;
        var orderedRows = new List<FrameworkElement>();
        var pendingUpdates = new List<PendingRowUpdate>();
        rowsPanel.Children.Clear();

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.Control == RenderControlKind.Button)
            {
                continue;
            }

            var effectiveRow = row.Control == RenderControlKind.ButtonGrid
                ? BuildMergedButtonGridRow(index, rows)
                : row;
            var rowKey = SitemapControlFactory.BuildRowIdentityKey(effectiveRow);

            if (existingByKey.TryGetValue(rowKey, out var bucket) && bucket.Count > 0)
            {
                var existing = bucket.Dequeue().Element;
                if (row.Control == RenderControlKind.ButtonGrid || ShouldRebuildRow(existing, effectiveRow, index))
                {
                    existing = CreateRowElementForIndex(index, rows, snapshot);
                    SitemapControlFactory.SetVisibility(existing, effectiveRow.IsVisible);
                    rebuilt++;
                }
                else
                {
                    pendingUpdates.Add(new PendingRowUpdate(existing, index, effectiveRow));
                    reused++;
                }

                orderedRows.Add(existing);
            }
            else
            {
                var insertedElement = CreateRowElementForIndex(index, rows, snapshot);
                SitemapControlFactory.SetVisibility(insertedElement, visible: false);
                if (effectiveRow.IsVisible)
                {
                    pendingUpdates.Add(new PendingRowUpdate(insertedElement, index, effectiveRow));
                }

                orderedRows.Add(insertedElement);
                inserted++;
            }

            if (row.Control == RenderControlKind.ButtonGrid)
            {
                while (index + 1 < rows.Count && rows[index + 1].Control == RenderControlKind.Button)
                {
                    index++;
                }
            }
        }

        var disappearing = existingByKey.Values
            .SelectMany(bucket => bucket)
            .OrderBy(item => item.ChildIndex)
            .ToList();

        foreach (var item in disappearing)
        {
            var insertIndex = Math.Min(item.ChildIndex, orderedRows.Count);
            orderedRows.Insert(insertIndex, item.Element);
        }

        foreach (var element in orderedRows)
        {
            rowsPanel.Children.Add(element);
        }

        foreach (var update in pendingUpdates)
        {
            SitemapControlFactory.UpdateState(update.Element, update.Row);
            SetRenderedRowTag(update.Element, update.RowIndex, update.Row);
        }

        foreach (var item in disappearing)
        {
            removed++;
            SitemapControlFactory.CollapseAndRemove(rowsPanel, item.Element);
        }

        DiagnosticLogger.Info(
            $"Flyout structural row reconcile oldVisualRows={oldVisualRows} targetVisualRows={orderedRows.Count - removed} " +
            $"currentVisualRows={rowsPanel.Children.Count} reused={reused} inserted={inserted} rebuilt={rebuilt} removed={removed}");
    }

    private static int CountVisualRows(IReadOnlyList<SitemapRowDescriptor> rows)
    {
        var count = 0;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.Control == RenderControlKind.Button)
            {
                continue;
            }

            count++;
            if (row.Control == RenderControlKind.ButtonGrid)
            {
                while (index + 1 < rows.Count && rows[index + 1].Control == RenderControlKind.Button)
                {
                    index++;
                }
            }
        }

        return count;
    }

    private static SitemapRowDescriptor BuildMergedButtonGridRow(int gridIndex, IReadOnlyList<SitemapRowDescriptor> rows)
    {
        var row = rows[gridIndex];
        var childOptions = new List<SitemapMapOption>();
        var scan = gridIndex + 1;
        while (scan < rows.Count && rows[scan].Control == RenderControlKind.Button)
        {
            var child = rows[scan];
            var command = child.Command ?? child.RawItemState ?? child.RawState ?? child.State ?? string.Empty;
            var isActive = string.Equals(child.RawItemState ?? child.RawState ?? child.State, "ON", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(child.Command, "ON", StringComparison.OrdinalIgnoreCase);
            childOptions.Add(new SitemapMapOption(
                command,
                child.Label,
                child.GridRow,
                child.GridColumn,
                isActive,
                child.Command,
                child.ReleaseCommand,
                child.Stateless,
                scan));
            scan++;
        }

        var visibleChildOptions = childOptions.Where(o => o.SourceRowIndex.HasValue && rows[o.SourceRowIndex.Value].IsVisible).ToList();
        if (visibleChildOptions.Count > 0)
        {
            childOptions = visibleChildOptions;
        }

        return childOptions.Count > 0 ? row with { SelectionOptions = childOptions } : row;
    }

    private FrameworkElement CreateRowElementForIndex(int index, IReadOnlyList<SitemapRowDescriptor> rows, SitemapRuntimeSnapshot snapshot)
    {
        var row = rows[index];
        var iconTransport = snapshot.ActiveTransport ?? TransportKind.Local;
        var iconBaseUri = iconTransport == TransportKind.Local
            ? settingsController.Current.LocalEndpoint
            : settingsController.Current.CloudEndpoint;
        var iconAuth = ResolveIconAuth(iconTransport);

        if (row.Control == RenderControlKind.ButtonGrid)
        {
            var mergedRow = BuildMergedButtonGridRow(index, rows);
            var childOptions = mergedRow.SelectionOptions;

            Func<SitemapMapOption, bool, Task>? sendGridCommand = async (option, isRelease) =>
            {
                var expectedCommand = isRelease ? option.ReleaseCommand : option.ClickCommand ?? option.Command;
                if (string.IsNullOrWhiteSpace(expectedCommand) ||
                    string.Equals(expectedCommand, "NULL", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (childOptions.Count == 0)
                {
                    await runtimeController.SendCommandForRowAsync(index, expectedCommand);
                    return;
                }

                if (option.SourceRowIndex.HasValue)
                {
                    await runtimeController.SendCommandForRowAsync(option.SourceRowIndex.Value, expectedCommand);
                    return;
                }

                await runtimeController.SendCommandForRowAsync(index, expectedCommand);
            };

            var element = SitemapControlFactory.Create(
                mergedRow,
                activateRow: null,
                sendCommand: null,
                iconBaseUri,
                settingsController.Current.UseWindows11Icons,
                iconAuth,
                chartDpi: (int)settingsController.Current.ChartQuality,
                sendButtonGridCommand: sendGridCommand);
            SetRenderedRowTag(element, index, mergedRow);
            return element;
        }

        var rowKey = SitemapControlFactory.BuildRowIdentityKey(row);
        Func<Task>? activateRow = null;
        if (row.Control == RenderControlKind.Toggle && row.Action == RenderActionKind.SendCommand)
            activateRow = () => OnRowActivatedByKeyAsync(rowKey);
        else if (row.Action == RenderActionKind.Navigate)
            activateRow = () => OnRowNavigateByKeyAsync(rowKey);
        Func<string, Task>? sendCommand = row.Action == RenderActionKind.SendCommand
            ? cmd => SendCommandForRowKeyAsync(rowKey, cmd)
            : null;

        var created = SitemapControlFactory.Create(
            row,
            activateRow,
            sendCommand,
            iconBaseUri,
            settingsController.Current.UseWindows11Icons,
            iconAuth,
            chartDpi: (int)settingsController.Current.ChartQuality);
        SetRenderedRowTag(created, index, row);
        return created;
    }

    private static void AddRenderedRow(StackPanel rowsPanel, int rowIndex, FrameworkElement rowElement)
    {
        // Tag is assigned by CreateRowElementForIndex for delta mapping metadata.
        rowsPanel.Children.Add(rowElement);
    }

    private static bool TryFindRenderedRow(StackPanel rowsPanel, int rowIndex, out FrameworkElement element, out int childIndex)
    {
        for (var i = 0; i < rowsPanel.Children.Count; i++)
        {
            if (rowsPanel.Children[i] is FrameworkElement candidate &&
                TryGetRenderedRowIndex(candidate, out var candidateRowIndex) &&
                candidateRowIndex == rowIndex)
            {
                element = candidate;
                childIndex = i;
                return true;
            }
        }

        element = null!;
        childIndex = -1;
        return false;
    }

    private static bool TryGetRenderedRowIndex(FrameworkElement element, out int rowIndex)
    {
        switch (element.Tag)
        {
            case int idx:
                rowIndex = idx;
                return true;
            case RenderedRowTag tag:
                rowIndex = tag.RowIndex;
                return true;
            default:
                rowIndex = -1;
                return false;
        }
    }

    private static void SetRenderedRowTag(FrameworkElement element, int rowIndex, SitemapRowDescriptor row)
    {
        element.Tag = new RenderedRowTag(
            rowIndex,
            SitemapControlFactory.BuildRowIdentityKey(row),
            SitemapControlFactory.BuildRowVisualStateKey(row, rowIndex));
    }

    private static bool ShouldRebuildRow(FrameworkElement existingElement, SitemapRowDescriptor updatedRow, int rowIndex)
    {
        if (existingElement.Tag is not RenderedRowTag tag)
        {
            return false;
        }

        return !string.Equals(
            tag.VisualStateKey,
            SitemapControlFactory.BuildRowVisualStateKey(updatedRow, rowIndex),
            StringComparison.Ordinal);
    }

    private void RefreshNotificationBadge()
    {
        if (notificationStore is null)
        {
            UnreadBadge.Visibility = Visibility.Collapsed;
            return;
        }

        var unread = notificationStore.UnreadCount;
        if (unread <= 0)
        {
            UnreadBadge.Visibility = Visibility.Collapsed;
            return;
        }

        UnreadBadgeText.Text = unread > 99 ? "99+" : unread.ToString();
        UnreadBadge.Visibility = Visibility.Visible;
    }

    private int ResolveOffscreenStartY()
    {
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var size = AppWindow.Size;
        var isTopAnchored = IsTopAnchored(workArea);
        var margin = 8;

        // Top-anchored flyouts enter from above; bottom-anchored enter from below.
        return isTopAnchored
            ? workArea.Y - size.Height - margin
            : workArea.Y + workArea.Height + margin;
    }

    private int ResolveOffscreenEndY()
    {
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var size = AppWindow.Size;
        var isTopAnchored = IsTopAnchored(workArea);
        var margin = 8;

        // Exit in the same direction as entrance.
        return isTopAnchored
            ? workArea.Y - size.Height - margin
            : workArea.Y + workArea.Height + margin;
    }

    private bool IsTopAnchored(RectInt32 workArea)
    {
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        var windowCenterY = position.Y + (size.Height / 2);
        var workAreaCenterY = workArea.Y + (workArea.Height / 2);
        return windowCenterY <= workAreaCenterY;
    }

    private void SetFlyoutAlwaysOnTop(bool isAlwaysOnTop)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = isAlwaysOnTop;
        }
    }

    private static void StripNonClientFrame(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        const int GWL_STYLE = -16;
        const int GWL_EXSTYLE = -20;

        const int WS_BORDER = 0x00800000;
        const int WS_DLGFRAME = 0x00400000;
        const int WS_CAPTION = 0x00C00000;
        const int WS_THICKFRAME = 0x00040000;
        const int WS_SYSMENU = 0x00080000;

        const int WS_EX_DLGMODALFRAME = 0x00000001;
        const int WS_EX_CLIENTEDGE = 0x00000200;
        const int WS_EX_STATICEDGE = 0x00020000;
        const int WS_EX_WINDOWEDGE = 0x00000100;

        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_FRAMECHANGED = 0x0020;

        var style = GetWindowLong(hwnd, GWL_STYLE);
        style &= ~(WS_BORDER | WS_DLGFRAME | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU);
        SetWindowLong(hwnd, GWL_STYLE, style);

        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle &= ~(WS_EX_DLGMODALFRAME | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE | WS_EX_WINDOWEDGE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);

        _ = SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public static void GrantForegroundPermission(IntPtr hwnd)
    {
        AllowSetForegroundWindow(0xFFFFFFFF);
        SetForegroundWindow(hwnd);
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

    private void ScheduleNativeDecorationApply()
    {
        var theme = DwmWindowDecorations.ResolveFlyoutTheme(
            settingsController.Current.FollowSystemTheme,
            IsSystemBackgroundDark());
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        DwmWindowDecorations.TryApply(hwnd, theme);
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            var queuedHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            DwmWindowDecorations.TryApply(queuedHwnd, theme);
        });
    }
}
