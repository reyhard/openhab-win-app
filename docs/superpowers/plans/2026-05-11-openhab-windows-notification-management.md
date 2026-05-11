# openHAB Windows Notification Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add right-click notification management, hidden-notification filtering, mark-all-read behavior, and title/message/tag search to the Windows notifications inbox.

**Architecture:** Keep notification semantics in `OpenHab.App.Notifications.NotificationStore`. The WinUI tray app should query filtered results and wire commands, but it should not duplicate hidden/read/search rules.

**Tech Stack:** .NET 10, C#, WinUI/Windows App SDK, xUnit.

---

## File Structure

- Modify `src/OpenHab.App/Notifications/NotificationStore.cs`
  - Add `NotificationVisibilityFilter`.
  - Add `GetNotifications(filter, searchText)`.
  - Add `Hide`, `Unhide`, and `MarkAllRead`.
  - Keep existing `Dismiss`/`DismissAll` as compatibility wrappers.
- Modify `tests/OpenHab.App.Tests/Notifications/NotificationStoreTests.cs`
  - Add focused app-layer tests for hiding, filtering, search, and mark-all-read.
- Modify `src/OpenHab.Windows.Tray/MainWindow.xaml`
  - Rename `DismissAllButton` to `MarkAllReadButton`.
  - Add search and filter controls under the notifications header.
- Modify `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
  - Query `NotificationStore.GetNotifications(...)`.
  - Wire search/filter refresh.
  - Add row right-click context menu for mark read/unread and hide/unhide.
  - Update empty-state text.

## Task 1: App-Layer Notification Semantics

**Files:**
- Modify: `src/OpenHab.App/Notifications/NotificationStore.cs`
- Test: `tests/OpenHab.App.Tests/Notifications/NotificationStoreTests.cs`

- [ ] **Step 1: Add failing tests for hidden, mark-all-read, filter, and search behavior**

Append these tests to `NotificationStoreTests` before the final closing brace:

```csharp
[Fact]
public void Hide_MarksNotificationHiddenAndRead()
{
    var store = new NotificationStore();
    store.AddOrUpdate("n1", "Water leak", DateTimeOffset.UtcNow);

    store.Hide("n1");

    var notification = store.GetNotifications(NotificationVisibilityFilter.All, null)
        .Single(n => n.Id == "n1");
    Assert.True(notification.IsDismissed);
    Assert.True(notification.IsRead);
    Assert.Equal(0, store.UnreadCount);
}

[Fact]
public void Unhide_RestoresVisibilityWithoutChangingReadState()
{
    var store = new NotificationStore();
    store.AddOrUpdate("n1", "Water leak", DateTimeOffset.UtcNow);
    store.Hide("n1");

    store.Unhide("n1");

    var notification = store.GetNotifications(NotificationVisibilityFilter.All, null)
        .Single(n => n.Id == "n1");
    Assert.False(notification.IsDismissed);
    Assert.True(notification.IsRead);
}

[Fact]
public void MarkAllRead_MarksOnlyVisibleNotificationsRead()
{
    var store = new NotificationStore();
    store.AddOrUpdate("visible", "Visible", DateTimeOffset.UtcNow);
    store.AddOrUpdate("hidden", "Hidden", DateTimeOffset.UtcNow);
    store.Hide("hidden");
    store.MarkUnread("hidden");

    store.MarkAllRead();

    var all = store.GetNotifications(NotificationVisibilityFilter.All, null);
    Assert.True(all.Single(n => n.Id == "visible").IsRead);
    Assert.False(all.Single(n => n.Id == "hidden").IsRead);
}

[Fact]
public void GetNotifications_FiltersByVisibilityAndReadState()
{
    var store = new NotificationStore();
    store.AddOrUpdate("unread", "Unread", DateTimeOffset.UtcNow.AddMinutes(-3));
    store.AddOrUpdate("read", "Read", DateTimeOffset.UtcNow.AddMinutes(-2));
    store.AddOrUpdate("hidden", "Hidden", DateTimeOffset.UtcNow.AddMinutes(-1));
    store.MarkRead("read");
    store.Hide("hidden");

    Assert.Equal(["hidden", "read", "unread"], store.GetNotifications(NotificationVisibilityFilter.All, null).Select(n => n.Id));
    Assert.Equal(["read", "unread"], store.GetNotifications(NotificationVisibilityFilter.Visible, null).Select(n => n.Id));
    Assert.Equal(["unread"], store.GetNotifications(NotificationVisibilityFilter.Unread, null).Select(n => n.Id));
    Assert.Equal(["read"], store.GetNotifications(NotificationVisibilityFilter.Read, null).Select(n => n.Id));
    Assert.Equal(["hidden"], store.GetNotifications(NotificationVisibilityFilter.Hidden, null).Select(n => n.Id));
}

[Fact]
public void GetNotifications_SearchesTitleMessageAndTagCaseInsensitively()
{
    var store = new NotificationStore();
    store.AddOrUpdate("title", "Body", DateTimeOffset.UtcNow, title: "Kitchen Alert");
    store.AddOrUpdate("message", "Garage door open", DateTimeOffset.UtcNow);
    store.AddOrUpdate("tag", "Other", DateTimeOffset.UtcNow, severity: "security");

    Assert.Equal(["title"], store.GetNotifications(NotificationVisibilityFilter.All, "kitchen").Select(n => n.Id));
    Assert.Equal(["message"], store.GetNotifications(NotificationVisibilityFilter.All, "GARAGE").Select(n => n.Id));
    Assert.Equal(["tag"], store.GetNotifications(NotificationVisibilityFilter.All, "Security").Select(n => n.Id));
}

[Fact]
public void GetNotifications_SearchDoesNotMatchIdsOrReferenceIds()
{
    var store = new NotificationStore();
    store.AddOrUpdate(
        "internal-id",
        "Normal message",
        DateTimeOffset.UtcNow,
        referenceId: "reference-token");

    Assert.Empty(store.GetNotifications(NotificationVisibilityFilter.All, "internal-id"));
    Assert.Empty(store.GetNotifications(NotificationVisibilityFilter.All, "reference-token"));
}
```

- [ ] **Step 2: Run tests and verify the expected failure**

Run:

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter NotificationStoreTests
```

Expected: build fails because `NotificationVisibilityFilter`, `GetNotifications`, `Hide`, `Unhide`, and `MarkAllRead` do not exist.

- [ ] **Step 3: Add `NotificationVisibilityFilter` and query implementation**

In `src/OpenHab.App/Notifications/NotificationStore.cs`, add this enum above `StoredNotification`:

```csharp
public enum NotificationVisibilityFilter
{
    Visible,
    Unread,
    Read,
    Hidden,
    All
}
```

Add this method after `GetAll()`:

```csharp
public IReadOnlyList<StoredNotification> GetNotifications(
    NotificationVisibilityFilter filter,
    string? searchText)
{
    lock (syncRoot)
    {
        return notifications.Values
            .Where(n => MatchesFilter(n, filter))
            .Where(n => MatchesSearch(n, searchText))
            .OrderByDescending(n => n.Created)
            .ToList();
    }
}
```

Add these private helpers near `TrimExcessLocked()`:

```csharp
private static bool MatchesFilter(StoredNotification notification, NotificationVisibilityFilter filter)
{
    return filter switch
    {
        NotificationVisibilityFilter.Visible => !notification.IsDismissed,
        NotificationVisibilityFilter.Unread => !notification.IsDismissed && !notification.IsRead,
        NotificationVisibilityFilter.Read => !notification.IsDismissed && notification.IsRead,
        NotificationVisibilityFilter.Hidden => notification.IsDismissed,
        NotificationVisibilityFilter.All => true,
        _ => true
    };
}

private static bool MatchesSearch(StoredNotification notification, string? searchText)
{
    if (string.IsNullOrWhiteSpace(searchText))
    {
        return true;
    }

    var query = searchText.Trim();
    return Contains(notification.Title, query)
        || Contains(notification.Message, query)
        || Contains(notification.Severity, query);
}

private static bool Contains(string? value, string query)
{
    return value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}
```

- [ ] **Step 4: Add hidden and mark-all-read commands**

Replace `Dismiss(string id)` with:

```csharp
public void Hide(string id)
{
    bool mutated = false;
    lock (syncRoot)
    {
        if (notifications.TryGetValue(id, out var existing) && !existing.IsDismissed)
        {
            notifications[id] = existing with { IsDismissed = true, IsRead = true };
            mutated = true;
        }
    }

    if (mutated)
    {
        Changed?.Invoke(this, EventArgs.Empty);
        _ = SaveAsync();
    }
}

public void Dismiss(string id)
{
    Hide(id);
}
```

Add these methods before `DismissAll()`:

```csharp
public void Unhide(string id)
{
    bool mutated = false;
    lock (syncRoot)
    {
        if (notifications.TryGetValue(id, out var existing) && existing.IsDismissed)
        {
            notifications[id] = existing with { IsDismissed = false };
            mutated = true;
        }
    }

    if (mutated)
    {
        Changed?.Invoke(this, EventArgs.Empty);
        _ = SaveAsync();
    }
}

public void MarkAllRead()
{
    bool mutated = false;
    lock (syncRoot)
    {
        foreach (var key in notifications.Keys.ToList())
        {
            var existing = notifications[key];
            if (!existing.IsDismissed && !existing.IsRead)
            {
                notifications[key] = existing with { IsRead = true };
                mutated = true;
            }
        }
    }

    if (mutated)
    {
        Changed?.Invoke(this, EventArgs.Empty);
        _ = SaveAsync();
    }
}
```

Keep `DismissAll()` as the legacy bulk-hide compatibility method. Update the assignment inside its loop so hidden notifications are also read:

```csharp
notifications[key] = existing with { IsDismissed = true, IsRead = true };
```

- [ ] **Step 5: Run app-layer tests**

Run:

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter NotificationStoreTests
```

Expected: all `NotificationStoreTests` pass.

- [ ] **Step 6: Commit app-layer changes**

Run:

```powershell
git add src/OpenHab.App/Notifications/NotificationStore.cs tests/OpenHab.App.Tests/Notifications/NotificationStoreTests.cs
git commit -m "feat: add notification management semantics"
```

## Task 2: Notifications Header Search And Filter Controls

**Files:**
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`

- [ ] **Step 1: Update notifications tab XAML controls**

In `MainWindow.xaml`, inside the `PivotItem Header="Notifications"` grid:

1. Change the row definitions from three rows to four rows:

```xml
<Grid.RowDefinitions>
    <RowDefinition Height="Auto" />
    <RowDefinition Height="Auto" />
    <RowDefinition Height="Auto" />
    <RowDefinition Height="*" />
</Grid.RowDefinitions>
```

2. Rename the button in the header:

```xml
<Button Grid.Column="2"
        x:Name="MarkAllReadButton"
        Content="Mark all read"
        Click="MarkAllReadButton_Click" />
```

3. Add this management row between the header grid and `LocalOnlyNote`:

```xml
<Grid Grid.Row="1" ColumnSpacing="8">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>
    <TextBox x:Name="NotificationSearchBox"
             PlaceholderText="Search notifications"
             TextChanged="NotificationSearchBox_TextChanged" />
    <ComboBox Grid.Column="1"
              x:Name="NotificationFilterBox"
              MinWidth="120"
              SelectedIndex="0"
              SelectionChanged="NotificationFilterBox_SelectionChanged">
        <ComboBoxItem Content="Visible" Tag="Visible" />
        <ComboBoxItem Content="Unread" Tag="Unread" />
        <ComboBoxItem Content="Read" Tag="Read" />
        <ComboBoxItem Content="Hidden" Tag="Hidden" />
        <ComboBoxItem Content="All" Tag="All" />
    </ComboBox>
</Grid>
```

4. Move `LocalOnlyNote` to `Grid.Row="2"` and the list container to `Grid.Row="3"`.

- [ ] **Step 2: Add filter state helpers to code-behind**

In `MainWindow.xaml.cs`, add these helper methods near `RefreshNotificationList()`:

```csharp
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
```

- [ ] **Step 3: Wire search and filter event handlers**

Replace `DismissAllButton_Click` with:

```csharp
private void MarkAllReadButton_Click(object sender, RoutedEventArgs e)
{
    notificationStore?.MarkAllRead();
}
```

Add these handlers near the button handler:

```csharp
private void NotificationSearchBox_TextChanged(object sender, TextChangedEventArgs e)
{
    RefreshNotificationList();
}

private void NotificationFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    RefreshNotificationList();
}
```

- [ ] **Step 4: Query filtered notifications and update empty-state text**

In `RefreshNotificationList()`, replace:

```csharp
var notifications = notificationStore.GetAll();
```

with:

```csharp
var filter = CurrentNotificationFilter;
var searchText = CurrentNotificationSearchText;
var notifications = notificationStore.GetNotifications(filter, searchText);
```

Replace the empty-state update:

```csharp
EmptyNotificationsText.Visibility = notifications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
```

with:

```csharp
EmptyNotificationsText.Text = GetEmptyNotificationsText(filter, searchText);
EmptyNotificationsText.Visibility = notifications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
```

Add this helper near `RefreshNotificationList()`:

```csharp
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
```

- [ ] **Step 5: Build tray project**

Run:

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build succeeds.

- [ ] **Step 6: Commit header search/filter UI**

Run:

```powershell
git add src/OpenHab.Windows.Tray/MainWindow.xaml src/OpenHab.Windows.Tray/MainWindow.xaml.cs
git commit -m "feat: add notification inbox filters"
```

## Task 3: Row Context Menu Management

**Files:**
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`

- [ ] **Step 1: Add context menu builder**

In `MainWindow.xaml.cs`, add this helper near `RefreshNotificationList()`:

```csharp
private MenuFlyout CreateNotificationContextMenu(StoredNotification notification)
{
    var menu = new MenuFlyout();

    var readItem = new MenuFlyoutItem
    {
        Text = notification.IsRead ? "Mark unread" : "Mark read"
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
        Text = notification.IsDismissed ? "Unhide" : "Hide"
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
```

- [ ] **Step 2: Attach the context menu to each notification row**

In `RefreshNotificationList()`, inside the `foreach (var n in notifications)` loop, after the `Button` object is created and before its `Click` handler, add:

```csharp
button.ContextFlyout = CreateNotificationContextMenu(n);
```

The button creation block should contain:

```csharp
var button = new Button
{
    Content = row,
    HorizontalAlignment = HorizontalAlignment.Stretch,
    HorizontalContentAlignment = HorizontalAlignment.Stretch,
    Padding = new Thickness(0),
    BorderThickness = new Thickness(0)
};
button.ContextFlyout = CreateNotificationContextMenu(n);
button.Click += (_, _) =>
{
    if (capturedIsUnread)
        notificationStore.MarkRead(capturedId);
    else
        notificationStore.MarkUnread(capturedId);
};
```

- [ ] **Step 3: Relabel notification tag in UI comments and variables**

In `RefreshNotificationList()`, rename local `hasSeverity` to `hasTag`. The rendered text can continue to use `n.Severity`:

```csharp
var hasTag = !string.IsNullOrWhiteSpace(n.Severity);
```

Then change:

```csharp
if (hasSeverity)
```

to:

```csharp
if (hasTag)
```

- [ ] **Step 4: Build tray project**

Run:

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build succeeds.

- [ ] **Step 5: Commit row context menu**

Run:

```powershell
git add src/OpenHab.Windows.Tray/MainWindow.xaml.cs
git commit -m "feat: add notification row context actions"
```

## Task 4: Verification And Cleanup

**Files:**
- Verify: `src/OpenHab.App/Notifications/NotificationStore.cs`
- Verify: `src/OpenHab.Windows.Tray/MainWindow.xaml`
- Verify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- Verify: `tests/OpenHab.App.Tests/Notifications/NotificationStoreTests.cs`

- [ ] **Step 1: Run targeted app tests**

Run:

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter NotificationStoreTests
```

Expected: all selected tests pass.

- [ ] **Step 2: Run direct app test project**

Run:

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj
```

Expected: test project passes, except for any pre-existing WinUI runtime failures already documented in status docs.

- [ ] **Step 3: Build tray app**

Run:

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build succeeds.

- [ ] **Step 4: Run full solution tests when practical**

Run:

```powershell
dotnet test OpenHab.Windows.sln
```

Expected: passes in a fully provisioned environment. If it fails because Windows App Runtime or DesktopBridge prerequisites are missing, record the exact failure and run the direct project gates from `docs/superpowers/verification/openhab-windows-quality-gates.md`.

- [ ] **Step 5: Check final git diff**

Run:

```powershell
git status --short
git diff -- src/OpenHab.App/Notifications/NotificationStore.cs tests/OpenHab.App.Tests/Notifications/NotificationStoreTests.cs src/OpenHab.Windows.Tray/MainWindow.xaml src/OpenHab.Windows.Tray/MainWindow.xaml.cs
```

Expected: only notification-management changes are present in the listed files. Existing unrelated package or planning files may remain in the worktree and must not be reverted.
