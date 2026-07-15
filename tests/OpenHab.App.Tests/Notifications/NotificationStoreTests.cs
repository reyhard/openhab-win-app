using OpenHab.App.Notifications;
using System.Text.Json;

namespace OpenHab.App.Tests.Notifications;

public sealed class NotificationStoreTests : IDisposable
{
    private readonly string tempRoot;
    private readonly string storageFilePath;

    public NotificationStoreTests()
    {
        tempRoot = Path.Combine(
            Path.GetTempPath(),
            "OpenHab.NotificationStoreTests",
            Guid.NewGuid().ToString("N"));
        storageFilePath = Path.Combine(tempRoot, "notifications.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup: fire-and-forget store saves may still hold handles.
        }
    }

    private NotificationStore CreateStore(bool persistChanges = false)
    {
        return new NotificationStore(storageFilePath, persistChanges);
    }

    private static StoredNotification CreateStoredNotification(
        string id,
        DateTimeOffset created,
        DateTimeOffset receivedAt)
    {
        return new StoredNotification(
            id,
            id,
            null,
            null,
            null,
            created,
            receivedAt,
            false,
            false,
            null,
            null,
            null,
            null,
            null,
            null);
    }

    // ─────────────────────── AddOrUpdate ───────────────────────

    [Fact]
    public void AddOrUpdate_NewNotification_StoresCorrectly()
    {
        var store = CreateStore();
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
        var store = CreateStore();
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
        var store = CreateStore();
        var created = new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero);

        store.AddOrUpdate("n1", "Original", created);
        store.Dismiss("n1");

        Assert.True(store.IsDismissed("n1"));

        store.AddOrUpdate("n1", "Updated", created);

        Assert.True(store.IsDismissed("n1"));
    }

    [Fact]
    public void AddOrUpdate_DuplicateId_PreservesUnreadDismissedState()
    {
        var store = CreateStore();
        var created1 = new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero);
        var created2 = new DateTimeOffset(2026, 5, 7, 11, 0, 0, TimeSpan.Zero);

        store.AddOrUpdate("n1", "Original", created1);
        store.Hide("n1");
        store.MarkUnread("n1");

        store.AddOrUpdate("n1", "Updated", created2);

        var notification = store.GetAll().Single(n => n.Id == "n1");
        Assert.True(notification.IsDismissed);
        Assert.False(notification.IsRead);
    }

    [Fact]
    public void AddOrUpdate_DirectIdMatchWinsOverReferenceReplacement()
    {
        var store = CreateStore();
        var created1 = new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero);
        var created2 = new DateTimeOffset(2026, 5, 7, 11, 0, 0, TimeSpan.Zero);

        store.AddOrUpdate("cloud-1", "Original 1", created1, referenceId: "ref-a");
        store.AddOrUpdate("cloud-2", "Original 2", created1, referenceId: "ref-b");

        store.AddOrUpdate("cloud-2", "Updated 2", created2, referenceId: "ref-a");

        var all = store.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Equal("Original 1", all.Single(n => n.Id == "cloud-1").Message);
        Assert.Equal("Updated 2", all.Single(n => n.Id == "cloud-2").Message);
    }

    [Fact]
    public void AddOrUpdate_PrefersVisibleReferenceMatchOverDismissedMatch()
    {
        var hiddenCreated = new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero);
        var visibleCreated = new DateTimeOffset(2026, 5, 7, 11, 0, 0, TimeSpan.Zero);
        var replacementCreated = new DateTimeOffset(2026, 5, 7, 12, 0, 0, TimeSpan.Zero);

        Directory.CreateDirectory(Path.GetDirectoryName(storageFilePath)!);

        var hidden = new StoredNotification(
            "cloud-hidden",
            "Hidden original",
            null,
            null,
            "info",
            hiddenCreated,
            DateTimeOffset.UtcNow,
            true,
            true,
            "ref-1",
            null,
            null,
            null,
            null,
            null);

        var visible = new StoredNotification(
            "cloud-visible",
            "Visible original",
            null,
            null,
            "info",
            visibleCreated,
            DateTimeOffset.UtcNow,
            false,
            false,
            "ref-1",
            null,
            null,
            null,
            null,
            null);

        File.WriteAllText(
            storageFilePath,
            JsonSerializer.Serialize(new { Notifications = new[] { hidden, visible } }));

        var store = CreateStore();
        store.AddOrUpdate("cloud-new", "Replacement", replacementCreated, referenceId: "ref-1");

        var all = store.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, n => n.Id == "cloud-hidden" && n.IsDismissed && n.IsRead);
        Assert.Contains(all, n => n.Id == "cloud-new" && !n.IsDismissed && !n.IsRead);
        Assert.DoesNotContain(all, n => n.Id == "cloud-visible");
    }

    [Fact]
    public void AddOrUpdate_PrefersLatestReceivedAtWhenCreatedMatches()
    {
        var created = new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero);
        var olderReceived = new DateTimeOffset(2026, 5, 7, 10, 1, 0, TimeSpan.Zero);
        var newerReceived = new DateTimeOffset(2026, 5, 7, 10, 2, 0, TimeSpan.Zero);
        var replacementCreated = new DateTimeOffset(2026, 5, 7, 10, 3, 0, TimeSpan.Zero);

        Directory.CreateDirectory(Path.GetDirectoryName(storageFilePath)!);

        var older = new StoredNotification(
            "cloud-older",
            "Older original",
            null,
            null,
            "info",
            created,
            olderReceived,
            false,
            false,
            "ref-2",
            null,
            null,
            null,
            null,
            null);

        var newer = new StoredNotification(
            "cloud-newer",
            "Newer original",
            null,
            null,
            "info",
            created,
            newerReceived,
            false,
            false,
            "ref-2",
            null,
            null,
            null,
            null,
            null);

        File.WriteAllText(
            storageFilePath,
            JsonSerializer.Serialize(new { Notifications = new[] { older, newer } }));

        var store = CreateStore();
        store.AddOrUpdate("cloud-replacement", "Replacement", replacementCreated, referenceId: "ref-2");

        var all = store.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, n => n.Id == "cloud-older");
        Assert.Contains(all, n => n.Id == "cloud-replacement");
        Assert.DoesNotContain(all, n => n.Id == "cloud-newer");
    }

    [Fact]
    public void AddOrUpdate_ReplacesVisibleNotificationWithSameReferenceId()
    {
        var store = CreateStore();
        var created1 = new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero);
        var created2 = new DateTimeOffset(2026, 5, 7, 11, 0, 0, TimeSpan.Zero);

        store.AddOrUpdate("cloud-1", "Original", created1, referenceId: "Ref-123");
        store.MarkRead("cloud-1");

        store.AddOrUpdate("cloud-2", "Replacement", created2, referenceId: "ref-123");

        var all = store.GetAll();
        Assert.Single(all);

        var notification = all[0];
        Assert.Equal("cloud-2", notification.Id);
        Assert.Equal("Replacement", notification.Message);
        Assert.Equal("ref-123", notification.ReferenceId);
        Assert.True(notification.IsRead);
        Assert.False(notification.IsDismissed);
    }

    [Fact]
    public void AddOrUpdate_ByReferenceIdPreservesHiddenState()
    {
        var store = CreateStore();
        var created1 = new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero);
        var created2 = new DateTimeOffset(2026, 5, 7, 11, 0, 0, TimeSpan.Zero);

        store.AddOrUpdate("cloud-1", "Original", created1, referenceId: "Ref-123");
        store.Hide("cloud-1");

        store.AddOrUpdate("cloud-2", "Replacement", created2, referenceId: "ref-123");

        var all = store.GetAll();
        Assert.Single(all);

        var notification = all[0];
        Assert.Equal("cloud-2", notification.Id);
        Assert.True(notification.IsDismissed);
        Assert.True(notification.IsRead);
    }

    [Fact]
    public void AddOrUpdate_ReplacesHiddenUnreadNotificationAndForcesRead()
    {
        var store = CreateStore();
        var created1 = new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero);
        var created2 = new DateTimeOffset(2026, 5, 7, 11, 0, 0, TimeSpan.Zero);

        store.AddOrUpdate("cloud-1", "Original", created1, referenceId: "Ref-123");
        store.Hide("cloud-1");
        store.MarkUnread("cloud-1");

        store.AddOrUpdate("cloud-2", "Replacement", created2, referenceId: "ref-123");

        var all = store.GetAll();
        Assert.Single(all);

        var notification = all[0];
        Assert.Equal("cloud-2", notification.Id);
        Assert.Equal("Replacement", notification.Message);
        Assert.Equal("ref-123", notification.ReferenceId);
        Assert.True(notification.IsDismissed);
        Assert.True(notification.IsRead);
    }

    [Fact]
    public void AddOrUpdate_WhitespaceReferenceIdPreservesExistingReference()
    {
        var store = CreateStore();
        var created1 = new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero);
        var created2 = new DateTimeOffset(2026, 5, 7, 11, 0, 0, TimeSpan.Zero);

        store.AddOrUpdate("n1", "Original", created1, referenceId: "ref-1");
        store.AddOrUpdate("n1", "Updated", created2, referenceId: "   ");

        var notification = store.GetAll().Single(n => n.Id == "n1");
        Assert.Equal("ref-1", notification.ReferenceId);
    }

    [Fact]
    public void IsSeen_KnownId_ReturnsTrue()
    {
        var store = CreateStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);

        Assert.True(store.IsSeen("n1"));
    }

    [Fact]
    public void IsSeen_UnknownId_ReturnsFalse()
    {
        var store = CreateStore();

        Assert.False(store.IsSeen("nonexistent"));
    }

    // ─────────────────────── IsDismissed ───────────────────────

    [Fact]
    public void IsDismissed_DismissedNotification_ReturnsTrue()
    {
        var store = CreateStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);
        store.Dismiss("n1");

        Assert.True(store.IsDismissed("n1"));
    }

    // ─────────────────────── MarkRead / MarkUnread ───────────────────────

    [Fact]
    public void MarkRead_SetsIsReadTrue()
    {
        var store = CreateStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);

        Assert.False(store.GetAll()[0].IsRead);

        store.MarkRead("n1");

        Assert.True(store.GetAll()[0].IsRead);
    }

    [Fact]
    public void MarkUnread_SetsIsReadFalse()
    {
        var store = CreateStore();
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
        var store = CreateStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);

        Assert.False(store.IsDismissed("n1"));

        store.Dismiss("n1");

        Assert.True(store.IsDismissed("n1"));
    }

    [Fact]
    public void DismissAll_DismissesAllNotifications()
    {
        var store = CreateStore();
        store.AddOrUpdate("n1", "msg1", DateTimeOffset.UtcNow);
        store.AddOrUpdate("n2", "msg2", DateTimeOffset.UtcNow);
        store.AddOrUpdate("n3", "msg3", DateTimeOffset.UtcNow);

        store.DismissAll();

        Assert.True(store.IsDismissed("n1"));
        Assert.True(store.IsDismissed("n2"));
        Assert.True(store.IsDismissed("n3"));
    }

    [Fact]
    public void HideByReferenceId_IsCaseInsensitive()
    {
        var store = CreateStore();
        store.AddOrUpdate("n1", "msg1", DateTimeOffset.UtcNow, referenceId: "ReF-123");

        store.HideByReferenceId("ref-123");

        var notification = store.GetAll().Single(n => n.Id == "n1");
        Assert.True(notification.IsDismissed);
        Assert.True(notification.IsRead);
    }

    [Fact]
    public void HideByTag_HidesAllMatchingNotificationsCaseInsensitively()
    {
        var store = CreateStore();
        store.AddOrUpdate("n1", "msg1", DateTimeOffset.UtcNow, severity: "warning");
        store.AddOrUpdate("n2", "msg2", DateTimeOffset.UtcNow, severity: "WARNING");
        store.AddOrUpdate("n3", "msg3", DateTimeOffset.UtcNow, severity: "info");

        store.HideByTag("Warning");

        Assert.True(store.IsDismissed("n1"));
        Assert.True(store.IsDismissed("n2"));
        Assert.True(store.GetAll().Single(n => n.Id == "n1").IsRead);
        Assert.True(store.GetAll().Single(n => n.Id == "n2").IsRead);
        Assert.False(store.IsDismissed("n3"));
    }
    // ─────────────────────── UnreadCount ───────────────────────

    [Fact]
    public void UnreadCount_ReflectsUnreadNotifications()
    {
        var store = CreateStore();
        store.AddOrUpdate("n1", "msg1", DateTimeOffset.UtcNow);
        store.AddOrUpdate("n2", "msg2", DateTimeOffset.UtcNow);

        Assert.Equal(2, store.UnreadCount);

        store.MarkRead("n1");

        Assert.Equal(1, store.UnreadCount);

        store.MarkUnread("n1");

        Assert.Equal(2, store.UnreadCount);
    }

    [Fact]
    public void UnreadCount_ExcludesDismissedNotifications()
    {
        var store = CreateStore();
        store.AddOrUpdate("n1", "msg1", DateTimeOffset.UtcNow);
        store.AddOrUpdate("n2", "msg2", DateTimeOffset.UtcNow);

        Assert.Equal(2, store.UnreadCount);

        store.Dismiss("n1");

        Assert.Equal(1, store.UnreadCount);
    }

    [Fact]
    public void UnreadCount_IsZeroAfterDismissAll()
    {
        var store = CreateStore();
        store.AddOrUpdate("n1", "msg1", DateTimeOffset.UtcNow);
        store.AddOrUpdate("n2", "msg2", DateTimeOffset.UtcNow);
        store.MarkRead("n2");

        Assert.Equal(1, store.UnreadCount);

        store.DismissAll();

        Assert.Equal(0, store.UnreadCount);
    }

    // ─────────────────────── GetSeenUndismissedIds ───────────────────────

    [Fact]
    public void GetSeenUndismissedIds_ExcludesDismissed()
    {
        var store = CreateStore();
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

    [Fact]
    public void GetRecentSeenUndismissedIds_RetainsNewestInOldestToNewestOrder()
    {
        var store = CreateStore();
        var baseTime = new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero);
        store.AddOrUpdate("oldest", "Oldest", baseTime);
        store.AddOrUpdate("middle", "Middle", baseTime.AddMinutes(1));
        store.AddOrUpdate("newest", "Newest", baseTime.AddMinutes(2));

        var ids = store.GetRecentSeenUndismissedIds(2);

        Assert.Equal(["middle", "newest"], ids);
    }

    [Fact]
    public void GetRecentSeenUndismissedIds_ExcludesDismissedNotifications()
    {
        var store = CreateStore();
        var baseTime = new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero);
        store.AddOrUpdate("visible-old", "Visible old", baseTime);
        store.AddOrUpdate("dismissed", "Dismissed", baseTime.AddMinutes(1));
        store.AddOrUpdate("visible-new", "Visible new", baseTime.AddMinutes(2));
        store.Dismiss("dismissed");

        var ids = store.GetRecentSeenUndismissedIds(2);

        Assert.Equal(["visible-old", "visible-new"], ids);
    }

    [Fact]
    public void GetRecentSeenUndismissedIds_UsesCreatedReceivedAtAndIdTieBreakers()
    {
        var created = new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero);
        var firstReceived = created.AddMinutes(1);
        var secondReceived = created.AddMinutes(2);
        Directory.CreateDirectory(Path.GetDirectoryName(storageFilePath)!);
        var entries = new[]
        {
            CreateStoredNotification("id-c", created, firstReceived),
            CreateStoredNotification("id-a", created, secondReceived),
            CreateStoredNotification("id-b", created, secondReceived)
        };
        File.WriteAllText(
            storageFilePath,
            JsonSerializer.Serialize(new { Notifications = entries }));
        var store = CreateStore();

        var ids = store.GetRecentSeenUndismissedIds(3);

        Assert.Equal(["id-c", "id-a", "id-b"], ids);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetRecentSeenUndismissedIds_RejectsNonPositiveLimit(int maxCount)
    {
        var store = CreateStore();

        Assert.Throws<ArgumentOutOfRangeException>(() => store.GetRecentSeenUndismissedIds(maxCount));
    }

    // ─────────────────────── Trim ───────────────────────

    [Fact]
    public void Trim_RemovesOldestDismissed_WhenExceedsMax()
    {
        var store = CreateStore();
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
    public async Task Store_SurvivesRoundTrip_LoadsCorrectly()
    {
        var original = CreateStore(persistChanges: true);
        var created = new DateTimeOffset(2026, 5, 7, 14, 0, 0, TimeSpan.Zero);
        original.AddOrUpdate("persist1", "Hello", created, title: "T", severity: "info");

        NotificationStore? loaded = null;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            loaded = CreateStore();
            if (loaded.IsSeen("persist1"))
            {
                break;
            }

            await Task.Delay(50);
        }

        Assert.NotNull(loaded);
        Assert.True(loaded!.IsSeen("persist1"));
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
        var store = CreateStore();
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
        var store = CreateStore();
        var fired = false;
        store.Changed += (_, _) => fired = true;

        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);

        Assert.True(fired);
    }

    [Fact]
    public void Changed_FiresOnMarkRead()
    {
        var store = CreateStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);
        var fired = false;
        store.Changed += (_, _) => fired = true;

        store.MarkRead("n1");

        Assert.True(fired);
    }

    [Fact]
    public void Changed_FiresOnDismiss()
    {
        var store = CreateStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);
        var fired = false;
        store.Changed += (_, _) => fired = true;

        store.Dismiss("n1");

        Assert.True(fired);
    }

    [Fact]
    public void Changed_FiresOnDismissAll()
    {
        var store = CreateStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);
        var fired = false;
        store.Changed += (_, _) => fired = true;

        store.DismissAll();

        Assert.True(fired);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenMarkReadOnAlreadyRead()
    {
        var store = CreateStore();
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
        var store = CreateStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);
        store.Dismiss("n1");
        var fired = false;
        store.Changed += (_, _) => fired = true;

        store.Dismiss("n1");

        Assert.False(fired);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenHideOnAlreadyHidden()
    {
        var store = CreateStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);
        store.Hide("n1");
        var fired = false;
        store.Changed += (_, _) => fired = true;

        store.Hide("n1");

        Assert.False(fired);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenUnhideOnAlreadyVisible()
    {
        var store = CreateStore();
        store.AddOrUpdate("n1", "msg", DateTimeOffset.UtcNow);
        var fired = false;
        store.Changed += (_, _) => fired = true;

        store.Unhide("n1");

        Assert.False(fired);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenMarkAllReadHasNoVisibleUnreadNotifications()
    {
        var store = CreateStore();
        store.AddOrUpdate("read", "Read", DateTimeOffset.UtcNow);
        store.AddOrUpdate("hidden", "Hidden", DateTimeOffset.UtcNow);
        store.MarkRead("read");
        store.Hide("hidden");
        store.MarkUnread("hidden");
        var fired = false;
        store.Changed += (_, _) => fired = true;

        store.MarkAllRead();

        Assert.False(fired);
    }

    [Fact]
    public void Hide_MarksNotificationHiddenAndRead()
    {
        var store = CreateStore();
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
        var store = CreateStore();
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
        var store = CreateStore();
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
        var store = CreateStore();
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
    public void GetNotifications_ExcludesHideOnlyNotificationsFromListViewsAndUnreadCount()
    {
        var store = CreateStore();
        store.AddOrUpdate(
            "hide-only",
            "",
            DateTimeOffset.UtcNow,
            severity: "Motion Tag",
            referenceId: "motion-123");

        Assert.Empty(store.GetNotifications(NotificationVisibilityFilter.Visible, null));
        Assert.Equal(0, store.UnreadCount);

        store.Hide("hide-only");

        Assert.Empty(store.GetNotifications(NotificationVisibilityFilter.All, null));
        Assert.Empty(store.GetNotifications(NotificationVisibilityFilter.Hidden, null));
        Assert.Empty(store.GetNotifications(NotificationVisibilityFilter.All, "Motion"));
        Assert.Equal(0, store.UnreadCount);
    }

    [Fact]
    public void GetNotifications_SearchesTitleMessageAndTagCaseInsensitively()
    {
        var store = CreateStore();
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
        var store = CreateStore();
        store.AddOrUpdate(
            "internal-id",
            "Normal message",
            DateTimeOffset.UtcNow,
            referenceId: "reference-token");

        Assert.Empty(store.GetNotifications(NotificationVisibilityFilter.All, "internal-id"));
        Assert.Empty(store.GetNotifications(NotificationVisibilityFilter.All, "reference-token"));
    }

    [Fact]
    public void GetNotifications_LoadedDismissedEntry_IsHiddenAndExcludedFromVisible()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storageFilePath)!);

        var seeded = new StoredNotification(
            "persist-hidden",
            "Hidden from persisted file",
            "Persisted",
            null,
            "info",
            new DateTimeOffset(2026, 5, 7, 9, 0, 0, TimeSpan.Zero),
            DateTimeOffset.UtcNow,
            true,
            true,
            null,
            null,
            null,
            null,
            null,
            null);

        var json = JsonSerializer.Serialize(new
        {
            Notifications = new[] { seeded },
        });
        File.WriteAllText(storageFilePath, json);

        var store = CreateStore();

        Assert.Contains(store.GetNotifications(NotificationVisibilityFilter.Hidden, null), n => n.Id == seeded.Id);
        Assert.DoesNotContain(store.GetNotifications(NotificationVisibilityFilter.Visible, null), n => n.Id == seeded.Id);
        Assert.Contains(store.GetNotifications(NotificationVisibilityFilter.All, null), n => n.Id == seeded.Id);
    }
}

