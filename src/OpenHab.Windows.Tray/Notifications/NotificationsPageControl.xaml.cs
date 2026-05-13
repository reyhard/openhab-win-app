using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenHab.App.Notifications;
using OpenHab.App.Settings;
using OpenHab.Core.Api;
using OpenHab.Core.Profiles;
using OpenHab.Windows.Tray.Rendering;
using OpenHab.Windows.Tray.Rendering.SitemapSurface;
using Windows.Storage.Streams;

namespace OpenHab.Windows.Tray.Notifications;

public sealed partial class NotificationsPageControl : UserControl
{
    private enum NotificationSortOrder
    {
        DateDescending,
        DateAscending,
        Name
    }

    private static readonly HttpClient IconHttpClient = new();

    private readonly AppSettingsController settingsController;
    private readonly NotificationStore? notificationStore;
    private readonly SitemapIconAuthResolver sitemapIconAuthResolver;
    private readonly DispatcherRefreshGate notificationRefreshGate;
    private bool notificationControlsReady;

    public NotificationsPageControl(AppSettingsController settingsController, NotificationStore? notificationStore)
    {
        this.settingsController = settingsController;
        this.notificationStore = notificationStore;
        sitemapIconAuthResolver = new SitemapIconAuthResolver(settingsController);
        notificationRefreshGate = new DispatcherRefreshGate(action => DispatcherQueue.TryEnqueue(() => action()));

        InitializeComponent();
        notificationControlsReady = true;

        if (notificationStore is not null)
        {
            notificationStore.Changed += (_, _) =>
            {
                notificationRefreshGate.Request(RefreshNotificationList);
            };
        }

        RefreshNotificationList();
    }

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

    private NotificationSortOrder CurrentNotificationSortOrder
    {
        get
        {
            if (NotificationSortBox?.SelectedItem is ComboBoxItem item
                && item.Tag is string tag
                && Enum.TryParse(tag, out NotificationSortOrder sortOrder))
            {
                return sortOrder;
            }

            return NotificationSortOrder.DateDescending;
        }
    }

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

    private MenuFlyout CreateNotificationContextMenu(StoredNotification notification)
    {
        var menu = new MenuFlyout();

        var readItem = new MenuFlyoutItem
        {
            Text = notification.IsRead ? "Mark unread" : "Mark read",
            Icon = new FontIcon { Glyph = notification.IsRead ? "\uE119" : "\uE715" }
        };
        readItem.Click += (_, _) =>
        {
            if (notification.IsRead)
            {
                notificationStore?.MarkUnread(notification.Id);
            }
            else
            {
                notificationStore?.MarkRead(notification.Id);
            }
        };
        menu.Items.Add(readItem);

        var visibilityItem = new MenuFlyoutItem
        {
            Text = notification.IsDismissed ? "Unhide" : "Hide",
            Icon = new FontIcon { Glyph = notification.IsDismissed ? "\uE8A7" : "\uE8F5" }
        };
        visibilityItem.Click += (_, _) =>
        {
            if (notification.IsDismissed)
            {
                notificationStore?.Unhide(notification.Id);
            }
            else
            {
                notificationStore?.Hide(notification.Id);
            }
        };
        menu.Items.Add(visibilityItem);

        return menu;
    }

    private void RefreshNotificationList()
    {
        try
        {
            var filter = CurrentNotificationFilter;
            var searchText = CurrentNotificationSearchText;

            LocalOnlyNote.Visibility = settingsController.Current.EndpointMode == EndpointMode.LocalOnly
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (notificationStore is null)
            {
                NotificationRows.Children.Clear();
                UnreadBadge.Visibility = Visibility.Collapsed;
                UnreadCountText.Text = "0";
                EmptyNotificationsText.Text = GetEmptyNotificationsText(filter, searchText);
                EmptyNotificationsText.Visibility = Visibility.Visible;
                return;
            }

            var sortOrder = CurrentNotificationSortOrder;
            var notifications = notificationStore.GetNotifications(filter, searchText);
            notifications = sortOrder switch
            {
                NotificationSortOrder.DateAscending => notifications
                    .OrderBy(n => n.Created)
                    .ThenBy(n => n.Title ?? "openHAB", StringComparer.OrdinalIgnoreCase)
                    .ThenBy(n => n.Message ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(n => n.Id, StringComparer.Ordinal)
                    .ToList(),
                NotificationSortOrder.Name => notifications
                    .OrderBy(n => n.Title ?? "openHAB", StringComparer.OrdinalIgnoreCase)
                    .ThenBy(n => n.Message ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(n => n.Id, StringComparer.Ordinal)
                    .ToList(),
                _ => notifications
                    .OrderByDescending(n => n.Created)
                    .ThenBy(n => n.Title ?? "openHAB", StringComparer.OrdinalIgnoreCase)
                    .ThenBy(n => n.Message ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(n => n.Id, StringComparer.Ordinal)
                    .ToList()
            };
            var useWin11Icons = settingsController.Current.UseWindows11Icons;

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
                var hasTag = !string.IsNullOrWhiteSpace(n.Severity);

                var row = new Grid
                {
                    Padding = new Thickness(8, 9, 10, 9),
                    ColumnSpacing = 10,
                    MinHeight = 58
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                AddNotificationIcon(row, 0, n.Icon, iconBaseUri, useWin11Icons, iconAuth);

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

                if (hasTag)
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

                var rowFrame = new Border
                {
                    Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Child = row
                };

                var capturedId = n.Id;
                var capturedIsUnread = isUnread;
                var button = new Button
                {
                    Content = rowFrame,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0)
                };
                button.ContextFlyout = CreateNotificationContextMenu(n);
                button.Click += (_, _) =>
                {
                    if (capturedIsUnread)
                    {
                        notificationStore.MarkRead(capturedId);
                    }
                    else
                    {
                        notificationStore.MarkUnread(capturedId);
                    }
                };

                NotificationRows.Children.Add(button);
            }

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

    private static void AddNotificationIcon(
        Grid grid,
        int column,
        string? iconName,
        Uri baseUri,
        bool useWindowsIcons,
        SitemapControlFactory.IconAuthContext iconAuth)
    {
        if (string.IsNullOrWhiteSpace(iconName))
        {
            return;
        }

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

    private static async Task LoadNotificationIconAsync(
        Image image,
        Uri baseUri,
        string iconName,
        SitemapControlFactory.IconAuthContext iconAuth)
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
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0)
            {
                return;
            }

            var source = await CreateImageSourceFromBytesAsync(bytes, response.Content.Headers.ContentType?.MediaType);
            if (source is not null)
            {
                image.Source = source;
            }
        }
        catch
        {
            // Best-effort icon loading
        }
    }

    private static async Task<ImageSource?> CreateImageSourceFromBytesAsync(byte[] bytes, string? mediaType)
    {
        if (!string.IsNullOrWhiteSpace(mediaType) && mediaType.Contains("svg", StringComparison.OrdinalIgnoreCase)
            || (bytes.Length > 0 && Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 256)).TrimStart().StartsWith("<svg", StringComparison.OrdinalIgnoreCase)))
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

    private void NotificationSortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!notificationControlsReady)
        {
            return;
        }

        RefreshNotificationList();
    }
}
