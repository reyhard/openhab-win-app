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

    [Theory]
    [InlineData(0, 1L, 1.0, "maxFiles")]
    [InlineData(-1, 1L, 1.0, "maxFiles")]
    [InlineData(1, 0L, 1.0, "maxBytes")]
    [InlineData(1, -1L, 1.0, "maxBytes")]
    [InlineData(1, 1L, 0.0, "maxAge")]
    [InlineData(1, 1L, -1.0, "maxAge")]
    public void Constructor_RejectsNonPositiveLimits(int maxFiles, long maxBytes, double maxAgeDays, string expectedParameter)
    {
        var now = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new NotificationMediaCache(
            tempRoot,
            maxFiles,
            maxBytes,
            TimeSpan.FromDays(maxAgeDays),
            () => now));

        Assert.Equal(expectedParameter, exception.ParamName);
        Assert.False(Directory.Exists(tempRoot));
    }

    [Fact]
    public async Task PublicConstructor_AppliesConfiguredByteLimit()
    {
        var cache = new NotificationMediaCache(
            tempRoot,
            maxFiles: 2,
            maxBytes: 4,
            maxAge: TimeSpan.FromDays(30));

        var oversized = await cache.StoreAsync("oversized", "jpg", new byte[5], CancellationToken.None);
        var stored = await cache.StoreAsync("stored", "jpg", [1, 2, 3, 4], CancellationToken.None);

        Assert.Null(oversized);
        Assert.NotNull(stored);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(stored!.LocalPath));
        Assert.Single(Directory.EnumerateFiles(tempRoot));
    }

    [Fact]
    public async Task StoreAsync_PayloadAtByteLimitIsStoredAndOversizedPayloadLeavesExistingFileUnchanged()
    {
        var now = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var cache = CreateCache(maxFiles: 4, maxBytes: 8, utcNow: () => now);
        var exactBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var atLimit = await cache.StoreAsync("exact", "png", exactBytes, CancellationToken.None);
        var oversized = await cache.StoreAsync("oversized", "png", new byte[9], CancellationToken.None);

        Assert.NotNull(atLimit);
        Assert.Null(oversized);
        Assert.Equal(exactBytes, await File.ReadAllBytesAsync(atLimit!.LocalPath));
        Assert.Equal(8, new FileInfo(atLimit.LocalPath).Length);
        Assert.Single(Directory.EnumerateFiles(tempRoot));
        Assert.False(File.Exists(Path.Combine(tempRoot, "oversized.png")));
    }

    [Fact]
    public async Task PruneAsync_RemovesOnlyEntriesStrictlyOlderThanMaxAge()
    {
        var boundary = new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero);
        var writerClock = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var writer = CreateCache(maxFiles: 10, maxBytes: 100, maxAge: TimeSpan.FromDays(30), utcNow: () => writerClock);
        var older = await writer.StoreAsync("older", "jpg", [1], CancellationToken.None);
        writerClock = boundary;
        var exactlyAtThreshold = await writer.StoreAsync("boundary", "jpg", [2], CancellationToken.None);
        writerClock = boundary.AddSeconds(1);
        var newer = await writer.StoreAsync("newer", "jpg", [3], CancellationToken.None);
        Assert.NotNull(older);
        Assert.NotNull(exactlyAtThreshold);
        Assert.NotNull(newer);

        var prunerClock = boundary.AddDays(1);
        var pruner = CreateCache(maxFiles: 10, maxBytes: 100, maxAge: TimeSpan.FromDays(1), utcNow: () => prunerClock);
        await pruner.PruneAsync(CancellationToken.None);

        Assert.False(File.Exists(older!.LocalPath));
        Assert.True(File.Exists(exactlyAtThreshold!.LocalPath));
        Assert.True(File.Exists(newer!.LocalPath));
        Assert.Equal(2, Directory.EnumerateFiles(tempRoot).Count());
    }

    [Fact]
    public async Task PruneAsync_CountLimitTieBreaksEqualTimestampsByOrdinalIgnoreCasePath()
    {
        var now = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var writer = CreateCache(maxFiles: 10, maxBytes: 100, utcNow: () => now);
        var alpha = await writer.StoreAsync("alpha", "jpg", [1], CancellationToken.None);
        var beta = await writer.StoreAsync("beta", "jpg", [2], CancellationToken.None);
        Assert.NotNull(alpha);
        Assert.NotNull(beta);
        Assert.Equal(
            new FileInfo(alpha!.LocalPath).LastWriteTimeUtc,
            new FileInfo(beta!.LocalPath).LastWriteTimeUtc);

        var pruner = CreateCache(maxFiles: 1, maxBytes: 100, utcNow: () => now);
        await pruner.PruneAsync(CancellationToken.None);

        Assert.False(File.Exists(alpha.LocalPath));
        Assert.True(File.Exists(beta.LocalPath));
        Assert.Equal(new byte[] { 2 }, await File.ReadAllBytesAsync(beta.LocalPath));
    }

    [Fact]
    public async Task StoreAsync_BackwardClockKeepsNewFileAndEvictsOlderEntry()
    {
        var now = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var cache = CreateCache(maxFiles: 1, maxBytes: 100, utcNow: () => now);
        var existing = await cache.StoreAsync("existing", "jpg", [1, 2], CancellationToken.None);
        Assert.NotNull(existing);

        now = now.AddHours(-1);
        var justStored = await cache.StoreAsync("fresh", "jpg", [3, 4, 5], CancellationToken.None);

        Assert.NotNull(justStored);
        Assert.False(File.Exists(existing!.LocalPath));
        Assert.True(File.Exists(justStored!.LocalPath));
        Assert.Equal(new byte[] { 3, 4, 5 }, await File.ReadAllBytesAsync(justStored.LocalPath));
        Assert.Equal(3, Directory.EnumerateFiles(tempRoot).Sum(path => new FileInfo(path).Length));
    }

    [Fact]
    public async Task StoreAsync_NormalizesExtensionAndRootSeparatorToSamePath()
    {
        var now = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var cache = CreateCache(utcNow: () => now);
        var trailingSeparatorCache = new NotificationMediaCache(
            tempRoot + Path.DirectorySeparatorChar,
            NotificationMediaCache.DefaultMaxFiles,
            NotificationMediaCache.DefaultMaxBytes,
            TimeSpan.FromDays(30),
            () => now);

        var withoutDot = await cache.StoreAsync("media", "jpg", [1, 2], CancellationToken.None);
        var withDot = await cache.StoreAsync("media", ".jpg", [3, 4, 5], CancellationToken.None);
        var throughTrailingSeparatorRoot = await trailingSeparatorCache.StoreAsync("media", "jpg", [6], CancellationToken.None);

        Assert.NotNull(withoutDot);
        Assert.NotNull(withDot);
        Assert.NotNull(throughTrailingSeparatorRoot);
        Assert.Equal(withoutDot!.LocalPath, withDot!.LocalPath);
        Assert.Equal(withoutDot.LocalPath, throughTrailingSeparatorRoot!.LocalPath);
        Assert.Equal(new byte[] { 6 }, await File.ReadAllBytesAsync(withoutDot.LocalPath));
        Assert.Single(Directory.EnumerateFiles(tempRoot));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(tempRoot),
            path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StoreAsync_RejectsDotSegmentsAndSeparatorExtensionWithoutWriting()
    {
        var cache = CreateCache();
        var parentDirectory = Path.GetDirectoryName(tempRoot)!;

        var dotKey = await cache.StoreAsync(".", "jpg", [1], CancellationToken.None);
        var dotDotKey = await cache.StoreAsync("..", "jpg", [1], CancellationToken.None);
        var backslashExtension = await cache.StoreAsync("key", $"..{Path.DirectorySeparatorChar}escape", [1], CancellationToken.None);
        var slashExtension = await cache.StoreAsync("key", "sub/dir", [1], CancellationToken.None);

        Assert.Null(dotKey);
        Assert.Null(dotDotKey);
        Assert.Null(backslashExtension);
        Assert.Null(slashExtension);
        Assert.False(Directory.Exists(tempRoot));
        Assert.False(File.Exists(Path.Combine(parentDirectory, "escape.jpg")));
        Assert.False(File.Exists(Path.Combine(parentDirectory, "key.jpg")));
    }

    [Fact]
    public async Task StoreAsync_ReturnsNullWhenCacheRootIsBlockedByRegularFile()
    {
        var now = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var blockerPath = Path.Combine(
            Path.GetTempPath(),
            "OpenHab.NotificationMediaCacheTests",
            $"blocked-{Guid.NewGuid():N}");
        var blockerBytes = new byte[] { 7, 7, 7 };
        Directory.CreateDirectory(Path.GetDirectoryName(blockerPath)!);
        await File.WriteAllBytesAsync(blockerPath, blockerBytes, CancellationToken.None);
        try
        {
            var cache = new NotificationMediaCache(
                blockerPath,
                NotificationMediaCache.DefaultMaxFiles,
                NotificationMediaCache.DefaultMaxBytes,
                TimeSpan.FromDays(30),
                () => now);

            var blocked = await cache.StoreAsync("blocked", "jpg", [1], CancellationToken.None);

            Assert.Null(blocked);
            Assert.True(File.Exists(blockerPath));
            Assert.False(Directory.Exists(blockerPath));
            Assert.Equal(blockerBytes, await File.ReadAllBytesAsync(blockerPath));

            File.Delete(blockerPath);
            var afterRemoval = await cache.StoreAsync("blocked", "jpg", [1, 2], CancellationToken.None);

            Assert.NotNull(afterRemoval);
            Assert.True(File.Exists(afterRemoval!.LocalPath));
            Assert.Equal(new byte[] { 1, 2 }, await File.ReadAllBytesAsync(afterRemoval.LocalPath));
        }
        finally
        {
            if (File.Exists(blockerPath))
            {
                File.Delete(blockerPath);
            }

            if (Directory.Exists(blockerPath))
            {
                Directory.Delete(blockerPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task StoreAsync_PreCanceledTokenIsObservableAndWritesNothing()
    {
        var now = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var cache = CreateCache(utcNow: () => now);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cache.StoreAsync("cancelled", "jpg", [1, 2, 3], cancellation.Token));

        Assert.False(Directory.Exists(tempRoot));
    }

    [Fact]
    public async Task PruneAsyncAndClear_LeaveMissingRootAbsent()
    {
        var now = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var cache = CreateCache(utcNow: () => now);

        await cache.PruneAsync(CancellationToken.None);
        cache.Clear();
        cache.Clear();

        Assert.False(Directory.Exists(tempRoot));
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
