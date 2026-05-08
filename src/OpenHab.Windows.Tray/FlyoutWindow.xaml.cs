using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Composition;
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
using Windows.UI;
using Windows.UI.ViewManagement;

namespace OpenHab.Windows.Tray;

public sealed partial class FlyoutWindow : Window
{
    private readonly AppSettingsController settingsController;
    private readonly SitemapRuntimeController runtimeController;
    private readonly Action requestOpenMainWindow;
    private readonly Action requestHideFlyout;
    private readonly UISettings uiSettings = new();
    private bool isRefreshing;
    private bool suppressNextDeactivationHide;
    private bool isEntranceAnimationRunning;
    private bool shouldRunEntranceAnimation;

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
        this.Activated += OnWindowActivated;
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
        ApplyFlyoutTheme();
        ScheduleNativeDecorationApply();
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
        suppressNextDeactivationHide = true;
        requestOpenMainWindow();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        suppressNextDeactivationHide = true;
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
        requestHideFlyout();
    }

    private void SitemapHeaderArea_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            ShowSitemapMenuAt(element);
        }
    }

    private async void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            if (suppressNextDeactivationHide)
            {
                suppressNextDeactivationHide = false;
                return;
            }

            requestHideFlyout();
            return;
        }

        ApplyFlyoutTheme();
        ScheduleNativeDecorationApply();

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
            // Animation is non-critical; avoid app termination on composition edge cases.
        }
        catch (InvalidOperationException)
        {
            // Composition state can be transient during startup; ignore.
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
        if (Content is not UIElement contentRoot) return;

        isEntranceAnimationRunning = true;
        var visual = ElementCompositionPreview.GetElementVisual(contentRoot);
        var compositor = visual.Compositor;
        var duration = CompositionAnimationHelper.ResolveDuration(
            settingsController.GetFlyoutAnimationDurationMs());

        try
        {
            // Pre-position: hidden, slightly below final position, slightly scaled down
            visual.Opacity = 0f;
            visual.Offset = new Vector3(0f, 12f, 0f);
            visual.Scale = new Vector3(0.97f, 0.97f, 1f);

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

            // Opacity: 0 → 1 with EaseOut
            var opacityAnim = CompositionAnimationHelper.BuildScalarEntrance(
                compositor, 0f, 1f, duration);
            visual.StartAnimation(nameof(visual.Opacity), opacityAnim);

            // Offset: Y=12 → Y=0 with EaseOut
            var offsetAnim = CompositionAnimationHelper.BuildOffsetEntrance(
                compositor,
                new Vector3(0f, 12f, 0f),
                Vector3.Zero,
                duration);
            visual.StartAnimation(nameof(visual.Offset), offsetAnim);

            // Scale: 0.97 → 1.0 with EaseOut (the subtle "Fluent pop")
            var scaleAnim = CompositionAnimationHelper.BuildScalarEntrance(
                compositor, 0.97f, 1f, duration);
            visual.StartAnimation("Scale.X", scaleAnim);
            visual.StartAnimation("Scale.Y", scaleAnim);

            batch.End();

            // Wait for batch completion instead of Task.Delay
            var tcs = new TaskCompletionSource<bool>();
            batch.Completed += (_, _) => tcs.TrySetResult(true);
            await tcs.Task;
        }
        finally
        {
            // Snap to final values to prevent sub-pixel drift
            visual.Opacity = 1f;
            visual.Offset = Vector3.Zero;
            visual.Scale = new Vector3(1f, 1f, 1f);
            isEntranceAnimationRunning = false;
        }
    }

    /// <summary>
    /// Hides the content visual before positioning to prevent flicker.
    /// </summary>
    public void PrepareForHideVisual()
    {
        if (Content is UIElement contentRoot)
        {
            var visual = ElementCompositionPreview.GetElementVisual(contentRoot);
            visual.Opacity = 0f;
            visual.Offset = Vector3.Zero;
            visual.Scale = new Vector3(1f, 1f, 1f);
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
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        DwmWindowDecorations.TryApply(hwnd);
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            var queuedHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            DwmWindowDecorations.TryApply(queuedHwnd);
        });
    }
}
