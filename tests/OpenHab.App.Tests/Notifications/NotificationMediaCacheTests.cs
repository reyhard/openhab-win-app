using OpenHab.App.Notifications;

namespace OpenHab.App.Tests.Notifications;

public sealed class NotificationMediaCacheTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(
        Path.GetTempPath(),
        "OpenHab.NotificationMediaCacheTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StoreAsync_CountLimitEvictsOnlyOldestFile()
    {
        var now = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var cache = CreateCache(maxFiles: 2, maxBytes: 100, utcNow: () => now);

        var oldest = await cache.StoreAsync("oldest", ".jpg", [1], CancellationToken.None);
        now = now.AddMinutes(1);
        var middle = await cache.StoreAsync("middle", ".jpg", [2], CancellationToken.None);
        now = now.AddMinutes(1);
        var newest = await cache.StoreAsync("newest", ".jpg", [3], CancellationToken.None);

        Assert.NotNull(oldest);
        Assert.NotNull(middle);
        Assert.NotNull(newest);
        Assert.False(File.Exists(oldest.LocalPath));
        Assert.True(File.Exists(middle.LocalPath));
        Assert.True(File.Exists(newest.LocalPath));
    }

    [Fact]
    public async Task StoreAsync_ByteLimitEvictsOldestFilesUntilUnderBudget()
    {
        var now = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var cache = CreateCache(maxFiles: 10, maxBytes: 4, utcNow: () => now);

        var oldest = await cache.StoreAsync("oldest", ".png", [1, 1], CancellationToken.None);
        now = now.AddMinutes(1);
        var middle = await cache.StoreAsync("middle", ".png", [2, 2], CancellationToken.None);
        now = now.AddMinutes(1);
        var newest = await cache.StoreAsync("newest", ".png", [3, 3], CancellationToken.None);

        Assert.NotNull(oldest);
        Assert.NotNull(middle);
        Assert.NotNull(newest);
        Assert.False(File.Exists(oldest.LocalPath));
        Assert.True(File.Exists(middle.LocalPath));
        Assert.True(File.Exists(newest.LocalPath));
        Assert.Equal(4, Directory.EnumerateFiles(tempRoot).Sum(path => new FileInfo(path).Length));
    }

    [Fact]
    public async Task StoreAsync_AgeLimitRemovesExpiredFiles()
    {
        var now = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var cache = CreateCache(maxFiles: 10, maxBytes: 100, maxAge: TimeSpan.FromDays(30), utcNow: () => now);
        var expired = await cache.StoreAsync("expired", ".gif", [1], CancellationToken.None);

        now = now.AddDays(31);
        var current = await cache.StoreAsync("current", ".gif", [2], CancellationToken.None);

        Assert.NotNull(expired);
        Assert.NotNull(current);
        Assert.False(File.Exists(expired.LocalPath));
        Assert.True(File.Exists(current.LocalPath));
    }

    [Fact]
    public async Task StoreAsync_RetainsJustWrittenFileWhenItFitsIndividually()
    {
        var now = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var cache = CreateCache(maxFiles: 10, maxBytes: 4, utcNow: () => now);
        var old = await cache.StoreAsync("old", ".webp", [1, 2], CancellationToken.None);

        now = now.AddMinutes(1);
        var justWritten = await cache.StoreAsync("current", ".webp", [1, 2, 3, 4], CancellationToken.None);

        Assert.NotNull(old);
        Assert.NotNull(justWritten);
        Assert.False(File.Exists(old.LocalPath));
        Assert.True(File.Exists(justWritten.LocalPath));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(justWritten.LocalPath));
    }

    [Fact]
    public async Task StoreAsync_LockedExpiredFileCleanupIsBestEffort()
    {
        var now = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var cache = CreateCache(maxFiles: 1, maxBytes: 100, maxAge: TimeSpan.FromDays(1), utcNow: () => now);
        var locked = await cache.StoreAsync("locked", ".bmp", [1], CancellationToken.None);
        Assert.NotNull(locked);

        await using var lockStream = new FileStream(locked.LocalPath, FileMode.Open, FileAccess.Read, FileShare.None);
        now = now.AddDays(2);

        var current = await cache.StoreAsync("current", ".bmp", [2], CancellationToken.None);

        Assert.NotNull(current);
        Assert.True(File.Exists(current.LocalPath));
    }

    [Fact]
    public async Task Clear_RemovesCacheFilesButNeverSiblingFiles()
    {
        var outsideRoot = Path.Combine(Path.GetDirectoryName(tempRoot)!, $"outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideRoot);
        var outsideFile = Path.Combine(outsideRoot, "keep.txt");
        await File.WriteAllTextAsync(outsideFile, "keep", CancellationToken.None);
        try
        {
            var cache = CreateCache();
            var cached = await cache.StoreAsync("cached", ".jpg", [1, 2, 3], CancellationToken.None);
            Assert.NotNull(cached);

            cache.Clear();

            Assert.False(File.Exists(cached.LocalPath));
            Assert.True(File.Exists(outsideFile));
        }
        finally
        {
            Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StoreAsync_RejectsPathTraversalAndDoesNotWriteOutsideRoot()
    {
        var cache = CreateCache();
        var outsidePath = Path.Combine(Path.GetDirectoryName(tempRoot)!, "outside.jpg");

        var result = await cache.StoreAsync("../outside", ".jpg", [1], CancellationToken.None);

        Assert.Null(result);
        Assert.False(File.Exists(outsidePath));
    }

    [Fact]
    public async Task StoreAsync_UsesAtomicTemporaryFileAndLeavesNoTemporaryArtifacts()
    {
        var cache = CreateCache();

        var result = await cache.StoreAsync("atomic", ".png", [1, 2, 3], CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(Directory.EnumerateFiles(tempRoot));
        Assert.DoesNotContain(Directory.EnumerateFiles(tempRoot), path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    private NotificationMediaCache CreateCache(
        int maxFiles = NotificationMediaCache.DefaultMaxFiles,
        long maxBytes = NotificationMediaCache.DefaultMaxBytes,
        TimeSpan? maxAge = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        return new NotificationMediaCache(
            tempRoot,
            maxFiles,
            maxBytes,
            maxAge,
            utcNow ?? (() => DateTimeOffset.UtcNow));
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
            // Ignore cleanup errors in tests.
        }
    }
}
