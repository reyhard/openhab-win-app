using System.Text.Json;

namespace OpenHab.App.Notifications;

public enum NotificationVisibilityFilter
{
    Visible,
    Unread,
    Read,
    Hidden,
    All
}

public sealed record StoredNotification(
    string Id,
    string Message,
    string? Title,
    string? Icon,
    string? Severity,
    DateTimeOffset Created,
    DateTimeOffset ReceivedAt,
    bool IsRead,
    bool IsDismissed,
    string? ReferenceId,
    string? OnClickAction,
    string? MediaAttachmentUrl,
    string? ActionButton1,
    string? ActionButton2,
    string? ActionButton3
);

public sealed class NotificationStore
{
    private const int MaxEntries = 500;

    private static readonly string StorageFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenHab.WinApp",
        "notifications.json");

    private readonly object syncRoot = new();
    private readonly Dictionary<string, StoredNotification> notifications = new();

    public event EventHandler? Changed;
    public int UnreadCount
    {
        get
        {
            lock (syncRoot)
            {
                return notifications.Values.Count(n => !n.IsRead && !n.IsDismissed);
            }
        }
    }

    public NotificationStore()
    {
        TryLoad();
    }

    public IReadOnlyList<StoredNotification> GetAll()
    {
        lock (syncRoot)
        {
            return notifications.Values
                .OrderByDescending(n => n.Created)
                .ToList();
        }
    }

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

    public IReadOnlySet<string> GetSeenUndismissedIds()
    {
        lock (syncRoot)
        {
            return notifications.Values
                .Where(n => !n.IsDismissed)
                .Select(n => n.Id)
                .ToHashSet();
        }
    }

    public bool IsSeen(string notificationId)
    {
        lock (syncRoot)
        {
            return notifications.ContainsKey(notificationId);
        }
    }

    public bool IsDismissed(string notificationId)
    {
        lock (syncRoot)
        {
            return notifications.TryGetValue(notificationId, out var n) && n.IsDismissed;
        }
    }

    public void AddOrUpdate(
        string id,
        string message,
        DateTimeOffset created,
        string? title = null,
        string? icon = null,
        string? severity = null,
        string? referenceId = null,
        string? onClickAction = null,
        string? mediaAttachmentUrl = null,
        string? actionButton1 = null,
        string? actionButton2 = null,
        string? actionButton3 = null)
    {
        bool mutated;
        lock (syncRoot)
        {
            var existingKey = id;
            var existing = notifications.TryGetValue(id, out var directMatch)
                ? directMatch
                : null;

            if (existing is null
                && !notifications.ContainsKey(id)
                && !string.IsNullOrWhiteSpace(referenceId))
            {
                var referenceMatch = notifications
                    .Where(entry =>
                        !string.IsNullOrWhiteSpace(entry.Value.ReferenceId)
                        && string.Equals(entry.Value.ReferenceId, referenceId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(entry => entry.Value.IsDismissed ? 1 : 0)
                    .ThenByDescending(entry => entry.Value.Created)
                    .FirstOrDefault();

                if (!referenceMatch.Equals(default(KeyValuePair<string, StoredNotification>)))
                {
                    existingKey = referenceMatch.Key;
                    existing = referenceMatch.Value;
                }
            }

            if (existing is not null)
            {
                var updated = existing with
                {
                    Id = id,
                    Message = message,
                    Title = title ?? existing.Title,
                    Icon = icon ?? existing.Icon,
                    Severity = severity ?? existing.Severity,
                    Created = created,
                    ReferenceId = referenceId ?? existing.ReferenceId,
                    OnClickAction = onClickAction ?? existing.OnClickAction,
                    MediaAttachmentUrl = mediaAttachmentUrl ?? existing.MediaAttachmentUrl,
                    ActionButton1 = actionButton1 ?? existing.ActionButton1,
                    ActionButton2 = actionButton2 ?? existing.ActionButton2,
                    ActionButton3 = actionButton3 ?? existing.ActionButton3
                };

                if (!string.Equals(existingKey, id, StringComparison.Ordinal))
                {
                    notifications.Remove(existingKey);
                }

                notifications[id] = updated;
                mutated = true;
            }
            else
            {
                var stored = new StoredNotification(
                    Id: id,
                    Message: message,
                    Title: title,
                    Icon: icon,
                    Severity: severity,
                    Created: created,
                    ReceivedAt: DateTimeOffset.UtcNow,
                    IsRead: false,
                    IsDismissed: false,
                    ReferenceId: referenceId,
                    OnClickAction: onClickAction,
                    MediaAttachmentUrl: mediaAttachmentUrl,
                    ActionButton1: actionButton1,
                    ActionButton2: actionButton2,
                    ActionButton3: actionButton3);
                notifications[id] = stored;
                mutated = true;

                if (notifications.Count > MaxEntries)
                {
                    TrimExcessLocked();
                }
            }
        }

        if (mutated)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            _ = SaveAsync();
        }
    }

    private void HideMatchingNotifications(Func<StoredNotification, bool> matches)
    {
        bool mutated = false;
        lock (syncRoot)
        {
            foreach (var key in notifications.Keys.ToList())
            {
                var existing = notifications[key];
                if (!matches(existing))
                {
                    continue;
                }

                if (!existing.IsDismissed || !existing.IsRead)
                {
                    notifications[key] = existing with { IsDismissed = true, IsRead = true };
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

    public void MarkRead(string id)
    {
        bool mutated = false;
        lock (syncRoot)
        {
            if (notifications.TryGetValue(id, out var existing) && !existing.IsRead)
            {
                notifications[id] = existing with { IsRead = true };
                mutated = true;
            }
        }

        if (mutated)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            _ = SaveAsync();
        }
    }

    public void MarkUnread(string id)
    {
        bool mutated = false;
        lock (syncRoot)
        {
            if (notifications.TryGetValue(id, out var existing) && existing.IsRead)
            {
                notifications[id] = existing with { IsRead = false };
                mutated = true;
            }
        }

        if (mutated)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            _ = SaveAsync();
        }
    }

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

    public void HideByReferenceId(string referenceId)
    {
        HideMatchingNotifications(notification =>
            !string.IsNullOrWhiteSpace(notification.ReferenceId)
            && string.Equals(notification.ReferenceId, referenceId, StringComparison.OrdinalIgnoreCase));
    }

    public void HideByTag(string tag)
    {
        HideMatchingNotifications(notification =>
            !string.IsNullOrWhiteSpace(notification.Severity)
            && string.Equals(notification.Severity, tag, StringComparison.OrdinalIgnoreCase));
    }

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

    public void DismissAll()
    {
        bool mutated = false;
        lock (syncRoot)
        {
            foreach (var key in notifications.Keys.ToList())
            {
                var existing = notifications[key];
                if (!existing.IsDismissed)
                {
                    notifications[key] = existing with { IsDismissed = true, IsRead = true };
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

    private void TrimExcessLocked()
    {
        // Remove oldest dismissed entries first.
        var dismissed = notifications.Values
            .Where(n => n.IsDismissed)
            .OrderBy(n => n.Created)
            .ToList();

        foreach (var d in dismissed)
        {
            if (notifications.Count <= MaxEntries) break;
            notifications.Remove(d.Id);
        }

        // If still over limit, remove oldest remaining entries.
        if (notifications.Count > MaxEntries)
        {
            var remaining = notifications.Values
                .OrderBy(n => n.Created)
                .ToList();

            foreach (var r in remaining)
            {
                if (notifications.Count <= MaxEntries) break;
                notifications.Remove(r.Id);
            }
        }
    }

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

    private async Task SaveAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(StorageFilePath)!;
            Directory.CreateDirectory(directory);

            List<StoredNotification> snapshot;
            lock (syncRoot)
            {
                snapshot = notifications.Values.ToList();
            }

            var data = new NotificationStoreData(snapshot);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(StorageFilePath, json);
        }
        catch
        {
            // Best-effort persistence — swallow IO errors.
        }
    }

    private void TryLoad()
    {
        try
        {
            if (!File.Exists(StorageFilePath)) return;
            var json = File.ReadAllText(StorageFilePath);
            var loaded = JsonSerializer.Deserialize<NotificationStoreData>(json);
            if (loaded?.Notifications is not null)
            {
                lock (syncRoot)
                {
                    foreach (var n in loaded.Notifications)
                    {
                        notifications[n.Id] = n;
                    }
                }
            }
        }
        catch
        {
            // Corrupt or missing file — start with empty store.
        }
    }

    private sealed record NotificationStoreData(List<StoredNotification> Notifications);
}
