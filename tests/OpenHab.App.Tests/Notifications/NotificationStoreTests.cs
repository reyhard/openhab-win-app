using OpenHab.App.Notifications;

namespace OpenHab.App.Tests.Notifications;

public sealed class NotificationStoreTests
{
    private static readonly string StorageFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenHab.WinApp",
        "notifications.json");

    public NotificationStoreTests()
    {
        // Retry deletion — fire-and-forget SaveAsync from a previous test may still be writing.
        for (int i = 0; i < 5; i++)
        {
            try { File.Delete(StorageFilePath); } catch { }
            if (!File.Exists(StorageFilePath)) break;
            Thread.Sleep(10);
        }
    }

    // ─────────────────────── AddOrUpdate ───────────────────────

    [Fact]
    public void AddOrUpdate_NewNotification_StoresCorrectly()
    {
        var store = new NotificationStore();
        var created = new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero);

        store.AddOrUpdate("n1", "Test message", created, title: "Title", severity: "high");

        var all = store.GetAll();
        Assert.Single(all);
        var n = all[0];
        Assert.Equal("n1", n.Id);
        Assert.Equal("Test message", n.Message);
        Assert.Equal("Title", n.Title);
        Assert.Equal("high", n.Severity);
        Assert.Equal(created, n.Created);
        Assert.False(n.IsRead);
        Assert.False(n.IsDismissed);
        Assert.NotEqual(default, n.ReceivedAt);
    }

    [Fact]
    public void AddOrUpdate_DuplicateId_UpdatesExisting()
    {
        var store = new NotificationStore();
        var created1 = new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero);
        var created2 = new DateTimeOffset(2026, 5, 7, 11, 0, 0, TimeSpan.Zero);

        store.AddOrUpdate("n1", "Original message", created1);
        store.MarkRead("n1");
        var original = store.GetAll()[0];
        var originalReceivedAt = original.ReceivedAt;

        store.AddOrUpdate("n1", "Updated message", created2, severity: "low");

        var updated = store.GetAll()[0];
        Assert.Equal("Updated message", updated.Message);
        Assert.Equal(created2, updated.Created);
        Assert.Equal("low", updated.Severity);
        // Must preserve IsRead
        Assert.True(updated.IsRead);
        // Must preserve ReceivedAt
        Assert.Equal(originalReceivedAt, updated.ReceivedAt);
    }

    [Fact]
    public void AddOrUpdate_DuplicateId_PreservesDismissedState()
    {
        var store = new NotificationStore();
        var created = new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero);

        store.AddOrUpdate("n1", "Original", created);
        store.Dismiss("n1");

        Assert.True(store.IsDismissed("n1"));

        store.AddOrUpdate("n1", "Updated", created);

        Assert.True(store.IsDismissed("n1"));
    }

    // ─────────────────────── IsSeen ───────────────────────

    [Fact]
    public void IsSeen_KnownId_ReturnsTrue()
    {
        var store = new NotificationStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);

        Assert.True(store.IsSeen("n1"));
    }

    [Fact]
    public void IsSeen_UnknownId_ReturnsFalse()
    {
        var store = new NotificationStore();

        Assert.False(store.IsSeen("nonexistent"));
    }

    // ─────────────────────── IsDismissed ───────────────────────

    [Fact]
    public void IsDismissed_DismissedNotification_ReturnsTrue()
    {
        var store = new NotificationStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);
        store.Dismiss("n1");

        Assert.True(store.IsDismissed("n1"));
    }

    // ─────────────────────── MarkRead / MarkUnread ───────────────────────

    [Fact]
    public void MarkRead_SetsIsReadTrue()
    {
        var store = new NotificationStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);

        Assert.False(store.GetAll()[0].IsRead);

        store.MarkRead("n1");

        Assert.True(store.GetAll()[0].IsRead);
    }

    [Fact]
    public void MarkUnread_SetsIsReadFalse()
    {
        var store = new NotificationStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);
        store.MarkRead("n1");

        Assert.True(store.GetAll()[0].IsRead);

        store.MarkUnread("n1");

        Assert.False(store.GetAll()[0].IsRead);
    }

    // ─────────────────────── Dismiss / DismissAll ───────────────────────

    [Fact]
    public void Dismiss_SetsIsDismissedTrue()
    {
        var store = new NotificationStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);

        Assert.False(store.IsDismissed("n1"));

        store.Dismiss("n1");

        Assert.True(store.IsDismissed("n1"));
    }

    [Fact]
    public void DismissAll_DismissesAllNotifications()
    {
        var store = new NotificationStore();
        store.AddOrUpdate("n1", "msg1", DateTimeOffset.UtcNow);
        store.AddOrUpdate("n2", "msg2", DateTimeOffset.UtcNow);
        store.AddOrUpdate("n3", "msg3", DateTimeOffset.UtcNow);

        store.DismissAll();

        Assert.True(store.IsDismissed("n1"));
        Assert.True(store.IsDismissed("n2"));
        Assert.True(store.IsDismissed("n3"));
    }

    // ─────────────────────── UnreadCount ───────────────────────

    [Fact]
    public void UnreadCount_ReflectsUnreadNotifications()
    {
        var store = new NotificationStore();
        store.AddOrUpdate("n1", "msg1", DateTimeOffset.UtcNow);
        store.AddOrUpdate("n2", "msg2", DateTimeOffset.UtcNow);

        Assert.Equal(2, store.UnreadCount);

        store.MarkRead("n1");

        Assert.Equal(1, store.UnreadCount);

        store.MarkUnread("n1");

        Assert.Equal(2, store.UnreadCount);
    }

    // ─────────────────────── GetSeenUndismissedIds ───────────────────────

    [Fact]
    public void GetSeenUndismissedIds_ExcludesDismissed()
    {
        var store = new NotificationStore();
        store.AddOrUpdate("n1", "msg1", DateTimeOffset.UtcNow);
        store.AddOrUpdate("n2", "msg2", DateTimeOffset.UtcNow);
        store.AddOrUpdate("n3", "msg3", DateTimeOffset.UtcNow);
        store.Dismiss("n2");

        var ids = store.GetSeenUndismissedIds();

        Assert.Equal(2, ids.Count);
        Assert.Contains("n1", ids);
        Assert.Contains("n3", ids);
        Assert.DoesNotContain("n2", ids);
    }

    // ─────────────────────── Trim ───────────────────────

    [Fact]
    public void Trim_RemovesOldestDismissed_WhenExceedsMax()
    {
        var store = new NotificationStore();
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Add 501 entries all dismissed, each with increasing Created time.
        for (int i = 0; i < 501; i++)
        {
            store.AddOrUpdate($"n{i}", $"msg{i}", baseTime.AddMinutes(i));
            store.Dismiss($"n{i}");
        }

        var all = store.GetAll();
        Assert.True(all.Count <= 500);

        // The oldest (n0) should have been trimmed.
        Assert.False(store.IsSeen("n0"));
        // The newest (n500) should still exist.
        Assert.True(store.IsSeen("n500"));
    }

    // ─────────────────────── Round-trip persistence ───────────────────────

    [Fact]
    public void Store_SurvivesRoundTrip_LoadsCorrectly()
    {
        // Give the fire-and-forget save time to complete.
        var original = new NotificationStore();
        var created = new DateTimeOffset(2026, 5, 7, 14, 0, 0, TimeSpan.Zero);
        original.AddOrUpdate("persist1", "Hello", created, title: "T", severity: "info");
        Thread.Sleep(200);

        // A new instance should load from the same file.
        var loaded = new NotificationStore();

        Assert.True(loaded.IsSeen("persist1"));
        var n = loaded.GetAll()[0];
        Assert.Equal("persist1", n.Id);
        Assert.Equal("Hello", n.Message);
        Assert.Equal("T", n.Title);
        Assert.Equal("info", n.Severity);
        Assert.Equal(created, n.Created);
    }

    // ─────────────────────── GetAll ordering ───────────────────────

    [Fact]
    public void GetAll_ReturnsNewestFirst()
    {
        var store = new NotificationStore();
        var t1 = new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 5, 7, 11, 0, 0, TimeSpan.Zero);
        var t3 = new DateTimeOffset(2026, 5, 7, 12, 0, 0, TimeSpan.Zero);

        store.AddOrUpdate("a", "oldest", t1);
        store.AddOrUpdate("b", "middle", t2);
        store.AddOrUpdate("c", "newest", t3);

        var all = store.GetAll();

        Assert.Equal(3, all.Count);
        Assert.Equal("c", all[0].Id);
        Assert.Equal("b", all[1].Id);
        Assert.Equal("a", all[2].Id);
    }

    // ─────────────────────── Changed event ───────────────────────

    [Fact]
    public void Changed_FiresOnAdd()
    {
        var store = new NotificationStore();
        var fired = false;
        store.Changed += (_, _) => fired = true;

        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);

        Assert.True(fired);
    }

    [Fact]
    public void Changed_FiresOnMarkRead()
    {
        var store = new NotificationStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);
        var fired = false;
        store.Changed += (_, _) => fired = true;

        store.MarkRead("n1");

        Assert.True(fired);
    }

    [Fact]
    public void Changed_FiresOnDismiss()
    {
        var store = new NotificationStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);
        var fired = false;
        store.Changed += (_, _) => fired = true;

        store.Dismiss("n1");

        Assert.True(fired);
    }

    [Fact]
    public void Changed_FiresOnDismissAll()
    {
        var store = new NotificationStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);
        var fired = false;
        store.Changed += (_, _) => fired = true;

        store.DismissAll();

        Assert.True(fired);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenMarkReadOnAlreadyRead()
    {
        var store = new NotificationStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);
        store.MarkRead("n1");
        var fired = false;
        store.Changed += (_, _) => fired = true;

        store.MarkRead("n1");

        Assert.False(fired);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenDismissOnAlreadyDismissed()
    {
        var store = new NotificationStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);
        store.Dismiss("n1");
        var fired = false;
        store.Changed += (_, _) => fired = true;

        store.Dismiss("n1");

        Assert.False(fired);
    }
}
