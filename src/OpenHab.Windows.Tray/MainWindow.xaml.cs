
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Windowing;
using Windows.System;
using OpenHab.App.Notifications;
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.Core;
using OpenHab.Core.Api;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using OpenHab.Windows.Tray.Rendering;
using Windows.Storage.Streams;
namespace OpenHab.Windows.Tray;

public sealed partial class MainWindow : Window
{
    private readonly AppSettingsController settingsController;
    private readonly SitemapRuntimeController runtimeController;
    private readonly NotificationStore? notificationStore;
    private readonly Action requestHideToTray;
    private bool isRefreshing;
    private bool suppressTokenEditTracking;
    private bool localTokenEdited;
    private bool cloudTokenEdited;
    private bool cloudUserNameEdited;
    private bool isHandlingCloseRequest;
    private bool suppressFlyoutWidthChange;


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

        InitializeComponent();
        AppWindow.Closing += AppWindow_Closing;
        this.Content.KeyDown += MainContent_KeyDown;
        InitializeSettingsControls();
        RefreshSettingsBindings();
        if (notificationStore is not null)
        {
            notificationStore.Changed += (_, _) =>
            {
                _ = DispatcherQueue.TryEnqueue(RefreshNotificationList);
            };
            RefreshNotificationList();
        }
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

    private void InitializeSettingsControls()
    {
        SkinCombo.ItemsSource = Enum.GetValues<SitemapSkinKind>();
        EndpointModeCombo.ItemsSource = Enum.GetValues<EndpointMode>();
    }

    private static readonly HttpClient IconHttpClient = new();

    private void RefreshNotificationList()
    {
        if (notificationStore is null) return;

        var notifications = notificationStore.GetAll();
        var useWin11Icons = settingsController.Current.UseWindows11Icons;

        // Resolve base URI for server icons
        var iconTransport = settingsController.Current.EndpointMode == EndpointMode.CloudOnly
            ? TransportKind.Cloud
            : TransportKind.Local;
        var iconBaseUri = iconTransport == TransportKind.Local
            ? settingsController.Current.LocalEndpoint
            : settingsController.Current.CloudEndpoint;
        var iconAuth = ResolveIconAuth(iconTransport);

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
        EmptyNotificationsText.Visibility = notifications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
        suppressFlyoutWidthChange = true;
        FlyoutWidthBox.Value = settingsController.Current.FlyoutWidth;
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

    private void RefreshRuntimeBindings()
    {
        var snapshot = runtimeController.Current;
        TitleText.Text = snapshot.Descriptor?.Title ?? "openHAB";
        StatusText.Text = snapshot.StatusText;
        BackButton.Visibility = runtimeController.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        SitemapRows.Children.Clear();

        var rows = snapshot.Descriptor?.Rows;
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

        await RunRuntimeOperationAsync(async ct =>
        {
            await runtimeController.ActivateRowAsync(rowIndex, ct);
        });
    }

    private async Task OnRowNavigateAsync(int rowIndex)
    {
        if (isRefreshing)
        {
            return;
        }

        await RunRuntimeOperationAsync(async ct => await runtimeController.NavigateToChildAsync(rowIndex, ct));
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
        NavigateBackIfPossible();
    }

    private void MainContent_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.GoBack)
        {
            if (NavigateBackIfPossible())
            {
                e.Handled = true;
            }
        }
    }

    private bool NavigateBackIfPossible()
    {
        if (!runtimeController.CanGoBack || isRefreshing)
        {
            return false;
        }

        isRefreshing = true;
        try
        {
            runtimeController.NavigateBack();
            RefreshRuntimeBindings();
            return true;
        }
        finally
        {
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

    private void DismissAllButton_Click(object sender, RoutedEventArgs e)
    {
        notificationStore?.DismissAll();
    }

    private void FollowThemeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        settingsController.SetFollowSystemTheme(FollowThemeToggle.IsOn);
    }

    private void UseWin11IconsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        settingsController.SetUseWindows11Icons(UseWin11IconsToggle.IsOn);
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
}
