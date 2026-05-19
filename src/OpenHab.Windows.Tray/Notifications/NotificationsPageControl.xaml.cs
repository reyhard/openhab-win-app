using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenHab.App.Localization;
using OpenHab.App.Notifications;
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.Core.Profiles;
using OpenHab.Windows.Tray.Localization;

namespace OpenHab.Windows.Tray.Notifications;

public sealed partial class NotificationsPageControl : UserControl
{
    private readonly AppSettingsController settingsController;
    private readonly NotificationStore? notificationStore;
    private readonly DispatcherRefreshGate notificationRefreshGate;
    private readonly ITextLocalizer text;
    private bool notificationControlsReady;

    public NotificationsPageControl(AppSettingsController settingsController, NotificationStore? notificationStore, ITextLocalizer? text = null)
    {
        this.settingsController = settingsController;
        this.notificationStore = notificationStore;
        this.text = text ?? DefaultEnglishTextLocalizer.Instance;
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

    private string GetEmptyNotificationsText(NotificationVisibilityFilter filter, string searchText)
    {
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            return text.Get("Notifications.Empty.NoMatches");
        }

        return filter switch
        {
            NotificationVisibilityFilter.Unread => text.Get("Notifications.Empty.NoUnread"),
            NotificationVisibilityFilter.Read => text.Get("Notifications.Empty.NoRead"),
            NotificationVisibilityFilter.Hidden => text.Get("Notifications.Empty.NoHidden"),
            NotificationVisibilityFilter.All => text.Get(AppResourceKeys.NotificationsEmptyNoNotifications),
            _ => text.Get(AppResourceKeys.NotificationsEmptyNoNotifications)
        };
    }

    private MenuFlyout CreateNotificationContextMenu(StoredNotification notification)
    {
        var menu = new MenuFlyout();

        var readItem = new MenuFlyoutItem
        {
            Text = notification.IsRead ? text.Get("Notifications.MarkUnread") : text.Get("Notifications.MarkRead"),
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
            Text = notification.IsDismissed ? text.Get("Notifications.Unhide") : text.Get("Notifications.Hide"),
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
                NotificationRowsList.ItemsSource = Array.Empty<NotificationRowViewModel>();
                UnreadBadge.Visibility = Visibility.Collapsed;
                UnreadCountText.Text = "0";
                EmptyNotificationsText.Text = GetEmptyNotificationsText(filter, searchText);
                EmptyNotificationsText.Visibility = Visibility.Visible;
                return;
            }

            var sortOrder = CurrentNotificationSortOrder;
            var notifications = notificationStore.GetNotifications(filter, searchText);
            var rows = NotificationListProjection.CreateRows(notifications, sortOrder, text, DateTimeOffset.UtcNow);
            NotificationRowsList.ItemsSource = rows;

            var unreadCount = notificationStore.UnreadCount;
            UnreadBadge.Visibility = unreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            UnreadCountText.Text = unreadCount.ToString();
            EmptyNotificationsText.Text = GetEmptyNotificationsText(filter, searchText);
            EmptyNotificationsText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            notificationRefreshGate.Drain(RefreshNotificationList);
        }
    }

    private void NotificationRowsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not NotificationRowViewModel notification)
        {
            return;
        }

        if (notification.IsUnread)
        {
            notificationStore?.MarkRead(notification.Id);
        }
        else
        {
            notificationStore?.MarkUnread(notification.Id);
        }
    }

    private void NotificationRowsList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            args.ItemContainer.ContextFlyout = null;
            return;
        }

        if (args.Item is NotificationRowViewModel notification)
        {
            args.ItemContainer.ContextFlyout = CreateNotificationContextMenu(notification.Notification);
        }
    }

    private void MarkAllReadButton_Click(object sender, RoutedEventArgs e)
    {
        notificationStore?.MarkAllRead();
    }

    private void NotificationSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshNotificationListIfReady();
    }

    private void NotificationFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshNotificationListIfReady();
    }

    private void NotificationSortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshNotificationListIfReady();
    }

    private void RefreshNotificationListIfReady()
    {
        if (notificationControlsReady)
        {
            RefreshNotificationList();
        }
    }

}
