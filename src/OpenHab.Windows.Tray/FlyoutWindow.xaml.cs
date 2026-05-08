using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
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

namespace OpenHab.Windows.Tray;

public sealed partial class FlyoutWindow : Window
{
    private readonly AppSettingsController settingsController;
    private readonly SitemapRuntimeController runtimeController;
    private readonly Action requestOpenMainWindow;
    private readonly Action requestHideFlyout;
    private readonly UISettings uiSettings = new();
    private bool isRefreshing;
    private bool isEntranceAnimationRunning;
    private bool isExitAnimationRunning;
    private bool shouldRunEntranceAnimation;
    private InputLightDismissAction? _lightDismissAction;

    public FlyoutWindow(
        AppSettingsController settingsController,
        SitemapRuntimeController runtimeController,
        Action requestOpenMainWindow,
        Action requestHideFlyout)
    {
        this.settingsController = settingsController;
        this.runtimeController = runtimeController;
        this.requestOpenMainWindow = requestOpenMainWindow;
        this.requestHideFlyout = requestHideFlyout;

        InitializeComponent();
        ApplyFlyoutTheme();
        ConfigureFlyoutWindow();
        _lightDismissAction = InputLightDismissAction.GetForWindowId(AppWindow.Id);
        _lightDismissAction.Dismissed += (_, _) => { _ = CloseFlyoutWithAnimationAsync(); };
        uiSettings.ColorValuesChanged += OnColorValuesChanged;
        runtimeController.SnapshotChanged += (_, _) =>
        {
            _ = DispatcherQueue.TryEnqueue(RefreshRuntimeBindings);
        };
        RefreshSettingsBindings();
        _ = LoadRuntimeAsync();
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

    internal void RefreshRuntimeBindings()
    {
        var snapshot = runtimeController.Current;
        TitleText.Text = snapshot.Descriptor?.Title ?? "openHAB";
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
        BackButton.Visibility = runtimeController.CanGoBack ? Visibility.Visible : Visibility.Collapsed;

        var rows = snapshot.Descriptor?.Rows;
        var changedIndices = snapshot.ChangedRowIndices;

        if (changedIndices is { Count: > 0 } && rows is not null)
        {
            foreach (var index in changedIndices)
            {
                if (index < 0 || index >= SitemapRows.Children.Count || index >= rows.Count) continue;
                var existing = SitemapRows.Children[index] as FrameworkElement;
                if (existing is null) continue;
                SitemapControlFactory.UpdateState(existing, rows[index]);
            }

            return;
        }

        SitemapRows.Children.Clear();
        if (rows is null)
        {
            return;
        }

        var iconTransport = snapshot.ActiveTransport ?? TransportKind.Local;
        var iconBaseUri = iconTransport == TransportKind.Local
            ? settingsController.Current.LocalEndpoint
            : settingsController.Current.CloudEndpoint;
        var iconAuth = ResolveIconAuth(iconTransport);

        for (var index = 0; index < rows.Count; index++)
        {
            var rowIndex = index;
            var row = rows[index];

            if (row.Control == RenderControlKind.ButtonGrid)
            {
                var childOptions = new List<SitemapMapOption>();
                var scan = index + 1;
                while (scan < rows.Count && rows[scan].Control == RenderControlKind.Button)
                {
                    var child = rows[scan];
                    var command = child.Command ?? child.RawItemState ?? child.RawState ?? child.State ?? string.Empty;
                    var isActive = string.Equals(child.RawItemState ?? child.RawState ?? child.State, "ON", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(child.Command, "ON", StringComparison.OrdinalIgnoreCase);
                    childOptions.Add(new SitemapMapOption(command, child.Label, child.GridRow, child.GridColumn, isActive));
                    scan++;
                }

                var mergedRow = childOptions.Count > 0 ? row with { SelectionOptions = childOptions } : row;
                Func<string, Task>? sendGridCommand = async cmd =>
                {
                    if (childOptions.Count == 0)
                    {
                        await runtimeController.SendCommandForRowAsync(rowIndex, cmd);
                        return;
                    }

                    for (var childIndex = index + 1; childIndex < scan; childIndex++)
                    {
                        var child = rows[childIndex];
                        var childCommand = child.Command ?? child.RawItemState ?? child.RawState ?? child.State ?? string.Empty;
                        if (string.Equals(childCommand, cmd, StringComparison.Ordinal))
                        {
                            await runtimeController.SendCommandForRowAsync(childIndex, cmd);
                            return;
                        }
                    }
                };

                SitemapRows.Children.Add(SitemapControlFactory.Create(
                    mergedRow,
                    activateRow: null,
                    sendGridCommand,
                    iconBaseUri,
                    settingsController.Current.UseWindows11Icons,
                    iconAuth));
                index = scan - 1;
                continue;
            }

            if (row.Control == RenderControlKind.Button)
            {
                continue;
            }
            Func<Task>? activateRow = null;
            if (row.Control == RenderControlKind.Toggle && row.Action == RenderActionKind.SendCommand)
                activateRow = () => OnRowActivatedAsync(rowIndex);
            else if (row.Action == RenderActionKind.Navigate)
                activateRow = () => OnRowNavigateAsync(rowIndex);
            Func<string, Task>? sendCommand = row.Action == RenderActionKind.SendCommand
                ? cmd => runtimeController.SendCommandForRowAsync(rowIndex, cmd)
                : null;
            SitemapRows.Children.Add(SitemapControlFactory.Create(
                row,
                activateRow,
                sendCommand,
                iconBaseUri,
                settingsController.Current.UseWindows11Icons,
                iconAuth));

            // Apply initial visibility
            var lastIndex = SitemapRows.Children.Count - 1;
            if (lastIndex >= 0 && SitemapRows.Children[lastIndex] is FrameworkElement element)
            {
                SitemapControlFactory.SetVisibility(element, row.IsVisible);
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

    private async Task OnRowNavigateAsync(int rowIndex)
    {
        if (isRefreshing)
        {
            return;
        }

        await RunRuntimeOperationAsync(async ct => await runtimeController.NavigateToChildAsync(rowIndex, ct));
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

    private void NavigateBack_Click(object sender, RoutedEventArgs e)
    {
        if (!runtimeController.CanGoBack || isRefreshing) return;
        isRefreshing = true;
        try
        {
            runtimeController.NavigateBack();
            RefreshRuntimeBindings();
        }
        finally { isRefreshing = false; }
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

    private void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (isRefreshing)
        {
            return;
        }

        if (runtimeController.NavigateToBreadcrumb(args.Index))
        {
            RefreshRuntimeBindings();
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
        while (sw.Elapsed.TotalMilliseconds < totalMs)
        {
            if (!shouldContinue())
            {
                return;
            }

            var t = Math.Clamp(sw.Elapsed.TotalMilliseconds / totalMs, 0d, 1d);
            var eased = 1d - Math.Pow(1d - t, 3d); // cubic ease-out
            var y = (int)Math.Round(startY + ((endY - startY) * eased));
            AppWindow.Move(new PointInt32(posX, y));
            await Task.Delay(16);
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

    public static void GrantForegroundPermission() => AllowSetForegroundWindow(0xFFFFFFFF);

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
