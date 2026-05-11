
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Text;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.System;
using OpenHab.App.Notifications;
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.Core;
using OpenHab.Core.Api;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using OpenHab.Windows.Tray.Rendering;
using OpenHab.Windows.Tray.Rendering.SitemapSurface;
using OpenHab.Windows.Tray.Startup;
using Windows.Storage.Streams;
namespace OpenHab.Windows.Tray;

public sealed partial class MainWindow : Window
{
    private readonly AppSettingsController settingsController;
    private readonly SitemapRuntimeController runtimeController;
    private readonly NotificationStore? notificationStore;
    private readonly Action requestHideToTray;
    private readonly SitemapIconAuthResolver sitemapIconAuthResolver;
    private readonly SitemapSurfaceRenderer sitemapSurfaceRenderer;
    private readonly DispatcherRefreshGate snapshotRefreshGate;
    private readonly DispatcherRefreshGate notificationRefreshGate;
    private bool isRefreshing;
    private bool suppressTokenEditTracking;
    private bool localTokenEdited;
    private bool cloudTokenEdited;
    private bool cloudUserNameEdited;
    private bool isHandlingCloseRequest;
    private bool suppressFlyoutWidthChange;
    private bool _activeSlotIsA = true;
    private bool _suppressNextSnapshotRefresh;
    private bool _isPageTransitionRunning;
    private bool _pendingSnapshotRefresh;
    private bool notificationControlsReady;

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
            sendCommandByRowKey: SendCommandForRowKeyAsync,
            sendCommandByRowIndex: (rowIndex, command) => runtimeController.SendCommandForRowAsync(rowIndex, command));
        snapshotRefreshGate = new DispatcherRefreshGate(action => DispatcherQueue.TryEnqueue(() => action()));
        notificationRefreshGate = new DispatcherRefreshGate(action => DispatcherQueue.TryEnqueue(() => action()));

        InitializeComponent();
        notificationControlsReady = true;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "openhab-icon.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);

        AppWindow.Closing += AppWindow_Closing;
        this.Content.KeyDown += MainContent_KeyDown;
        this.Content.PointerPressed += MainContent_PointerPressed;
        InitializeSettingsControls();
        RefreshSettingsBindings();
        if (notificationStore is not null)
        {
            notificationStore.Changed += (_, _) =>
            {
                notificationRefreshGate.Request(RefreshNotificationList);
            };
            RefreshNotificationList();
        }
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
        SidePanelPivot.SelectedIndex = 0;
    }

    private void InitializeSettingsControls()
    {
        SkinCombo.ItemsSource = Enum.GetValues<SitemapSkinKind>();
        EndpointModeCombo.ItemsSource = Enum.GetValues<EndpointMode>();

        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        VersionText.Text = $"openHAB Windows App v{version}";
    }

    private static readonly HttpClient IconHttpClient = new();

    private NotificationVisibilityFilter CurrentNotificationFilter
    {
        get
        {
            if (NotificationFilterBox?.SelectedItem is ComboBoxItem item
                && item.Tag is string tag
                && Enum.TryParse(tag, out NotificationVisibilityFilter filter))
            {
                return filter;
            }

            return NotificationVisibilityFilter.Visible;
        }
    }

    private string CurrentNotificationSearchText => NotificationSearchBox?.Text ?? string.Empty;

    private static string GetEmptyNotificationsText(NotificationVisibilityFilter filter, string searchText)
    {
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            return "No matching notifications";
        }

        return filter switch
        {
            NotificationVisibilityFilter.Unread => "No unread notifications",
            NotificationVisibilityFilter.Read => "No read notifications",
            NotificationVisibilityFilter.Hidden => "No hidden notifications",
            NotificationVisibilityFilter.All => "No notifications",
            _ => "No notifications"
        };
    }

    private void RefreshNotificationList()
    {
        try
        {
            if (notificationStore is null) return;

            LocalOnlyNote.Visibility = settingsController.Current.EndpointMode == EndpointMode.LocalOnly
                ? Visibility.Visible
                : Visibility.Collapsed;

            var filter = CurrentNotificationFilter;
            var searchText = CurrentNotificationSearchText;
            var notifications = notificationStore.GetNotifications(filter, searchText);
            var useWin11Icons = settingsController.Current.UseWindows11Icons;

            // Resolve base URI for server icons
            var iconTransport = settingsController.Current.EndpointMode == EndpointMode.CloudOnly
                ? TransportKind.Cloud
                : TransportKind.Local;
            var iconBaseUri = iconTransport == TransportKind.Local
                ? settingsController.Current.LocalEndpoint
                : settingsController.Current.CloudEndpoint;
            var iconAuth = sitemapIconAuthResolver.Resolve(iconTransport);

            NotificationRows.Children.Clear();

            foreach (var n in notifications)
            {
                var elapsed = DateTimeOffset.UtcNow - n.Created;
                var timeStr = elapsed.TotalMinutes < 1 ? "Just now"
                    : elapsed.TotalHours < 1 ? $"{(int)elapsed.TotalMinutes}m ago"
                    : elapsed.TotalDays < 1 ? $"{(int)elapsed.TotalHours}h ago"
                    : $"{(int)elapsed.TotalDays}d ago";

                var isUnread = !n.IsRead && !n.IsDismissed;
                var title = n.Title ?? "openHAB";
                var hasSeverity = !string.IsNullOrWhiteSpace(n.Severity);

                var row = new Grid { Padding = new Thickness(0, 8, 0, 0), ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Column 0: Icon
                AddNotificationIcon(row, 0, n.Icon, iconBaseUri, useWin11Icons, iconAuth);

                // Column 1: Title + Preview + Tag
                var contentPanel = new StackPanel { Spacing = 2 };
                Grid.SetColumn(contentPanel, 1);

                var titleBlock = new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 2
                };
                contentPanel.Children.Add(titleBlock);

                var previewBlock = new TextBlock
                {
                    Text = n.Message,
                    Opacity = 0.68,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 3
                };
                contentPanel.Children.Add(previewBlock);

                if (hasSeverity)
                {
                    var tagBorder = new Border
                    {
                        Background = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 1, 6, 1),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Child = new TextBlock { Text = n.Severity, FontSize = 11, Opacity = 0.6 }
                    };
                    contentPanel.Children.Add(tagBorder);
                }

                row.Children.Add(contentPanel);

                // Column 2: Timestamp + unread dot
                var metaPanel = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(metaPanel, 2);

                metaPanel.Children.Add(new TextBlock
                {
                    Text = timeStr,
                    Opacity = 0.68,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
                });

                if (isUnread)
                {
                    metaPanel.Children.Add(new FontIcon
                    {
                        Glyph = "\uEA3A",
                        FontSize = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
                    });
                }

                row.Children.Add(metaPanel);

                // Click handler: toggle read/unread
                var capturedId = n.Id;
                var capturedIsUnread = isUnread;
                var button = new Button
                {
                    Content = row,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(0),
                    BorderThickness = new Thickness(0)
                };
                button.Click += (_, _) =>
                {
                    if (capturedIsUnread)
                        notificationStore.MarkRead(capturedId);
                    else
                        notificationStore.MarkUnread(capturedId);
                };

                NotificationRows.Children.Add(button);
            }

            // Update header
            var unreadCount = notificationStore.UnreadCount;
            UnreadBadge.Visibility = unreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            UnreadCountText.Text = unreadCount.ToString();
            EmptyNotificationsText.Text = GetEmptyNotificationsText(filter, searchText);
            EmptyNotificationsText.Visibility = notifications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            notificationRefreshGate.Drain(RefreshNotificationList);
        }
    }

    private static void AddNotificationIcon(Grid grid, int column, string? iconName,
        Uri baseUri, bool useWindowsIcons, SitemapControlFactory.IconAuthContext iconAuth)
    {
        if (string.IsNullOrWhiteSpace(iconName)) return;

        if (useWindowsIcons)
        {
            var glyph = SitemapControlFactory.ResolveGlyphForIcon(iconName);
            if (glyph is not null)
            {
                var fontIcon = new FontIcon
                {
                    Glyph = glyph,
                    FontSize = 18,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 0, 0),
                    Opacity = 0.8
                };
                Grid.SetColumn(fontIcon, column);
                grid.Children.Add(fontIcon);
                return;
            }
        }

        // Fall back to server icon endpoint
        var image = new Image
        {
            Width = 20,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0)
        };
        Grid.SetColumn(image, column);
        grid.Children.Add(image);

        _ = LoadNotificationIconAsync(image, baseUri, iconName, iconAuth);
    }

    private static async Task LoadNotificationIconAsync(Image image, Uri baseUri,
        string iconName, SitemapControlFactory.IconAuthContext iconAuth)
    {
        var iconUri = SitemapControlFactory.BuildOpenHabIconUri(baseUri, iconName, null, "svg");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, iconUri);
            if (!string.IsNullOrWhiteSpace(iconAuth.ApiToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", iconAuth.ApiToken);
            }
            else if (!string.IsNullOrWhiteSpace(iconAuth.BasicUserName))
            {
                var raw = $"{iconAuth.BasicUserName}:{iconAuth.BasicPassword ?? string.Empty}";
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
            }

            using var response = await IconHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0) return;

            var source = await CreateImageSourceFromBytesAsync(bytes, response.Content.Headers.ContentType?.MediaType);
            if (source is not null)
                image.Source = source;
        }
        catch
        {
            // Best-effort icon loading
        }
    }

    private static async Task<ImageSource?> CreateImageSourceFromBytesAsync(byte[] bytes, string? mediaType)
    {
        if (!string.IsNullOrWhiteSpace(mediaType) && mediaType.Contains("svg", StringComparison.OrdinalIgnoreCase) ||
            (bytes.Length > 0 && Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 256)).TrimStart().StartsWith("<svg", StringComparison.OrdinalIgnoreCase)))
        {
            var svgSource = new SvgImageSource();
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var status = await svgSource.SetSourceAsync(stream);
            return status == SvgImageSourceLoadStatus.Success ? svgSource : null;
        }

        try
        {
            var bitmap = new BitmapImage();
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch
        {
            return null;
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
        SkinCombo.SelectedItem = settingsController.Current.Skin;
        EndpointModeCombo.SelectedItem = settingsController.Current.EndpointMode;
        LocalEndpointText.Text = settingsController.Current.LocalEndpoint.ToString();
        CloudEndpointText.Text = settingsController.Current.CloudEndpoint.ToString();

        // Sitemap selection is reflected via title/header tap menu.

        suppressTokenEditTracking = true;
        LocalTokenBox.Password = string.Empty;
        CloudPasswordBox.Password = string.Empty;
        CloudUserNameText.Text = settingsController.Current.CloudUserName ?? string.Empty;
        suppressTokenEditTracking = false;

        FollowThemeToggle.IsOn = settingsController.Current.FollowSystemTheme;
        UseWin11IconsToggle.IsOn = settingsController.Current.UseWindows11Icons;
        LaunchAtStartupToggle.IsOn = settingsController.Current.LaunchAtStartup;
        suppressFlyoutWidthChange = true;
        FlyoutWidthBox.Value = settingsController.Current.FlyoutWidth;
        NotificationPollBox.Value = settingsController.Current.NotificationPollIntervalSeconds;
        suppressFlyoutWidthChange = false;

        LocalTokenBox.PlaceholderText = settingsController.Current.HasLocalToken
            ? "Stored token configured. Type to replace, or leave unchanged."
            : "Enter token (optional)";
        CloudPasswordBox.PlaceholderText = settingsController.Current.HasCloudCredentials
            ? "Stored password configured. Type to replace, or leave unchanged."
            : "Enter myopenHAB password";
        localTokenEdited = false;
        cloudTokenEdited = false;
        cloudUserNameEdited = false;
    }

    internal void RefreshRuntimeBindings(StackPanel? targetRows = null)
    {
        var rowsPanel = targetRows ?? ActiveRows;
        var snapshot = runtimeController.Current;
        RefreshChromeBindings(snapshot);
        sitemapSurfaceRenderer.Refresh(rowsPanel, snapshot);
        snapshotRefreshGate.Drain(() => RefreshRuntimeBindings(targetRows: null));
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

    private Task OnRowActivatedByKeyAsync(string rowKey)
    {
        return SitemapRowPlanner.TryResolveRowIndex(runtimeController.Current.Descriptor?.Rows, rowKey, out var rowIndex)
            ? OnRowActivatedAsync(rowIndex)
            : Task.CompletedTask;
    }

    private async Task OnRowNavigateAsync(int rowIndex)
    {
        if (isRefreshing) return;
        isRefreshing = true;
        _isPageTransitionRunning = true;
        try
        {
            _suppressNextSnapshotRefresh = true;
            await runtimeController.NavigateToChildAsync(rowIndex, CancellationToken.None);

            InactiveSlotContainer.Visibility = Visibility.Visible;
            RefreshRuntimeBindings(InactiveRows);
            RefreshSettingsBindings();

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

    private Task OnRowNavigateByKeyAsync(string rowKey)
    {
        return SitemapRowPlanner.TryResolveRowIndex(runtimeController.Current.Descriptor?.Rows, rowKey, out var rowIndex)
            ? OnRowNavigateAsync(rowIndex)
            : Task.CompletedTask;
    }

    private Task SendCommandForRowKeyAsync(string rowKey, string command)
    {
        return SitemapRowPlanner.TryResolveRowIndex(runtimeController.Current.Descriptor?.Rows, rowKey, out var rowIndex)
            ? runtimeController.SendCommandForRowAsync(rowIndex, command)
            : Task.CompletedTask;
    }

    private async void SkinCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRefreshing || SkinCombo.SelectedItem is not SitemapSkinKind skin)
        {
            return;
        }

        settingsController.SetSkin(skin);
        await RefreshRuntimeAsync();
    }

    private async void EndpointModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRefreshing || EndpointModeCombo.SelectedItem is not EndpointMode endpointMode)
        {
            return;
        }

        settingsController.SetEndpointMode(endpointMode);
        await RefreshRuntimeAsync();
    }

    private async void EndpointText_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(LocalEndpointText.Text, UriKind.Absolute, out var localEndpoint)
            || !Uri.TryCreate(CloudEndpointText.Text, UriKind.Absolute, out var cloudEndpoint))
        {
            RefreshSettingsBindings();
            return;
        }

        try
        {
            settingsController.SetEndpoints(localEndpoint, cloudEndpoint);
            await RefreshRuntimeAsync();
        }
        catch (ArgumentException)
        {
            RefreshSettingsBindings();
        }
    }

    private async void TokenBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box || box.Tag is not string tag) return;
        if (isRefreshing) return;

        var wasEdited = IsTokenBoxEdited(tag);
        if (!wasEdited)
        {
            return;
        }

        var transportKind = tag == "Local" ? TransportKind.Local : TransportKind.Cloud;
        var token = box.Password;

        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                await settingsController.ClearApiTokenAsync(transportKind, CancellationToken.None);
                await RefreshRuntimeAsync();
            }
            else
            {
                await settingsController.SetApiTokenAsync(transportKind, token, CancellationToken.None);
                await RefreshRuntimeAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to save token: {ex.Message}";
            RefreshSettingsBindings();
        }
    }

    private void TokenBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box || box.Tag is not string tag || suppressTokenEditTracking)
        {
            return;
        }

        SetTokenBoxEdited(tag, false);
    }

    private void TokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box || box.Tag is not string tag || suppressTokenEditTracking)
        {
            return;
        }

        SetTokenBoxEdited(tag, true);
    }

    private bool IsTokenBoxEdited(string tag) => tag == "Local" ? localTokenEdited : cloudTokenEdited;

    private void SetTokenBoxEdited(string tag, bool edited)
    {
        if (tag == "Local")
        {
            localTokenEdited = edited;
            return;
        }

        cloudTokenEdited = edited;
    }

    private void CloudUserNameText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (suppressTokenEditTracking)
        {
            return;
        }

        cloudUserNameEdited = true;
    }

    private void CloudPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (suppressTokenEditTracking)
        {
            return;
        }

        cloudTokenEdited = true;
    }

    private async void CloudCredentials_LostFocus(object sender, RoutedEventArgs e)
    {
        if (isRefreshing)
        {
            return;
        }

        await Task.Yield();
        if (CloudUserNameText.FocusState != FocusState.Unfocused
            || CloudPasswordBox.FocusState != FocusState.Unfocused)
        {
            return;
        }

        if (!cloudUserNameEdited && !cloudTokenEdited)
        {
            return;
        }

        var userName = CloudUserNameText.Text.Trim();
        var password = CloudPasswordBox.Password;

        try
        {
            if (string.IsNullOrWhiteSpace(userName))
            {
                await settingsController.ClearCloudCredentialsAsync(CancellationToken.None);
                await RefreshRuntimeAsync();
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                StatusText.Text = "Cloud password is required when username is set. Existing credentials were not changed.";
                RefreshSettingsBindings();
                return;
            }

            await settingsController.SetCloudCredentialsAsync(userName, password, CancellationToken.None);
            await RefreshRuntimeAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to save cloud credentials: {ex.Message}";
            RefreshSettingsBindings();
        }
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
        _ = NavigateBackWithAnimationAsync();
    }

    private void MainContent_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.GoBack && runtimeController.CanGoBack && !isRefreshing)
        {
            e.Handled = true;
            _ = NavigateBackWithAnimationAsync();
        }
    }

    private void MainContent_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
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
        await RefreshRuntimeAsync();
    }

    private void MarkAllReadButton_Click(object sender, RoutedEventArgs e)
    {
        notificationStore?.MarkAllRead();
    }

    private void NotificationSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!notificationControlsReady)
        {
            return;
        }

        RefreshNotificationList();
    }

    private void NotificationFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!notificationControlsReady)
        {
            return;
        }

        RefreshNotificationList();
    }

    private void FollowThemeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        settingsController.SetFollowSystemTheme(FollowThemeToggle.IsOn);
    }

    private void UseWin11IconsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        settingsController.SetUseWindows11Icons(UseWin11IconsToggle.IsOn);
    }

    private async void LaunchAtStartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        var enabled = LaunchAtStartupToggle.IsOn;
        settingsController.SetLaunchAtStartup(enabled);
        await Startup.StartupManager.SetEnabledAsync(enabled);
    }

    private void FlyoutWidthBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (suppressFlyoutWidthChange || double.IsNaN(args.NewValue))
        {
            return;
        }

        var width = (int)Math.Round(args.NewValue);
        if (width < AppSettingsController.MinFlyoutWidth || width > AppSettingsController.MaxFlyoutWidth)
        {
            return;
        }

        settingsController.SetFlyoutWidth(width);
    }

    private void NotificationPollBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (suppressFlyoutWidthChange || double.IsNaN(args.NewValue))
        {
            return;
        }

        var seconds = (int)args.NewValue;
        if (seconds < AppSettingsController.MinNotificationPollIntervalSeconds
            || seconds > AppSettingsController.MaxNotificationPollIntervalSeconds)
        {
            return;
        }

        settingsController.SetNotificationPollInterval(seconds);
    }

    private void ViewLogsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logPath = DiagnosticLogger.LogPath;
            if (File.Exists(logPath))
            {
                Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{logPath}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open logs: {ex.Message}";
        }
    }

    /// <summary>Updates header chrome independently of sitemap rows.</summary>
    private void RefreshChromeBindings(SitemapRuntimeSnapshot snapshot)
    {
        TitleText.Text = snapshot.Descriptor?.Title ?? "openHAB";
        StatusText.Text = snapshot.StatusText;
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
