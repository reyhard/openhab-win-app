# openHAB Windows Notification Management Design

Date: 2026-05-11

## Purpose

Add notification management options to the Windows companion app inbox:

- mark notifications unread/read
- hide individual notifications
- reveal and manage hidden notifications
- search notifications

The feature should preserve the existing project layering. Notification state and query behavior belong in `OpenHab.App.Notifications`; WinUI should render the resulting list and wire user actions.

## Current Context

The app already has a persisted `NotificationStore` with:

- `StoredNotification.IsRead`
- `StoredNotification.IsDismissed`
- `UnreadCount`
- `MarkRead`
- `MarkUnread`
- `Dismiss`
- `DismissAll`
- persisted storage in `%localappdata%\OpenHab.WinApp\notifications.json`

The current main-window notifications tab renders `notificationStore.GetAll()`, displays an unread badge, lets left-click toggle read/unread, and exposes `Dismiss all`. The existing persisted `IsDismissed` field should remain for compatibility, but user-facing behavior should call this state "hidden".

## Behavior

Notifications have two independent management states:

- read/unread
- visible/hidden

Rules:

- New notifications start visible and unread.
- `Mark all read` replaces the current user-facing `Dismiss all` action.
- `Mark all read` marks every non-hidden notification as read.
- `Hide` is a per-notification action.
- Hiding an unread notification also marks it read, so it leaves the unread count immediately.
- Hidden notifications do not appear in `Visible`, `Unread`, or `Read` filters.
- Hidden notifications appear in `Hidden` and `All`.
- Hidden notifications can be restored with `Unhide`.
- `Unhide` restores visibility without changing the notification read state.
- `Mark unread` remains available for read notifications, including hidden notifications when the active filter exposes them.
- The unread badge counts visible unread notifications only.

## UI

The notifications tab keeps the current inbox layout and adds a compact management row under the header:

- a search box with placeholder text `Search notifications`
- a filter selector with `Visible`, `Unread`, `Read`, `Hidden`, and `All`
- a renamed bulk action button: `Mark all read`

The row right-click context menu is the primary per-notification management path:

- unread rows show `Mark read`
- read rows show `Mark unread`
- visible rows show `Hide`
- hidden rows show `Unhide`

Left-click behavior remains simple and keeps the current interaction: clicking a row toggles read/unread.

Search filters within the selected notification filter. Empty-state text should reflect the active state, for example:

- `No notifications`
- `No unread notifications`
- `No hidden notifications`
- `No matching notifications`

## App-Layer API

Add a small query surface to `OpenHab.App.Notifications` so UI code does not own notification semantics.

Suggested API shape:

```csharp
public enum NotificationVisibilityFilter
{
    Visible,
    Unread,
    Read,
    Hidden,
    All
}

public IReadOnlyList<StoredNotification> GetNotifications(
    NotificationVisibilityFilter filter,
    string? searchText);
```

Add store commands:

- `Hide(string id)`
- `Unhide(string id)`
- `MarkAllRead()`

The existing `Dismiss` and `DismissAll` methods may remain as compatibility wrappers if needed by current callers or persisted terminology. New UI and tests should use hidden/read language.

The store owns:

- filtering by visibility/read state
- case-insensitive search
- hidden/read side effects
- persistence and `Changed` events after mutation

## Search

Search matches these user-facing fields:

- title
- message
- tag

The current storage field for tag is `Severity`. It is deprecated as a name, but renaming the persisted field is not required for this feature. UI should label it as a tag.

Search must not match internal identifiers:

- notification id
- reference id

## Persistence And Compatibility

No storage migration is required for the first implementation. Existing `notifications.json` entries with `IsDismissed = true` should load as hidden notifications.

If future work renames the persisted field, it should include an explicit migration and backward-compatible JSON handling. That is out of scope for this feature.

## Testing

Focused app-layer tests should cover:

- `Hide` sets hidden and marks unread notifications read.
- `Unhide` restores visibility without making the notification unread.
- `MarkAllRead` marks non-hidden notifications read.
- `Visible`, `Unread`, `Read`, `Hidden`, and `All` filters return the correct notifications.
- Search matches title, message, and tag case-insensitively.
- Search does not match notification id or reference id.
- Existing dismissed entries still load as hidden.
- `UnreadCount` excludes hidden notifications.

UI wiring should be checked with a tray app build after implementation. Direct app-layer tests are the primary iteration gate; full solution tests remain the completion gate when practical.

## Out Of Scope

- Swipe gestures.
- A visible per-row action button.
- Renaming the persisted `Severity` or `IsDismissed` JSON fields.
- Deleting hidden notifications permanently.
- Server-side notification updates.
- Search across internal ids or reference ids.
